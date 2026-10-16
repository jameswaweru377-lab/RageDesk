using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using RangeDesk.Core;

namespace RangeDesk.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DownloadEngine _engine;
    private readonly string _catalogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "catalog-v5.json");
    private readonly string _preferencesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "preferences-v5.json");
    private RangeDeskPreferences _preferences = new();
    private readonly string _schedulesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "schedules-v5.json");
    private readonly Dictionary<string, ScheduledDownload> _scheduled =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _automationFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "automation-rules-v5.json");
    private readonly List<DownloadAutomationRule> _automationRules = new();
    private readonly string _sitePresetsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "site-presets-v5.json");
    private readonly Dictionary<string, CustomSitePreset> _customSites =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _scheduleTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _browserCaptureTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _bandwidthCalendarFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "bandwidth-calendar-v5.json");
    private readonly List<BandwidthCalendarRule> _bandwidthWindows = new();
    private readonly Dictionary<Guid,int> _appliedBandwidthCaps = new();
    private bool _bandwidthUpdateInProgress;
    private DownloadEntry? _editingSchedule;
    private string _navigation = "all";
    private string _downloadTab = "all";
    private string? _categoryNavigation;
    private DownloadEntry? _selected;
    private string? _labelNavigation;
    private string? _siteNavigation;
    private bool _sidebarCollapsed;
    private SidebarSectionExpansion _sidebarSections = new();
    private readonly Dictionary<Guid, DownloadStatus> _lastStatuses = new();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);


    private sealed record SavedDownload(string Url, string Destination, string Category, string Labels, int Connections = 1, int BandwidthKiBps = 0, string? ExpectedSha256 = null, bool Adaptive = false, SafePostDownloadAction PostDownloadAction = SafePostDownloadAction.None);

    public MainWindow()
    {
        InitializeComponent();
        try { _preferences = RangeDeskPreferencesStore.Load(_preferencesFile); }
        catch (Exception ex)
        {
            StatusMessage.Text = "Could not read download settings; using defaults: " + ex.Message;
        }
        _engine = new DownloadEngine(SynchronizationContext.Current,
            maxConcurrentDownloads: _preferences.MaxConcurrentDownloads,
            proxyUri: _preferences.ProxyUri);
        VisibleDownloads = CollectionViewSource.GetDefaultView(Downloads);
        VisibleDownloads.Filter = MatchesFilters;
        DataContext = this;
        LoadCatalog();
        try
        {
            _bandwidthWindows.AddRange(BandwidthCalendarStore.Load(_bandwidthCalendarFile));
            RefreshBandwidthCalendar();
        }
        catch (Exception ex) { StatusMessage.Text = "Could not load bandwidth calendar: " + ex.Message; }
        try
        {
            _automationRules.AddRange(DownloadAutomationRuleStore.Load(_automationFile));
            RefreshAutomationRules();
        }
        catch (Exception ex) { StatusMessage.Text = "Could not load automation rules: " + ex.Message; }
        try
        {
            foreach (var site in CustomSitePresetStore.Load(_sitePresetsFile))
                _customSites.Add(site.Name, site);
            RenderCustomSitePresets();
        }
        catch (Exception ex) { StatusMessage.Text = "Could not load site presets: " + ex.Message; }
        try
        {
            foreach (var scheduled in ScheduledQueueStore.Load(_schedulesFile))
                _scheduled.Add(scheduled.Destination, scheduled);
        }
        catch (Exception ex) { StatusMessage.Text = "Could not read schedules: " + ex.Message; }
        _scheduleTimer.Tick += async (_, _) =>
        {
            RunDueSchedules();
            CheckBrowserInbox();
            await ApplyBandwidthCalendarAsync();
        };
        _scheduleTimer.Start();
        _browserCaptureTimer.Tick += (_, _) => CheckAutomaticBrowserCaptures();
        _browserCaptureTimer.Start();
        CheckAutomaticBrowserCaptures();
        RunDueSchedules(); // An overdue queued item starts when the user reopens RangeDesk.
        CheckBrowserInbox(); // Captured links require manual confirmation.
        Closing += (_, _) => { _scheduleTimer.Stop(); _browserCaptureTimer.Stop(); _engine.Dispose(); };
        SourceInitialized += (_, _) =>
        {
            int enabled = 1;
            try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int)); }
            catch (DllNotFoundException) { }
        };
        RefreshFilter();
        RefreshCounters(); // Includes scheduled jobs loaded after initial binding.
        RefreshDashboard();
    }

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();
    public ICollectionView VisibleDownloads { get; }
    public int DownloadsCount => Downloads.Count;
    public int CompletedCount => Downloads.Count(d => d.Status == DownloadStatus.Completed);
    public int DownloadingCount => Downloads.Count(d => d.Status == DownloadStatus.Downloading);
    public int PausedCount => Downloads.Count(d => d.Status == DownloadStatus.Paused);
    public int PendingCount => Downloads.Count(d => d.Status == DownloadStatus.Queued);
    public int FailedCount => Downloads.Count(d => d.Status == DownloadStatus.Failed);

    public string GreetingText => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening"
    };

    public string ActiveSummaryLabel => $"{DownloadingCount} active download{(DownloadingCount == 1 ? "" : "s")}  ·  {AggregateSpeedLabel}";
    public string AggregateSpeedLabel => DownloadingCount == 0 ? "—" :
        DownloadEntry.FormatSize(Downloads.Where(d => d.Status == DownloadStatus.Downloading)
            .Sum(d => d.BytesPerSecond)) + "/s";

    private IReadOnlyList<DownloadEntry> KnownPendingDownloads() =>
        Downloads.Where(d => d.Status is not (DownloadStatus.Completed or DownloadStatus.Canceled or DownloadStatus.Failed)
                             && d.Total is > 0).ToArray();

    public string AggregateTotalSizeLabel
    {
        get
        {
            var known = KnownPendingDownloads();
            return known.Count == 0 ? "—" : DownloadEntry.FormatSize(known.Sum(d => (double)d.Total!.Value));
        }
    }

    public double AggregateProgressPercent
    {
        get
        {
            var known = KnownPendingDownloads();
            double total = known.Sum(d => (double)d.Total!.Value);
            if (total <= 0) return 0;
            double received = known.Sum(d => (double)Math.Min(d.Received, d.Total!.Value));
            return Math.Clamp(received / total * 100, 0, 100);
        }
    }

    public string AggregateProgressLabel => KnownPendingDownloads().Count == 0
        ? "—" : $"{AggregateProgressPercent:0}%";

    public string AggregateEtaLabel
    {
        get
        {
            var active = Downloads.Where(d => d.Status == DownloadStatus.Downloading).ToArray();
            if (active.Length == 0 || active.Any(d => d.Total is null)) return "—";
            double speed = active.Sum(d => d.BytesPerSecond);
            if (speed <= 0) return "—";
            double remaining = active.Sum(d => Math.Max(0, d.Total!.Value - d.Received));
            double seconds = remaining / speed;
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) return "—";
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m"
                : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s"
                : $"{time.Seconds}s";
        }
    }

    private void RefreshDashboard()
    {
        foreach (var property in new[] {
            nameof(GreetingText), nameof(ActiveSummaryLabel), nameof(AggregateSpeedLabel),
            nameof(AggregateTotalSizeLabel), nameof(AggregateProgressPercent),
            nameof(AggregateProgressLabel), nameof(AggregateEtaLabel) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public int QueueCount => Downloads.Count(d => d.Status != DownloadStatus.Completed);
    public int ScheduledCount => _scheduled.Count;
    public int SoftwareCount => CategoryCount("Software");
    public int VideosCount => CategoryCount("Videos");
    public int DocumentsCount => CategoryCount("Documents");
    public int ArchivesCount => CategoryCount("Archives");
    public int ImagesCount => CategoryCount("Images");
    public int WorkCount => LabelCount("Work");
    public int PersonalCount => LabelCount("Personal");
    public int ResearchCount => LabelCount("Research");
    public int ImportantCount => LabelCount("Important");
    public int MicrosoftCount => SiteCount("Microsoft");
    public int GitHubCount => SiteCount("GitHub");
    public int GoogleDriveCount => SiteCount("Google Drive");
    public int YouTubeCount => SiteCount("YouTube");
    private int SiteCount(string site) => Downloads.Count(d => SitePresets.Matches(d.Host, site));

    private int CategoryCount(string name) => Downloads.Count(
        d => string.Equals(d.Category, name, StringComparison.OrdinalIgnoreCase));
    private int LabelCount(string name) => Downloads.Count(d =>
        d.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Contains(name, StringComparer.OrdinalIgnoreCase));

    private void RefreshCounters()
    {
        foreach (var property in new[] {
            nameof(DownloadsCount), nameof(CompletedCount), nameof(QueueCount), nameof(ScheduledCount),
            nameof(DownloadingCount), nameof(PausedCount), nameof(PendingCount), nameof(FailedCount),
            nameof(SoftwareCount), nameof(VideosCount), nameof(DocumentsCount),
            nameof(ArchivesCount), nameof(ImagesCount),
            nameof(WorkCount), nameof(PersonalCount), nameof(ResearchCount), nameof(ImportantCount),
            nameof(MicrosoftCount), nameof(GitHubCount), nameof(GoogleDriveCount), nameof(YouTubeCount) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        RefreshCustomSiteCounters();
    }
    public DownloadEntry? SelectedDownload
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            if (value is null && DetailsOverlay.Visibility == Visibility.Visible)
                DetailsOverlay.Visibility = Visibility.Collapsed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDownload)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadCatalog()
    {
        try
        {
            if (!File.Exists(_catalogFile)) return;
            var saved = JsonSerializer.Deserialize<List<SavedDownload>>(File.ReadAllText(_catalogFile));
            if (saved is null) return;
            foreach (var record in saved)
            {
                try
                {
                    var item = _engine.Create(record.Url, record.Destination,
                        record.Category, record.Labels, record.Connections is 1 or 2 or 4 or 8 or 16 or 32 ? record.Connections : 1,
                        record.BandwidthKiBps is >= 0 and <= 1048576 ? record.BandwidthKiBps : 0,
                        record.ExpectedSha256, record.Adaptive, record.PostDownloadAction);
                    if (File.Exists(item.Destination))
                        item.Report(DownloadStatus.Completed,
                            new FileInfo(item.Destination).Length, new FileInfo(item.Destination).Length);
                    else if (File.Exists(item.Destination + ".rdpartial"))
                        item.Report(DownloadStatus.Paused,
                            new FileInfo(item.Destination + ".rdpartial").Length, null);
                    Track(item);
                }
                catch (Exception) { /* One malformed catalog entry must not block startup. */ }
            }
        }
        catch (Exception ex)
        {
            StatusMessage.Text = "Could not load the old download list: " + ex.Message;
        }
    }

    private bool SaveCatalog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogFile)!);
            var saved = Downloads.Select(d => new SavedDownload(d.Url, d.Destination, d.Category, d.Labels, d.RequestedConnections, d.BandwidthKiBps, d.ExpectedSha256, d.Adaptive, d.PostDownloadAction));
            string temp = _catalogFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, _catalogFile, true);
            return true;
        }
        catch (Exception ex) { StatusMessage.Text = "Could not save download history: " + ex.Message; return false; }
    }

    private void Track(DownloadEntry item)
    {
        _lastStatuses[item.Id] = item.Status;
        Downloads.Add(item);
        RefreshCounters();
        RefreshDashboard();
        item.PropertyChanged += (_, _) =>
        {
            void Update()
            {
                // Update aggregate values while retaining the existing row objects.
                // Filter only when state changes; per-byte filtering causes flicker.
                RefreshDashboard();
                if (_lastStatuses.TryGetValue(item.Id, out var old) && old != item.Status)
                {
                    _lastStatuses[item.Id] = item.Status;
                    RefreshCounters();
                    if (item.Status == DownloadStatus.Failed)
                        StatusMessage.Text = "Download failed: " + item.Name +
                            ". Select it to inspect the error and resume.";
                    else if (item.Status == DownloadStatus.Completed)
                    {
                        StatusMessage.Text = "Download completed: " + item.Name;
                        ExecutePostDownloadAction(item, old);
                    }
                    RefreshFilter();
                }
            }
            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.BeginInvoke((Action)Update);
        };
        if (SelectedDownload is null) SelectedDownload = item;
        RefreshFilter();
    }

    private static SafePostDownloadAction SelectedPostDownloadAction(ComboBox selector) =>
        Enum.Parse<SafePostDownloadAction>(
            (selector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None");

    private void ExecutePostDownloadAction(DownloadEntry item, DownloadStatus previous)
    {
        if (!PostDownloadActionPolicy.ShouldRun(item.PostDownloadAction,
                previous, item.Status, File.Exists(item.Destination))) return;
        try
        {
            switch (item.PostDownloadAction)
            {
                case SafePostDownloadAction.PlayNotificationSound:
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
                case SafePostDownloadAction.OpenContainingFolder:
                    string? folder = Path.GetDirectoryName(item.Destination);
                    if (folder is not null && Directory.Exists(folder))
                        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage.Text = "Downloaded " + item.Name +
                ", but its optional post-download action failed: " + ex.GetType().Name;
        }
    }

    private bool MatchesFilters(object candidate)
    {
        if (candidate is not DownloadEntry item) return false;
        DownloadStatus? status = null;
        if (StatusFilter?.SelectedItem is ComboBoxItem state
            && state.Content is string label
            && Enum.TryParse<DownloadStatus>(label, ignoreCase: true, out var selected))
            status = selected;

        string? category = _categoryNavigation;
        if (CategoryFilter?.SelectedItem is ComboBoxItem categoryOption
            && categoryOption.Content is string selection && selection != "All categories")
        {
            if (category is not null && !category.Equals(selection, StringComparison.OrdinalIgnoreCase))
                return false;
            category = selection;
        }

        string? labelFilter = _labelNavigation;
        if (LabelFilter?.SelectedItem is ComboBoxItem labelOption &&
            labelOption.Content is string selectedLabel && selectedLabel != "All labels")
        {
            if (labelFilter is not null && !labelFilter.Equals(selectedLabel, StringComparison.OrdinalIgnoreCase))
                return false;
            labelFilter = selectedLabel;
        }
        if (!string.Equals(_downloadTab, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<DownloadStatus>(_downloadTab, true, out var tabStatus)
                || item.Status != tabStatus) return false;
        }
        if (_siteNavigation is not null && !MatchesSite(item.Host, _siteNavigation))
            return false;
        if (_navigation == "scheduled" && !_scheduled.ContainsKey(item.Destination)) return false;
        return DownloadListVisibility.Matches(item, _navigation == "scheduled" ? "all" : _navigation, GlobalSearch?.Text,
            status, category, labelFilter);
    }

    private void RefreshFilter()
    {
        if (VisibleDownloads is null) return;
        VisibleDownloads.Refresh();
        UpdateTabAppearance();
        if (SidebarPanel is not null)
        {
            foreach (var button in Descendants<Button>(SidebarPanel))
            {
                if (button.Tag is not string tag || !button.IsEnabled) continue;
                bool selected = (_categoryNavigation is null && _labelNavigation is null &&
                                 _siteNavigation is null && tag == _navigation)
                             || (_categoryNavigation is not null && tag == _categoryNavigation)
                             || (_labelNavigation is not null && tag == _labelNavigation)
                             || (_siteNavigation is not null && tag == _siteNavigation);
                button.Background = selected
                    ? (System.Windows.Media.Brush)FindResource("V5SelectedBrush")
                    : System.Windows.Media.Brushes.Transparent;
            }
        }
        if (EmptyState is not null)
            EmptyState.Visibility = VisibleDownloads.Cast<object>().Any()
                ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = button.Tag as string ?? "all";
        _downloadTab = "all";
        _categoryNavigation = null;
        _labelNavigation = null;
        _siteNavigation = null;
        ViewTitle.Text = _navigation == "completed" ? "Completed" :
            _navigation == "queue" ? "Queue" : _navigation == "scheduled" ? "Scheduled" : "Downloads";
        RefreshFilter();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = "all";
        _downloadTab = "all";
        _categoryNavigation = button.Tag as string;
        _labelNavigation = null;
        _siteNavigation = null;
        ViewTitle.Text = _categoryNavigation ?? "Downloads";
        RefreshFilter();
    }

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = "all";
        _downloadTab = "all";
        _categoryNavigation = null;
        _labelNavigation = button.Tag as string;
        _siteNavigation = null;
        ViewTitle.Text = _labelNavigation is null ? "Downloads" : "Label: " + _labelNavigation;
        RefreshFilter();
    }

    private void SitePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _siteNavigation = button.Tag as string;
        _navigation = "all";
        _downloadTab = "all";
        _categoryNavigation = null;
        _labelNavigation = null;
        ViewTitle.Text = _siteNavigation?.StartsWith("custom:", StringComparison.Ordinal) == true
            ? _siteNavigation["custom:".Length..] : _siteNavigation ?? "Downloads";
        RefreshFilter();
    }

    private bool MatchesSite(string host, string preset)
    {
        if (preset.StartsWith("custom:", StringComparison.Ordinal))
            return _customSites.TryGetValue(preset["custom:".Length..], out var custom)
                && custom.MatchesHost(host);
        return SitePresets.Matches(host, preset);
    }

    private void RenderCustomSitePresets()
    {
        CustomSitesPanel.Children.Clear();
        foreach (var site in _customSites.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Button
            {
                Tag = "custom:" + site.Name,
                ToolTip = site.HostSuffix,
                Style = (Style)FindResource("SidebarRowStyle")
            };
            row.Click += SitePreset_Click;

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "\uE774",
                FontFamily = (FontFamily)FindResource("V5IconFont"),
                FontSize = 18,
                Foreground = (Brush)FindResource("V5AccentLightBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 24,
                TextAlignment = TextAlignment.Center
            };
            content.Children.Add(icon);
            var caption = new TextBlock
            {
                Text = site.Name, Tag = "NavCaption", FontSize = 12,
                Foreground = (Brush)FindResource("V5TextBrush"),
                Margin = new Thickness(5, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(caption, 1);
            content.Children.Add(caption);

            var count = new TextBlock
            {
                Tag = "CustomSiteCount", FontSize = 10, FontWeight = FontWeights.SemiBold,
                Text = Downloads.Count(d => site.MatchesHost(d.Host)).ToString(),
                Foreground = (Brush)FindResource("V5AccentLightBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var badge = new Border
            {
                Tag = "NavBadge",
                MinWidth = 29, Height = 23,
                Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(20, 54, 89)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = count
            };
            Grid.SetColumn(badge, 2);
            content.Children.Add(badge);
            row.Content = content;

            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Remove preset" };
            remove.Click += (_, _) => RemoveCustomSite(site.Name);
            menu.Items.Add(remove);
            row.ContextMenu = menu;
            CustomSitesPanel.Children.Add(row);
        }
        RefreshFilter();
    }

    private void RefreshCustomSiteCounters()
    {
        if (CustomSitesPanel is null) return;
        foreach (var button in CustomSitesPanel.Children.OfType<Button>())
        {
            if (button.Tag is not string tag || !tag.StartsWith("custom:", StringComparison.Ordinal))
                continue;
            if (!_customSites.TryGetValue(tag["custom:".Length..], out var site))
                continue;
            foreach (var text in Descendants<TextBlock>(button))
                if (text.Tag as string == "CustomSiteCount")
                    text.Text = Downloads.Count(d => site.MatchesHost(d.Host)).ToString();
        }
    }

    private void AddSitePreset_Click(object sender, RoutedEventArgs e)
    {
        CustomSiteName.Clear();
        CustomSiteDomain.Clear();
        CustomSiteError.Text = "";
        CustomSiteOverlay.Visibility = Visibility.Visible;
        CustomSiteName.Focus();
    }

    private void CloseSitePreset_Click(object sender, RoutedEventArgs e) =>
        CustomSiteOverlay.Visibility = Visibility.Collapsed;

    private void SaveSitePreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var site = new CustomSitePreset(CustomSiteName.Text, CustomSiteDomain.Text).Validate();
            if (_customSites.ContainsKey(site.Name))
                throw new ArgumentException("A site preset with that name already exists.");
            CustomSitePresetStore.Save(_sitePresetsFile, _customSites.Values.Append(site));
            _customSites.Add(site.Name, site);
            RenderCustomSitePresets();
            CustomSiteOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = "Site preset saved: " + site.Name;
        }
        catch (Exception ex) { CustomSiteError.Text = ex.Message; }
    }

    private void RemoveCustomSite(string name)
    {
        if (!_customSites.ContainsKey(name)) return;
        if (MessageBox.Show(this, "Remove site filter '" + name + "'? Downloaded files will not be deleted.",
            "Remove site preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            CustomSitePresetStore.Save(_sitePresetsFile,
                _customSites.Values.Where(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            _customSites.Remove(name);
            if (string.Equals(_siteNavigation, "custom:" + name, StringComparison.OrdinalIgnoreCase))
                _siteNavigation = null;
            RenderCustomSitePresets();
            RefreshFilter();
        }
        catch (Exception ex) { StatusMessage.Text = "Cannot remove site preset: " + ex.Message; }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void ToggleSidebarSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section }) return;
        _sidebarSections = _sidebarSections.Toggle(section);
        UpdateSidebarSectionVisibility();
    }

    private void UpdateSidebarSectionVisibility()
    {
        CategoryItems.Visibility = _sidebarSections.IsVisible("categories", _sidebarCollapsed)
            ? Visibility.Visible : Visibility.Collapsed;
        LabelItems.Visibility = _sidebarSections.IsVisible("labels", _sidebarCollapsed)
            ? Visibility.Visible : Visibility.Collapsed;
        SiteItems.Visibility = _sidebarSections.IsVisible("sites", _sidebarCollapsed)
            ? Visibility.Visible : Visibility.Collapsed;
        CategoriesChevron.Text = _sidebarSections.Categories ? "⌄" : "›";
        LabelsChevron.Text = _sidebarSections.Labels ? "⌄" : "›";
        SitesChevron.Text = _sidebarSections.Sites ? "⌄" : "›";
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 75 : 244);
        SidebarDock.Margin = _sidebarCollapsed
            ? new Thickness(4, 14, 4, 10)
            : new Thickness(12, 14, 12, 10);
        SidebarBrandColumn.Width = new GridLength(_sidebarCollapsed ? 0 : 43);
        SidebarBrand.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarBrandIcon.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // Keep the group rows reachable as icons in compact mode. Only the
        // text headers disappear; original dropdown preferences are retained.
        SidebarSections.Visibility = Visibility.Visible;
        CategoriesHeader.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LabelsHeader.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SitesHeader.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        AutomationNavButton.Visibility = Visibility.Visible;
        UpdateSidebarSectionVisibility(); // Reveal group icons before recomputing their compact geometry.

        foreach (var caption in Descendants<TextBlock>(SidebarPanel))
        {
            if (caption.Tag as string == "NavCaption")
                caption.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        foreach (var badge in Descendants<Border>(SidebarPanel).Where(x => x.Tag as string == "NavBadge"))
            badge.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // A three-column nav grid still reserves its first 31px icon rail
        // when text is hidden. Redistribute that space so every icon is
        // centered and clickable instead of clipped against the edge.
        foreach (var button in Descendants<Button>(SidebarPanel))
        {
            if (button.Content is not Grid grid || grid.ColumnDefinitions.Count != 3)
                continue;
            button.Padding = _sidebarCollapsed
                ? new Thickness(3, 5, 3, 5)
                : new Thickness(10, 5, 10, 5);
            grid.ColumnDefinitions[0].Width = _sidebarCollapsed
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(31);
            grid.ColumnDefinitions[1].Width = _sidebarCollapsed
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[2].Width = _sidebarCollapsed
                ? new GridLength(0)
                : GridLength.Auto;
        }

        SidebarToggle.ToolTip = _sidebarCollapsed ? "Expand navigation" : "Collapse navigation";
        UpdateSidebarSectionVisibility();
    }

    /// <summary>
    /// Show a submitted download where it stays visible through state changes.
    /// </summary>
    private void ShowSubmittedDownload(bool addToQueue)
    {
        _navigation = "all";
        _downloadTab = addToQueue ? "Queued" : "all";
        _categoryNavigation = null;
        _labelNavigation = null;
        _siteNavigation = null;
        ViewTitle.Text = addToQueue ? "Queue" : "Downloads";
        if (StatusFilter is not null) StatusFilter.SelectedIndex = 0;
        if (CategoryFilter is not null) CategoryFilter.SelectedIndex = 0;
        if (LabelFilter is not null) LabelFilter.SelectedIndex = 0;
        if (GlobalSearch is not null) GlobalSearch.Clear();
        RefreshFilter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null && GlobalSearch is not null)
            SearchHint.Visibility = string.IsNullOrEmpty(GlobalSearch.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        RefreshFilter();
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || GlobalSearch is null ||
            !DownloadEngine.IsAllowedUrl(GlobalSearch.Text.Trim())) return;
        Add_Click(sender, new RoutedEventArgs());
        NewUrl.Text = GlobalSearch.Text.Trim();
        GlobalSearch.Clear();
        e.Handled = true;
    }

    private void DownloadTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button tab) return;
        _downloadTab = tab.Tag as string ?? "all";
        _navigation = "all";
        _categoryNavigation = null;
        _labelNavigation = null;
        _siteNavigation = null;
        if (StatusFilter is not null) StatusFilter.SelectedIndex = 0;
        RefreshFilter();
    }

    private void UpdateTabAppearance()
    {
        if (TabAll is null) return;
        foreach (var tab in new[] { TabAll, TabDownloading, TabPaused, TabQueued, TabCompleted, TabFailed })
        {
            bool selected = (tab.Tag as string ?? "all").Equals(_downloadTab, StringComparison.OrdinalIgnoreCase);
            tab.Background = (Brush)FindResource(selected ? "V5LightBlue" : "V5LightCanvas");
            tab.Foreground = selected ? Brushes.White : (Brush)FindResource("V5LightText");
        }
    }

    private bool _cardsView;
    private void ListView_Click(object sender, RoutedEventArgs e) => SetViewMode(false);
    private void CardsView_Click(object sender, RoutedEventArgs e) => SetViewMode(true);

    private void SetViewMode(bool cards)
    {
        _cardsView = cards;
        if (CardsPanel is not null) CardsPanel.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        if (DownloadsGrid is not null) DownloadsGrid.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        if (ListViewButton is not null)
            ListViewButton.Background = (Brush)FindResource(cards ? "V5LightSurface" : "V5SelectedBrush");
        if (CardsViewButton is not null)
            CardsViewButton.Background = (Brush)FindResource(cards ? "V5SelectedBrush" : "V5LightSurface");
    }

    private void DownloadCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DownloadEntry entry }) return;
        SelectedDownload = entry;
        DownloadsGrid.SelectedItem = entry;
        ShowDownloadDetails();
    }

    private void SortChooser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VisibleDownloads is null || !VisibleDownloads.CanSort || SortChooser is null) return;
        using (VisibleDownloads.DeferRefresh())
        {
            VisibleDownloads.SortDescriptions.Clear();
            switch (SortChooser.SelectedIndex)
            {
                case 1:
                    VisibleDownloads.SortDescriptions.Add(
                        new SortDescription(nameof(DownloadEntry.AddedAt), ListSortDirection.Descending));
                    break;
                case 2:
                    VisibleDownloads.SortDescriptions.Add(
                        new SortDescription(nameof(DownloadEntry.Name), ListSortDirection.Ascending));
                    break;
                case 3:
                    VisibleDownloads.SortDescriptions.Add(
                        new SortDescription(nameof(DownloadEntry.BytesPerSecond), ListSortDirection.Descending));
                    break;
            }
        }
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e) =>
        ExtraFilters.IsOpen = !ExtraFilters.IsOpen;

    private void PasteLink_Click(object sender, RoutedEventArgs e)
    {
        Add_Click(sender, e);
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (DownloadEngine.IsAllowedUrl(text)) NewUrl.Text = text;
            }
        }
        catch (System.Runtime.InteropServices.ExternalException) { /* Clipboard busy; manual entry still works. */ }
    }

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = _preferences.DownloadFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void RowQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DownloadEntry entry }) return;
        SelectedDownload = entry;
        if (entry.Status == DownloadStatus.Completed)
        {
            OpenFolder_Click(sender, e);
            return;
        }
        if (entry.Status == DownloadStatus.Downloading) _engine.Pause(entry);
        else StartManually(entry);
        e.Handled = true;
    }

    private void RowMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DownloadEntry entry } button) return;
        SelectedDownload = entry;
        var menu = new ContextMenu
        {
            Background = (Brush)FindResource("V5ElevatedBrush"),
            BorderBrush = (Brush)FindResource("V5OutlineBrush"),
            Foreground = (Brush)FindResource("V5TextBrush")
        };
        var details = new MenuItem { Header = "View details..." };
        details.Click += (_, _) => { SelectedDownload = entry; ShowDownloadDetails(); };
        menu.Items.Add(details);
        menu.Items.Add(new Separator());
        var resume = new MenuItem { Header = "Resume", IsEnabled = entry.Status != DownloadStatus.Completed };
        resume.Click += (_, _) => StartManually(entry);
        var pause = new MenuItem { Header = "Pause", IsEnabled = entry.Status == DownloadStatus.Downloading };
        pause.Click += (_, _) => _engine.Pause(entry);
        var stop = new MenuItem { Header = "Stop", IsEnabled = entry.Status != DownloadStatus.Completed };
        stop.Click += (_, _) => StopManually(entry);
        var folder = new MenuItem { Header = "Open folder" };
        folder.Click += (_, _) => OpenFolder_Click(button, new RoutedEventArgs());
        menu.Items.Add(resume);
        menu.Items.Add(pause);
        menu.Items.Add(stop);
        var editSchedule = new MenuItem
        {
            Header = _scheduled.ContainsKey(entry.Destination) ? "Edit scheduled start..." : "Schedule start...",
            IsEnabled = entry.Status == DownloadStatus.Queued
        };
        editSchedule.Click += (_, _) => OpenScheduleEditor(entry);
        menu.Items.Add(editSchedule);
        menu.Items.Add(new Separator());
        var startQueue = new MenuItem { Header = "Start all queued downloads" };
        startQueue.Click += StartQueue_Click;
        var earlier = new MenuItem { Header = "Move earlier", IsEnabled = Downloads.IndexOf(entry) > 0 };
        earlier.Click += (_, _) => MoveQueue(entry, -1);
        var later = new MenuItem { Header = "Move later", IsEnabled = Downloads.IndexOf(entry) < Downloads.Count - 1 };
        later.Click += (_, _) => MoveQueue(entry, 1);
        menu.Items.Add(startQueue);
        menu.Items.Add(earlier);
        menu.Items.Add(later);
        menu.Items.Add(new Separator());
        menu.Items.Add(folder);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void MoveQueue(DownloadEntry item, int direction)
    {
        int index = Downloads.IndexOf(item);
        int destination = index + direction;
        if (index < 0 || destination < 0 || destination >= Downloads.Count) return;
        Downloads.Move(index, destination);
        SelectedDownload = item;
        SaveCatalog(); // queue ordering survives application restart
        RefreshFilter();
    }

    private void OpenScheduleEditor(DownloadEntry entry)
    {
        if (entry.Status != DownloadStatus.Queued) return;
        _editingSchedule = entry;
        ScheduledEditTarget.Text = entry.Name;
        ScheduledEditError.Text = "";
        var existing = _scheduled.GetValueOrDefault(entry.Destination);
        DateTime local = existing?.DueAt.ToLocalTime().DateTime ?? DateTime.Now.AddMinutes(30);
        ScheduledEditDate.SelectedDate = local.Date;
        ScheduledEditTime.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        ScheduleRecurrence frequency = existing?.Recurrence ?? ScheduleRecurrence.Once;
        for (int i = 0; i < ScheduledEditRecurrence.Items.Count; i++)
            if (ScheduledEditRecurrence.Items[i] is ComboBoxItem option &&
                option.Tag?.ToString() == frequency.ToString())
            { ScheduledEditRecurrence.SelectedIndex = i; break; }
        ScheduledEditOverlay.Visibility = Visibility.Visible;
    }

    private void CloseEditedSchedule_Click(object sender, RoutedEventArgs e)
    {
        _editingSchedule = null;
        ScheduledEditOverlay.Visibility = Visibility.Collapsed;
    }

    private void SaveEditedSchedule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_editingSchedule is not DownloadEntry item ||
                item.Status != DownloadStatus.Queued)
                throw new InvalidOperationException("Only queued downloads can be scheduled.");
            if (ScheduledEditDate.SelectedDate is not DateTime date ||
                !TimeSpan.TryParseExact(ScheduledEditTime.Text.Trim(), @"hh\:mm",
                    CultureInfo.InvariantCulture, out TimeSpan clock))
                throw new ArgumentException("Enter a date and 24-hour time (HH:mm).");
            var recurrence = Enum.Parse<ScheduleRecurrence>(
                (ScheduledEditRecurrence.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Once");
            DateTimeOffset due = ScheduledQueueStore.FromLocal(
                date, clock, TimeZoneInfo.Local, DateTimeOffset.Now);
            var updated = RecurringSchedulePolicy.Configure(
                new ScheduledDownload(item.Destination, due), recurrence, TimeZoneInfo.Local);
            var next = _scheduled.Values.Where(x =>
                !x.Destination.Equals(item.Destination, StringComparison.OrdinalIgnoreCase))
                .Append(updated).ToArray();
            ScheduledQueueStore.Save(_schedulesFile, next);
            _scheduled[item.Destination] = updated;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScheduledCount)));
            StatusMessage.Text = "Scheduled download: " + item.Name;
            RefreshFilter();
            CloseEditedSchedule_Click(sender, e);
        }
        catch (Exception ex) { ScheduledEditError.Text = ex.Message; }
    }

    private void RemoveEditedSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSchedule is not DownloadEntry item) return;
        try
        {
            var next = _scheduled.Values.Where(x =>
                !x.Destination.Equals(item.Destination, StringComparison.OrdinalIgnoreCase)).ToArray();
            ScheduledQueueStore.Save(_schedulesFile, next);
            _scheduled.Remove(item.Destination);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScheduledCount)));
            StatusMessage.Text = "Schedule removed; download remains queued: " + item.Name;
            RefreshFilter();
            CloseEditedSchedule_Click(sender, e);
        }
        catch (Exception ex) { ScheduledEditError.Text = ex.Message; }
    }

    private void SaveSchedules()
    {
        ScheduledQueueStore.Save(_schedulesFile, _scheduled.Values);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScheduledCount)));
    }

    /// <summary>The user can override a scheduled start by explicitly resuming.</summary>
    private bool StartManually(DownloadEntry item)
    {
        if (!_engine.Start(item)) return false;
        if (_scheduled.Remove(item.Destination))
        {
            SaveSchedules();
            RefreshFilter();
        }
        return true;
    }

    private void StopManually(DownloadEntry item)
    {
        if (_scheduled.Remove(item.Destination))
        {
            SaveSchedules();
            RefreshFilter();
        }
        _engine.Cancel(item);
    }

    private void RunDueSchedules()
    {
        if (_scheduled.Count == 0) return;
        try
        {
            bool changed = false;
            foreach (var scheduled in _scheduled.Values.ToArray())
            {
                var item = Downloads.FirstOrDefault(d =>
                    string.Equals(d.Destination, scheduled.Destination, StringComparison.OrdinalIgnoreCase));
                if (item is null || item.Status is DownloadStatus.Completed or DownloadStatus.Canceled)
                {
                    _scheduled.Remove(scheduled.Destination);
                    changed = true;
                    continue;
                }
                if (!ScheduledQueueStore.IsDue(scheduled, DateTimeOffset.Now)) continue;
                if (item?.Status == DownloadStatus.Queued && _engine.Start(item))
                {
                    _scheduled.Remove(scheduled.Destination);
                    changed = true;
                    StatusMessage.Text = "Scheduled download started: " + item.Name;
                }
                // A missed recurring window remains queued until its next matching window.
            }
            if (changed) { SaveSchedules(); RefreshFilter(); }
        }
        catch (Exception ex) { StatusMessage.Text = "Schedule check failed: " + ex.Message; }
    }

    private void StartQueue_Click(object sender, RoutedEventArgs e)
    {
        int started = 0;
        foreach (var item in Downloads.Where(d => d.Status == DownloadStatus.Queued &&
                                                 !_scheduled.ContainsKey(d.Destination)).ToArray())
            if (StartManually(item)) started++;
        StatusMessage.Text = started > 0
            ? $"Queued transfers started in saved order: {started}"
            : "There are no queued transfers to start.";
    }

    private void ShowDownloadDetails()
    {
        if (SelectedDownload is null) return;
        DetailsOverlay.Visibility = Visibility.Visible;
        DetailsCloseButton.Focus();
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) =>
        DetailsOverlay.Visibility = Visibility.Collapsed;

    private void DownloadGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DownloadsGrid.SelectedItem is not DownloadEntry entry) return;
        SelectedDownload = entry;
        ShowDownloadDetails();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DetailsOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            DetailsOverlay.Visibility = Visibility.Collapsed;
            DownloadsGrid.Focus();
            e.Handled = true;
            return;
        }
        if (DetailsOverlay.Visibility != Visibility.Visible && e.Key == Key.Enter &&
            DownloadsGrid.IsKeyboardFocusWithin && SelectedDownload is not null)
        {
            ShowDownloadDetails();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            GlobalSearch.Focus();
            GlobalSearch.SelectAll();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => RefreshFilter();

    /// <summary>
    /// Opt-in extension-only takeover: acknowledge to the browser ONLY if
    /// RangeDesk has persisted a new entry and started the download engine.
    /// Errors leave the browser paused until native host rejects/resumes it.
    /// </summary>
    private void CheckAutomaticBrowserCaptures()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            BrowserAutomaticCaptureJob? job;
            try { job = BrowserAutomaticCapture.TryTake(BrowserAutomaticCapture.DefaultDirectory); }
            catch (Exception) { return; }
            if (job is null) return;

            bool accepted = false;
            try
            {
                if (DateTimeOffset.UtcNow >= job.ExpiresAtUtc) continue;
                var candidate = BatchDownloadPlanner.Plan(job.Url, _preferences.DownloadFolder,
                    Downloads.Select(d => d.Destination)).Single();
                var decision = DownloadAutomationPolicy.Apply(candidate.Url, candidate.Destination,
                    DownloadCategory.Guess(Path.GetFileName(candidate.Destination)), "",
                    _preferences.DownloadFolder, _automationRules);
                if (Downloads.Any(d => string.Equals(d.Destination, decision.Destination,
                        StringComparison.OrdinalIgnoreCase)) ||
                    File.Exists(decision.Destination) ||
                    File.Exists(decision.Destination + ".rdpartial") ||
                    Directory.Exists(decision.Destination + ".rdsegments") ||
                    Directory.Exists(decision.Destination + ".rdadaptive"))
                    throw new IOException("Automatic download destination is already in use.");

                var item = _engine.Create(job.Url, decision.Destination, decision.Category,
                    decision.Labels, _preferences.DefaultConnections, _preferences.DefaultBandwidthKiBps);
                if (DateTimeOffset.UtcNow >= job.ExpiresAtUtc) continue;
                Track(item);
                if (!SaveCatalog())
                {
                    Downloads.Remove(item);
                    _lastStatuses.Remove(item.Id);
                    RefreshCounters();
                    continue;
                }
                if (!_engine.Start(item))
                {
                    Downloads.Remove(item);
                    _lastStatuses.Remove(item.Id);
                    SaveCatalog();
                    RefreshCounters();
                    continue;
                }
                accepted = true;
                SelectedDownload = item;
                StatusMessage.Text = "Browser download started in RangeDesk: " + item.Name;
            }
            catch (Exception)
            {
                // Never log full URLs (may contain signed query parameters).
                StatusMessage.Text = "Browser capture could not be started; browser download remains available.";
            }
            finally
            {
                try { BrowserAutomaticCapture.Complete(BrowserAutomaticCapture.DefaultDirectory, job, accepted); }
                catch (IOException) { } // Native host will time out and resume browser.
            }
        }
    }

    /// <summary>
    /// A browser user gesture queues a URL for review, never auto-starts a
    /// transfer. The Add Download form is the only handoff destination.
    /// </summary>
    private void CheckBrowserInbox()
    {
        if (AddOverlay.Visibility == Visibility.Visible ||
            SettingsOverlay.Visibility == Visibility.Visible ||
            CustomSiteOverlay.Visibility == Visibility.Visible ||
            ScheduledEditOverlay.Visibility == Visibility.Visible) return;
        try
        {
            string? url = BrowserLinkInbox.TryTake(BrowserLinkInbox.DefaultDirectory);
            if (url is null) return;
            Add_Click(this, new RoutedEventArgs());
            NewUrl.Text = url;
            StatusMessage.Text = "Link received from browser. Review the destination and confirm the download.";
        }
        catch (Exception ex) { StatusMessage.Text = "Browser handoff failed: " + ex.GetType().Name; }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        ApplyNewDownloadDefaults();
        ScheduleDownload.IsChecked = false;
        PostDownloadChoice.SelectedIndex = 0;
        ScheduledRecurrence.SelectedIndex = 0;
        var localFuture = DateTime.Now.AddMinutes(30);
        ScheduledDate.SelectedDate = localFuture.Date;
        ScheduledTime.Text = localFuture.ToString("HH:mm", CultureInfo.InvariantCulture);
        AddOverlay.Visibility = Visibility.Visible;
        NewUrl.Focus();
    }

    private static void SelectNumericChoice(ComboBox dropdown, int number)
    {
        for (int i = 0; i < dropdown.Items.Count; i++)
        {
            if (dropdown.Items[i] is ComboBoxItem choice &&
                int.TryParse(choice.Tag?.ToString(), out int value) && value == number)
            {
                dropdown.SelectedIndex = i;
                return;
            }
        }
        dropdown.SelectedIndex = 0;
    }

    private static int GetNumericChoice(ComboBox dropdown, int fallback) =>
        dropdown.SelectedItem is ComboBoxItem item &&
        int.TryParse(item.Tag?.ToString(), out int value) ? value : fallback;

    private void ApplyNewDownloadDefaults()
    {
        SelectNumericChoice(ConnectionChoice, _preferences.DefaultConnections);
        SelectNumericChoice(BandwidthChoice, _preferences.DefaultBandwidthKiBps);
        StartAutomatically.IsChecked = _preferences.StartImmediately;
        QueueOnly.IsChecked = !_preferences.StartImmediately;
        AdaptiveWorkers.IsChecked = false; // Experimental scheduler requires explicit opt-in.
    }
    private void CloseAdd_Click(object sender, RoutedEventArgs e) => AddOverlay.Visibility = Visibility.Collapsed;

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (QueueOnly is not null && StartAutomatically is not null)
            QueueOnly.IsChecked = StartAutomatically.IsChecked != true;
    }

    private void QueueOnly_Click(object sender, RoutedEventArgs e)
    {
        if (StartAutomatically is not null && QueueOnly is not null)
            StartAutomatically.IsChecked = QueueOnly.IsChecked != true;
    }

    private bool _syncingFileName;

    private void Destination_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncingFileName || NewDestination is null || NewFileName is null) return;
        _syncingFileName = true;
        try { NewFileName.Text = Path.GetFileName(NewDestination.Text); }
        finally { _syncingFileName = false; }
    }

    private void FileName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncingFileName || NewDestination is null || NewFileName is null) return;
        string fileName = NewFileName.Text.Trim();
        if (fileName.Length == 0 || fileName == "." || fileName == ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        string? parent;
        try { parent = Path.GetDirectoryName(NewDestination.Text); }
        catch (ArgumentException) { return; }
        if (string.IsNullOrWhiteSpace(parent) ||
            Path.GetFileName(NewDestination.Text).Equals(fileName, StringComparison.Ordinal)) return;
        _syncingFileName = true;
        try { NewDestination.Text = Path.Combine(parent, fileName); }
        finally { _syncingFileName = false; }
    }

    private void PasteUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var text = Clipboard.GetText().Trim();
            if (text.Contains('\n') || text.Contains('\r'))
            {
                try { BatchDownloadPlanner.Plan(text, _preferences.DownloadFolder, Downloads.Select(d => d.Destination)); }
                catch (Exception ex) { FormError.Text = ex.Message; return; }
            }
            else if (!DownloadEngine.IsAllowedUrl(text))
            {
                FormError.Text = "Clipboard does not contain a direct HTTP or HTTPS URL.";
                return;
            }
            NewUrl.Text = text;
            NewUrl.Focus();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            FormError.Text = "Clipboard is busy. Paste the link manually.";
        }
    }

    private void Url_Changed(object sender, TextChangedEventArgs e)
    {
        if (NewUrl is null || NewDestination is null ||
            !string.IsNullOrWhiteSpace(NewDestination.Text)) return;
        if (!Uri.TryCreate(NewUrl.Text, UriKind.Absolute, out var address)) return;
        string name = Uri.UnescapeDataString(Path.GetFileName(address.AbsolutePath));
        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        NewDestination.Text = Path.Combine(
            _preferences.DownloadFolder, name);
        // The initial "Other" is an automatic hint; never override a category
        // the user deliberately selected in the New Download dialog.
        if (NewCategory is not null && NewCategory.SelectedIndex == 0)
        {
            string guessed = DownloadCategory.Guess(name);
            for (int i = 0; i < NewCategory.Items.Count; i++)
                if (NewCategory.Items[i] is ComboBoxItem option
                    && string.Equals(option.Content?.ToString(), guessed, StringComparison.OrdinalIgnoreCase))
                {
                    NewCategory.SelectedIndex = i;
                    break;
                }
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var browse = new SaveFileDialog
        {
            FileName = Path.GetFileName(NewDestination.Text),
            InitialDirectory = Path.GetDirectoryName(NewDestination.Text),
            Title = "Save download as",
            OverwritePrompt = false
        };
        if (browse.ShowDialog(this) == true) NewDestination.Text = browse.FileName;
    }

    private void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NewUrl.Text.Contains('\n') || NewUrl.Text.Contains('\r'))
            {
                AddBatchDownloads();
                return;
            }
            string category = (NewCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";
            int connections = ConnectionChoice?.SelectedItem is ComboBoxItem option &&
                              int.TryParse(option.Tag?.ToString(), out var parsed)
                ? parsed : 8;
            int bandwidth = BandwidthChoice?.SelectedItem is ComboBoxItem limit &&
                            int.TryParse(limit.Tag?.ToString(), out var capped)
                ? capped : 0;
            string? checksum = null;
            if (VerifyChecksum.IsChecked == true)
            {
                checksum = NewChecksum.Text.Trim();
                if (checksum.Length == 0)
                    throw new ArgumentException("Enter the expected SHA-256 or disable verification.");
            }
            var decision = DownloadAutomationPolicy.Apply(
                NewUrl.Text.Trim(), NewDestination.Text.Trim(), category, NewLabels.Text.Trim(),
                _preferences.DownloadFolder, _automationRules);
            if (Downloads.Any(d => string.Equals(d.Destination, decision.Destination,
                    StringComparison.OrdinalIgnoreCase)))
                throw new IOException("That destination is already tracked. Choose another filename.");
            var item = _engine.Create(NewUrl.Text.Trim(), decision.Destination, decision.Category,
                decision.Labels, connections, bandwidth, checksum,
                AdaptiveWorkers.IsChecked == true, SelectedPostDownloadAction(PostDownloadChoice));
            if (File.Exists(item.Destination))
                throw new IOException("A file already exists at that path. Choose a different name.");
            ScheduledDownload? schedule = null;
            if (ScheduleDownload.IsChecked == true)
            {
                if (ScheduledDate.SelectedDate is not DateTime day ||
                    !TimeSpan.TryParseExact(ScheduledTime.Text.Trim(), @"hh\:mm",
                        CultureInfo.InvariantCulture, out var clock))
                    throw new ArgumentException("Choose a date and a 24-hour time (HH:mm).");
                var dueAt = ScheduledQueueStore.FromLocal(day, clock, TimeZoneInfo.Local, DateTimeOffset.Now);
                ScheduleRecurrence recurrence = Enum.Parse<ScheduleRecurrence>(
                    (ScheduledRecurrence.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Once");
                schedule = RecurringSchedulePolicy.Configure(
                    new ScheduledDownload(item.Destination, dueAt), recurrence, TimeZoneInfo.Local);
            }
            bool queued = schedule is not null || QueueOnly.IsChecked == true;
            ShowSubmittedDownload(queued);
            Track(item);
            SaveCatalog();
            if (schedule is not null)
            {
                _scheduled[item.Destination] = schedule;
                SaveSchedules();
                RefreshFilter();
            }
            if (!queued && !_engine.Start(item))
                throw new InvalidOperationException("Could not start this download.");
            SelectedDownload = item;
            AddOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = (schedule is not null ? "Scheduled: " :
                queued ? "Added to queue: " : "Download started: ") + item.Name;
            NewUrl.Clear();
            NewDestination.Clear();
            NewCategory.SelectedIndex = 0;
            NewLabels.Clear();
            QueueOnly.IsChecked = !_preferences.StartImmediately;
            StartAutomatically.IsChecked = _preferences.StartImmediately;
            VerifyChecksum.IsChecked = false;
            NewChecksum.Clear();
            AdaptiveWorkers.IsChecked = false;
            ScheduleDownload.IsChecked = false;
            PostDownloadChoice.SelectedIndex = 0;
        }
        catch (Exception ex) { FormError.Text = ex.Message; }
    }

    /// <summary>
    /// All items are validated before any are added. The full original URLs stay
    /// in the local catalog only; the UI displays source hostnames.
    /// </summary>
    private void AddBatchDownloads()
    {
        if (ScheduleDownload.IsChecked == true)
            throw new ArgumentException("Schedule a single download at a time; batch scheduling is not yet supported.");
        if (VerifyChecksum.IsChecked == true)
            throw new ArgumentException("An expected SHA-256 applies to one file. Disable it for a batch.");
        string? folder = Path.GetDirectoryName(NewDestination.Text);
        if (string.IsNullOrWhiteSpace(folder)) folder = _preferences.DownloadFolder;
        var candidates = BatchDownloadPlanner.Plan(NewUrl.Text, folder,
            Downloads.Select(d => d.Destination));
        string category = (NewCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";
        int connections = GetNumericChoice(ConnectionChoice, 8);
        int bandwidth = GetNumericChoice(BandwidthChoice, 0);
        bool queued = QueueOnly.IsChecked == true;
        var items = candidates.Select(candidate =>
        {
            var decision = DownloadAutomationPolicy.Apply(candidate.Url, candidate.Destination,
                category == "Other" ? DownloadCategory.Guess(Path.GetFileName(candidate.Destination)) : category,
                NewLabels.Text.Trim(), _preferences.DownloadFolder, _automationRules);
            return _engine.Create(candidate.Url, decision.Destination, decision.Category,
                decision.Labels, connections, bandwidth,
                adaptive: AdaptiveWorkers.IsChecked == true,
                postDownloadAction: SelectedPostDownloadAction(PostDownloadChoice));
        }).ToArray();
        if (items.Select(x => x.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length ||
            items.Any(x => Downloads.Any(d => string.Equals(d.Destination, x.Destination,
                StringComparison.OrdinalIgnoreCase)) || File.Exists(x.Destination) ||
                File.Exists(x.Destination + ".rdpartial") ||
                Directory.Exists(x.Destination + ".rdsegments") ||
                Directory.Exists(x.Destination + ".rdadaptive")))
            throw new IOException("An automation rule produced a filename collision. Adjust its subfolder or rename the file.");
        ShowSubmittedDownload(queued);
        foreach (var item in items) Track(item);
        SaveCatalog();
        int started = 0;
        if (!queued)
            foreach (var item in items)
                if (StartManually(item)) started++;
        SelectedDownload = items.LastOrDefault();
        AddOverlay.Visibility = Visibility.Collapsed;
        StatusMessage.Text = queued
            ? $"Added {items.Length} distinct links to the queue."
            : $"Added {items.Length} distinct links; started {started}. Other transfers may wait for available slots.";
        NewUrl.Clear();
        NewDestination.Clear();
        NewCategory.SelectedIndex = 0;
        NewLabels.Clear();
        QueueOnly.IsChecked = !_preferences.StartImmediately;
        StartAutomatically.IsChecked = _preferences.StartImmediately;
        AdaptiveWorkers.IsChecked = false;
        ScheduleDownload.IsChecked = false;
        PostDownloadChoice.SelectedIndex = 0;
    }

    private IReadOnlyList<DownloadEntry> ActionSelection()
    {
        if (DownloadsGrid?.SelectedItems is { Count: > 0 })
            return DownloadsGrid.SelectedItems.OfType<DownloadEntry>().ToArray();
        return SelectedDownload is null ? Array.Empty<DownloadEntry>() : [SelectedDownload];
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ActionSelection()) _engine.Pause(item);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ActionSelection()) StopManually(item);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        int started = 0;
        foreach (var item in ActionSelection())
            if (StartManually(item)) started++;
        if (started == 0) StatusMessage.Text = "No selected downloads could be started.";
        else StatusMessage.Text = $"{started} download(s) started.";
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDownload is { Status: DownloadStatus.Completed } item && File.Exists(item.Destination))
            Process.Start(new ProcessStartInfo(item.Destination) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDownload is { } item)
        {
            string? folder = Path.GetDirectoryName(item.Destination);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }

    private void PopulateSettings(RangeDeskPreferences preferences)
    {
        SettingsDownloadFolder.Text = preferences.DownloadFolder;
        SelectNumericChoice(SettingsConnectionsChoice, preferences.DefaultConnections);
        SelectNumericChoice(SettingsConcurrentChoice, preferences.MaxConcurrentDownloads);
        SelectNumericChoice(SettingsBandwidthChoice, preferences.DefaultBandwidthKiBps);
        SettingsStartImmediately.IsChecked = preferences.StartImmediately;
        SettingsProxyUri.Text = preferences.ProxyUri ?? "";
        SettingsError.Text = "";
    }

    private void RefreshBandwidthCalendar()
    {
        BandwidthCalendarList.Items.Clear();
        foreach (var rule in _bandwidthWindows)
            BandwidthCalendarList.Items.Add($"{rule.Name} · " +
                (string.IsNullOrEmpty(rule.HostSuffix) ? "All sites" : rule.HostSuffix) +
                $" · {rule.Start:HH:mm}–{rule.End:HH:mm} · {rule.CapKiBps} KiB/s");
    }

    private async Task ApplyBandwidthCalendarAsync()
    {
        if (_bandwidthUpdateInProgress) return;
        _bandwidthUpdateInProgress = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.Now;
            foreach(var download in Downloads.ToArray())
            {
                if (download.Status != DownloadStatus.Downloading)
                {
                    _appliedBandwidthCaps.Remove(download.Id);
                    continue;
                }
                int limit = BandwidthCalendarPolicy.EffectiveLimit(
                    download.BandwidthKiBps,download.Host,now,TimeZoneInfo.Local,_bandwidthWindows);
                if (_appliedBandwidthCaps.TryGetValue(download.Id,out int current) && current==limit)
                    continue;
                if (await _engine.SetLiveBandwidthLimitAsync(download, limit))
                {
                    _appliedBandwidthCaps[download.Id] = limit;
                    download.ReportAppliedBandwidthLimit(limit);
                }
            }
        }
        catch(Exception ex) { StatusMessage.Text="Bandwidth calendar failed: "+ex.Message; }
        finally { _bandwidthUpdateInProgress=false; }
    }

    private void AddBandwidthRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TimeOnly.TryParseExact(BandwidthStart.Text.Trim(),"HH:mm",
                CultureInfo.InvariantCulture,DateTimeStyles.None,out var start) ||
                !TimeOnly.TryParseExact(BandwidthEnd.Text.Trim(),"HH:mm",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var end))
                throw new ArgumentException("Use 24-hour HH:mm start and end times.");
            var rule = new BandwidthCalendarRule(BandwidthRuleName.Text,BandwidthRuleDomain.Text,
                start,end,GetNumericChoice(BandwidthWindowCap,512)).Validate();
            if(_bandwidthWindows.Any(r=>r.Name.Equals(rule.Name,StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("This bandwidth window name already exists.");
            BandwidthCalendarStore.Save(_bandwidthCalendarFile,_bandwidthWindows.Append(rule));
            _bandwidthWindows.Add(rule);
            RefreshBandwidthCalendar();
            BandwidthCalendarError.Text="";
            BandwidthRuleName.Clear();BandwidthRuleDomain.Clear();
            _appliedBandwidthCaps.Clear();
            _ = ApplyBandwidthCalendarAsync();
        }
        catch(Exception ex){BandwidthCalendarError.Text=ex.Message;}
    }

    private void RemoveBandwidthRule_Click(object sender, RoutedEventArgs e)
    {
        int index=BandwidthCalendarList.SelectedIndex;
        if(index<0 || index>=_bandwidthWindows.Count)return;
        try
        {
            var next=_bandwidthWindows.Where((_,i)=>i!=index).ToArray();
            BandwidthCalendarStore.Save(_bandwidthCalendarFile,next);
            _bandwidthWindows.Clear();_bandwidthWindows.AddRange(next);
            RefreshBandwidthCalendar();
            BandwidthCalendarError.Text="";
            _appliedBandwidthCaps.Clear();
            _ = ApplyBandwidthCalendarAsync();
        }
        catch(Exception ex){BandwidthCalendarError.Text=ex.Message;}
    }

    private void ReorderBandwidthRule_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button {Tag:string direction} ||
            !int.TryParse(direction,out int change))return;
        int index=BandwidthCalendarList.SelectedIndex,target=index+change;
        if(index<0 || target<0 || target>=_bandwidthWindows.Count)return;
        try
        {
            var next=_bandwidthWindows.ToList();
            (next[index],next[target])=(next[target],next[index]);
            BandwidthCalendarStore.Save(_bandwidthCalendarFile,next);
            _bandwidthWindows.Clear();_bandwidthWindows.AddRange(next);
            RefreshBandwidthCalendar();
            BandwidthCalendarList.SelectedIndex=target;
            BandwidthCalendarError.Text="";
            _appliedBandwidthCaps.Clear();
            _ = ApplyBandwidthCalendarAsync();
        }
        catch(Exception ex){BandwidthCalendarError.Text=ex.Message;}
    }

    private void RefreshAutomationRules()
    {
        AutomationRuleList.Items.Clear();
        foreach (var rule in _automationRules)
            AutomationRuleList.Items.Add($"{rule.Name} — {rule.HostSuffix} {rule.Extension} → {rule.Category}" +
                (rule.Subfolder.Length > 0 ? " / " + rule.Subfolder : ""));
    }

    private void OpenSchedulerSetup_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Scheduler");
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this,
                "The optional scheduler setup is not in this installation. Use the complete portable ZIP.",
                "Optional Windows scheduling", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        StatusMessage.Text = "Optional scheduling instructions are in Scheduler\\README.md. No task was enabled.";
    }

    private void OpenAutomationSettings_Click(object sender, RoutedEventArgs e)
    {
        Settings_Click(sender, e);
        SettingsTabControl.SelectedItem = SettingsAutomationTab;
    }

    private void AddAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rule = new DownloadAutomationRule(RuleName.Text,
                RuleDomain.Text, RuleExtension.Text,
                (RuleCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other",
                RuleLabel.Text, RuleSubfolder.Text).Validate();
            if (_automationRules.Any(r => r.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A rule with this name already exists.");
            DownloadAutomationRuleStore.Save(_automationFile, _automationRules.Append(rule));
            _automationRules.Add(rule);
            RefreshAutomationRules();
            RuleName.Clear(); RuleDomain.Clear(); RuleExtension.Clear();
            RuleLabel.Clear(); RuleSubfolder.Clear();
            RuleCategory.SelectedIndex = 0;
            AutomationRuleError.Text = "";
            StatusMessage.Text = "New downloads will use automation rule: " + rule.Name;
        }
        catch (Exception ex) { AutomationRuleError.Text = ex.Message; }
    }

    private void ReorderAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string direction } ||
            !int.TryParse(direction, out int offset)) return;
        int index = AutomationRuleList.SelectedIndex;
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _automationRules.Count) return;
        try
        {
            var next = _automationRules.ToList();
            (next[index], next[target]) = (next[target], next[index]);
            DownloadAutomationRuleStore.Save(_automationFile, next);
            _automationRules.Clear();
            _automationRules.AddRange(next);
            RefreshAutomationRules();
            AutomationRuleList.SelectedIndex = target;
            AutomationRuleError.Text = "";
        }
        catch (Exception ex) { AutomationRuleError.Text = ex.Message; }
    }

    private void RemoveAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        int index = AutomationRuleList.SelectedIndex;
        if (index < 0 || index >= _automationRules.Count) return;
        try
        {
            DownloadAutomationRuleStore.Save(_automationFile,
                _automationRules.Where((_, i) => i != index));
            _automationRules.RemoveAt(index);
            RefreshAutomationRules();
            AutomationRuleError.Text = "";
        }
        catch (Exception ex) { AutomationRuleError.Text = ex.Message; }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettings(_preferences);
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsDownloadFolder.Focus();
    }

    private void BrowseSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = new OpenFolderDialog
        {
            Title = "Choose the default RangeDesk download folder",
            Multiselect = false
        };
        if (Directory.Exists(SettingsDownloadFolder.Text))
            folder.InitialDirectory = SettingsDownloadFolder.Text;
        if (folder.ShowDialog(this) == true)
            SettingsDownloadFolder.Text = folder.FolderName;
    }

    private void RestoreSettings_Click(object sender, RoutedEventArgs e) =>
        PopulateSettings(new RangeDeskPreferences());

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var updated = new RangeDeskPreferences
            {
                DownloadFolder = SettingsDownloadFolder.Text.Trim(),
                DefaultConnections = GetNumericChoice(SettingsConnectionsChoice, 8),
                MaxConcurrentDownloads = GetNumericChoice(SettingsConcurrentChoice, 2),
                DefaultBandwidthKiBps = GetNumericChoice(SettingsBandwidthChoice, 0),
                StartImmediately = SettingsStartImmediately.IsChecked == true,
                ProxyUri = SettingsProxyUri.Text.Trim()
            }.Validate();
            bool restartNeeded = updated.MaxConcurrentDownloads != _preferences.MaxConcurrentDownloads ||
                !string.Equals(updated.ProxyUri, _preferences.ProxyUri, StringComparison.OrdinalIgnoreCase);
            RangeDeskPreferencesStore.Save(_preferencesFile, updated);
            _preferences = updated;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = restartNeeded
                ? "Settings saved. Restart RangeDesk to apply the changed network or simultaneous-download settings."
                : "Settings saved. New downloads will use these defaults; active transfers are unchanged.";
        }
        catch (Exception ex)
        {
            SettingsError.Text = "Could not save settings: " + ex.Message;
        }
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;
}
