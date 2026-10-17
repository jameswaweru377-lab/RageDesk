using Xunit;

namespace RangeDesk.Tests;

/// <summary>Non-destructive UI contracts for the approved desktop/browser redesign.
/// The actual WPF XAML is also compiled on the Windows workflow.</summary>
public sealed class ArcticGlassUiContractTests
{
    private static string Source(string relative)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
        }
        throw new FileNotFoundException("Cannot locate UI contract source: " + relative);
    }

    [Fact]
    public void ArcticThemeIsAReadableLightPaletteForAllAppSurfaces()
    {
        string theme = Source("src/RangeDesk.App/Themes/V5Theme.xaml");
        Assert.Contains("x:Key=\"V5CanvasBrush\" Color=\"#F2F7FE\"", theme);
        Assert.Contains("x:Key=\"V5SidebarBrush\" Color=\"#F8FBFF\"", theme);
        Assert.Contains("x:Key=\"V5TextBrush\" Color=\"#192F49\"", theme);
        Assert.Contains("x:Key=\"V5LightSurface\" Color=\"#FFFFFF\"", theme);
        Assert.Contains("x:Key=\"V5AccentBrush\" Color=\"#147DF3\"", theme);
    }

    [Fact]
    public void LabelsDisappearFromVisibleDownloadsAndNewDownloadForms()
    {
        string xaml = Source("src/RangeDesk.App/MainWindow.xaml");
        Assert.DoesNotContain("Header=\"Label\"", xaml);
        Assert.DoesNotContain("Text=\"Labels (optional", xaml);
        Assert.Contains("Legacy labels are visually retired", xaml);
        Assert.Contains("<StackPanel Visibility=\"Collapsed\">", xaml);
        Assert.Contains("x:Name=\"LabelFilter\" Visibility=\"Collapsed\"", xaml);
        Assert.Contains("x:Name=\"NewLabels\" Visibility=\"Collapsed\"", xaml);
        Assert.Contains("x:Name=\"RuleLabel\" Visibility=\"Collapsed\"", xaml);
        Assert.Contains("Sites / Groups", xaml);
    }

    [Fact]
    public void DesktopDialogsShareArcticSurfacesAndDetailsActionsAreScoped()
    {
        string xaml = Source("src/RangeDesk.App/MainWindow.xaml");
        string code = Source("src/RangeDesk.App/MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"AddOverlay\" Grid.ColumnSpan=\"2\"", xaml);
        Assert.Contains("Width=\"540\" MaxHeight=\"660\"", xaml);
        Assert.Contains("Width=\"820\" MaxHeight=\"720\"", xaml);
        Assert.Contains("Click=\"DetailsResume_Click\"", xaml);
        Assert.Contains("Click=\"DetailsPause_Click\"", xaml);
        Assert.Contains("private void DetailsPause_Click", code);
        Assert.Contains("private void DetailsResume_Click", code);
        Assert.Contains("SelectedDownload is { IsMedia: false", code);
    }
}
