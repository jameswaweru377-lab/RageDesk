using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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

    private sealed record SavedDownload(string Url, string Destination, string Category, string Labels);

    public MainWindow()
    {
        InitializeComponent();
        _engine = new DownloadEngine(SynchronizationContext.Current);
        VisibleDownloads = CollectionViewSource.GetDefaultView(Downloads);
        VisibleDownloads.Filter = IsVisible;
        DataContext = this;
        LoadCatalog();
        Closing += (_, _) => _engine.Dispose();
    }

    public ObservableCollection<DownloadEntry> Downloads { get; } = new();
    public ICollectionView VisibleDownloads { get; }
    public DownloadEntry? SelectedDownload
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
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
        Downloads.Add(item);
        item.PropertyChanged += (_, _) =>
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => VisibleDownloads.Refresh());
                return;
            }
            VisibleDownloads.Refresh(); // keeps object identity and selection
        };
        if (SelectedDownload is null) SelectedDownload = item;
    }

    private bool IsVisible(object candidate)
    {
        if (candidate is not DownloadEntry item) return false;
        if (_navigation == "completed" && item.Status != DownloadStatus.Completed) return false;
        if (_navigation == "queue" && item.Status is not
            (DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Canceled or DownloadStatus.Failed)) return false;
        if (_categoryNavigation is not null && item.Category != _categoryNavigation) return false;
        if (GlobalSearch is not null && !string.IsNullOrWhiteSpace(GlobalSearch.Text))
        {
            string search = GlobalSearch.Text.Trim();
            if (!item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !item.Host.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (StatusFilter?.SelectedItem is ComboBoxItem state && state.Content is string label &&
            label != "All statuses" && !item.Status.ToString().Equals(label, StringComparison.OrdinalIgnoreCase))
            return false;
        if (CategoryFilter?.SelectedItem is ComboBoxItem category && category.Content is string group &&
            group != "All categories" && !item.Category.Equals(group, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private void RefreshFilter()
    {
        if (VisibleDownloads is null) return;
        VisibleDownloads.Refresh();
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = button.Tag as string ?? "all";
        _categoryNavigation = null;
        ViewTitle.Text = _navigation == "completed" ? "Completed" :
            _navigation == "queue" ? "Queue" : "Downloads";
        RefreshFilter();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _navigation = "all";
        _categoryNavigation = button.Tag as string;
        ViewTitle.Text = _categoryNavigation ?? "Downloads";
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
            var item = _engine.Create(NewUrl.Text.Trim(), NewDestination.Text.Trim(), category);
            if (File.Exists(item.Destination))
                throw new IOException("A file already exists at that path. Choose a different name.");
            Track(item);
            SaveCatalog();
            if (!_engine.Start(item))
                throw new InvalidOperationException("Could not start this download.");
            SelectedDownload = item;
            AddOverlay.Visibility = Visibility.Collapsed;
            StatusMessage.Text = "Download started: " + item.Name;
            NewUrl.Clear();
            NewDestination.Clear();
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
