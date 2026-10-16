using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// User-initiated, unencrypted, finite HLS MPEG-TS VOD downloader.
/// Manifest changes abort without modifying an earlier checkpoint.
/// A segment is only committed after its full body is validated and flushed;
/// a crash mid-append is recovered by truncating to the committed byte count.
/// </summary>
public sealed class HlsVodTransfer(HttpClient http, TimeSpan? timeout = null)
{
    private sealed record Checkpoint(string Url, string Fingerprint, int Finished, long Bytes);
    private const int MaxPlaylistBytes = 1_048_576;
    private const long MaxSegmentBytes = 64L * 1024 * 1024;
    private const long MaxDownloadBytes = 32L * 1024 * 1024 * 1024;

    public async Task<long> DownloadAsync(DownloadEntry entry, SharedBandwidthLimiter limiter,
        Action<long, long?, double, int> progress, CancellationToken token)
    {
        if (!HlsPlaylist.IsHlsUrl(entry.Url))
            throw new ArgumentException("This is not a direct HLS playlist URL.");
        if (!entry.Destination.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("This HLS preview creates MPEG-TS (.ts) files, not MP4. Choose a .ts destination.");

        var source = new Uri(entry.Url);
        string text = await GetPlaylistAsync(source, token);
        var master = HlsPlaylist.Parse(text, source);
        if (master.IsMaster)
        {
            // Preview chooses the highest published BANDWIDTH variant. External
            // audio or unsupported fMP4/encrypted variants fail explicitly.
            var best = master.Variants.OrderByDescending(x => x.Bandwidth)
                .ThenByDescending(x => x.Height ?? 0).First();
            source = best.PlaylistUrl;
            text = await GetPlaylistAsync(source, token);
        }

        var media = HlsPlaylist.Parse(text, source);
        if (media.IsMaster) throw new NotSupportedException("Nested HLS master playlists are not supported.");
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.AbsoluteUri + "\n" + string.Join("\n", media.Segments.Select(x => x.AbsoluteUri)) + "\n" + text)));
        string partial = entry.Destination + ".rdhls-partial";
        string journal = entry.Destination + ".rdhls-state";
        string scratch = entry.Destination + ".rdhls-segment";
        var directory = Path.GetDirectoryName(entry.Destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        int completed = 0;
        long committed = 0;
        if (File.Exists(partial) != File.Exists(journal))
            throw new InvalidDataException("An incomplete HLS checkpoint is missing its matching file; it was preserved.");
        if (File.Exists(partial))
        {
            Checkpoint? old;
            try { old = JsonSerializer.Deserialize<Checkpoint>(await File.ReadAllTextAsync(journal, token)); }
            catch (JsonException ex) { throw new InvalidDataException("HLS checkpoint is unreadable; original data was preserved.", ex); }
            if (old is null || old.Url != entry.Url || old.Fingerprint != fingerprint ||
                old.Finished < 0 || old.Finished > media.Segments.Count ||
                old.Bytes < 0 || old.Bytes > MaxDownloadBytes ||
                new FileInfo(partial).Length < old.Bytes)
                throw new InvalidDataException("HLS playlist or checkpoint changed; saved video was preserved.");
            completed = old.Finished;
            committed = old.Bytes;
        }
        else
        {
            // Initialize both companions before starting the first network segment.
            await using (var blank = new FileStream(partial, FileMode.CreateNew, FileAccess.Write))
                await blank.FlushAsync(token);
            await SaveCheckpointAsync(journal, new Checkpoint(entry.Url, fingerprint, 0, 0), token);
        }

        // Crash-safe truncation: only successfully recorded whole segments are kept.
        await using var output = new FileStream(partial, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 131072, FileOptions.Asynchronous);
        if (output.Length != committed) output.SetLength(committed);
        output.Position = committed;

        var watch = Stopwatch.StartNew();
        long initialBytes = committed;
        progress(committed, null, 0, 1);
        for (int i = completed; i < media.Segments.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            // Scratch file is private and overwritten only after checkpoint
            // validation; never append a partially received segment.
            long segmentBytes = await DownloadSegmentAsync(media.Segments[i], scratch, limiter, token);
            if (segmentBytes < 188 || segmentBytes % 188 != 0 ||
                committed > MaxDownloadBytes - segmentBytes)
                throw new InvalidDataException("Invalid or oversized MPEG-TS segment; checkpoint preserved.");
            await using (var part = new FileStream(scratch, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131072, FileOptions.Asynchronous))
            {
                // Guard against HTML error pages and non-MPEG TS media.
                if (part.ReadByte() != 0x47)
                    throw new InvalidDataException("HLS media segment was not MPEG-TS; checkpoint preserved.");
                part.Position = 0;
                await part.CopyToAsync(output, token);
            }
            await output.FlushAsync(token);
            output.Flush(true);
            committed += segmentBytes;
            await SaveCheckpointAsync(journal, new Checkpoint(entry.Url, fingerprint, i + 1, committed), token);
            double rate = watch.Elapsed.TotalSeconds > 0.001
                ? (committed - initialBytes) / watch.Elapsed.TotalSeconds : 0;
            progress(committed, null, rate, 1);
            try { File.Delete(scratch); } catch (IOException) { }
        }
        await output.FlushAsync(token);
        output.Flush(true);
        // Dispose the writer before hash check + atomic final placement.
        await output.DisposeAsync();
        await Sha256Verification.VerifyAsync(partial, entry.ExpectedSha256, token);
        token.ThrowIfCancellationRequested();
        File.Move(partial, entry.Destination, overwrite: false);
        File.Delete(journal);
        if (File.Exists(scratch)) File.Delete(scratch);
        return committed;
    }

    private async Task<string> GetPlaylistAsync(Uri uri, CancellationToken token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.AcceptEncoding.ParseAdd("identity");
        using var response = await TransferInactivityTimeout.SendAsync(http, req, token, timeout);
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentLength > MaxPlaylistBytes)
            throw new InvalidDataException("HLS playlist is unavailable or exceeds the safe size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var data = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            int n = await TransferInactivityTimeout.ReadAsync(input, buffer, token, timeout);
            if (n == 0) break;
            if (data.Length + n > MaxPlaylistBytes)
                throw new InvalidDataException("HLS playlist exceeds the safe size limit.");
            data.Write(buffer, 0, n);
        }
        return Encoding.UTF8.GetString(data.ToArray());
    }

    private async Task<long> DownloadSegmentAsync(Uri uri, string scratch, SharedBandwidthLimiter limiter,
        CancellationToken token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.AcceptEncoding.ParseAdd("identity");
        using var response = await TransferInactivityTimeout.SendAsync(http, req, token, timeout);
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentLength > MaxSegmentBytes)
            throw new InvalidDataException("HLS segment is unavailable or exceeds the safe size limit.");
        long bytes = 0;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(scratch, FileMode.Create, FileAccess.Write,
            FileShare.None, 131072, FileOptions.Asynchronous);
        byte[] buffer = new byte[131072];
        while (true)
        {
            int count = await TransferInactivityTimeout.ReadAsync(input, buffer, token, timeout);
            if (count == 0) break;
            if (bytes + count > MaxSegmentBytes) throw new InvalidDataException("HLS segment too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            await limiter.AccountAsync(count, token);
            bytes += count;
        }
        if (response.Content.Headers.ContentLength is long expected && bytes != expected)
            throw new EndOfStreamException("HLS segment ended before Content-Length.");
        await output.FlushAsync(token);
        output.Flush(true);
        return bytes;
    }

    private static async Task SaveCheckpointAsync(string journal, Checkpoint state, CancellationToken token)
    {
        string temp = journal + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state), token);
            File.Move(temp, journal, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
