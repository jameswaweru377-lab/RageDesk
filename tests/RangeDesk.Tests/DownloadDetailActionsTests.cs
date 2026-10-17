using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadDetailActionsTests
{
    [Theory]
    [InlineData(DownloadStatus.Completed)]
    public void FinishedTasksShowOpenAndDownloadAgainNeverPauseOrResume(DownloadStatus status)
    {
        Assert.True(DownloadDetailActions.CanOpen(status));
        Assert.True(DownloadDetailActions.CanDownloadAgain(status, false));
        Assert.False(DownloadDetailActions.CanPause(status, false));
        Assert.False(DownloadDetailActions.CanResume(status, false));
        Assert.False(DownloadDetailActions.CanStart(status, false));
        Assert.False(DownloadDetailActions.CanDownloadAgain(status, true));
    }

    [Theory]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Canceled)]
    public void InterruptedTasksResumeButCannotOpenIncompleteFile(DownloadStatus status)
    {
        Assert.True(DownloadDetailActions.CanResume(status, false));
        Assert.False(DownloadDetailActions.CanOpen(status));
        Assert.False(DownloadDetailActions.CanDownloadAgain(status, false));
    }

    [Fact]
    public void InProgressTasksOnlyPauseOrStop()
    {
        Assert.True(DownloadDetailActions.CanPause(DownloadStatus.Downloading, false));
        Assert.True(DownloadDetailActions.CanStop(DownloadStatus.Downloading));
        Assert.False(DownloadDetailActions.CanResume(DownloadStatus.Downloading, false));
        Assert.False(DownloadDetailActions.CanPause(DownloadStatus.Downloading, true));
    }

    [Fact]
    public void NewCopyNeverCollidesWithCompletedFileCatalogOrSidecars()
    {
        string root = Path.Combine(Path.GetTempPath(), "rd-again-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "setup.exe");
            File.WriteAllText(original, "Completed original");
            string first = DownloadAgainPath.Create(original, new[] { original });
            Assert.Equal(Path.Combine(root,"setup (again 1).exe"), first);
            File.WriteAllText(first + ".rdpartial", "unconfirmed progress");
            string next = DownloadAgainPath.Create(original,new[] {original});
            Assert.Equal(Path.Combine(root,"setup (again 2).exe"), next);
            Assert.Equal("Completed original",File.ReadAllText(original));
            Assert.Equal("unconfirmed progress",File.ReadAllText(first+".rdpartial"));
        }
        finally { Directory.Delete(root,recursive:true); }
    }
}
