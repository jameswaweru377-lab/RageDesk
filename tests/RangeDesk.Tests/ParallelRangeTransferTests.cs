using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class ParallelRangeTransferTests
{
    private static readonly byte[] Source = Enumerable.Range(0, 3_400_000)
        .Select(i => (byte)((i * 73 + 17) % 251)).ToArray();

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task ParallelTransferAssemblesExactBytesAndRemovesSegmentJournal(int workers)
    {
        await InFolder(async folder =>
        {
            var ranges = new ConcurrentBag<(long From, long To)>();
            using var handler = new RangeHandler(request => ValidResponse(request, ranges));
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var download = engine.Create("https://example.test/archive.iso",
                Path.Combine(folder, "archive.iso"), connections: workers);
            Assert.True(engine.Start(download));
            await Wait(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
            Assert.Equal(Source.Length, download.Received);
            Assert.Equal(SHA256.HashData(Source),
                SHA256.HashData(await File.ReadAllBytesAsync(download.Destination)));
            Assert.False(Directory.Exists(download.Destination + ".rdsegments"));
            // Small files deliberately use fewer network workers than requested:
            // each assigned range needs enough bytes to justify its connection.
            int expectedWorkers = Math.Min(workers,
                (int)Math.Max(2, Source.LongLength / ParallelRangeTransfer.MinimumSegmentLength));
            Assert.True(ranges.Count >= expectedWorkers);
            Assert.Equal(0, ranges.Count(r => r.From < 0 || r.To >= Source.Length || r.To < r.From));
        });
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public async Task LargeFileActuallyUsesAllRequestedWorkers(int requested)
    {
        // Enough unique bytes for the minimum segment length * 32.
        byte[] content = Enumerable.Range(0, 18_000_000)
            .Select(i => (byte)((i * 31 + 7) % 253)).ToArray();
        await InFolder(async folder =>
        {
            var segments = new ConcurrentBag<(long Start, long End)>();
            using var handler = new RangeHandler(request =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                long start = range.From!.Value;
                long end = range.To ?? content.LongLength - 1;
                segments.Add((start, end));
                var reply = new HttpResponseMessage(HttpStatusCode.PartialContent);
                reply.Headers.ETag = EntityTagHeaderValue.Parse("\"test-v32\"");
                reply.Content = new ByteArrayContent(content[(int)start..((int)end + 1)]);
                reply.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, content.LongLength);
                return reply;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var item = engine.Create("https://example.test/large.iso",
                Path.Combine(folder, "large.iso"), connections: requested);
            Assert.True(engine.Start(item));
            await Wait(item);
            Assert.Equal(DownloadStatus.Completed, item.Status);
            Assert.Equal(content.Length, new FileInfo(item.Destination).Length);
            Assert.Equal(SHA256.HashData(content),
                SHA256.HashData(await File.ReadAllBytesAsync(item.Destination)));
            Assert.Equal(requested + 1, segments.Count); // one probe plus one request per worker
        });
    }

    [Fact]
    public async Task ExistingSegmentsResumeFromTheirOwnOffsetsAndCannotOverwriteOtherRanges()
    {
        await InFolder(async folder =>
        {
            string destination = Path.Combine(folder, "resumed.zip");
            string url = "https://example.test/resumed.zip";
            string stateDir = destination + ".rdsegments";
            Directory.CreateDirectory(stateDir);
            await File.WriteAllTextAsync(Path.Combine(stateDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    Url = url, Length = (long)Source.Length,
                    ETag = "\"stable-v1\"", Connections = 4
                }));
            var (start0, end0) = ParallelRangeTransfer.SegmentBounds(Source.Length, 4, 0);
            var (start1, end1) = ParallelRangeTransfer.SegmentBounds(Source.Length, 4, 1);
            await File.WriteAllBytesAsync(Path.Combine(stateDir, "00.part"),
                Source[(int)start0..((int)end0 + 1)]);
            await File.WriteAllBytesAsync(Path.Combine(stateDir, "01.part"),
                Source[(int)start1..((int)start1 + 9000)]);
            var requests = new ConcurrentBag<long>();
            using var handler = new RangeHandler(request =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                requests.Add(range.From!.Value);
                Assert.Equal("\"stable-v1\"", request.Headers.IfRange?.EntityTag?.ToString());
                return ValidResponse(request);
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var download = engine.Create(url, destination, connections: 4);
            engine.Start(download);
            await Wait(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
            Assert.Contains(start1 + 9000, requests);
            Assert.DoesNotContain(start0, requests); // the already complete first part is untouched
            Assert.Equal(Source, await File.ReadAllBytesAsync(destination));
            Assert.False(Directory.Exists(stateDir));
        });
    }

    [Fact]
    public async Task StrongValidatorMismatchDoesNotPublishIncompleteFile()
    {
        await InFolder(async folder =>
        {
            using var handler = new RangeHandler(request =>
            {
                var response = ValidResponse(request);
                var range = request.Headers.Range!.Ranges.Single();
                if (range.From != 0) response.Headers.ETag = EntityTagHeaderValue.Parse("\"changed-v2\"");
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var download = engine.Create("https://example.test/file.iso",
                Path.Combine(folder, "failed.iso"), connections: 4);
            engine.Start(download);
            await Wait(download);
            Assert.Equal(DownloadStatus.Failed, download.Status);
            Assert.False(File.Exists(download.Destination));
            Assert.True(Directory.Exists(download.Destination + ".rdsegments"));
        });
    }

    [Fact]
    public async Task MissingValidatorSafelyFallsBackToDirectStream()
    {
        await InFolder(async folder =>
        {
            int probes = 0, direct = 0;
            using var handler = new RangeHandler(request =>
            {
                if (request.Headers.Range is not null)
                {
                    Interlocked.Increment(ref probes);
                    var response = ValidResponse(request);
                    response.Headers.ETag = null;
                    return response;
                }
                Interlocked.Increment(ref direct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Source) };
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var download = engine.Create("https://example.test/unverifiable.bin",
                Path.Combine(folder, "unverifiable.bin"), connections: 8);
            engine.Start(download);
            await Wait(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
            Assert.Equal(1, probes);
            Assert.Equal(1, direct);
            Assert.Equal(Source, await File.ReadAllBytesAsync(download.Destination));
            Assert.False(Directory.Exists(download.Destination + ".rdsegments"));
        });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void FixedSegmentBoundsCoverFileExactlyWithoutOverlap(int connections)
    {
        const long total = 39_123_231;
        long expected = 0;
        for (int i = 0; i < connections; i++)
        {
            var (first, last) = ParallelRangeTransfer.SegmentBounds(total, connections, i);
            Assert.Equal(expected, first);
            Assert.True(last >= first);
            expected = last + 1;
        }
        Assert.Equal(total, expected);
    }

    [Fact]
    public async Task Temporary429Then206RetriesAndProducesExactOutput()
    {
        await InFolder(async folder =>
        {
            var requestCount = new ConcurrentDictionary<string, int>();
            using var handler = new RangeHandler(request =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                var key = $"{range.From}-{range.To}";
                int n = requestCount.AddOrUpdate(key, 1, (_, old) => old + 1);
                if (n == 1)
                {
                    var busy = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    busy.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                    return busy;
                }
                return ValidResponse(request);
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create("https://example.test/retry.iso",
                Path.Combine(folder, "retry.iso"), connections: 4);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.All(requestCount.Values, n => Assert.Equal(2, n));
            Assert.Equal(Source, await File.ReadAllBytesAsync(entry.Destination));
        });
    }

    [Fact]
    public async Task WorkerMetricsReflectTheActualRangeCounts()
    {
        await InFolder(async folder =>
        {
            using var handler = new RangeHandler(request => ValidResponse(request));
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create("https://example.test/metrics.iso",
                Path.Combine(folder, "metrics.iso"), connections: 4);
            entry.ReportWorkers(new[] { new WorkerMetric(0, 12, 100, 22, true) });
            Assert.Single(entry.Workers);
            Assert.Equal(12, entry.Workers[0].Received);
            Assert.Equal(12.0, entry.Workers[0].Percent);
            Assert.Equal("Downloading", entry.Workers[0].StateLabel);
            entry.ReportWorkers(new[] { new WorkerMetric(0, 100, 100, 0, false) });
            Assert.Equal("Completed", entry.Workers[0].StateLabel);
            Assert.Equal(100, entry.Workers[0].Percent);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(4, entry.Workers.Count);
            Assert.All(entry.Workers, w => Assert.Equal(100, w.Percent));
        });
    }

    private static HttpResponseMessage ValidResponse(HttpRequestMessage request,
        ConcurrentBag<(long From, long To)>? recorded = null)
    {
        var range = request.Headers.Range!.Ranges.Single();
        long first = range.From!.Value;
        long last = range.To ?? Source.LongLength - 1;
        recorded?.Add((first, last));
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Headers.ETag = EntityTagHeaderValue.Parse("\"stable-v1\"");
        response.Content = new ByteArrayContent(Source[(int)first..((int)last + 1)]);
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(first, last, Source.Length);
        return response;
    }

    private static async Task Wait(DownloadEntry entry)
    {
        for (int i = 0; i < 250; i++)
        {
            if (entry.Status is DownloadStatus.Completed or DownloadStatus.Failed) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Segment transfer did not complete.");
    }

    private static async Task InFolder(Func<string, Task> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RangeDeskParallel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { await action(folder); }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class RangeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(request));
    }
}
