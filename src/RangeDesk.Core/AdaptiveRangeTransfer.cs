using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Optional adaptive range scheduler with a durable v2 journal. Idle workers
/// request cooperative splits; the donor narrows its own assigned end before
/// a helper starts the new non-overlapping suffix. V1 segment journals remain
/// handled by ParallelRangeTransfer, never migrated in place.
/// </summary>
public sealed class AdaptiveRangeTransfer(HttpClient http, TimeSpan? networkIdleTimeout = null)
{
    private const long MinimumPieceLength = 512 * 1024;
    private const int MaximumPieces = 512;
    private sealed record PieceRecord(int Id, long From, long To);
    private sealed record Manifest(string Url, long Length, string ETag, int Connections,
        List<PieceRecord> Pieces);
    private sealed class Piece(int id, long from, long to)
    {
        public int Id = id;
        public long From = from;
        public long To = to;
        public long Downloaded;
        public int Active;
        public int SplitRequested;
        public DownloadRateEstimator Rate = new();
    }

    public async Task<bool> TryDownloadAsync(DownloadEntry entry,
        SharedBandwidthLimiter limiter,
        Action<long, long, double, int> progress,
        Action<IReadOnlyList<WorkerMetric>>? workerProgress,
        CancellationToken token)
    {
        if (entry.RequestedConnections < 2) return false;
        string folder = entry.Destination + ".rdadaptive";
        string manifestPath = Path.Combine(folder, "manifest.json");
        if (File.Exists(entry.Destination + ".rdpartial") ||
            File.Exists(entry.Destination + ".rdstate") ||
            Directory.Exists(entry.Destination + ".rdsegments"))
            return false;

        Manifest saved;
        if (Directory.Exists(folder))
        {
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("Adaptive segment folder has no manifest; original files preserved.");
            try
            {
                saved = JsonSerializer.Deserialize<Manifest>(
                    await File.ReadAllTextAsync(manifestPath, token)) ??
                    throw new InvalidDataException("Adaptive manifest is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Adaptive manifest cannot be read safely.", ex);
            }
            if (saved.Url != entry.Url || saved.Length < 1 ||
                saved.Connections < 2 || saved.Connections > ParallelRangeTransfer.MaximumConnections ||
                !EntityTagHeaderValue.TryParse(saved.ETag, out var oldTag) || oldTag.IsWeak)
                throw new InvalidDataException("Adaptive manifest does not match the download.");
        }
        else
        {
            using var probe = await SendRangeAsync(entry.Url, 0, 0, null, token);
            var range = probe.Content.Headers.ContentRange;
            var tag = probe.Headers.ETag;
            if (probe.StatusCode != HttpStatusCode.PartialContent ||
                range is null || range.From != 0 || range.To != 0 ||
                range.Length is not long total || total < 2 * MinimumPieceLength ||
                tag is null || tag.IsWeak ||
                probe.Content.Headers.ContentEncoding.Count != 0)
                return false;

            int connections = (int)Math.Min(entry.RequestedConnections,
                Math.Max(2L, total / MinimumPieceLength));
            var pieces = Enumerable.Range(0, connections).Select(i =>
            {
                var (first, last) = ParallelRangeTransfer.SegmentBounds(total, connections, i);
                return new PieceRecord(i, first, last);
            }).ToList();
            saved = new Manifest(entry.Url, total, tag.ToString(), connections, pieces);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(manifestPath + ".tmp", JsonSerializer.Serialize(saved), token);
            File.Move(manifestPath + ".tmp", manifestPath, false);
        }

        ValidateManifest(saved);
        var piecesById = saved.Pieces.ToDictionary(
            p => p.Id, p => new Piece(p.Id, p.From, p.To));
        var piecesList = piecesById.Values.OrderBy(p => p.From).ToList();
        var pending = new Queue<Piece>();
        long received = 0;
        foreach (var piece in piecesList)
        {
            string path = PartPath(piece);
            if (File.Exists(path))
            {
                piece.Downloaded = new FileInfo(path).Length;
                if (piece.Downloaded > piece.To - piece.From + 1)
                    throw new InvalidDataException("Saved adaptive piece exceeds its allocated range.");
            }
            received += piece.Downloaded;
            if (piece.Downloaded < piece.To - piece.From + 1) pending.Enqueue(piece);
        }

        long transferred = received;
        int activeCount = 0;
        int nextId = piecesById.Keys.Max() + 1;
        object sync = new();
        var rate = new DownloadRateEstimator();
        var clock = Stopwatch.StartNew();
        var rateOrigins = piecesList.ToDictionary(p => p.Id, p => p.Downloaded);
        rate.Update(TimeSpan.Zero, 0);
        foreach (var p in piecesList) p.Rate.Update(TimeSpan.Zero, 0);
        long initialBytes = received;

        void PersistManifestUnderLock()
        {
            var snapshot = new Manifest(saved.Url, saved.Length, saved.ETag, saved.Connections,
                piecesList.OrderBy(p => p.From)
                    .Select(p => new PieceRecord(p.Id, p.From, p.To)).ToList());
            string temp = manifestPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, manifestPath, true);
        }

