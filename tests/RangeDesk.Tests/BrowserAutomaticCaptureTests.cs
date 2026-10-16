using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BrowserAutomaticCaptureTests
{
    [Theory]
    [InlineData("https://example.test/release.zip", true)]
    [InlineData("https://example.test/release.exe?ticket=private", true)]
    [InlineData("https://example.test/index.html", false)]
    [InlineData("https://example.test/download?id=1", false)]
    [InlineData("file:///C:/secret.zip", false)]
    [InlineData("https://user:password@example.test/release.zip", false)]
    public void AutomaticCaptureOnlyAllowsDirectSupportedFiles(string url, bool expected)
        => Assert.Equal(expected, BrowserAutomaticCapture.IsEligibleUrl(url));

    [Fact]
    public void AcknowledgedHandoffOnlyAfterGuiClaimsRequest()
    {
        WithDirectory(folder =>
        {
            var job = BrowserAutomaticCapture.Queue(folder,
                """{"action":"capture_download","url":"https://example.test/releases/app.zip"}""");
            var claimed = BrowserAutomaticCapture.TryTake(folder);
            Assert.NotNull(claimed);
            Assert.Equal(job, claimed);
            Assert.Null(BrowserAutomaticCapture.TryTake(folder));
            BrowserAutomaticCapture.Complete(folder, claimed!, true);
            var reply = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, job.Id + ".reply")));
            Assert.True(reply.RootElement.GetProperty("accepted").GetBoolean());
            Assert.False(File.Exists(Path.Combine(folder, job.Id + ".processing")));
        });
    }

    [Fact]
    public async Task NativeWaitReceivesExplicitRejection()
    {
        await WithDirectoryAsync(async folder =>
        {
            var job = BrowserAutomaticCapture.Queue(folder,
                """{"action":"capture_download","url":"https://example.test/installer.msi"}""");
            var claimed = BrowserAutomaticCapture.TryTake(folder)!;
            BrowserAutomaticCapture.Complete(folder, claimed, false);
            Assert.False(await BrowserAutomaticCapture.WaitForDecisionAsync(folder, job));
            Assert.False(File.Exists(Path.Combine(folder, job.Id + ".reply")));
        });
    }

    [Fact]
    public void ExpiredHandoffIsNeverClaimed()
    {
        WithDirectory(folder =>
        {
            var t = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
            var job = BrowserAutomaticCapture.Queue(folder,
                """{"action":"capture_download","url":"https://example.test/installer.zip"}""", t);
            Assert.Null(BrowserAutomaticCapture.TryTake(folder, t.AddSeconds(21)));
            Assert.False(File.Exists(Path.Combine(folder, job.Id + ".pending")));
            Assert.False(File.Exists(Path.Combine(folder, job.Id + ".processing")));
        });
    }

    [Theory]
    [InlineData("""{"action":"read_cookies","url":"https://example.test/file.zip"}""")]
    [InlineData("""{"action":"capture_download","url":"https://example.test/"}""")]
    [InlineData("""{"action":"capture_download","url":"https://u:p@example.test/file.zip"}""")]
    public void ForbiddenAutomaticCaptureNeverQueues(string request)
    {
        WithDirectory(folder =>
        {
            Assert.Throws<ArgumentException>(() => BrowserAutomaticCapture.Queue(folder, request));
            Assert.False(Directory.Exists(folder));
        });
    }

    private static void WithDirectory(Action<string> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-auto-" + Guid.NewGuid().ToString("N"));
        try { test(folder); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static async Task WithDirectoryAsync(Func<string, Task> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-auto-" + Guid.NewGuid().ToString("N"));
        try { await test(folder); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
