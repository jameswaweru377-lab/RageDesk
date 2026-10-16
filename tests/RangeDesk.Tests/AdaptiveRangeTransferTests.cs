using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class AdaptiveRangeTransferTests
{
    private static readonly byte[] Content = Enumerable.Range(0, 12_000_000)
        .Select(i => (byte)((i * 47 + 13) % 251)).ToArray();

    [Fact]
    public async Task IdleWorkerSplitsSlowTailAndReassemblesExactFile()
    {
        await WithFolder(async folder =>
        {
            var requests = new ConcurrentBag<(long First, long Last)>();
            long delayedFrom = ParallelRangeTransfer.SegmentBounds(Content.Length, 4, 3).First;
            using var handler = new RangeHandler(async (req, token) =>
            {
                var range = req.Headers.Range!.Ranges.Single();
                long first = range.From!.Value;
                long last = range.To ?? Content.Length - 1;
                requests.Add((first, last));
                if (first == delayedFrom)
                    await Task.Delay(350, token); // other workers become idle first
                return Reply(req);
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create("https://example.test/large.bin",
                Path.Combine(folder, "large.bin"), connections: 4, adaptive: true);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(SHA256.HashData(Content),
                SHA256.HashData(await File.ReadAllBytesAsync(entry.Destination)));
            Assert.True(requests.Count > 5,
                $"Expected at least one donated child range beyond the 4 initial workers and one probe. Actual: {requests.Count}");
            Assert.False(Directory.Exists(entry.Destination + ".rdadaptive"));
            Assert.True(entry.Workers.Count > 4);
        });
    }

    [Fact]
    public async Task ExistingAdaptiveCheckpointResumesSuffixesWithoutErasingPrefixes()
    {
        await WithFolder(async folder =>
        {
            string destination = Path.Combine(folder, "resume.bin");
            string dir = destination + ".rdadaptive";
            Directory.CreateDirectory(dir);
            string url = "https://example.test/resume.bin";
            var pieces = Enumerable.Range(0, 4).Select(i =>
            {
                var (first, last) = ParallelRangeTransfer.SegmentBounds(Content.Length, 4, i);
                return new { Id = i, From = first, To = last };
            }).ToArray();
            await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    Url = url, Length = (long)Content.Length,
                    ETag = "\"stable-v2\"", Connections = 4, Pieces = pieces
                }));
            long prefix = 119_000;
            await File.WriteAllBytesAsync(Path.Combine(dir, "0000.part"), Content[..(int)prefix]);
            var seen = new ConcurrentBag<long>();
            using var handler = new RangeHandler((req, _) =>
            {
                seen.Add(req.Headers.Range!.Ranges.Single().From!.Value);
                return Task.FromResult(Reply(req));
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create(url, destination, connections: 4, adaptive: true);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Contains(prefix, seen);
            Assert.DoesNotContain(0L, seen); // manifest recovery avoids a fresh probe
            Assert.Equal(Content, await File.ReadAllBytesAsync(destination));
        });
    }

    [Fact]
    public async Task OverlappingCheckpointIsRejectedWithoutPublication()
    {
        await WithFolder(async folder =>
        {
            string destination = Path.Combine(folder, "invalid.bin");
            string dir = destination + ".rdadaptive";
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    Url = "https://example.test/invalid.bin", Length = 10L,
                    ETag = "\"etag\"", Connections = 2,
                    Pieces = new[]
                    {
                        new { Id = 0, From = 0L, To = 6L },
                        new { Id = 1, From = 6L, To = 9L }
                    }
                }));
            using var engine = new DownloadEngine(client: new HttpClient(new RangeHandler(
                (req, _) => Task.FromResult(Reply(req)))));
            var entry = engine.Create("https://example.test/invalid.bin", destination,
                connections: 2, adaptive: true);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.False(File.Exists(destination));
            Assert.True(Directory.Exists(dir));
        });
    }

    [Fact]
    public async Task UnsupportedAdaptiveServerProbesOnceAndUsesSafeDirectStream()
    {
        await WithFolder(async folder =>
        {
            int probes = 0, directs = 0;
            using var handler = new RangeHandler((request, _) =>
            {
                if (request.Headers.Range is not null) Interlocked.Increment(ref probes);
                else Interlocked.Increment(ref directs);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Content)
                });
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create("https://example.test/no-ranges.bin",
                Path.Combine(folder, "no-ranges.bin"), connections: 4, adaptive: true);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(1, probes);
            Assert.Equal(1, directs);
            Assert.Equal(Content, await File.ReadAllBytesAsync(entry.Destination));
        });
    }

    [Fact]
    public async Task InvalidChecksumDoesNotPublishAdaptiveFileOrDestroyPieces()
    {
        await WithFolder(async folder =>
        {
            using var handler = new RangeHandler((request, _) =>
                Task.FromResult(Reply(request)));
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create("https://example.test/checksum.bin",
                Path.Combine(folder, "checksum.bin"), connections: 4,
                expectedSha256: new string('0', 64), adaptive: true);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.False(File.Exists(entry.Destination));
            Assert.True(Directory.Exists(entry.Destination + ".rdadaptive"));
        });
    }

    private static HttpResponseMessage Reply(HttpRequestMessage request)
    {
        var range = request.Headers.Range!.Ranges.Single();
        long first = range.From!.Value;
        long last = range.To ?? Content.LongLength - 1;
        var result = new HttpResponseMessage(HttpStatusCode.PartialContent);
        result.Headers.ETag = EntityTagHeaderValue.Parse("\"stable-v2\"");
        result.Content = new ByteArrayContent(Content[(int)first..((int)last + 1)]);
        result.Content.Headers.ContentRange = new ContentRangeHeaderValue(first, last, Content.Length);
        return result;
    }

    private static async Task WithFolder(Func<string, Task> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RDAdaptive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { await action(folder); }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task Wait(DownloadEntry entry)
    {
        for (int i = 0; i < 300; i++)
        {
            if (entry.Status is DownloadStatus.Completed or DownloadStatus.Failed) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Adaptive work scheduler did not complete in time.");
    }

    private sealed class RangeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => respond(request, token);
    }
}
