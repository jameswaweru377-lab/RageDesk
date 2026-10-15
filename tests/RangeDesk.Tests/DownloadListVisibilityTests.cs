using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadListVisibilityTests
{
    private static DownloadEntry Entry(DownloadStatus status, string category = "Software",
        string labels = "Work, Important")
    {
        var item = new DownloadEntry("https://example.test/releases/installer.bin",
            @"C:\Downloads\installer.bin", category, labels);
        item.Report(status, status == DownloadStatus.Completed ? 10 : 1, 10);
        return item;
    }

    [Theory]
    [InlineData(DownloadStatus.Queued)]
    [InlineData(DownloadStatus.Downloading)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Canceled)]
    [InlineData(DownloadStatus.Failed)]
    public void QueueDoesNotHideActiveOrIncompleteItems(DownloadStatus status)
    {
        Assert.True(DownloadListVisibility.Matches(Entry(status), page: "queue"));
        Assert.True(DownloadListVisibility.Matches(Entry(status), page: "all"));
    }

    [Fact]
    public void CompletedItemIsVisibleInDownloadsAndCompletedButNotQueue()
    {
        var item = Entry(DownloadStatus.Completed);
        Assert.True(DownloadListVisibility.Matches(item, page: "all"));
        Assert.True(DownloadListVisibility.Matches(item, page: "completed"));
        Assert.False(DownloadListVisibility.Matches(item, page: "queue"));
    }

    [Fact]
    public void FilteredNavigationIsExplicitAndCaseInsensitive()
    {
        var item = Entry(DownloadStatus.Downloading);
        Assert.True(DownloadListVisibility.Matches(item, search: "INSTALLER", category: "software", label: "work"));
        Assert.False(DownloadListVisibility.Matches(item, search: "missing"));
        Assert.False(DownloadListVisibility.Matches(item, label: "personal"));
        Assert.False(DownloadListVisibility.Matches(item, status: DownloadStatus.Paused));
    }
}
