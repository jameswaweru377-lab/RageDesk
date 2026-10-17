using Xunit;

namespace RangeDesk.Tests;

/// <summary>
/// Contract guards for the local-only WebView2 renderer/native integration.
/// Windows UI and Chrome extension acceptance remain separate release gates.
/// </summary>
public sealed class ReactNativeActionContractTests
{
    private static string Read(string relative)
    {
        for (DirectoryInfo? d = new(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void NativeBridgeOnlyAcceptsTrustedOriginBoundedMessagesAndCatalogIds()
    {
        string host = Read("src/RangeDesk.App/ReactDashboardPreviewWindow.cs");
        string main = Read("src/RangeDesk.App/MainWindow.xaml.cs");
        Assert.Contains("args.Source", host);
        Assert.Contains("StartAddress.AbsoluteUri", host);
        Assert.Contains("json.Length > 4096", host);
        Assert.Contains("GetArrayLength() > 64", host);
        Assert.Contains("TryParseExact", host);
        Assert.Contains("ui.download", host);
        Assert.Contains("ui.submit", host);
        Assert.Contains("ui.schedule", host);
        Assert.Contains("ui.folder", host);
        Assert.Contains("OfferCapturedLink", host);
        Assert.Contains("AddReactDownload", main);
        Assert.Contains("SaveReactSchedule", main);
        Assert.Contains("ChooseReactDownloadFolder", main);
        Assert.Contains("ui.setting", host);
        Assert.Contains("ui.browser", host);
        Assert.Contains("Downloads.FirstOrDefault(item => item.Id == id)", main);
        Assert.Contains("item.Status == DownloadStatus.Downloading", main);
        Assert.Contains("RangeDeskPreferencesStore.Save", main);
        Assert.Contains("ChromeIntegrationSetup.Register", main);
        Assert.DoesNotContain("Process.Start", host);
        Assert.DoesNotContain("HttpClient", host);
    }

    [Fact]
    public void HistoryRemovalKeepsUserFilesAndRejectsActiveTransfers()
    {
        string main = Read("src/RangeDesk.App/MainWindow.xaml.cs");
        Assert.Contains("action == \"removeHistory\"", main);
        Assert.Contains("Remove history only", main);
        Assert.Contains("DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled", main);
        Assert.Contains("if (!SaveCatalog())", main);
        Assert.Contains("originals", main);
        Assert.Contains("Files were not deleted", main);
        string bridge = Read("frontend/src/nativeBridge.ts");
        Assert.Contains("removeHistory", bridge);
        Assert.Contains("postMessage", bridge);
    }

    [Fact]
    public void BrowserExtensionStillRequiresExplicitUserOptIn()
    {
        string js = Read("browser-capture/chromium/background.js");
        string react = Read("frontend/src/SettingsPage.tsx");
        Assert.Contains("autoCapture = false", js);
        Assert.Contains("sendBrowserAction(\"connect\")", react);
        Assert.Contains("sendBrowserAction(\"extensionFolder\")", react);
        Assert.Contains("sendBrowserAction(\"check\")", react);
        Assert.Contains("Native host connection", react);
    }
}
