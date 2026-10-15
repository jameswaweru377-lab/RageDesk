using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

/// <summary>Controlled HTTP resume cases: no public network activity.</summary>
public sealed class ResumeSafetyTests
{
    [Fact]
    public async Task Valid206ResumesOnlyMissingBytesAndMatchesOriginal()
    {
        byte[] expected = MakeContent(220_000);
        await InTemporaryFolder(async folder =>
        {
            string destination = Path.Combine(folder, "resume.bin");
            string url = "https://fixtures.example.test/resume.bin";
            await WriteInterruptedState(destination, url, expected, 68_000, "\"version-one\"");
            using var handler = new ScriptedHandler(request =>
            {
                Assert.Equal(68_000, request.Headers.Range?.Ranges.Single().From);
                Assert.Equal("\"version-one\"", request.Headers.IfRange?.EntityTag?.ToString());
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"version-one\"");
                response.Content = new ByteArrayContent(expected[68_000..]);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(68_000, expected.Length - 1, expected.Length);
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var item = engine.Create(url, destination);
            engine.Start(item);
            await WaitForTerminalState(item);
            Assert.Equal(DownloadStatus.Completed, item.Status);
            Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".rdpartial"));
        });
    }

    [Fact]
    public async Task ServerIgnoringRangeRestartsInsteadOfAppending()
    {
        byte[] expected = MakeContent(85_000);
        await InTemporaryFolder(async folder =>
        {
            string destination = Path.Combine(folder, "fallback.bin");
            string url = "https://fixtures.example.test/fallback.bin";
            await WriteInterruptedState(destination, url, expected, 25_000, "\"version-one\"");
            using var handler = new ScriptedHandler(request =>
            {
                Assert.NotNull(request.Headers.Range);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"version-two\"");
                response.Content = new ByteArrayContent(expected);
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var item = engine.Create(url, destination);
            engine.Start(item);
            await WaitForTerminalState(item);
            Assert.Equal(DownloadStatus.Completed, item.Status);
            Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        });
    }

    [Fact]
    public async Task ChangedEtagNeverPublishesMixedBytes()
    {
        byte[] expected = MakeContent(85_000);
        await InTemporaryFolder(async folder =>
        {
            string destination = Path.Combine(folder, "conflict.bin");
            string url = "https://fixtures.example.test/conflict.bin";
            await WriteInterruptedState(destination, url, expected, 25_000, "\"version-one\"");
            using var handler = new ScriptedHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"version-two\"");
                response.Content = new ByteArrayContent(expected[25_000..]);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(25_000, expected.Length - 1, expected.Length);
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var item = engine.Create(url, destination);
            engine.Start(item);
            await WaitForTerminalState(item);
            Assert.Equal(DownloadStatus.Failed, item.Status);
            Assert.False(File.Exists(destination));
            Assert.True(File.Exists(destination + ".rdpartial"));
            Assert.Equal(25_000, new FileInfo(destination + ".rdpartial").Length);
        });
    }

    [Fact]
    public async Task InvalidContentRangeCannotCorruptPartial()
    {
        byte[] expected = MakeContent(85_000);
        await InTemporaryFolder(async folder =>
        {
            string destination = Path.Combine(folder, "wrongrange.bin");
            string url = "https://fixtures.example.test/wrongrange.bin";
            await WriteInterruptedState(destination, url, expected, 25_000, "\"version-one\"");
            using var handler = new ScriptedHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"version-one\"");
                response.Content = new ByteArrayContent(expected[25_000..]);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(24_999, expected.Length - 1, expected.Length);
                return response;
            });
            using var engine = new DownloadEngine(client: new HttpClient(handler));
            var item = engine.Create(url, destination);
            engine.Start(item);
            await WaitForTerminalState(item);
            Assert.Equal(DownloadStatus.Failed, item.Status);
            Assert.False(File.Exists(destination));
            Assert.Equal(25_000, new FileInfo(destination + ".rdpartial").Length);
        });
    }

    private static byte[] MakeContent(int count) =>
        Enumerable.Range(0, count).Select(i => (byte)((i * 17 + 3) % 251)).ToArray();

    private static async Task WriteInterruptedState(string destination, string url,
        byte[] bytes, int complete, string etag)
    {
        await File.WriteAllBytesAsync(destination + ".rdpartial", bytes[..complete]);
        await File.WriteAllTextAsync(destination + ".rdstate",
            JsonSerializer.Serialize(new { Url = url, TotalBytes = (long?)bytes.Length, StrongETag = etag }));
    }

    private static async Task InTemporaryFolder(Func<string, Task> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RangeDeskResume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { await action(folder); }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task WaitForTerminalState(DownloadEntry entry)
    {
        for (int i = 0; i < 150; i++)
        {
            if (entry.Status is DownloadStatus.Completed or DownloadStatus.Failed) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Transfer did not settle.");
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            Task.FromResult(respond(request));
    }
}
