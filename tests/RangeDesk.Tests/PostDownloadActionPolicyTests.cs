using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class PostDownloadActionPolicyTests
{
    [Theory]
    [InlineData(SafePostDownloadAction.PlayNotificationSound)]
    [InlineData(SafePostDownloadAction.OpenContainingFolder)]
    public void ExplicitActionRunsOnceAfterVerifiedCompletion(SafePostDownloadAction action)
    {
        Assert.True(PostDownloadActionPolicy.ShouldRun(action, DownloadStatus.Downloading,
            DownloadStatus.Completed, finalFileExists: true));
        Assert.False(PostDownloadActionPolicy.ShouldRun(action, DownloadStatus.Completed,
            DownloadStatus.Completed, finalFileExists: true));
        Assert.False(PostDownloadActionPolicy.ShouldRun(action, DownloadStatus.Downloading,
            DownloadStatus.Failed, finalFileExists: true));
        Assert.False(PostDownloadActionPolicy.ShouldRun(action, DownloadStatus.Downloading,
            DownloadStatus.Completed, finalFileExists: false));
    }

    [Fact]
    public void AutomaticActionsAreDisabledByDefault()
    {
        Assert.False(PostDownloadActionPolicy.ShouldRun(SafePostDownloadAction.None,
            DownloadStatus.Downloading, DownloadStatus.Completed, true));
    }

    [Fact]
    public void ArbitraryValuesCannotEnableAnExecutableAction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PostDownloadActionPolicy.Validate((SafePostDownloadAction)13));
    }
}
