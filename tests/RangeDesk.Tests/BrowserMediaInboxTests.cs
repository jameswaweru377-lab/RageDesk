using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BrowserMediaInboxTests
{
    [Fact]
    public void OneClickQualitySelectionIsQueuedAndConsumedExactlyOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-media-inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            BrowserMediaInbox.Enqueue(folder,
                """{"action":"media_download","url":"https://example.org/watch?id=test","formatId":"webm-1080"}""");
            var selected = BrowserMediaInbox.TryTake(folder);
            Assert.NotNull(selected);
            Assert.Equal("webm-1080", selected.FormatId);
            Assert.Equal("https://example.org/watch?id=test", selected.Url);
            Assert.Null(BrowserMediaInbox.TryTake(folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("""{"action":"read_cookies","url":"https://example.org/watch","formatId":"720"}""")]
    [InlineData("""{"action":"media_download","url":"file:///etc/passwd","formatId":"720"}""")]
    [InlineData("""{"action":"media_download","url":"https://user:pass@example.org/watch","formatId":"720"}""")]
    [InlineData("""{"action":"media_download","url":"https://example.org/watch","formatId":"../../bad"}""")]
    [InlineData("""{"action":"media_download","url":"https://example.org/watch","formatId":"bad+audio"}""")]
    [InlineData("""{"action":"media_download","url":"https://example.org/video.mp4","formatId":"720"}""")]
    public void RejectsCommandsCredentialsUnsafeFormatsAndDirectFileMisuse(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-media-inbox-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() => BrowserMediaInbox.Enqueue(folder, json));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void CorruptedQueuedRequestIsDiscardedSafely()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-media-inbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "bad.json"), "{ invalid JSON");
            Assert.Null(BrowserMediaInbox.TryTake(folder));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
