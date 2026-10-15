using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadCategoryTests
{
    [Theory]
    [InlineData("GitHubDesktopSetup-x64.exe", "Software")]
    [InlineData("Docker Desktop Installer.msi", "Software")]
    [InlineData("movie.mkv", "Videos")]
    [InlineData("proposal.pdf", "Documents")]
    [InlineData("archive.7z", "Archives")]
    [InlineData("photo.png", "Images")]
    [InlineData("unrecognized.blob", "Other")]
    public void ExtensionHintsArePredictable(string fileName, string expected) =>
        Assert.Equal(expected, DownloadCategory.Guess(fileName));
}
