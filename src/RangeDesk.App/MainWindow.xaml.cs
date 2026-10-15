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
using Microsoft.Win32;
using RangeDesk.Core;

namespace RangeDesk.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DownloadEngine _engine;
    private readonly string _catalogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "catalog-v5.json");
    private string _navigation = "all";
    private string? _categoryNavigation;
    private DownloadEntry? _selected;
    private string? _labelNavigation;
    private bool _sidebarCollapsed;
    private readonly Dictionary<Guid, DownloadStatus> _lastStatuses = new();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);


    private sealed record SavedDownload(string Url, string Destination, string Category, string Labels);

    public MainWindow()
    {
        InitializeComponent();
        _engine = new DownloadEngine(SynchronizationContext.Current);
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
    }

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();
    public ICollectionView VisibleDownloads { get; }
    public int DownloadsCount => Downloads.Count;
    public int CompletedCount => Downloads.Count(d => d.Status == DownloadStatus.Completed);
    public int QueueCount => Downloads.Count(d => d.Status != DownloadStatus.Completed);
    public int SoftwareCount => CategoryCount("Software");
    public int VideosCount => CategoryCount("Videos");
    public int DocumentsCount => CategoryCount("Documents");
    public int ArchivesCount => CategoryCount("Archives");
    public int ImagesCount => CategoryCount("Images");
    public int WorkCount => LabelCount("Work");
    public int PersonalCount => LabelCount("Personal");
    public int ImportantCount => LabelCount("Important");

    private int CategoryCount(string name) => Downloads.Count(
        d => string.Equals(d.Category, name, StringComparison.OrdinalIgnoreCase));
    private int LabelCount(string name) => Downloads.Count(d =>
        d.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Contains(name, StringComparer.OrdinalIgnoreCase));

    private void RefreshCounters()
    {
        foreach (var property in new[] {
            nameof(DownloadsCount), nameof(CompletedCount), nameof(QueueCount),
            nameof(SoftwareCount), nameof(VideosCount), nameof(DocumentsCount),
            nameof(ArchivesCount), nameof(ImagesCount),
            nameof(WorkCount), nameof(PersonalCount), nameof(ImportantCount) })
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
                        record.Category, record.Labels);
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
            var saved = Downloads.Select(d => new SavedDownload(d.Url, d.Destination, d.Category, d.Labels));
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
        item.PropertyChanged += (_, _) =>
        {
            void Update()
            {
                // Keep the same rows and selection: filter only when state changes.
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

        return DownloadListVisibility.Matches(item, _navigation, GlobalSearch?.Text,
            status, category, _labelNavigation);
    }

    private void RefreshFilter()
    {
        if (VisibleDownloads is null) return;
        VisibleDownloads.Refresh();
        if (SidebarPanel is not null)
        {
            foreach (var button in Descendants<Button>(SidebarPanel))
            {
                if (button.Tag is not string tag || !button.IsEnabled) continue;
                bool selected = (_categoryNavigation is null && _labelNavigation is null &&
                                 tag == _navigation)
                             || (_categoryNavigation is not null && tag == _categoryNavigation)
                             || (_labelNavigation is not null && tag == _labelNavigation);
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
        _categoryNavigation = null;
        _labelNavigation = null;
        ViewTitle.Text = _navigation == "completed" ? "Completed" :
            _navigation == "queue" ? "Queue" : "Downloads";
        RefreshFilter();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = "all";
        _categoryNavigation = button.Tag as string;
        _labelNavigation = null;
        ViewTitle.Text = _categoryNavigation ?? "Downloads";
        RefreshFilter();
    }

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = "all";
        _categoryNavigation = null;
        _labelNavigation = button.Tag as string;
        ViewTitle.Text = _labelNavigation is null ? "Downloads" : "Label: " + _labelNavigation;
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
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 62 : 224);
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
        _navigation = addToQueue ? "queue" : "all";
        _categoryNavigation = null;
        _labelNavigation = null;
        ViewTitle.Text = addToQueue ? "Queue" : "Downloads";
        if (StatusFilter is not null) StatusFilter.SelectedIndex = 0;
        if (CategoryFilter is not null) CategoryFilter.SelectedIndex = 0;
        if (GlobalSearch is not null) GlobalSearch.Clear();
        RefreshFilter();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshFilter();
    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => RefreshFilter();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        FormError.Text = "";
        AddOverlay.Visibility = Visibility.Visible;
        NewUrl.Focus();
    }
    private void CloseAdd_Click(object sender, RoutedEventArgs e) => AddOverlay.Visibility = Visibility.Collapsed;

    private void Url_Changed(object sender, TextChangedEventArgs e)
    {
        if (NewUrl is null || NewDestination is null ||
            !string.IsNullOrWhiteSpace(NewDestination.Text)) return;
        if (!Uri.TryCreate(NewUrl.Text, UriKind.Absolute, out var address)) return;
        string name = Uri.UnescapeDataString(Path.GetFileName(address.AbsolutePath));
        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        NewDestination.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", name);
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
            var item = _engine.Create(NewUrl.Text.Trim(), NewDestination.Text.Trim(), category,
                NewLabels.Text.Trim());
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
            NewLabels.Clear();
            QueueOnly.IsChecked = false;
        }
        catch (Exception ex) { FormError.Text = ex.Message; }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDownload is { } selected) _engine.Pause(selected);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDownload is { } selected) _engine.Cancel(selected);
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDownload is { } selected)
        {
            if (!_engine.Start(selected))
                StatusMessage.Text = "Download is already running or has completed.";
        }
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

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Visible;
    private void CloseSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;
}
