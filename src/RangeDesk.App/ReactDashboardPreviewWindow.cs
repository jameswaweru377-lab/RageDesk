using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace RangeDesk.App;

/// <summary>
/// Opt-in local React renderer of the *existing* MainWindow. No duplicate engine or media
/// host is created. Commands are allowlisted, ID-scoped and run through existing native services.
/// </summary>
internal sealed class ReactDashboardPreviewWindow : Window
{
    private const string AssetHost = "ragedesk-app.example";
    private static readonly Uri StartAddress = new($"https://{AssetHost}/index.html");
    private readonly MainWindow _source;
    private readonly WebView2 _browser = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _ready;
    private bool? _frameDarkMode;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private bool _closed;
    private string? _lastSnapshot;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ReactDashboardPreviewWindow(MainWindow owner)
    {
        _source = owner;
        Owner = owner;
        Title = "RageDesk — New UI";
        Width = 1480;
        Height = 900;
        MinWidth = 1000;
        MinHeight = 660;
        Background = new SolidColorBrush(Color.FromRgb(4, 17, 36));
        Content = _browser;
        SourceInitialized += (_, _) => UpdateNativeFrameTheme(
            _source.BuildReactPreviewSnapshot().Theme != RangeDeskTheme.Arctic);
        Loaded += async (_, _) => await InitializePreviewAsync();
        _refresh.Tick += (_, _) => PublishSnapshot();
        Closed += (_, _) =>
        {
            _closed = true;
            _ready = false;
            _refresh.Stop();
            _browser.Dispose();
        };
    }

    private async Task InitializePreviewAsync()
    {
        var assetDirectory = Path.Combine(AppContext.BaseDirectory, "web");
        if (!File.Exists(Path.Combine(assetDirectory, "index.html")))
        {
            ShowPreviewError("The optional React dashboard assets are not installed. " +
                             "The existing WPF RageDesk interface remains available.");
            return;
        }
        try
        {
            string profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RangeDesk", "webview2-preview");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory);
            if (_closed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_closed) return;
            var core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsWebMessageEnabled = true;

            core.SetVirtualHostNameToFolderMapping(AssetHost, assetDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) =>
            {
                // Do not allow redirects, arbitrary external sites or browser-originated navigation.
                if (!string.Equals(args.Uri, StartAddress.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                    args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) ||
                    !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                    !uri.Host.Equals(AssetHost, StringComparison.OrdinalIgnoreCase) ||
                    !uri.IsDefaultPort)
                {
                    args.Response = core.Environment.CreateWebResourceResponse(
                        new MemoryStream(Array.Empty<byte>()), 403, "Blocked", "Content-Type: text/plain");
                }
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (!string.Equals(args.Source, StartAddress.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                    return;
                string json = args.WebMessageAsJson;
                if (json.Length > 4096) return;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("version", out var version) ||
                        version.ValueKind != JsonValueKind.Number ||
                        !version.TryGetInt32(out int number) || number != 1 ||
                        !root.TryGetProperty("type", out var type) ||
                        type.ValueKind != JsonValueKind.String ||
                        type.GetString() is not ("dashboard.ready" or "ui.openSettings" or
                            "ui.openAddDownload" or "ui.download" or "ui.browser" or "ui.setting" or
                            "ui.submit" or "ui.schedule" or "ui.folder"))
                        return;
                    string? messageType = type.GetString();
                    if (messageType == "dashboard.ready")
                    {
                        _ready = true;
                        PublishSnapshot();
                        _refresh.Start();
                    }
                    else if (_ready && messageType is "ui.openSettings" or "ui.openAddDownload")
                    {
                        _source.OpenNativeDialogFromReact(messageType);
                    }
                    else if (_ready && messageType is "ui.download" or "ui.browser" or "ui.setting" or "ui.submit" or "ui.schedule" or "ui.folder")
                    {
                        ExecuteNativeCommand(root, messageType);
                    }
                }
                catch (JsonException) { /* Untrusted renderer messages are ignored. */ }
            };
            _browser.Source = StartAddress;
        }
        catch (Exception)
        {
            // Do not record query strings, local file paths or content details in crash logs.
            ShowPreviewError("React preview could not start (WebView2 Runtime or bundled assets " +
                             "may be unavailable). Your original WPF application is unchanged.");
        }
    }


