using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class BrowserMediaJobStateTests
{
    [Fact]
    public void QueuedAndCompletedSnapshotsAreHonestAndContainNoUrl()
    {
        string jobId = Guid.NewGuid().ToString("N");
        string path = Path.Combine(BrowserMediaJobState.DefaultDirectory, jobId + ".json");
        try
        {
            BrowserMediaJobState.Update(jobId, "queued", "Selection received");
            var first = BrowserMediaJobState.Read(jobId);
            Assert.NotNull(first);
            Assert.Equal("queued", first.State);
            Assert.Null(first.DownloadedBytes);
            BrowserMediaJobState.Update(jobId, "downloading", "Downloading current stream",
                100, 1000, false);
            var next = BrowserMediaJobState.Read(jobId);
            Assert.NotNull(next);
            Assert.Equal("downloading", next.State);
            Assert.Equal(100, next.DownloadedBytes);
            Assert.Equal(1000, next.TotalBytes);
            BrowserMediaJobState.Update(jobId, "completed", "Verified output");
            var final = BrowserMediaJobState.Read(jobId);
            Assert.NotNull(final);
            Assert.Equal("completed", final.State);
            Assert.DoesNotContain("http", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void JobIdsAreValidatedToAvoidArbitraryPathReads()
    {
        Assert.Throws<ArgumentException>(() => BrowserMediaJobState.Read("../file"));
        Assert.Throws<ArgumentException>(() =>
            BrowserMediaJobState.Update("not-a-guid", "queued", "test"));
    }

    [Fact]
    public void PortableMediaToolsDenoIsDiscoveredWithoutExecutingIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RangeDesk-runtime-" + Guid.NewGuid().ToString("N"));
        string tools = Path.Combine(folder, "MediaTools");
        Directory.CreateDirectory(tools);
        string deno = Path.Combine(tools, "deno.exe");
        try
        {
            File.WriteAllText(deno, "test-only placeholder");
            Assert.Equal(deno, OptionalMediaExtractor.DenoExecutable(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
