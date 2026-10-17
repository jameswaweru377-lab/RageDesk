using Xunit;

namespace RangeDesk.Tests;

/// <summary>
/// Source contracts for the approved RageDesk screenshots. Screenshot pixel comparison
/// remains a separate Windows UI acceptance gate; these prevent accidental UI removal.
/// </summary>
public sealed class ReactApprovedVisualParityTests
{
    private static string Read(string relative)
    {
        foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? folder = new(root); folder is not null; folder = folder.Parent)
            {
                string p = Path.Combine(folder.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) return File.ReadAllText(p);
            }
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void MainDashboardRetainsApprovedPanelsAndAddsOnlyRequestedDetails()
    {
        string app = Read("frontend/src/App.tsx");
        string skin = Read("frontend/src/visual-parity.css");
        foreach (string token in new[] {
            "ACTIVE DOWNLOADS", "CURRENT SPEED", "DOWNLOADED TODAY", "TIME REMAINING",
            "Network &amp; Performance", "Top Sources", "Recent Activities",
            "Sites / Groups", "Install RageDesk", "Browser Extension",
            "CategoryPill", "SegmentedProgress", "selection-toolbar", "settings-open",
            "Sync coming soon", "free", "No failed downloads"
        })
            Assert.Contains(token, app + skin);
        Assert.Contains("checked={selectedIds.has(r.id)}", app);
        Assert.Contains("aria-label={`Select ${r.name}`}", app);
        Assert.Contains("onClick={()=>openNativeDialog(\"ui.openAddDownload\")}", app);
        Assert.Contains("title=\"Delete requires a validated native confirmation flow\"", app);
        Assert.Contains("disabled", app); // No destructive bridge until parity is verified.
    }

    [Fact]
    public void SettingsFollowsApprovedHorizontalSectionsButGroundsDisplayedOptions()
    {
        string settings = Read("frontend/src/SettingsPage.tsx");
        string skin = Read("frontend/src/visual-parity.css");
        foreach (string section in new[] {
            "General", "Downloads", "Browser Capture", "Connections", "Appearance", "Advanced",
            "General Settings", "Application Preview", "Startup & Behavior",
            "Notifications", "Data Management"
        })
            Assert.Contains(section, settings);
        Assert.Contains("grid-template-columns:repeat(6", skin);
        Assert.Contains("actual?.defaultConnections", settings);
        Assert.Contains("actual?.startImmediately", settings);
        Assert.Contains("openNativeDialog(\"ui.openSettings\")", settings);
        Assert.Contains("read-only", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeProjectionExposesOnlyDiskTotalsAndNonSensitiveSettings()
    {
        string projection = Read("src/RangeDesk.App/ReactDashboardSnapshot.cs");
        string host = Read("src/RangeDesk.App/ReactDashboardPreviewWindow.cs");
        string main = Read("src/RangeDesk.App/MainWindow.xaml.cs");
        Assert.Contains("DriveInfo", projection);
        Assert.Contains("AvailableFreeSpace", projection);
        Assert.Contains("TotalSize", projection);
        Assert.Contains("ReactSettingsDisplay", projection);
        Assert.Contains("!string.IsNullOrWhiteSpace(preferences.ProxyUri)", projection);
        Assert.DoesNotContain("new ReactDiskStorage(root", projection);
        Assert.DoesNotContain("preferences.ProxyUri))", projection);
        Assert.Contains("ui.openSettings", host);
        Assert.Contains("ui.openAddDownload", host);
        Assert.Contains("args.Source", host);
        Assert.Contains("version.ValueKind != JsonValueKind.Number", host);
        Assert.Contains("OpenNativeDialogFromReact", main);
        Assert.DoesNotContain("Process.Start", host);
        Assert.DoesNotContain("_engine.Start(", host);
    }
}
