using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class SitePresetTests
{
    [Theory]
    [InlineData("desktop.githubusercontent.com", "GitHub", true)]
    [InlineData("evilgithub.com", "GitHub", false)]
    [InlineData("github.com.evil.invalid", "GitHub", false)]
    [InlineData("download.microsoft.com", "Microsoft", true)]
    [InlineData("microsoft.com.example.org", "Microsoft", false)]
    [InlineData("drive.google.com", "Google Drive", true)]
    [InlineData("googleusercontent.com", "Google Drive", true)]
    [InlineData("youtube.com", "YouTube", true)]
    [InlineData("r3---sn.googlevideo.com", "YouTube", true)]
    public void FiltersHostsAtDomainBoundary(string host, string site, bool expected) =>
        Assert.Equal(expected, SitePresets.Matches(host, site));
}
