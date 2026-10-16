using RangeDesk.Core;

namespace RangeDesk.Tests;

public sealed class RangeDeskPreferencesTests
{
    [Fact]
    public void Defaults_AreValidAndDoNotRequireAnExistingFolder()
    {
        var settings = new RangeDeskPreferences().Validate();
        Assert.Equal(8, settings.DefaultConnections);
        Assert.Equal(0, settings.DefaultBandwidthKiBps);
        Assert.True(settings.StartImmediately);
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

    [Fact]
    public void RelativeFolderIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            (new RangeDeskPreferences { DownloadFolder = "relative/downloads" }).Validate());
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
                StartImmediately = false
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