    internal bool OfferCapturedLink(string address)
    {
        if (!_ready || _closed || !IsVisible || _browser.CoreWebView2 is null ||
            address.Length is < 8 or > 3500 ||
            !Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            !string.IsNullOrWhiteSpace(parsed.UserInfo))
            return false;
        // This user-selected link is sent only to our bundled, CSP-locked local UI.
        // It is never included in telemetry, logs, or periodic dashboard snapshots.
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            version = 1, type = "ui.browserCaptured", payload = new { address }
        }, _json));
        Activate();
        return true;
    }

    private void ExecuteNativeCommand(JsonElement root, string messageType)
    {
        // Only the bundled, locally mapped, top-level origin is permitted. The caller
        // may identify an existing download, but never supply a file path, URL or shell command.
        if (!root.TryGetProperty("requestId", out var request) ||
            request.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(request.GetString(), "D", out var requestId) ||
            !root.TryGetProperty("action", out var action) ||
            action.ValueKind != JsonValueKind.String)
            return;

        string actionName = action.GetString() ?? "";
        if (actionName.Length is < 2 or > 32 || !actionName.All(c => char.IsAsciiLetter(c)))
            return;
        string message;
        try
        {
            if (messageType == "ui.browser")
            {
                if (root.TryGetProperty("ids", out _)) return;
                message = _source.ExecuteReactBrowserAction(actionName);
            }
            else if (messageType == "ui.submit")
            {
                if (actionName != "addDownload" ||
                    !root.TryGetProperty("address", out var address) ||
                    address.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("queued", out var queued) ||
                    queued.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return;
                message = _source.AddReactDownload(address.GetString() ?? "", queued.GetBoolean());
            }
            else if (messageType == "ui.schedule")
            {
                if (actionName != "schedule" ||
                    !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                    !Guid.TryParseExact(id.GetString(), "N", out var parsedId) ||
                    !root.TryGetProperty("localTime", out var localTime) ||
                    localTime.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("recurrence", out var frequency) ||
                    frequency.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("remove", out var remove) ||
                    remove.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return;
                string timeText = localTime.GetString() ?? "";
                string recurrenceText = frequency.GetString() ?? "";
                if (timeText.Length != 16 || recurrenceText.Length > 9) return;
                message = _source.SaveReactSchedule(parsedId, timeText, recurrenceText, remove.GetBoolean());
            }
            else if (messageType == "ui.folder")
            {
                if (actionName != "browse") return;
                message = _source.ChooseReactDownloadFolder(this);
            }
            else if (messageType == "ui.setting")
            {
                if (root.TryGetProperty("ids", out _) ||
                    !root.TryGetProperty("value", out var value))
                    return;
                message = _source.UpdateReactPreference(actionName, value);
            }
            else
            {
                if (!root.TryGetProperty("ids", out var idsJson) ||
                    idsJson.ValueKind != JsonValueKind.Array ||
                    idsJson.GetArrayLength() > 64)
                    return;
                var ids = new List<Guid>();
                foreach (var id in idsJson.EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.String ||
                        !Guid.TryParseExact(id.GetString(), "N", out var parsed))
                        return;
                    ids.Add(parsed);
                }
                message = _source.ExecuteReactDownloadAction(actionName, ids, this);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Never return download URLs, paths, tokens or exception messages to JS.
            message = "Native action failed: " + ex.GetType().Name;
        }
        if (_closed || _browser.CoreWebView2 is null) return;
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            version = 1,
            type = "ui.actionResult",
            payload = new { requestId = requestId.ToString("D"), message }
        }, _json));
        PublishSnapshot();
    }

    private void UpdateNativeFrameTheme(bool dark)
    {
        if (_frameDarkMode == dark) return;
        _frameDarkMode = dark;
        try
        {
            int enabled = dark ? 1 : 0;
            // Windows DWM paints the standard native caption; the HTML theme cannot style it.
            // 20 is supported on modern Windows 10/11; 19 covers some earlier Windows builds.
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { /* Keep the standard Windows frame. */ }
        catch (EntryPointNotFoundException) { }
    }

    private void PublishSnapshot()
    {
        if (!_ready || _closed || !IsVisible || _browser.CoreWebView2 is null) return;
        // The source is the same live WPF window, so no second engine or duplicated queue is created.
        var snapshot = _source.BuildReactPreviewSnapshot();
        UpdateNativeFrameTheme(snapshot.Theme != RangeDeskTheme.Arctic);
        string json = JsonSerializer.Serialize(new
        {
            version = 1,
            type = "dashboard.snapshot",
            payload = snapshot
        }, _json);
        if (json == _lastSnapshot) return;
        _lastSnapshot = json;
        _browser.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void ShowPreviewError(string message)
    {
        if (_closed) return;
        Content = new Border
        {
            Background = Background,
            Padding = new Thickness(35),
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }
}
