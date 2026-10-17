using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RangeDesk.Core;

/// <summary>
/// Opt-in, entirely local Windows CI network laboratory. This exercises the
/// production DownloadEngine over real loopback TCP sockets and validates every
/// result against an immutable, known SHA-256. Simulated connection resets are
/// NOT the same as IP packet loss; do not present the results as ISP benchmarks.
/// </summary>
public static class V7NetworkBenchmark
{
    private const int MiB = 1024 * 1024;
    private static readonly int[] FullMatrix = [1, 2, 4, 8, 16, 32];
    private static readonly int[] RecoveryMatrix = [1, 8, 32];

    private sealed record Scenario(
        string Id, int SizeMiB, int ChunkDelayMs, int HeaderDelayMs = 0,
        int JitterMs = 0, bool AggregateCap = false, bool DropOnce = false);

    private sealed record Measurement(
        string Scenario, int RequestedConnections, int ObservedWorkers,
        int ObservedRangeRequests, int DroppedResponses, int Pauses,
        long Bytes, double Seconds, double ThroughputMiBs, string Sha256);

    public static async Task RunAsync(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        string scratch = Path.Combine(Path.GetTempPath(),
            "ragedesk-v7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var results = new List<Measurement>();
        string reportPath = Path.Combine(output, "v7-results.json");
        var scenarios = new[]
        {
            new Scenario("per_connection_throttle", 16, 20),
            new Scenario("aggregate_bandwidth_cap", 16, 20, AggregateCap: true),
            new Scenario("high_latency_and_jitter", 16, 9, HeaderDelayMs: 140, JitterMs: 35),
            new Scenario("abrupt_tcp_disconnect", 16, 6, DropOnce: true)
        };
        try
        {
            foreach (var scenario in scenarios)
            {
                int[] counts = scenario.DropOnce ? RecoveryMatrix : FullMatrix;
                foreach (int count in counts)
                {
                    var result = await MeasureAsync(scenario, count, scratch);
                    results.Add(result);
                    Console.WriteLine("V7_NETWORK " + JsonSerializer.Serialize(result));
                    await WriteReportAsync(reportPath, results, complete: false);
                }
            }

            foreach (int count in RecoveryMatrix)
            {
                var result = await PauseAndResumeAsync(
                    new Scenario("pause_then_resume_new_engine", 16, 22), count, scratch);
                results.Add(result);
                Console.WriteLine("V7_NETWORK " + JsonSerializer.Serialize(result));
                await WriteReportAsync(reportPath, results, complete: false);
            }

            if (results.Count != 24)
                throw new InvalidDataException("The V7 test matrix is incomplete.");
            await WriteReportAsync(reportPath, results, complete: true);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task<Measurement> MeasureAsync(
        Scenario scenario, int count, string scratch)
    {
        byte[] body = Body(scenario.SizeMiB);
        string hash = Convert.ToHexString(SHA256.HashData(body));
        await using var server = new RangeLabServer(body, scenario);
        string destination = Path.Combine(scratch,
            scenario.Id + "-" + count + ".bin");
        using var engine = new DownloadEngine();
        var item = engine.Create(server.Url, destination, connections: count);
        var watch = Stopwatch.StartNew();
        if (!engine.Start(item)) throw new InvalidOperationException("Start was rejected.");
        try
        {
            await AwaitStatusAsync(item, watch, TimeSpan.FromSeconds(90));
            ValidateCompleted(item, destination, body.Length, hash);
            // The engine has a 512 KiB minimum per segment. The 16 MiB
            // body is deliberately large enough for all 32 real workers.
            if (count > 1 && item.WorkerCount != count)
                throw new InvalidDataException(
                    "The benchmark did not engage the requested worker count.");
            if (count > 1 && server.RangeRequests < count + 1)
                throw new InvalidDataException(
                    "Expected actual parallel HTTP 206 requests, not a silent fallback.");
            if (scenario.DropOnce && server.DroppedResponses != 1)
                throw new InvalidDataException(
                    "The disconnect scenario never triggered exactly one TCP reset.");
        }
        finally
        {
            if (item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed))
                engine.Pause(item);
        }
        watch.Stop();
        long length = new FileInfo(destination).Length;
        return new Measurement(scenario.Id, count, item.WorkerCount,
            server.RangeRequests, server.DroppedResponses, 0,
            length, Math.Round(watch.Elapsed.TotalSeconds, 3),
            Math.Round(length / (double)MiB /
                Math.Max(watch.Elapsed.TotalSeconds, 0.001), 3), hash);
    }

    private static async Task<Measurement> PauseAndResumeAsync(
        Scenario scenario, int count, string scratch)
    {
        byte[] body = Body(scenario.SizeMiB);
        string hash = Convert.ToHexString(SHA256.HashData(body));
        await using var server = new RangeLabServer(body, scenario);
        string destination = Path.Combine(scratch,
            scenario.Id + "-" + count + ".bin");
        var watch = Stopwatch.StartNew();
        using (var firstEngine = new DownloadEngine())
        {
            var entry = firstEngine.Create(server.Url, destination, connections: count);
            if (!firstEngine.Start(entry))
                throw new InvalidOperationException("First start was rejected.");
            try
            {
                // UI/worker progress can tick after the transfer has already
                // finished on localhost (especially with 32 workers). Pause
                // when the server has emitted 1 MiB instead: this forces a
                // genuinely partial checkpoint, regardless of telemetry lag.
                while (server.PayloadBytesSent < MiB &&
                       entry.Status is not (DownloadStatus.Failed or DownloadStatus.Completed) &&
                       watch.Elapsed < TimeSpan.FromSeconds(20))
                    await Task.Delay(5);
                if (server.PayloadBytesSent < MiB ||
                    entry.Status is DownloadStatus.Failed or DownloadStatus.Completed)
                    throw new InvalidDataException("Could not interrupt an active partial transfer.");
                firstEngine.Pause(entry);
                await AwaitPausedAsync(entry);
                if (entry.Received == 0)
                    throw new InvalidDataException("Interrupted transfer saved no progress.");
                if (count > 1 &&
                    DownloadCheckpointInspector.Read(entry) is not { Received: > 0 })
                    throw new InvalidDataException("Segment checkpoint was not durable after pausing.");
                if (count == 1 &&
                    (!File.Exists(destination + ".rdpartial") ||
                     !File.Exists(destination + ".rdstate")))
                    throw new InvalidDataException("Single-stream pause lost its private journal.");
            }
            finally
            {
                if (entry.Status == DownloadStatus.Downloading)
                    firstEngine.Pause(entry);
            }
        }
        // Count only the requests generated by the new engine. The original
        // pause can occur before all 32 initial workers have even connected;
        // requiring (32+2) requests across both phases is not a valid assertion.
        int requestsBeforeResume = server.RangeRequests;
        // Resume from a new engine instance: this is a process-restart analogue,
        // not a complete Windows process-kill or disk-power-loss simulation.
        using (var resumedEngine = new DownloadEngine())
        {
            var entry = resumedEngine.Create(server.Url, destination, connections: count);
            if (!resumedEngine.Start(entry))
                throw new InvalidOperationException("Restart was rejected.");
            try
            {
                await AwaitStatusAsync(entry, watch, TimeSpan.FromSeconds(90));
                ValidateCompleted(entry, destination, body.Length, hash);
                if (server.RangeRequests <= requestsBeforeResume)
                    throw new InvalidDataException("The resumed engine issued no HTTP range requests.");
            }
            finally
            {
                if (entry.Status is not (DownloadStatus.Completed or DownloadStatus.Failed))
                    resumedEngine.Pause(entry);
            }
            watch.Stop();
            long length = new FileInfo(destination).Length;
            return new Measurement(scenario.Id, count, entry.WorkerCount,
                server.RangeRequests, server.DroppedResponses, 1,
                length, Math.Round(watch.Elapsed.TotalSeconds, 3),
                Math.Round(length / (double)MiB /
                    Math.Max(watch.Elapsed.TotalSeconds, 0.001), 3), hash);
        }
    }

    private static async Task AwaitPausedAsync(DownloadEntry entry)
    {
        var clock = Stopwatch.StartNew();
        while (entry.Status != DownloadStatus.Paused &&
               entry.Status != DownloadStatus.Failed &&
               clock.Elapsed < TimeSpan.FromSeconds(12))
            await Task.Delay(10);
        if (entry.Status != DownloadStatus.Paused)
            throw new InvalidDataException("Pausing did not reach a durable checkpoint.");
        // Let the engine release its file handles before another process opens them.
        await Task.Delay(120);
    }

    private static async Task AwaitStatusAsync(
        DownloadEntry item, Stopwatch clock, TimeSpan timeout)
    {
        while (item.Status is not (DownloadStatus.Completed or
                   DownloadStatus.Failed or DownloadStatus.Canceled) &&
               clock.Elapsed < timeout)
            await Task.Delay(30);
        if (item.Status != DownloadStatus.Completed)
            throw new InvalidDataException(
                "V7 download did not complete: " + item.Status +
                "; safe local-loopback diagnostic: " +
                (item.Error is null ? "none" :
                 item.Error[..Math.Min(180, item.Error.Length)]));
    }

    private static void ValidateCompleted(
        DownloadEntry item, string path, int expectedLength, string hash)
    {
        if (item.Status != DownloadStatus.Completed || !File.Exists(path))
            throw new InvalidDataException("No verified output was published.");
        if (new FileInfo(path).Length != expectedLength ||
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Output bytes or SHA-256 mismatch.");
        if (File.Exists(path + ".rdpartial") ||
            Directory.Exists(path + ".rdsegments") ||
            Directory.Exists(path + ".rdadaptive"))
            throw new InvalidDataException("Successful transfer left an untrusted partial.");
    }

    private static byte[] Body(int mebibytes)
    {
        byte[] body = new byte[mebibytes * MiB];
        for (int i = 0; i < body.Length; i++)
            body[i] = (byte)((i * 131 + 17) & 255);
        return body;
    }

    private static Task WriteReportAsync(
        string path, IReadOnlyList<Measurement> results, bool complete)
    {
        var report = new
        {
            title = "RageDesk V7 controlled TCP test matrix",
            measuredUtc = DateTimeOffset.UtcNow,
            complete,
            requestedCounts = FullMatrix,
            expectedCases = 24,
            finishedCases = results.Count,
            server = "real HTTP/1.1 TCP on 127.0.0.1, immutable strong ETag, verified byte ranges",
            scenarios = new[]
            {
                "Per-connection 32KiB/20ms cap",
                "One aggregate server 32KiB/20ms cap shared across responses",
                "140ms HTTP response latency plus deterministic chunk jitter",
                "One deliberate TCP disconnect, bounded retry and integrity verification",
                "Pause and resume using a newly created download engine"
            },
            limitations = "Windows hosted runner, not user's ISP or actual website. " +
                "Chunk delays approximate throttling and latency, not kernel TCP congestion or IP packet loss. " +
                "TCP disconnect is an application-layer fault, not lost IP packets. " +
                "Engine recreation does not simulate total power loss. Speeds are not IDM comparisons.",
            runs = results
        };
        return File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class RangeLabServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _aggregate = new(1, 1);
        private readonly Task _accept;
        private readonly byte[] _bytes;
        private readonly Scenario _scenario;
        private int _rangeRequests, _dropped;
        private long _payloadBytesSent;
        // Network bytes sent, not a claim about durable client-side progress.
        public long PayloadBytesSent => Interlocked.Read(ref _payloadBytesSent);
        public int RangeRequests => Volatile.Read(ref _rangeRequests);
        public int DroppedResponses => Volatile.Read(ref _dropped);
        public string Url { get; }

        public RangeLabServer(byte[] body, Scenario scenario)
        {
            _bytes = body;
            _scenario = scenario;
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/v7-test.bin";
            _accept = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Task.Run(async () =>
                    {
                        try { await HandleAsync(client, _stop.Token); }
                        catch (Exception ex) when (ex is IOException or SocketException or
                                                   OperationCanceledException or ObjectDisposedException) { }
                        finally { client.Dispose(); }
                    });
                }
            }
            catch (Exception ex) when (_stop.IsCancellationRequested &&
                                       ex is OperationCanceledException or SocketException) { }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken cancel)
        {
            await using var network = client.GetStream();
            using var reader = new StreamReader(
                network, Encoding.ASCII, false, 4096, leaveOpen: true);
            string? startLine = await reader.ReadLineAsync(cancel);
            if (startLine is null) return;
            bool head = startLine.StartsWith("HEAD ", StringComparison.Ordinal);
            if (!head && !startLine.StartsWith("GET ", StringComparison.Ordinal)) return;
            long first = 0, last = _bytes.Length - 1;
            string? headerRange = null;
            while (true)
            {
                var line = await reader.ReadLineAsync(cancel);
                if (string.IsNullOrEmpty(line)) break;
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    headerRange = line["Range:".Length..].Trim();
            }
            bool range = false;
            if (headerRange is not null)
            {
                if (!headerRange.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                    return;
                string[] pieces = headerRange[6..].Split('-', 2);
                // Standard HTTP resume sends "Range: bytes=N-" (open-ended),
                // while the parallel workers request "Range: bytes=N-M".
                // Reject neither valid form or the test server itself will
                // manufacture a fake transfer failure on every retry.
                if (pieces.Length != 2 || !long.TryParse(pieces[0], out first))
                    return;
                if (pieces[1].Length == 0)
                    last = _bytes.Length - 1;
                else if (!long.TryParse(pieces[1], out last))
                    return;
                if (first < 0 || last < first || last >= _bytes.Length) return;
                range = true;
                Interlocked.Increment(ref _rangeRequests);
            }

            if (_scenario.HeaderDelayMs > 0)
                await Task.Delay(_scenario.HeaderDelayMs, cancel);

            long size = last - first + 1;
            string headers = (range
                ? "HTTP/1.1 206 Partial Content\r\n"
                : "HTTP/1.1 200 OK\r\n") +
                "Content-Type: application/octet-stream\r\n" +
                "Accept-Ranges: bytes\r\n" +
                "ETag: \"ragedesk-v7-immutable\"\r\n" +
                (range ? $"Content-Range: bytes {first}-{last}/{_bytes.Length}\r\n" : "") +
                $"Content-Length: {size}\r\nConnection: close\r\n\r\n";
            await network.WriteAsync(Encoding.ASCII.GetBytes(headers), cancel);
            if (head) return;
            int sent = 0, chunkIndex = 0;
            for (long at = first; at <= last;)
            {
                int length = checked((int)Math.Min(32768, last - at + 1));
                if (_scenario.AggregateCap)
                {
                    await _aggregate.WaitAsync(cancel);
                    try
                    {
                        await network.WriteAsync(_bytes.AsMemory((int)at, length), cancel);
                        Interlocked.Add(ref _payloadBytesSent, length);
                        await Task.Delay(_scenario.ChunkDelayMs, cancel);
                    }
                    finally { _aggregate.Release(); }
                }
                else
                {
                    await network.WriteAsync(_bytes.AsMemory((int)at, length), cancel);
                    Interlocked.Add(ref _payloadBytesSent, length);
                    if (_scenario.ChunkDelayMs > 0)
                        await Task.Delay(_scenario.ChunkDelayMs, cancel);
                }
                at += length;
                sent += length;
                chunkIndex++;
                if (_scenario.JitterMs > 0 && chunkIndex % 4 == 0)
                    await Task.Delay(_scenario.JitterMs, cancel);
                // Never drop the mandatory byte-range probe; only a real data
                // transfer is deliberately truncated once per test server.
                if (_scenario.DropOnce && size > 65536 &&
                    sent >= 98304 && Interlocked.CompareExchange(ref _dropped, 1, 0) == 0)
                {
                    client.Client.LingerState = new LingerOption(true, 0);
                    client.Close();
                    return;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _accept; }
            catch (ObjectDisposedException) { }
            _aggregate.Dispose();
            _stop.Dispose();
        }
    }
}
