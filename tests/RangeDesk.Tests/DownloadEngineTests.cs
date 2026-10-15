using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadEngineTests
{
    [Theory]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("ftp://example.com/unsafe.zip")]
    [InlineData("https://user:password@example.com/file.zip")]
    [InlineData("not-an-http-address")]
    public void InvalidUrisAreRejected(string url) =>
        Assert.False(DownloadEngine.IsAllowedUrl(url));

    [Fact]
    public void UnknownLengthHasNoInventedPercentageOrEta()
    {
        var record = new DownloadEntry("https://example.org/file.bin", @"C:\Downloads\file.bin");
        record.Report(DownloadStatus.Downloading, 42, null, 640, 1);
        Assert.Null(record.Percent);
        Assert.True(record.IsIndeterminate);
        Assert.Equal("—", record.EtaLabel);
        Assert.Equal(1, record.Connections);
    }

    [Fact]
    public void ProgressCannotExceed100Percent()
    {
        var record = new DownloadEntry("https://example.org/a", @"C:\Downloads\a");
        record.Report(DownloadStatus.Downloading, 250, 100);
        Assert.Equal(100, record.Percent);
    }

    [Fact]
    public async Task DownloadsExactBytesFromLocalServer()
    {
        byte[] expected = Enumerable.Range(0, 350_000).Select(i => (byte)(i % 251)).ToArray();
        using var server = new LoopbackServer(expected);
        string folder = Path.Combine(Path.GetTempPath(), "RangeDeskTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var engine = new DownloadEngine();
            var entry = engine.Create(server.Url, Path.Combine(folder, "test.bin"));
            Assert.True(engine.Start(entry));
            await WaitUntilAsync(() => entry.Status is DownloadStatus.Completed or DownloadStatus.Failed);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(expected.LongLength, entry.Received);
            Assert.Equal(SHA256.HashData(expected), SHA256.HashData(await File.ReadAllBytesAsync(entry.Destination)));
            Assert.False(File.Exists(entry.Destination + ".rdpartial"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        byte[] expected = Enumerable.Range(0, 12_345).Select(i => (byte)(i % 241)).ToArray();
        using var server = new LoopbackServer(expected);
        string folder = Path.Combine(Path.GetTempPath(), "RangeDeskTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string destination = Path.Combine(folder, "existing.txt");
            await File.WriteAllTextAsync(destination, "keep me");
            using var engine = new DownloadEngine();
            var entry = engine.Create(server.Url, destination);
            Assert.True(engine.Start(entry));
            await WaitUntilAsync(() => entry.Status == DownloadStatus.Failed);
            Assert.Equal("keep me", await File.ReadAllTextAsync(destination));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task WaitUntilAsync(Func<bool> ready)
    {
        for (int i = 0; i < 200; i++)
        {
            if (ready()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("RangeDesk background transfer did not finish in 10 seconds.");
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _content;

        public LoopbackServer(byte[] content)
        {
            _content = content;
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            Url = $"http://127.0.0.1:{port}/download";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string Url { get; }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext request;
                try { request = await _listener.GetContextAsync(); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                try
                {
                    request.Response.StatusCode = 200;
                    request.Response.Headers.Add("ETag", "\"test-etag-001\"");
                    request.Response.ContentLength64 = _content.Length;
                    await request.Response.OutputStream.WriteAsync(_content);
                    request.Response.Close();
                }
                catch { try { request.Response.Abort(); } catch { } }
            }
        }
        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            _cts.Dispose();
        }
    }
}
