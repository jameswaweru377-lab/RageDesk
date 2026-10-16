using System.Text.Json;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class ChromeIntegrationSetupTests
{
    [Fact]
    public void ChromeManifestAllowsOnlyPinnedDevelopmentExtensionId()
    {
        var host = Path.Combine(Path.GetTempPath(), "RangeDesk", "NativeHost", "RangeDesk.NativeHost.exe");
        using var doc = JsonDocument.Parse(ChromeIntegrationSetup.BuildManifest(host));
        var root = doc.RootElement;
        Assert.Equal("com.rangedesk.capture", root.GetProperty("name").GetString());
        Assert.Equal("stdio", root.GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath(host), root.GetProperty("path").GetString());
        var origins = root.GetProperty("allowed_origins");
        Assert.Equal(1, origins.GetArrayLength());
        Assert.Equal("chrome-extension://" + ChromeIntegrationSetup.ExtensionId + "/",
            origins[0].GetString());
        Assert.Matches("^[a-p]{32}$", ChromeIntegrationSetup.ExtensionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NativeHost.exe")]
    public void ChromeManifestRejectsNonAbsoluteHostPath(string path)
        => Assert.Throws<ArgumentException>(() => ChromeIntegrationSetup.BuildManifest(path));

    [Fact]
    public void NativeHostExecutableUsesPortablePackageLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "RangeDesk-Preview");
        Assert.Equal(Path.Combine(root, "NativeHost", "RangeDesk.NativeHost.exe"),
            ChromeIntegrationSetup.HostExecutable(root));
        Assert.Equal(Path.Combine(root, "BrowserCapture", "chromium"),
            ChromeIntegrationSetup.ExtensionFolder(root));
    }

    [Fact]
    public void CurrentUserChromeRegistrationRoundTripsOnIsolatedWindowsCiOnly()
    {
        // Never mutate a developer's own Chrome registration during dotnet test.
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;

        string root = Path.Combine(Path.GetTempPath(), "RangeDesk-Chrome-Setup-" + Guid.NewGuid().ToString("N"));
        var host = ChromeIntegrationSetup.HostExecutable(root);
        var ext = Path.Combine(ChromeIntegrationSetup.ExtensionFolder(root), "manifest.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(host)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ext)!);
            File.WriteAllText(host, "test stub (not executed)");
            File.WriteAllText(ext, """{"manifest_version":3,"name":"RangeDesk test"}""");

            ChromeIntegrationSetup.Register(root);
            Assert.True(ChromeIntegrationSetup.IsRegistered(root));
            Assert.False(ChromeIntegrationSetup.IsRegistered(Path.Combine(root, "different-app")));
            Assert.True(ChromeIntegrationSetup.Disconnect());
            Assert.False(ChromeIntegrationSetup.IsRegistered(root));
        }
        finally
        {
            ChromeIntegrationSetup.Disconnect();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
