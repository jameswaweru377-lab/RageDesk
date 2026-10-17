using Xunit;
using RangeDesk.Core;

namespace RangeDesk.Tests;

public sealed class RangeDeskPreferencesTests
{
    [Fact]
    public void CobaltIsDefaultAndArcticPersistsForNextLaunch()
    {
        var defaults = new RangeDeskPreferences().Validate();
        Assert.Equal("Cobalt", defaults.Theme);
        Assert.Throws<ArgumentException>(() =>
            (new RangeDeskPreferences { Theme = "NotATheme" }).Validate());
        string directory = Path.Combine(Path.GetTempPath(), "rd-theme-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "preferences-v5.json");
        try
        {
            RangeDeskPreferencesStore.Save(file, defaults with { Theme = "Arctic" });
            Assert.Equal("Arctic", RangeDeskPreferencesStore.Load(file).Theme);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Defaults_AreValidAndDoNotRequireAnExistingFolder()
    {
        var settings = new RangeDeskPreferences().Validate();
        Assert.Equal(8, settings.DefaultConnections);
        Assert.Equal(0, settings.DefaultBandwidthKiBps);
        Assert.True(settings.StartImmediately);
        Assert.Equal(2, settings.MaxConcurrentDownloads);
        Assert.True(Path.IsPathFullyQualified(settings.DownloadFolder));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(64)]
    public void InvalidConnectionDefaultsAreRejected(int connections)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (new RangeDeskPreferences { DefaultConnections = connections }).Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1048577)]
    public void InvalidRateDefaultsAreRejected(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (new RangeDeskPreferences { DefaultBandwidthKiBps = rate }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InvalidSimultaneousDownloadLimitIsRejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (new RangeDeskPreferences { MaxConcurrentDownloads = count }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DownloadEngine(maxConcurrentDownloads: count));
    }

    [Fact]
    public void RelativeFolderIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            (new RangeDeskPreferences { DownloadFolder = "relative/downloads" }).Validate());
    }

    [Theory]
    [InlineData("http://user:pass@proxy.example:8080")]
    [InlineData("https://proxy.example:8080")]
    [InlineData("http://proxy.example/path")]
    [InlineData("http://proxy.example/?token=abc")]
    public void UnsafeProxyConfigurationsAreRejected(string proxy)
    {
        Assert.Throws<ArgumentException>(() =>
            (new RangeDeskPreferences { ProxyUri = proxy }).Validate());
    }

    [Fact]
    public void SafeUnauthenticatedProxyIsAccepted()
    {
        var valid = new RangeDeskPreferences { ProxyUri = "http://127.0.0.1:8080" }.Validate();
        Assert.StartsWith("http://127.0.0.1:8080", valid.ProxyUri);
        using var engine = new DownloadEngine(proxyUri: valid.ProxyUri);
    }

    [Fact]
    public void SettingsSurviveRestartWithoutTouchingTheDownloadCatalog()
    {
        string root = Path.Combine(Path.GetTempPath(), "rangedesk-preferences-test-" + Guid.NewGuid());
        string file = Path.Combine(root, "preferences-v5.json");
        try
        {
            var expected = new RangeDeskPreferences
            {
                DownloadFolder = Path.Combine(root, "new-downloads"),
                DefaultConnections = 16,
                DefaultBandwidthKiBps = 4096,
                StartImmediately = false,
                MaxConcurrentDownloads = 3,
                ProxyUri = "http://127.0.0.1:8080"
            }.Validate();
            Assert.False(Directory.Exists(expected.DownloadFolder));
            RangeDeskPreferencesStore.Save(file, expected);
            Assert.Equal(expected, RangeDeskPreferencesStore.Load(file));
            Assert.False(Directory.Exists(expected.DownloadFolder));
            Assert.False(File.Exists(Path.Combine(root, "catalog-v5.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidJsonDoesNotSilentlyOverwriteOldPreferences()
    {
        string root = Path.Combine(Path.GetTempPath(), "rangedesk-preferences-test-" + Guid.NewGuid());
        string file = Path.Combine(root, "preferences-v5.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(file, "not json");
            Assert.Throws<System.Text.Json.JsonException>(() => RangeDeskPreferencesStore.Load(file));
            Assert.Equal("not json", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
