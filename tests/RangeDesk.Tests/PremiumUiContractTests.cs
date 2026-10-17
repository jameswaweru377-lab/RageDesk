using Xunit;

namespace RangeDesk.Tests;

/// <summary>Paint-only contracts for the approved premium skin on the existing UI.</summary>
public sealed class PremiumUiContractTests
{
    private static string Source(string relative)
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? folder = new(root); folder is not null; folder = folder.Parent)
            {
                string path = Path.Combine(folder.FullName,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) return File.ReadAllText(path);
            }
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void MainWindowRetainsTheSameLiveSectionsAndDownloadActions()
    {
        string xaml = Source("src/RangeDesk.App/MainWindow.xaml");
        Assert.Contains("x:Name=\"DownloadsGrid\"", xaml);
        Assert.Contains("x:Name=\"StatusMessage\"", xaml);
        Assert.Contains("PremiumMetricCard", xaml);
        Assert.Contains("PremiumDashboardPanel", xaml);
        Assert.Contains("DownloadedTodayLabel", xaml);
        Assert.Contains("AggregateSpeedLabel", xaml);
        Assert.Contains("AggregateEtaLabel", xaml);
        Assert.Contains("AnalyticsGraph", xaml);
        Assert.Contains("Click=\"Add_Click\"", xaml);
        Assert.Contains("Click=\"Pause_Click\"", xaml);
    }

    [Fact]
    public void PopupHasNoWindowsDefaultWhiteTabsOrGridHeaders()
    {
        string popup = Source("src/RangeDesk.App/DownloadProgressWindow.cs");
        string skin = Source("src/RangeDesk.App/Themes/ProgressWindowPremium.xaml");
        Assert.Contains("ProgressWindowPremium.xaml", popup);
        Assert.Contains("PremiumSecondaryButton", popup);
        Assert.Contains("PremiumPrimaryButton", popup);
        Assert.Contains("GridViewColumnHeader", skin);
        Assert.Contains("TabItem", skin);
        Assert.Contains("ListViewItem", skin);
        Assert.Contains("IsKeyboardFocused", skin);
        Assert.Contains("ContentSource=\"SelectedContent\"", skin);
        Assert.Contains("PART_SelectedContentHost", skin);
        Assert.Contains("status == DownloadStatus.Completed", popup);
        Assert.Contains("status == DownloadStatus.Downloading", popup);
        Assert.DoesNotContain("Chrome" + "Button", popup);
    }

    [Fact]
    public void PopupHasRichLocallyGroundedIdentityAndStateAwareControls()
    {
        string popup = Source("src/RangeDesk.App/DownloadProgressWindow.cs");
        string art = Source("src/RangeDesk.App/RageDeskPopupArtwork.cs");
        string skin = Source("src/RangeDesk.App/Themes/ProgressWindowPremium.xaml");

        Assert.Contains("RageDeskPopupArtwork.BrandMark", popup);
        Assert.Contains("RecognizedSource", popup);
        Assert.Contains("TryReadLocalExecutableIconAsync", popup);
        Assert.Contains("DownloadStatus.Completed", popup);
        Assert.Contains("DisplayRemainingTime", popup);
        Assert.Contains("Connections (", popup);
        Assert.Contains("0 MB/s", popup);
        Assert.Contains("ButtonCaption", popup);
        Assert.Contains("TabHeader", popup);
        Assert.Contains("GridViewColumn", popup);

        // Recognized sources are local and filename+domain-scoped; no URL icon API.
        Assert.Contains("desktop.githubusercontent.com", art);
        Assert.Contains("dl.google.com", art);
        Assert.Contains("GoogleDriveSetup", art);
        Assert.Contains("File.Exists(item.Destination)", art);
        Assert.DoesNotContain("HttpClient", art);
        Assert.DoesNotContain("WebRequest", art);
        Assert.Contains("ProgressBar", skin);
        Assert.Contains("PopupScrollThumb", skin);
        Assert.Contains("PART_Track", skin);
    }

    [Fact]
    public void ThemeGradientColorsAreReplacedWhenDarkLightToggleChanges()
    {
        string theme = Source("src/RangeDesk.App/RangeDeskTheme.cs");
        string xaml = Source("src/RangeDesk.App/Themes/V5Theme.xaml");
        foreach (var token in new[] {
            "V5PremiumToolbarTop", "V5PremiumToolbarBottom",
            "V5PremiumPrimaryTop", "V5PremiumPrimaryBottom"
        })
        {
            Assert.Contains(token, theme);
            Assert.Contains(token, xaml);
        }
        Assert.Contains("DynamicResource V5PremiumToolbarFill", xaml);
        Assert.Contains("DynamicResource V5PremiumPrimaryFill", xaml);
    }
}
