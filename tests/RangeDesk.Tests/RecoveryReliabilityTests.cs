using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class RecoveryReliabilityTests
{
    private const string Url = "https://fixture.example.test/large.bin";

    [Fact]
    public async Task FixedSegmentCheckpointRestoresActualBytes()
    {
        await InFolder(async folder =>
        {
            var target = Path.Combine(folder, "fixed.bin");
            var entry = new DownloadEntry(Url, target, connections: 4);
            string saved = target + ".rdsegments";
            Directory.CreateDirectory(saved);
            await File.WriteAllTextAsync(Path.Combine(saved, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    Url, Length = 8_000_000L, ETag = "\"stable\"",
                    Connections = 4
                }));
            await File.WriteAllBytesAsync(Path.Combine(saved, "00.part"), new byte[400_000]);
            await File.WriteAllBytesAsync(Path.Combine(saved, "01.part"), new byte[700_000]);
            var progress = DownloadCheckpointInspector.Read(entry);
            Assert.True(progress.HasValue);
            Assert.Equal(1_100_000, progress.Value.Received);
            Assert.Equal(8_000_000, progress.Value.Total);
            Assert.False(File.Exists(target));
        });
    }

    [Fact]
    public async Task AdaptiveCheckpointRestoresPiecesAndRejectsOverlaps()
    {
        await InFolder(async folder =>
        {
            string target = Path.Combine(folder, "adaptive.bin");
            string saved = target + ".rdadaptive";
            Directory.CreateDirectory(saved);
            var entry = new DownloadEntry(Url, target, connections: 4, adaptive: true);
            async Task Save(long secondStart)
            {
                await File.WriteAllTextAsync(Path.Combine(saved, "manifest.json"),
                    JsonSerializer.Serialize(new
                    {
                        Url, Length = 8_000_000L, ETag = "\"stable\"",
                        Connections = 4,
                        Pieces = new[]
                        {
                            new { Id = 0, From = 0L, To = 3_999_999L },
                            new { Id = 1, From = secondStart, To = 7_999_999L }
                        }
                    }));
            }
            await Save(4_000_000);
            await File.WriteAllBytesAsync(Path.Combine(saved, "0000.part"), new byte[750_000]);
            await File.WriteAllBytesAsync(Path.Combine(saved, "0001.part"), new byte[500_000]);
            var good = DownloadCheckpointInspector.Read(entry);
            Assert.True(good.HasValue);
            Assert.Equal(1_250_000, good.Value.Received);
            Assert.Equal(8_000_000, good.Value.Total);
            await Save(3_999_999);
            Assert.Throws<InvalidDataException>(() => DownloadCheckpointInspector.Read(entry));
            Assert.True(File.Exists(Path.Combine(saved, "0000.part")));
        });
    }

    [Fact]
    public async Task CheckpointIdentityMismatchDoesNotInventProgress()
    {
        await InFolder(async folder =>
        {
            string target = Path.Combine(folder, "mismatch.bin");
            string saved = target + ".rdsegments";
            Directory.CreateDirectory(saved);
            await File.WriteAllTextAsync(Path.Combine(saved, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    Url = "https://other.example.test/file", Length = 8_000_000L,
                    ETag = "\"stable\"", Connections = 4
                }));
            var entry = new DownloadEntry(Url, target, connections: 4);
            Assert.Throws<InvalidDataException>(() => DownloadCheckpointInspector.Read(entry));
            Assert.True(Directory.Exists(saved));
        });
    }

    [Fact]
    public async Task DirectDownloadRetriesMissingSuffixWithoutRewritingSavedBytes()
    {
        await InFolder(async folder =>
        {
            byte[] content = Content(360_000);
            long? resumedFrom = null;
            int requests = 0;
            using var handler = new ScriptedHandler(request =>
            {
                int count = Interlocked.Increment(ref requests);
                if (count == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.ETag = EntityTagHeaderValue.Parse("\"direct-etag\"");
                    response.Content = new ByteArrayContent(content[..105_000]);
                    response.Content.Headers.ContentLength = content.Length;
                    return response;
                }

                var range = request.Headers.Range?.Ranges.Single();
                Assert.NotNull(range);
                resumedFrom = range.From;
                Assert.Equal("\"direct-etag\"", request.Headers.IfRange?.EntityTag?.ToString());
                var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent);
                resumed.Headers.ETag = EntityTagHeaderValue.Parse("\"direct-etag\"");
                resumed.Content = new ByteArrayContent(content[105_000..]);
                resumed.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(105_000, content.Length - 1, content.Length);
                return resumed;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create(Url, Path.Combine(folder, "direct.bin"));
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(2, requests);
            Assert.Equal(105_000L, resumedFrom);
            Assert.Equal(content, await File.ReadAllBytesAsync(entry.Destination));
        });
    }

    [Fact]
    public async Task DirectDownloadRetries429ButNeverRetries404()
    {
        await InFolder(async folder =>
        {
            int busyRequests = 0;
            byte[] content = Content(64_000);
            using (var handler = new ScriptedHandler(_ =>
            {
                if (Interlocked.Increment(ref busyRequests) == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                };
            }))
            using (var engine = new DownloadEngine(client: new HttpClient(handler)))
            {
                var entry = engine.Create(Url, Path.Combine(folder, "busy.bin"));
                Assert.True(engine.Start(entry));
                await Wait(entry);
                Assert.Equal(DownloadStatus.Completed, entry.Status);
                Assert.Equal(2, busyRequests);
                Assert.Equal(content, await File.ReadAllBytesAsync(entry.Destination));
            }

            int missingRequests = 0;
            using (var handler = new ScriptedHandler(_ =>
            {
                Interlocked.Increment(ref missingRequests);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }))
            using (var engine = new DownloadEngine(client: new HttpClient(handler)))
            {
                var entry = engine.Create(Url, Path.Combine(folder, "missing.bin"));
                Assert.True(engine.Start(entry));
                await Wait(entry);
                Assert.Equal(DownloadStatus.Failed, entry.Status);
                Assert.Equal(1, missingRequests);
                Assert.False(File.Exists(entry.Destination));
            }
        });
    }

    [Fact]
    public async Task DirectRetryWithoutStrongValidatorRestartsPrivatePartialFromZero()
    {
        await InFolder(async folder =>
        {
            byte[] content = Content(80_000);
            int attempts = 0;
            using var handler = new ScriptedHandler(request =>
            {
                Assert.Null(request.Headers.Range);
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    var shortResponse = new HttpResponseMessage(HttpStatusCode.OK);
                    shortResponse.Content = new ByteArrayContent(content[..20_000]);
                    shortResponse.Content.Headers.ContentLength = content.Length;
                    return shortResponse;
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                };
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create(Url, Path.Combine(folder, "no-etag.bin"));
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(2, attempts);
            Assert.Equal(content, await File.ReadAllBytesAsync(entry.Destination));
        });
    }

    [Fact]
    public async Task Resume404DoesNotRetryOrDestroyExistingPartial()
    {
        await InFolder(async folder =>
        {
            string target = Path.Combine(folder, "resume-404.bin");
            await File.WriteAllBytesAsync(target + ".rdpartial", Content(12_000));
            await File.WriteAllTextAsync(target + ".rdstate", JsonSerializer.Serialize(
                new { Url, TotalBytes = (long?)50_000, StrongETag = "\"v1\"" }));
            int requests = 0;
            using var handler = new ScriptedHandler(request =>
            {
                Assert.NotNull(request.Headers.Range);
                Interlocked.Increment(ref requests);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var entry = engine.Create(Url, target);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.Equal(1, requests);
            Assert.Equal(12_000, new FileInfo(target + ".rdpartial").Length);
            Assert.False(File.Exists(target));
        });
    }

    [Fact]
    public async Task FixedSegmentCannotCompleteWhenResponseNeverTerminates()
    {
        await InFolder(async folder =>
        {
            byte[] content = Content(4_194_304);
            using var handler = new ScriptedHandler(request =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                int from = (int)range.From!.Value;
                int to = (int)(range.To ?? (content.Length - 1));
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"stable\"");
                if (to > 0 && from == 0)
                    response.Content = new StreamContent(new StallsAtEof(content[from..(to + 1)]));
                else
                    response.Content = new ByteArrayContent(content[from..(to + 1)]);
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(from, to, content.Length);
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler),
                networkIdleTimeout: TimeSpan.FromMilliseconds(80));
            var entry = engine.Create(Url, Path.Combine(folder, "stalled.bin"), connections: 2);
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.False(File.Exists(entry.Destination));
            Assert.True(Directory.Exists(entry.Destination + ".rdsegments"));
        });
    }

    private static byte[] Content(int count) =>
        Enumerable.Range(0, count).Select(i => (byte)((i * 23 + 7) % 251)).ToArray();

    private static async Task InFolder(Func<string, Task> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RDRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task Wait(DownloadEntry entry)
    {
        for (int i = 0; i < 300; i++)
        {
            if (entry.Status is DownloadStatus.Completed or DownloadStatus.Failed) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Recovery test did not reach a terminal state.");
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancel) => Task.FromResult(respond(request));
    }

    private sealed class StallsAtEof(byte[] bytes) : Stream
    {
        private readonly MemoryStream _source = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position
        {
            get => _source.Position;
            set => throw new NotSupportedException();
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int n = _source.Read(buffer.Span);
            if (n != 0) return n;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
