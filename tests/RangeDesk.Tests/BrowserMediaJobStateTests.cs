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

public sealed class MediaDownloadEntryTests
{
    [Fact]
    public void BrowserMediaRowTracksRealDestinationAndShowsHonestProgress()
    {
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RangeDeskTests"));
        string requestedPath = Path.Combine(folder, "Requested-1080p.mkv");
        string actualPath = Path.Combine(folder, "Video-1080p-" + id[..8] + ".webm");
        var entry = new DownloadEntry("https://www.youtube.com/watch?v=abcdefghijk", requestedPath, "Videos");
        entry.AttachMediaJob(id, "137");
        Assert.True(entry.IsMedia);
        Assert.Equal("137", entry.MediaFormatId);
        Assert.Equal("Videos", entry.Category);
        entry.UpdateMediaStage("Downloading video stream");
        entry.Report(DownloadStatus.Downloading, 1024, null);
        Assert.True(entry.IsIndeterminate);
        Assert.Null(entry.Percent);
        Assert.Equal("Downloading video stream", entry.MediaStage);
        entry.UpdateMediaOutput(actualPath);
        Assert.Equal(actualPath, entry.Destination);
        Assert.EndsWith(".webm", entry.Name);
        entry.Report(DownloadStatus.Completed, 2048, 2048);
        Assert.Equal(DownloadStatus.Completed, entry.Status);
        Assert.Equal(100, entry.Percent);
    }

    [Fact]
    public void MediaIdentityAndOutputPathRejectUnsafeValues()
    {
        var entry = new DownloadEntry("https://example.org/video", Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "some-video.mkv")), "Videos");
        Assert.Throws<ArgumentException>(() => entry.AttachMediaJob("../bad", "137"));
        Assert.Throws<ArgumentException>(() => entry.AttachMediaJob(
            Guid.NewGuid().ToString("N"), "../../path"));
        Assert.Throws<ArgumentException>(() => entry.UpdateMediaOutput("relative.mp4"));
    }
}
