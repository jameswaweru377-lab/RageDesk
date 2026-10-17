using Xunit;

namespace RangeDesk.Tests;

public sealed class ReactWebView2MigrationContractTests
{
    private static string Read(string relativePath)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void ReactIsPrimaryWhileWpfStillOwnsTheNativeEngine()
    {
        string app = Read("src/RangeDesk.App/App.xaml");
        string main = Read("src/RangeDesk.App/MainWindow.xaml.cs");
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", app);
        Assert.Contains("--wpf-classic", main);
        Assert.Contains("ReactDashboardReady", main);
        Assert.Contains("new ReactDashboardPreviewWindow(this)", main);
        Assert.Contains("_engine = new DownloadEngine", main);
        Assert.Contains("LoadCatalog()", main);
        Assert.Contains("CheckAutomaticBrowserCaptures()", main);
    }

    [Fact]
    public void BridgeAcceptsOnlyReadyAndPublishesReadOnlyPrivateSafeData()
    {
        string bridge = Read("src/RangeDesk.App/ReactDashboardPreviewWindow.cs");
        string shape = Read("src/RangeDesk.App/ReactDashboardSnapshot.cs");
        Assert.Contains("dashboard.ready", bridge);
        Assert.Contains("dashboard.snapshot", bridge);
        Assert.Contains("TryGetProperty(\"version\"", bridge);
        Assert.Contains("args.Source", bridge);
        Assert.Contains("DenyCors", bridge);
        Assert.Contains("core.NewWindowRequested", bridge);
        Assert.Contains("core.PermissionRequested", bridge);
        Assert.Contains("core.WebResourceRequested", bridge);
        Assert.Contains("CoreWebView2PermissionState.Deny", bridge);
        Assert.DoesNotContain("DownloadEngine(", bridge);
        Assert.DoesNotContain("Process.Start", bridge);
        Assert.DoesNotContain("HttpClient", bridge);
        Assert.DoesNotContain("Url =", shape);
        Assert.DoesNotContain("Destination =", shape);
        Assert.Contains("Take(500)", shape);
        Assert.Contains("knownCompletions.TryGetValue", shape);
        Assert.Contains("DateTime.Today", shape);
    }

    [Fact]
    public void BundleHasOfflineCspAndOriginalApprovedDashboardSections()
    {
        string html = Read("frontend/index.html");
        string ui = Read("frontend/src/App.tsx");
        string workflow = Read(".github/workflows/windows-ci.yml");
        Assert.Contains("connect-src 'none'", html);
        Assert.Contains("script-src 'self'", html);
        Assert.Contains("ACTIVE DOWNLOADS", ui);
        Assert.Contains("CURRENT SPEED", ui);
        Assert.Contains("DOWNLOADED TODAY", ui);
        Assert.Contains("TIME REMAINING", ui);
        Assert.Contains("Network &amp; Performance", ui);
        Assert.Contains("Top Sources", ui);
        Assert.Contains("Recent Activities", ui);
        Assert.Contains("working-directory: frontend", workflow);
        Assert.Contains("npm run build", workflow);
        Assert.Contains("dist/index.html", workflow);
        Assert.DoesNotContain("https://fonts.googleapis.com", ui);
    }
}
