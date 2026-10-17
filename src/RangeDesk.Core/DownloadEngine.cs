using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Independent HTTP/HTTPS transfer implementation with validated optional parallel
/// segments, single-stream fallback, and protected local partial files.
/// </summary>
public sealed class DownloadEngine : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _networkIdleTimeout;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<Guid, DownloadStatus> _stopIntent = new();
    private readonly ConcurrentDictionary<Guid, SharedBandwidthLimiter> _limiters = new();
    private readonly ConcurrentDictionary<string, Guid> _claimedPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizationContext? _ui;

    private sealed record PartialMetadata(string Url, long? TotalBytes, string? StrongETag);
    public DownloadEngine(SynchronizationContext? ui = null, HttpClient? client = null, int maxConcurrentDownloads = 2, string? proxyUri = null, TimeSpan? networkIdleTimeout = null)
    {
        if (maxConcurrentDownloads is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentDownloads));
        _slots = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);
        _networkIdleTimeout = TransferInactivityTimeout.Validate(networkIdleTimeout);
        _ui = ui;
        if (client is not null && !string.IsNullOrWhiteSpace(proxyUri))
            throw new ArgumentException("An explicitly supplied HTTP client already owns its proxy configuration.");
        if (client is not null) _http = client;
        else
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                MaxConnectionsPerServer = 40
            };
            if (!string.IsNullOrWhiteSpace(proxyUri))
            {
                string validProxy = new RangeDeskPreferences { ProxyUri = proxyUri }.Validate().ProxyUri!;
                handler.Proxy = new WebProxy(validProxy);
                handler.UseProxy = true;
            }
            _http = new HttpClient(handler);
        }
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public static bool IsAllowedUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            && u.UserInfo.Length == 0
            && u.Host.Length > 0;
    }

    public DownloadEntry Create(string url, string destination, string category = "Other", string labels = "", int connections = 1, int bandwidthKiBps = 0, string? expectedSha256 = null, bool adaptive = false, SafePostDownloadAction postDownloadAction = SafePostDownloadAction.None, bool automaticConnections = false)
    {
        if (!IsAllowedUrl(url)) throw new ArgumentException("Enter a valid HTTP or HTTPS download URL without credentials.");
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
            throw new ArgumentException("Choose a complete local filename for the download.");
        return new DownloadEntry(url, Path.GetFullPath(destination), category, labels, connections, bandwidthKiBps, expectedSha256, adaptive, postDownloadAction, automaticConnections);
    }

    public bool Start(DownloadEntry entry)
    {
        if (entry.Status is DownloadStatus.Downloading or DownloadStatus.Completed) return false;
        var cts = new CancellationTokenSource();
        if (!_claimedPaths.TryAdd(entry.Destination, entry.Id))
        {
            cts.Dispose();
            return false; // never allow two writers for the same path
        }
        if (!_active.TryAdd(entry.Id, cts))
        {
            _claimedPaths.TryRemove(entry.Destination, out _);
            cts.Dispose();
            return false;
        }
        _stopIntent.TryRemove(entry.Id, out _);
        _ = Task.Run(() => TransferAsync(entry, cts));
        return true;
    }

    public async ValueTask<bool> SetLiveBandwidthLimitAsync(DownloadEntry entry,
        int limitKiBps, CancellationToken cancellationToken = default)
    {
        if (!_limiters.TryGetValue(entry.Id, out var limiter)) return false;
        await limiter.SetLimitAsync(limitKiBps, cancellationToken);
        return true;
    }

    public void Pause(DownloadEntry entry) => Stop(entry, DownloadStatus.Paused);
    public void Cancel(DownloadEntry entry) => Stop(entry, DownloadStatus.Canceled);

    private void Stop(DownloadEntry entry, DownloadStatus state)
    {
        if (!_active.TryGetValue(entry.Id, out var cts))
        {
            if (entry.Status is DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Failed)
                Publish(entry, state, entry.Received, entry.Total);
            return;
        }
        _stopIntent[entry.Id] = state;
        cts.Cancel();
    }

    private void Publish(DownloadEntry e, DownloadStatus status, long received, long? total,
        double rate = 0, int connections = 0, string? error = null)
    {
        void Action() => e.Report(status, received, total, rate, connections, error);
        if (_ui is not null) _ui.Post(_ => Action(), null);
        else Action();
    }

    private async Task TransferAsync(DownloadEntry e, CancellationTokenSource cts)
    {
        var token = cts.Token;
        string partial = e.Destination + ".rdpartial";
        string journal = e.Destination + ".rdstate";
        long received = 0;
        long? expected = null;
        bool slotTaken = false;
        void SegmentProgress(long bytes, long total, double speed, int workers)
        {
            received = bytes;
            expected = total;
            Publish(e, DownloadStatus.Downloading, bytes, total, speed, workers);
        }

        // Use the durable piece lengths when an interruption occurs before the next
        // telemetry tick. A paused job must not jump back to zero in the UI.
        void RestoreSegmentProgress()
        {
            try
            {
                if (DownloadCheckpointInspector.Read(e) is { } checkpoint)
                {
                    received = checkpoint.Received;
                    expected = checkpoint.Total;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                // The transfer already handles invalid checkpoints as failures.
                // Do not conceal its original exception with a second inspection error.
            }
        }

        try
        {
            await _slots.WaitAsync(token);
            slotTaken = true;
            token.ThrowIfCancellationRequested();

            if (File.Exists(e.Destination))
                throw new IOException("The destination already exists. Choose a different filename; existing files are never overwritten.");

            var limiter = new SharedBandwidthLimiter(e.BandwidthKiBps);
            if (!_limiters.TryAdd(e.Id, limiter))
                throw new InvalidOperationException("Download already has an active limiter.");
            if (HlsPlaylist.IsHlsUrl(e.Url))
            {
                if (File.Exists(partial) || File.Exists(journal) ||
                    Directory.Exists(e.Destination + ".rdsegments") ||
                    Directory.Exists(e.Destination + ".rdadaptive"))
                    throw new InvalidDataException("A different download checkpoint already exists at this destination; it was preserved.");
                var hls = new HlsVodTransfer(_http, _networkIdleTimeout);
                long complete = await hls.DownloadAsync(e, limiter, (bytes, total, speed, workers) =>
                {
                    received = bytes;
                    expected = total;
                    Publish(e, DownloadStatus.Downloading, bytes, total, speed, workers);
                }, token);
                Publish(e, DownloadStatus.Completed, complete, complete);
                return;
            }
            if (File.Exists(e.Destination + ".rdhls-state") ||
                File.Exists(e.Destination + ".rdhls-partial"))
                throw new InvalidDataException("An HLS checkpoint exists. Resume its original HLS playlist rather than replacing the saved media.");
            if (!e.Adaptive && Directory.Exists(e.Destination + ".rdadaptive"))
                throw new InvalidDataException("Adaptive checkpoint exists. Reopen this transfer with adaptive mode to resume safely.");
            if (e.Adaptive && e.RequestedConnections > 1)
            {
                var adaptive = new AdaptiveRangeTransfer(_http, _networkIdleTimeout);
                bool completed = await adaptive.TryDownloadAsync(e, limiter,
                    SegmentProgress,
                    metrics =>
                    {
                        void Update() => e.ReportWorkers(metrics);
                        if (_ui is not null) _ui.Post(_ => Update(), null);
                        else Update();
                    }, token);
                if (completed)
                {
                    var assembledLength = new FileInfo(e.Destination).Length;
                    Publish(e, DownloadStatus.Completed, assembledLength, assembledLength);
                    return;
                }
            }
            // An adaptive probe already established whether parallel mode is viable.
            // Avoid probing again unless we must resume a legacy v1 segment journal.
            if (e.RequestedConnections > 1 &&
                (!e.Adaptive || Directory.Exists(e.Destination + ".rdsegments")))
            {
                var parallel = new ParallelRangeTransfer(_http, _networkIdleTimeout);
                bool completed = await parallel.TryDownloadAsync(e, e.RequestedConnections, limiter,
                    SegmentProgress,
                    token,
                    metrics =>
                    {
                        void Update() => e.ReportWorkers(metrics);
                        if (_ui is not null) _ui.Post(_ => Update(), null);
                        else Update();
                    });
                if (completed)
                {
                    var assembledLength = new FileInfo(e.Destination).Length;
                    Publish(e, DownloadStatus.Completed, assembledLength, assembledLength);
                    return;
                }
            }

            for (int attempt = 1; ; attempt++)
            {
            RetryConditionHeaderValue? retryAfter = null;
            try
            {
            // Every request starts with a fresh reading of the durable state.
            // Without a strong validator, a retry must re-download from byte zero.
            received = 0;
            expected = null;
            string? validator = null;
            if (File.Exists(partial) && File.Exists(journal))
            {
                try
                {
                    var previous = JsonSerializer.Deserialize<PartialMetadata>(await File.ReadAllTextAsync(journal, token));
                    if (previous is not null && previous.Url == e.Url &&
                        !string.IsNullOrWhiteSpace(previous.StrongETag))
                    {
                        validator = previous.StrongETag;
                        expected = previous.TotalBytes;
                        received = new FileInfo(partial).Length;
                        if (expected is long total && received > total)
                        {
                            validator = null;
                            expected = null;
                            received = 0;
                        }
                    }
                }
                catch (JsonException) { received = 0; validator = null; }
                catch (IOException) { received = 0; validator = null; }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, e.Url);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (received > 0 && validator is not null)
            {
                request.Headers.Range = new RangeHeaderValue(received, null);
                request.Headers.TryAddWithoutValidation("If-Range", validator);
            }

            Publish(e, DownloadStatus.Downloading, received, expected);
            using var response = await TransferInactivityTimeout.SendAsync(_http, request, token, _networkIdleTimeout);
            retryAfter = response.Headers.RetryAfter;

            bool resuming = received > 0 && validator is not null;
            if (resuming && response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range is null || !range.HasLength || range.From != received ||
                    range.To is null || range.Length < received ||
                    (expected is long known && known != range.Length))
                    throw new InvalidDataException("The server returned an incompatible byte range. The partial download was preserved.");
                if (response.Headers.ETag?.ToString() != validator)
                    throw new InvalidDataException("The server file changed. The old partial download was preserved.");
                expected = range.Length;
            }
            else if (resuming && response.StatusCode == HttpStatusCode.OK)
            {
                received = 0; // server ignored Range; overwrite only our private partial file
                expected = response.Content.Headers.ContentLength;
                validator = null;
            }
            else if (resuming)
                throw new HttpRequestException(
                    $"Cannot safely resume: server returned {(int)response.StatusCode}.",
                    null, response.StatusCode);

            response.EnsureSuccessStatusCode();
            if (!resuming && response.StatusCode != HttpStatusCode.OK)
                throw new InvalidDataException("New transfers require a complete HTTP 200 response.");

            if (!resuming || response.StatusCode == HttpStatusCode.OK)
            {
                expected = response.Content.Headers.ContentLength;
                validator = response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() : null;
            }

            var directory = Path.GetDirectoryName(e.Destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            // Metadata is written before receiving bytes, so an interrupted download can be validated later.
            string metadata = JsonSerializer.Serialize(new PartialMetadata(e.Url, expected, validator));
            string tempJournal = journal + ".tmp";
            await File.WriteAllTextAsync(tempJournal, metadata, token);
            File.Move(tempJournal, journal, true);

            await using (var output = new FileStream(partial,
                resuming && response.StatusCode == HttpStatusCode.PartialContent ? FileMode.Open : FileMode.Create,
                FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                if (received > 0) output.Seek(received, SeekOrigin.Begin);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                byte[] buffer = new byte[131072];
                // Network receive rate is independent of checkpoint flushes.
                // A 500 ms timer samples even during stalled reads, while an
                // 8 second rolling window smooths normal TCP bursts.
                long measuredReceived = received;
                using var telemetryStop = new CancellationTokenSource();
                var estimator = new DownloadRateEstimator();
                long startingBytes = received;
                var telemetryWatch = Stopwatch.StartNew();
                estimator.Update(TimeSpan.Zero, 0);
                var telemetry = Task.Run(async () =>
                {
                    using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
                    try
                    {
                        while (await ticks.WaitForNextTickAsync(telemetryStop.Token))
                        {
                            var observed = Math.Max(0, Interlocked.Read(ref measuredReceived) - startingBytes);
                            double rollingRate = estimator.Update(telemetryWatch.Elapsed, observed);
                            Publish(e, DownloadStatus.Downloading,
                                Interlocked.Read(ref measuredReceived), expected, rollingRate, 1);
                        }
                    }
                    catch (OperationCanceledException) when (telemetryStop.IsCancellationRequested) { }
                });
                try
                {
                while (true)
                {
                    int count = await TransferInactivityTimeout.ReadAsync(input, buffer, token, _networkIdleTimeout);
                    if (count == 0) break;
                    if (expected is long max && received + count > max)
                        throw new InvalidDataException("The server sent more bytes than declared.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    await limiter.AccountAsync(count, token);
                    received += count;
                    Interlocked.Exchange(ref measuredReceived, received);
                }
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
                }
                finally
                {
                    telemetryStop.Cancel();
                    await telemetry;
                }
            }

            if (expected is long length && received != length)
                throw new EndOfStreamException($"Download ended early ({received} of {length} bytes). Partial data was preserved.");
            token.ThrowIfCancellationRequested();
            await Sha256Verification.VerifyAsync(partial, e.ExpectedSha256, token);
            File.Move(partial, e.Destination, false);
            if (File.Exists(journal)) File.Delete(journal);
            Publish(e, DownloadStatus.Completed, received, expected ?? received);
            break;
            }
            catch (Exception ex) when (attempt < HttpRetryPolicy.MaximumAttempts &&
                !token.IsCancellationRequested &&
                (ex is TimeoutException or EndOfStreamException or System.Net.Http.HttpIOException ||
                 ex is HttpRequestException httpError &&
                 (httpError.StatusCode is null || HttpRetryPolicy.IsTemporary(httpError.StatusCode.Value))))
            {
                // Retry only recoverable transport failures. Each subsequent attempt
                // reopens the on-disk journal and resumes with If-Range, or safely
                // restarts the private partial if no strong validator exists.
                await Task.Delay(HttpRetryPolicy.Delay(attempt, retryAfter), token);
            }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            RestoreSegmentProgress();
            var wanted = _stopIntent.TryGetValue(e.Id, out var requested) ? requested : DownloadStatus.Paused;
            Publish(e, wanted, received, expected);
        }
        catch (Exception ex)
        {
            RestoreSegmentProgress();
            Publish(e, DownloadStatus.Failed, received, expected, error: ex.Message);
        }
        finally
        {
            if (slotTaken) _slots.Release();
            _limiters.TryRemove(e.Id, out _);
            _active.TryRemove(e.Id, out _);
            _claimedPaths.TryRemove(e.Destination, out _);
            _stopIntent.TryRemove(e.Id, out _);
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var task in _active.Values) task.Cancel();
        _http.Dispose();
        // Semaphore disposal while downloads settle is intentionally avoided.
    }
}
