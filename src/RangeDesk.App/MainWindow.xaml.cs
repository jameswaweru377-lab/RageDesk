using System.Collections.ObjectModel;
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
    private string _navigation = "all";
    private string _downloadTab = "all";
    private string? _categoryNavigation;
    private DownloadEntry? _selected;
    private string? _labelNavigation;
    private string? _siteNavigation;
    private bool _sidebarCollapsed;
    private readonly Dictionary<Guid, DownloadStatus> _lastStatuses = new();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);


    private sealed record SavedDownload(string Url, string Destination, string Category, string Labels, int Connections = 1, int BandwidthKiBps = 0, string? ExpectedSha256 = null, bool Adaptive = false);

    public MainWindow()
    {
        InitializeComponent();
        try { _preferences = RangeDeskPreferencesStore.Load(_preferencesFile); }
        catch (Exception ex)
        {
            StatusMessage.Text = "Could not read download settings; using defaults: " + ex.Message;
        }
        _engine = new DownloadEngine(SynchronizationContext.Current,
            maxConcurrentDownloads: _preferences.MaxConcurrentDownloads);
        VisibleDownloads = CollectionViewSource.GetDefaultView(Downloads);
        VisibleDownloads.Filter = MatchesFilters;
        DataContext = this;
        LoadCatalog();
        Closing += (_, _) => _engine.Dispose();
        SourceInitialized += (_, _) =>
        {
            int enabled = 1;
            try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int)); }
            catch (DllNotFoundException) { }
        };
        RefreshFilter();
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
            nameof(DownloadsCount), nameof(CompletedCount), nameof(QueueCount),
            nameof(DownloadingCount), nameof(PausedCount), nameof(PendingCount), nameof(FailedCount),
            nameof(SoftwareCount), nameof(VideosCount), nameof(DocumentsCount),
            nameof(ArchivesCount), nameof(ImagesCount),
            nameof(WorkCount), nameof(PersonalCount), nameof(ResearchCount), nameof(ImportantCount),
            nameof(MicrosoftCount), nameof(GitHubCount), nameof(GoogleDriveCount), nameof(YouTubeCount) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public DownloadEntry? SelectedDownload
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            InspectorPanel.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
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
                        record.ExpectedSha256, record.Adaptive);
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

    private void SaveCatalog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogFile)!);
            var saved = Downloads.Select(d => new SavedDownload(d.Url, d.Destination, d.Category, d.Labels, d.RequestedConnections, d.BandwidthKiBps, d.ExpectedSha256, d.Adaptive));
            string temp = _catalogFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, _catalogFile, true);
        }
        catch (Exception ex) { StatusMessage.Text = "Could not save download history: " + ex.Message; }
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
                        StatusMessage.Text = "Download completed: " + item.Name;
                    RefreshFilter();
                }
            }
            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.BeginInvoke((Action)Update);
        };
        if (SelectedDownload is null) SelectedDownload = item;
        RefreshFilter();
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
        if (_siteNavigation is not null && !SitePresets.Matches(item.Host, _siteNavigation))
            return false;
        return DownloadListVisibility.Matches(item, _navigation, GlobalSearch?.Text,
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
            _navigation == "queue" ? "Queue" : "Downloads";
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
        ViewTitle.Text = _siteNavigation ?? "Downloads";
        RefreshFilter();
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

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 55 : 202);
        SidebarBrand.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarBrandIcon.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        foreach (var caption in Descendants<TextBlock>(SidebarPanel))
        {
            if (caption.Tag is string tag && tag == "NavCaption" ||
                caption.Text is "CATEGORIES" or "LABELS" or "SITE PRESETS")
                caption.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        foreach (var badge in Descendants<Border>(SidebarPanel).Where(x => x.Tag as string == "NavBadge"))
            badge.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        SidebarToggle.ToolTip = _sidebarCollapsed ? "Expand navigation" : "Collapse navigation";
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
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
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
        else _engine.Start(entry);
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
        var resume = new MenuItem { Header = "Resume", IsEnabled = entry.Status != DownloadStatus.Completed };
        resume.Click += (_, _) => _engine.Start(entry);
        var pause = new MenuItem { Header = "Pause", IsEnabled = entry.Status == DownloadStatus.Downloading };
        pause.Click += (_, _) => _engine.Pause(entry);
        var stop = new MenuItem { Header = "Stop", IsEnabled = entry.Status != DownloadStatus.Completed };
        stop.Click += (_, _) => _engine.Cancel(entry);
        var folder = new MenuItem { Header = "Open folder" };
        folder.Click += (_, _) => OpenFolder_Click(button, new RoutedEventArgs());
        menu.Items.Add(resume);
        menu.Items.Add(pause);
        menu.Items.Add(stop);
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

    private void StartQueue_Click(object sender, RoutedEventArgs e)
    {
        int started = 0;
        foreach (var item in Downloads.Where(d => d.Status == DownloadStatus.Queued).ToArray())
            if (_engine.Start(item)) started++;
        StatusMessage.Text = started > 0
            ? $"Queued transfers started in saved order: {started}"
            : "There are no queued transfers to start.";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            GlobalSearch.Focus();
            GlobalSearch.SelectAll();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => RefreshFilter();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        ApplyNewDownloadDefaults();
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
            if (!DownloadEngine.IsAllowedUrl(text))
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
            var item = _engine.Create(NewUrl.Text.Trim(), NewDestination.Text.Trim(), category,
                NewLabels.Text.Trim(), connections, bandwidth, checksum,
                AdaptiveWorkers.IsChecked == true);
            if (File.Exists(item.Destination))
                throw new IOException("A file already exists at that path. Choose a different name.");
            bool queued = QueueOnly.IsChecked == true;
            ShowSubmittedDownload(queued);
            Track(item);
            SaveCatalog();
            if (!queued && !_engine.Start(item))
                throw new InvalidOperationException("Could not start this download.");
            SelectedDownload = item;
            AddOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = (queued ? "Added to queue: " : "Download started: ") + item.Name;
            NewUrl.Clear();
            NewDestination.Clear();
            NewCategory.SelectedIndex = 0;
            NewLabels.Clear();
            QueueOnly.IsChecked = !_preferences.StartImmediately;
            StartAutomatically.IsChecked = _preferences.StartImmediately;
            VerifyChecksum.IsChecked = false;
            NewChecksum.Clear();
            AdaptiveWorkers.IsChecked = false;
        }
        catch (Exception ex) { FormError.Text = ex.Message; }
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
        foreach (var item in ActionSelection()) _engine.Cancel(item);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        int started = 0;
        foreach (var item in ActionSelection())
            if (_engine.Start(item)) started++;
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
        SettingsError.Text = "";
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
                StartImmediately = SettingsStartImmediately.IsChecked == true
            }.Validate();
            bool restartNeeded = updated.MaxConcurrentDownloads != _preferences.MaxConcurrentDownloads;
            RangeDeskPreferencesStore.Save(_preferencesFile, updated);
            _preferences = updated;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = restartNeeded
                ? "Settings saved. Restart RangeDesk to apply the new simultaneous-download limit."
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
