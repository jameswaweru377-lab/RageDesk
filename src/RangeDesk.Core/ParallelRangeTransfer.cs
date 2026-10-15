using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Independently authored segmented HTTP downloader. Segments have disjoint files,
/// strong ETag validators and a durable manifest; none can overwrite a live range.
/// If byte ranges or strong validators are unsupported, new downloads fall back
/// to the existing direct-stream path without creating segment files.
/// </summary>
public sealed class ParallelRangeTransfer(HttpClient http)
{
    private sealed record Manifest(string Url, long Length, string ETag, int Connections);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public const int MaximumConnections = 32;
    public const long MinimumSegmentLength = 512 * 1024;

    public static (long First, long Last) SegmentBounds(long total, int segments, int index)
    {
        if (total <= 0 || segments < 1 || index < 0 || index >= segments)
            throw new ArgumentOutOfRangeException(nameof(index));
        long first = total / segments * index + Math.Min(index, (int)(total % segments));
        long length = total / segments + (index < total % segments ? 1 : 0);
        return (first, first + length - 1);
    }

    /// <returns>True when a verified segmented file was completed, false when the
    /// server is unsuitable and a normal download can safely be attempted.</returns>
    public async Task<bool> TryDownloadAsync(DownloadEntry entry, int connections,
        SharedBandwidthLimiter limiter, Action<long, long, double, int> progress, CancellationToken token,
        Action<IReadOnlyList<WorkerMetric>>? workerProgress = null)
    {
        if (connections < 2 || connections > MaximumConnections)
            throw new ArgumentOutOfRangeException(nameof(connections));
        var directory = entry.Destination + ".rdsegments";
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (File.Exists(entry.Destination + ".rdpartial") ||
            File.Exists(entry.Destination + ".rdstate"))
            return false; // Existing v5 single-stream resume takes precedence.

        Manifest? manifest;
        if (Directory.Exists(directory))
        {
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("A segment folder exists without its manifest. Preserve it for recovery.");
            try
            {
                manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath, token));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The segmented download manifest is invalid; no data was removed.", ex);
            }
            if (manifest is null || manifest.Url != entry.Url ||
                manifest.Length < 1 || manifest.Connections < 2 ||
                manifest.Connections > MaximumConnections ||
                !EntityTagHeaderValue.TryParse(manifest.ETag, out var etag) || etag.IsWeak)
                throw new InvalidDataException("Segment manifest does not match a validated transfer.");
        }
        else
        {
            // HTTP 206 probe: never infer range support from Accept-Ranges alone.
            using var response = await SendRangeWithRetryAsync(entry.Url, 0, 0, null, token);
            var range = response.Content.Headers.ContentRange;
            var tag = response.Headers.ETag;
            if (response.StatusCode != HttpStatusCode.PartialContent ||
                range is null || !range.HasLength || range.From != 0 || range.To != 0 ||
                range.Length is not long length || length < 2 * MinimumSegmentLength ||
                tag is null || tag.IsWeak || response.Content.Headers.ContentEncoding.Count > 0)
                return false;

            int actual = (int)Math.Min(connections, Math.Max(2L, length / MinimumSegmentLength));
            manifest = new Manifest(entry.Url, length, tag.ToString(), actual);
            Directory.CreateDirectory(directory);
            // Atomic manifest replacement; preserve existing segment data on errors.
            var temporary = manifestPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, JsonOptions), token);
            File.Move(temporary, manifestPath, false);
        }

        var state = manifest!;
        long received = 0;
        var workerBytes = new long[state.Connections];
        var workerActive = new int[state.Connections];
        var segmentSizes = new long[state.Connections];
        for (int i = 0; i < state.Connections; i++)
        {
            var (first, last) = SegmentBounds(state.Length, state.Connections, i);
            var path = Path.Combine(directory, $"{i:D2}.part");
            segmentSizes[i] = last - first + 1;
            if (!File.Exists(path)) continue;
            long available = new FileInfo(path).Length;
            if (available > last - first + 1)
                throw new InvalidDataException($"Segment {i} exceeds its assigned byte range.");
            received += available;
            workerBytes[i] = available;
        }

        var initialWorkerBytes = (long[])workerBytes.Clone();
        var workerRateEstimators = Enumerable.Range(0, state.Connections)
            .Select(_ => new DownloadRateEstimator()).ToArray();
        for (int i = 0; i < state.Connections; i++)
            workerRateEstimators[i].Update(TimeSpan.Zero, 0);

        int active = 0;
        long measured = received;
        long startingBytes = received;
        var rate = new DownloadRateEstimator();
        var watch = Stopwatch.StartNew();
        rate.Update(TimeSpan.Zero, 0);
        using var timerStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var timer = Task.Run(async () =>
        {
            using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await ticks.WaitForNextTickAsync(timerStop.Token))
                {
                    long current = Interlocked.Read(ref measured);
                    double speed = rate.Update(watch.Elapsed, Math.Max(0, current - startingBytes));
                    progress(current, state.Length, speed, Volatile.Read(ref active));
                    if (workerProgress is not null)
                    {
                        var snapshot = new WorkerMetric[state.Connections];
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            long done = Interlocked.Read(ref workerBytes[i]);
                            double workerRate = workerRateEstimators[i].Update(watch.Elapsed,
                                Math.Max(0, done - initialWorkerBytes[i]));
                            snapshot[i] = new WorkerMetric(i, done, segmentSizes[i], workerRate,
                                Volatile.Read(ref workerActive[i]) == 1);
                        }
                        workerProgress(snapshot);
                    }
                }
            }
            catch (OperationCanceledException) when (timerStop.IsCancellationRequested) { }
        }, CancellationToken.None);

        try
        {
            using var workerStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = Enumerable.Range(0, state.Connections).Select(async i =>
            {
                try
                {
                    var (first, last) = SegmentBounds(state.Length, state.Connections, i);
                    var path = Path.Combine(directory, $"{i:D2}.part");
                    long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (existing == last - first + 1) return;
                    Interlocked.Increment(ref active);
                    Interlocked.Exchange(ref workerActive[i], 1);
                    try
                    {
                        using var response = await SendRangeWithRetryAsync(entry.Url,
                            first + existing, last, state.ETag, workerStop.Token);
                        var range = response.Content.Headers.ContentRange;
                        if (response.StatusCode != HttpStatusCode.PartialContent ||
                            range is null || !range.HasLength ||
                            range.From != first + existing || range.To != last ||
                            range.Length != state.Length ||
                            response.Headers.ETag?.ToString() != state.ETag ||
                            response.Content.Headers.ContentEncoding.Count > 0)
                            throw new InvalidDataException($"Worker {i}: server failed the range/validator check.");

                        await using var output = new FileStream(path, FileMode.OpenOrCreate,
                            FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
                        if (output.Length != existing)
                            throw new IOException("Segment changed while preparing to resume.");
                        output.Position = existing;
                        await using var input = await response.Content.ReadAsStreamAsync(workerStop.Token);
                        byte[] buffer = new byte[131072];
                        long current = existing;
                        while (current < last - first + 1)
                        {
                            int wanted = (int)Math.Min(buffer.Length, last - first + 1 - current);
                            int count = await input.ReadAsync(buffer.AsMemory(0, wanted), workerStop.Token);
                            if (count == 0) throw new EndOfStreamException($"Worker {i}: range ended early.");
                            await output.WriteAsync(buffer.AsMemory(0, count), workerStop.Token);
                            await limiter.AccountAsync(count, workerStop.Token);
                            current += count;
                            Interlocked.Add(ref measured, count);
                            Interlocked.Add(ref workerBytes[i], count);
                        }
                        // Do not accept excess content beyond a declared range.
                        if (await input.ReadAsync(buffer.AsMemory(0, 1), workerStop.Token) != 0)
                            throw new InvalidDataException($"Worker {i}: excess bytes after requested range.");
                        await output.FlushAsync(workerStop.Token);
                        output.Flush(true);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref workerActive[i], 0);
                        Interlocked.Decrement(ref active);
                    }
                }
                catch
                {
                    workerStop.Cancel(); // prevent other writers from completing a mismatched set
                    throw;
                }
            }).ToArray();
            await Task.WhenAll(tasks);
            if (workerProgress is not null)
                workerProgress(Enumerable.Range(0, state.Connections)
                    .Select(i => new WorkerMetric(i, Interlocked.Read(ref workerBytes[i]),
                        segmentSizes[i], 0, false)).ToArray());

            // Re-validate every segment before assembling. Never publish partial
            // or mismatched output as the final destination.
            for (int i = 0; i < state.Connections; i++)
            {
                var (first, last) = SegmentBounds(state.Length, state.Connections, i);
                var path = Path.Combine(directory, $"{i:D2}.part");
                if (!File.Exists(path) || new FileInfo(path).Length != last - first + 1)
                    throw new InvalidDataException("Cannot assemble incomplete range segments.");
            }
            token.ThrowIfCancellationRequested();
            string assembling = entry.Destination + ".rdassembling";
            // Old interrupted assembly is ours, safe to overwrite only this path.
            await using (var output = new FileStream(assembling, FileMode.Create,
                FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                for (int i = 0; i < state.Connections; i++)
                {
                    await using var input = new FileStream(Path.Combine(directory, $"{i:D2}.part"),
                        FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, token);
                }
                await output.FlushAsync(token);
                output.Flush(true);
            }
            if (new FileInfo(assembling).Length != state.Length)
                throw new InvalidDataException("Assembled file length differs from the validated source.");
            token.ThrowIfCancellationRequested();
            await Sha256Verification.VerifyAsync(assembling, entry.ExpectedSha256, token);
            File.Move(assembling, entry.Destination, false);
            // Deleting the journal is best effort after verified final rename.
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            progress(state.Length, state.Length, 0, 0);
            return true;
        }
        finally
        {
            timerStop.Cancel();
            await timer;
        }
    }
    private async Task<HttpResponseMessage> SendRangeWithRetryAsync(
        string url, long from, long to, string? validator, CancellationToken token)
    {
        for (int attempt = 1; attempt <= HttpRetryPolicy.MaximumAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.Range = new RangeHeaderValue(from, to);
            if (validator is not null)
                request.Headers.TryAddWithoutValidation("If-Range", validator);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (HttpRequestException) when (attempt < HttpRetryPolicy.MaximumAttempts)
            {
                await Task.Delay(HttpRetryPolicy.Delay(attempt, null), token);
                continue;
            }

            if (HttpRetryPolicy.IsTemporary(response.StatusCode) &&
                attempt < HttpRetryPolicy.MaximumAttempts)
            {
                var delay = HttpRetryPolicy.Delay(attempt, response.Headers.RetryAfter);
                response.Dispose();
                await Task.Delay(delay, token);
                continue;
            }
            return response;
        }
        throw new HttpRequestException("The download exhausted its bounded retry attempts.");
    }

}