        void SplitIfRequested(Piece donor)
        {
            if (Volatile.Read(ref donor.SplitRequested) == 0) return;
            lock (sync)
            {
                donor.SplitRequested = 0;
                long remaining = donor.To - donor.From + 1 - Interlocked.Read(ref donor.Downloaded);
                if (remaining < 2 * MinimumPieceLength || piecesList.Count >= MaximumPieces || nextId >= MaximumPieces) return;
                long originalEnd = donor.To;
                long dividing = donor.From + donor.Downloaded + remaining / 2;
                if (dividing <= donor.From + donor.Downloaded || dividing > originalEnd) return;
                var helper = new Piece(nextId++, dividing, originalEnd);
                long earlierEnd = donor.To;
                donor.To = dividing - 1;
                piecesList.Add(helper);
                rateOrigins[helper.Id] = 0;
                helper.Rate.Update(TimeSpan.Zero, 0);
                try { PersistManifestUnderLock(); }
                catch
                {
                    donor.To = earlierEnd;
                    piecesList.Remove(helper);
                    rateOrigins.Remove(helper.Id);
                    throw;
                }
                pending.Enqueue(helper);
            }
        }

        using var allStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var timerStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var timer = Task.Run(async () =>
        {
            using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await ticks.WaitForNextTickAsync(timerStop.Token))
                {
                    List<WorkerMetric> metrics;
                    double currentRate;
                    long currentBytes;
                    int currentActive;
                    lock (sync)
                    {
                        currentBytes = Interlocked.Read(ref transferred);
                        currentRate = rate.Update(clock.Elapsed, currentBytes - initialBytes);
                        currentActive = activeCount;
                        metrics = piecesList.OrderBy(p => p.Id).Select(p =>
                        {
                            long bytes = Interlocked.Read(ref p.Downloaded);
                            double pieceRate = p.Rate.Update(clock.Elapsed,
                                Math.Max(0, bytes - rateOrigins[p.Id]));
                            return new WorkerMetric(p.Id, bytes, p.To - p.From + 1,
                                pieceRate, Volatile.Read(ref p.Active) == 1);
                        }).ToList();
                    }
                    progress(currentBytes, saved.Length, currentRate, currentActive);
                    workerProgress?.Invoke(metrics);
                }
            }
            catch (OperationCanceledException) when (timerStop.IsCancellationRequested) { }
        });

        async Task ProcessPiece(Piece piece)
        {
            byte[] buffer = new byte[131072];
            // Restart only a truncated or stalled response body. All other
            // failures (especially validator/range mismatches) remain fatal.
            // Each retry uses the durable length of this piece, and must pass
            // the full strong-ETag and Content-Range checks again.
            for (int attempt = 1; attempt <= HttpRetryPolicy.MaximumAttempts; attempt++)
            {
                allStop.Token.ThrowIfCancellationRequested();
                long existing = Interlocked.Read(ref piece.Downloaded);
                long lastAtRequest = piece.To;
                if (existing == lastAtRequest - piece.From + 1) return;

                try
                {
                    using var response = await SendRangeAsync(saved.Url,
                        piece.From + existing, lastAtRequest, saved.ETag, allStop.Token);
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent ||
                        range is null || !range.HasLength ||
                        range.From != piece.From + existing ||
                        range.To != lastAtRequest || range.Length != saved.Length ||
                        response.Headers.ETag?.ToString() != saved.ETag ||
                        response.Content.Headers.ContentEncoding.Count != 0)
                        throw new InvalidDataException($"Adaptive range {piece.Id} did not match the strong validator.");

                    await using var output = new FileStream(PartPath(piece), FileMode.OpenOrCreate,
                        FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
                    if (output.Length != existing)
                        throw new IOException("Adaptive range file changed during recovery.");
                    output.Position = existing;
                    await using var input = await response.Content.ReadAsStreamAsync(allStop.Token);
                    while (piece.Downloaded < piece.To - piece.From + 1)
                    {
                        long remaining = piece.To - piece.From + 1 - piece.Downloaded;
                        int count = await TransferInactivityTimeout.ReadAsync(input, buffer.AsMemory(0,
                            (int)Math.Min(buffer.Length, remaining)), allStop.Token, networkIdleTimeout);
                        if (count == 0)
                            throw new EndOfStreamException($"Adaptive range {piece.Id} ended early.");
                        await output.WriteAsync(buffer.AsMemory(0, count), allStop.Token);
                        await limiter.AccountAsync(count, allStop.Token);
                        Interlocked.Add(ref piece.Downloaded, count);
                        Interlocked.Add(ref transferred, count);
                        if (Volatile.Read(ref piece.SplitRequested) != 0)
                        {
                            // Before narrowing the donor's committed interval, ensure
                            // the received prefix has reached durable storage.
                            await output.FlushAsync(allStop.Token);
                            output.Flush(true);
                            SplitIfRequested(piece);
                        }
                    }

                    // Split donors deliberately end early; unsplit ranges must
                    // reject extra content, including on every retry.
                    if (piece.To == lastAtRequest &&
                        await TransferInactivityTimeout.ReadAsync(input, buffer.AsMemory(0, 1),
                            allStop.Token, networkIdleTimeout) != 0)
                        throw new InvalidDataException("Server returned bytes beyond its declared range.");

                    await output.FlushAsync(allStop.Token);
                    output.Flush(true);
                    return;
                }
                catch (Exception ex) when (
                    (ex is TimeoutException or System.Net.Http.HttpIOException) &&
                    Interlocked.Read(ref piece.Downloaded) == piece.To - piece.From + 1 &&
                    !allStop.Token.IsCancellationRequested)
                {
                    // A full-length checkpoint without a validated response end
                    // must never be treated as complete after a restart.
                    UnconfirmedRangeTail.RewindOneByte(PartPath(piece), piece.Downloaded);
                    Interlocked.Decrement(ref piece.Downloaded);
                    Interlocked.Decrement(ref transferred);
                    if (attempt >= HttpRetryPolicy.MaximumAttempts) throw;
                    await Task.Delay(HttpRetryPolicy.Delay(attempt, null), allStop.Token);
                }
                catch (Exception ex) when ((ex is TimeoutException or EndOfStreamException or
                    System.Net.Http.HttpIOException) &&
                    attempt < HttpRetryPolicy.MaximumAttempts &&
                    Interlocked.Read(ref piece.Downloaded) < piece.To - piece.From + 1 &&
                    !allStop.Token.IsCancellationRequested)
                {
                    // The response and output stream have been disposed. Never
                    // re-request already saved bytes or silently restart a piece.
                    await Task.Delay(HttpRetryPolicy.Delay(attempt, null), allStop.Token);
                }
            }
        }

        async Task WorkerLoop()
        {
            while (true)
            {
                allStop.Token.ThrowIfCancellationRequested();
                Piece? piece = null;
                lock (sync)
                {
                    if (pending.Count > 0)
                    {
                        piece = pending.Dequeue();
                        Interlocked.Exchange(ref piece.Active, 1);
                        activeCount++;
                    }
                    else if (activeCount == 0) return;
                    else
                    {
                        var donor = piecesList.Where(p => p.Active == 1 && p.SplitRequested == 0)
                            .OrderByDescending(p => p.To - p.From + 1 - Interlocked.Read(ref p.Downloaded))
                            .FirstOrDefault(p =>
                                p.To - p.From + 1 - Interlocked.Read(ref p.Downloaded) >=
                                2 * MinimumPieceLength);
                        if (donor is not null) Interlocked.Exchange(ref donor.SplitRequested, 1);
                    }
                }

                if (piece is null)
                {
                    await Task.Delay(40, allStop.Token);
                    continue;
                }

                try { await ProcessPiece(piece); }
                catch
                {
                    allStop.Cancel();
                    throw;
                }
                finally
                {
                    lock (sync)
                    {
                        Interlocked.Exchange(ref piece.Active, 0);
                        activeCount--;
                    }
                }
            }
        }

        try
        {
            await Task.WhenAll(Enumerable.Range(0, saved.Connections).Select(_ => WorkerLoop()));
            List<Piece> verified;
            lock (sync) verified = piecesList.OrderBy(p => p.From).ToList();
            long next = 0;
            foreach (var piece in verified)
            {
                if (piece.From != next || piece.Downloaded != piece.To - piece.From + 1 ||
                    !File.Exists(PartPath(piece)) ||
                    new FileInfo(PartPath(piece)).Length != piece.Downloaded)
                    throw new InvalidDataException("Adaptive pieces have a gap, overlap, or incomplete file.");
                next = checked(piece.To + 1);
            }
            if (next != saved.Length) throw new InvalidDataException("Adaptive segments do not cover the full file.");

            string assembling = entry.Destination + ".rdassembling";
            await using (var output = new FileStream(assembling, FileMode.Create,
                FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                foreach (var piece in verified)
                {
                    await using var input = new FileStream(PartPath(piece), FileMode.Open,
                        FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, token);
                }
                await output.FlushAsync(token);
                output.Flush(true);
            }
            if (new FileInfo(assembling).Length != saved.Length)
                throw new InvalidDataException("Assembled adaptive file has incorrect length.");
            token.ThrowIfCancellationRequested();
            await Sha256Verification.VerifyAsync(assembling, entry.ExpectedSha256, token);
            File.Move(assembling, entry.Destination, false);
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            progress(saved.Length, saved.Length, 0, 0);
            workerProgress?.Invoke(verified.Select(p => new WorkerMetric(
                p.Id, p.Downloaded, p.To - p.From + 1, 0, false)).ToArray());
            return true;
        }
        finally
        {
            timerStop.Cancel();
            await timer;
        }

        string PartPath(Piece piece) => Path.Combine(folder, $"{piece.Id:D4}.part");
    }

    private static void ValidateManifest(Manifest saved)
    {
        if (saved.Pieces.Count is < 2 or > MaximumPieces)
            throw new InvalidDataException("Adaptive manifest has an invalid piece count.");
        if (saved.Pieces.Select(p => p.Id).Distinct().Count() != saved.Pieces.Count ||
            saved.Pieces.Any(p => p.Id < 0 || p.Id >= MaximumPieces))
            throw new InvalidDataException("Adaptive piece IDs are invalid.");
        long covered = 0;
        foreach (var p in saved.Pieces.OrderBy(p => p.From))
        {
            if (p.From != covered || p.To < p.From || p.To >= saved.Length)
                throw new InvalidDataException("Adaptive manifest contains a gap or overlapping ranges.");
            covered = checked(p.To + 1);
        }
        if (covered != saved.Length) throw new InvalidDataException("Adaptive manifest lacks end of file.");
    }

    private async Task<HttpResponseMessage> SendRangeAsync(string url, long from,
        long to, string? etag, CancellationToken token)
    {
        for (int attempt = 1; attempt <= HttpRetryPolicy.MaximumAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Range", etag);
            HttpResponseMessage response;
            try
            {
                response = await TransferInactivityTimeout.SendAsync(http, request, token, networkIdleTimeout);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TimeoutException) &&
                attempt < HttpRetryPolicy.MaximumAttempts && !token.IsCancellationRequested)
            {
                await Task.Delay(HttpRetryPolicy.Delay(attempt, null), token);
                continue;
            }
            if (HttpRetryPolicy.IsTemporary(response.StatusCode) &&
                attempt < HttpRetryPolicy.MaximumAttempts)
            {
                var backoff = HttpRetryPolicy.Delay(attempt, response.Headers.RetryAfter);
                response.Dispose();
                await Task.Delay(backoff, token);
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Adaptive download exhausted retry attempts.");
    }
}
