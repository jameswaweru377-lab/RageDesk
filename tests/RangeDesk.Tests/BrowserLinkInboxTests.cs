using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BrowserLinkInboxTests
{
    [Fact]
    public void UserInitiatedLinkCanBeTakenExactlyOnce()
    {
        var folder=Path.Combine(Path.GetTempPath(),"rd-bridge-"+Guid.NewGuid());
        try
        {
            BrowserLinkInbox.Enqueue(folder,"{\"action\":\"add_link\",\"url\":\"https://example.com/movie.mp4?token=private\"}");
            Assert.Equal("https://example.com/movie.mp4?token=private",BrowserLinkInbox.TryTake(folder));
            Assert.Null(BrowserLinkInbox.TryTake(folder));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }

    [Theory]
    [InlineData("{\"action\":\"add_link\",\"url\":\"file:///etc/passwd\"}")]
    [InlineData("{\"action\":\"add_link\",\"url\":\"https://user:secret@example.com/\"}")]
    [InlineData("{\"action\":\"read_cookies\",\"url\":\"https://example.com/\"}")]
    public void RejectUnsupportedActionsAndCredentials(string json)
    {
        var folder=Path.Combine(Path.GetTempPath(),"rd-bridge-"+Guid.NewGuid());
        Assert.Throws<ArgumentException>(()=>BrowserLinkInbox.Enqueue(folder,json));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void CorruptMessageIsDiscardedWithoutReturningUrl()
    {
        var folder=Path.Combine(Path.GetTempPath(),"rd-bridge-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"bad.json"),"{ bad json");
            Assert.Null(BrowserLinkInbox.TryTake(folder));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
