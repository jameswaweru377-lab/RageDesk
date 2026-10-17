using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RangeDesk.App.Controls;
using RangeDesk.Core;

namespace RangeDesk.App;

/// <summary>
/// An independent, low-overhead progress window. The download engine retains ownership
/// of all transfers and checkpoints. This window observes real task state only.
/// </summary>
public sealed class DownloadProgressWindow : Window
{
    public enum Command { StartOrResume, Pause, Stop, OpenFolder }

    private static readonly Brush TextColor = Brush("#EFF8FF");
    private static readonly Brush Muted = Brush("#A4C2E4");
    private static readonly Brush Accent = Brush("#26B5FF");
    private static readonly Brush Surface = Brush("#030E20");
    private static readonly Brush Card = Brush("#071B37");
    private static readonly Brush Outline = Brush("#1D4B81");
    private static readonly Brush PremiumPanelFill = new LinearGradientBrush(
        Color.FromRgb(13, 42, 79), Color.FromRgb(4, 18, 39), new Point(0, 0), new Point(1, 1));
    private static readonly Brush PremiumProgressFill = new LinearGradientBrush(
        Color.FromRgb(44, 213, 255), Color.FromRgb(5, 95, 250), new Point(0, 0), new Point(1, 0));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly DownloadEntry _entry;
    private readonly Action<DownloadEntry, Command> _onCommand;
    private readonly TextBlock _subtitle = Label("", 14, Muted);
    private readonly TextBlock _name = Label("", 22, TextColor, true);
    private readonly TextBlock _origin = Label("", 13, Muted);
    private readonly TextBlock _type = Label("", 13, Accent);
    private readonly TextBlock _size = Label("", 13, Muted);
    private readonly TextBlock _bytes = Label("", 16, Accent, true);
    private readonly TextBlock _speed = Label("", 16, Accent, true);
    private readonly TextBlock _eta = Label("", 16, Muted);
    private readonly TextBlock _percent = Label("", 30, Accent, true);
    private readonly TextBlock _connectionCaption = Label("", 12, TextColor, true);
    private readonly TextBlock _sourceCaption = Label("", 12, Muted);
    private readonly TextBlock _fileCategory = Label("", 13, TextColor);
    private readonly TextBlock _fileTypeChip = Label("", 13, TextColor);
    private readonly Grid _previewSlot = new();
    private bool _localIconApplied;
    private bool _localIconPending;
    private readonly ProgressBar _progress = new() { Height = 13, Minimum = 0, Maximum = 100,
        Background = Brush("#061528"), Foreground = PremiumProgressFill, BorderBrush = Outline, BorderThickness = new Thickness(0) };
    private readonly TextBlock _details = Label("", 12, TextColor);
    private readonly TextBlock _workersNotice = Label("", 12, Muted);
    private readonly Button _primary = ActionButton("", true);
    private readonly Button _pause = ActionButton("Pause");
    private readonly Button _stop = ActionButton("Stop");
    private readonly Button _dismiss = ActionButton("Close");
    private readonly string _kind;

    public DownloadProgressWindow(DownloadEntry entry, Action<DownloadEntry, Command> onCommand)
    {
        _entry = entry;
        _onCommand = onCommand;
        _kind = PreviewKind(entry);
        Title = "RageDesk Pro · " + entry.Name;
        Width = 846;
        Height = 700;
        MinWidth = 750;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/RangeDesk;component/Themes/ProgressWindowPremium.xaml", UriKind.Relative)
        });
        _primary.Style = (Style)FindResource("PremiumPrimaryButton");
        _pause.Style = (Style)FindResource("PremiumSecondaryButton");
        _stop.Style = (Style)FindResource("PremiumSecondaryButton");
        _dismiss.Style = (Style)FindResource("PremiumSecondaryButton");
        SourceInitialized += (_, _) =>
        {
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { } // Old Windows build; keep functional chrome.
            catch (EntryPointNotFoundException) { }
        };
        Background = Surface;
        Foreground = TextColor;
        FontFamily = new FontFamily("Segoe UI");
        Content = BuildLayout();
        _primary.Click += (_, _) => _onCommand(_entry,
            _entry.Status == DownloadStatus.Completed ? Command.OpenFolder : Command.StartOrResume);
        _pause.Click += (_, _) => _onCommand(_entry, Command.Pause);
        _stop.Click += (_, _) => _onCommand(_entry, Command.Stop);
        _dismiss.Click += (_, _) => Close();
        _entry.PropertyChanged += OnEntryChanged;
        Closed += (_, _) => _entry.PropertyChanged -= OnEntryChanged;
        Refresh();
        _ = TryApplyLocalIconAsync();
    }

    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
    private static TextBlock Label(string value, double size, Brush brush, bool bold = false) =>
        new() { Text = value, FontSize = size, Foreground = brush, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private static Button ActionButton(string title, bool primary = false) =>
        new() { Content = title, Height = 39, MinWidth = primary ? 148 : 112,
                Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(15, 6, 15, 6) };
    private static Border Panel(UIElement child, Thickness? margin = null, Thickness? padding = null) =>
        new() { Child = child, Background = PremiumPanelFill, BorderBrush = Outline,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Margin = margin ?? new Thickness(0), Padding = padding ?? new Thickness(14) };

    private FrameworkElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid { Margin = new Thickness(3, 1, 3, 18) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(RageDeskPopupArtwork.BrandMark(51));
        var headline = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Label("RageDesk", 27, TextColor, true));
        title.Children.Add(new TextBlock { Text = " Pro", FontSize = 27, FontWeight = FontWeights.Bold,
            Foreground = Accent, VerticalAlignment = VerticalAlignment.Center });
        headline.Children.Add(title);
        headline.Children.Add(_subtitle);
        Grid.SetColumn(headline, 1); heading.Children.Add(headline);
        Grid.SetRow(heading, 0); root.Children.Add(heading);

        var summary = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var preview = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _previewSlot.Width = 152;
        _previewSlot.Height = 118;
        _previewSlot.Children.Add(RageDeskPopupArtwork.PreviewGraphic(_entry, _kind));
        preview.Children.Add(_previewSlot);
        preview.Children.Add(Label(RageDeskPopupArtwork.RecognizedSource(_entry) is { Length: > 0 } name
            ? name : _kind, 13, Muted, true));
        Grid.SetColumn(preview, 0);
        var artwork = Panel(preview, new Thickness(0, 0, 18, 0), new Thickness(16));
        artwork.Effect = new DropShadowEffect { Color = Color.FromRgb(0, 112, 255),
            BlurRadius = 15, ShadowDepth = 0, Opacity = 0.18 };
        summary.Children.Add(artwork);
        var file = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _name.ToolTip = _entry.Name; // Full filenames stay accessible even when ellipsized.
        file.Children.Add(_name);
        file.Children.Add(_origin);
        file.Children.Add(new Border { Height = 13, Background = Brushes.Transparent });
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        chips.Children.Add(InfoChip(_fileTypeChip));
        chips.Children.Add(InfoChip(_fileCategory));
        file.Children.Add(chips);
        file.Children.Add(_size);
        file.Children.Add(_sourceCaption);
        Grid.SetColumn(file, 1); summary.Children.Add(file);
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        var progress = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        var readout = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        readout.Children.Add(_bytes);
        var speedReadout = ReadoutWithIcon("\uE9D9", _speed);
        var clockReadout = ReadoutWithIcon("\uE823", _eta);
        Grid.SetColumn(speedReadout, 1); readout.Children.Add(speedReadout);
        Grid.SetColumn(clockReadout, 2); readout.Children.Add(clockReadout);
        Grid.SetColumn(_percent, 3); readout.Children.Add(_percent);
        progress.Children.Add(readout);
        progress.Children.Add(_progress);
        Grid.SetRow(progress, 2); root.Children.Add(progress);

        var tabs = new TabControl { Margin = new Thickness(0, 5, 0, 18),
            Background = Surface, Foreground = TextColor, BorderBrush = Outline };
        var workerList = new ListView { ItemsSource = _entry.Workers, Background = Surface,
            Foreground = TextColor, BorderBrush = Outline, BorderThickness = new Thickness(0) };
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "#", DisplayMemberBinding = new Binding(nameof(DownloadWorkerState.Title)), Width = 75 });
        gv.Columns.Add(new GridViewColumn { Header = "Downloaded", DisplayMemberBinding = new Binding(nameof(DownloadWorkerState.BytesLabel)), Width = 210 });
        gv.Columns.Add(new GridViewColumn { Header = "Speed", DisplayMemberBinding = new Binding(nameof(DownloadWorkerState.SpeedLabel)), Width = 155 });
        gv.Columns.Add(new GridViewColumn { Header = "Status", DisplayMemberBinding = new Binding(nameof(DownloadWorkerState.StateLabel)), Width = 130 });
        workerList.View = gv;
        var connectionLayout = new DockPanel();
        DockPanel.SetDock(_workersNotice, Dock.Top);
        _workersNotice.Margin = new Thickness(8, 9, 8, 8);
        connectionLayout.Children.Add(_workersNotice);
        connectionLayout.Children.Add(workerList);
        var connectionsTab = new TabItem { Header = TabHeader("\uE71B", _connectionCaption), Content = connectionLayout };
        tabs.Items.Add(connectionsTab);
        tabs.Items.Add(new TabItem { Header = TabHeader("\uE9D9", Label("Speed", 12, TextColor)),
            Content = new SpeedGraph { Source = _entry, MinHeight = 150, Margin = new Thickness(8) } });
        tabs.Items.Add(new TabItem { Header = TabHeader("\uE8A5", Label("Details", 12, TextColor)),
            Content = Panel(_details, new Thickness(7), new Thickness(16)) });
        tabs.Items.Add(new TabItem { Header = TabHeader("\uE8EF", Label("Log", 12, TextColor)),
            Content = new ListBox { ItemsSource = _entry.Events,
                Background = Surface, Foreground = TextColor, BorderThickness = new Thickness(0) } });
        Grid.SetRow(tabs, 3); root.Children.Add(tabs);

        var actions = new DockPanel { LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _pause.Content = ButtonCaption("\uE769", "Pause");
        _stop.Content = ButtonCaption("\uE71A", "Stop");
        left.Children.Add(_pause); left.Children.Add(_stop);
        DockPanel.SetDock(left, Dock.Left); actions.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_dismiss); right.Children.Add(_primary);
        DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
        Grid.SetRow(actions, 4); root.Children.Add(actions);
        return root;
    }

    private static string PreviewKind(DownloadEntry entry)
    {
        string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (extension is ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".iso") return "Archive";
        if (extension is ".exe" or ".msi" or ".msix" or ".apk") return "Application";
        if (extension is ".mp4" or ".mkv" or ".webm" or ".mov" || entry.IsMedia) return "Video";
        if (extension is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a") return "Music";
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp") return "Image";
        if (extension is ".pdf" or ".docx" or ".txt") return "Document";
        return "File";
    }



    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke((Action)Refresh);
    }

    private void Refresh()
    {
        var status = _entry.Status;
        _subtitle.Text = status + " · " + (_entry.Host.Length == 0 ? "Local download" : _entry.Host);
        _name.Text = _entry.Name;
        _origin.Text = _entry.Host;
        _type.Text = _kind + " · " + Path.GetExtension(_entry.Name).ToUpperInvariant();
        _size.Text = _entry.Total is long total ? "File size: " + DownloadEntry.FormatSize(total) : "File size: unknown";
        _bytes.Text = _entry.TransferredLabel;
        _speed.Text = "↓ " + _entry.SpeedLabel;
        _eta.Text = "ETA: " + _entry.EtaLabel;
        _percent.Text = _entry.ProgressLabel;
        _progress.IsIndeterminate = _entry.IsIndeterminate;
        _progress.Value = _entry.Percent ?? 0;
        _workersNotice.Text = _entry.IsMedia
            ? "Video is managed by the external media tool; per-worker HTTP measurements are unavailable."
            : _entry.ConnectionSummary + " · only measured worker connections are shown";
        _details.Text = $"Status: {status}\nSource: {_entry.Host}\nName: {_entry.Name}\nCategory: {_entry.Category}\nSave to: {Path.GetDirectoryName(_entry.Destination)}\nReceived: {_entry.TransferredLabel}\nConnections: {_entry.ConnectionSummary}\nChecksum: {_entry.ChecksumLabel}\n{_entry.Error}";
        _details.TextWrapping = TextWrapping.Wrap;
        _primary.Visibility = status == DownloadStatus.Downloading || (status != DownloadStatus.Completed && _entry.IsMedia && status != DownloadStatus.Queued)
            ? Visibility.Collapsed : Visibility.Visible;
        _primary.Content = status == DownloadStatus.Completed ? "Open Folder" :
            status == DownloadStatus.Queued ? "Start Download" : "Resume";
        _pause.Visibility = status == DownloadStatus.Downloading && !_entry.IsMedia ? Visibility.Visible : Visibility.Collapsed;
        _stop.Visibility = status == DownloadStatus.Downloading ? Visibility.Visible : Visibility.Collapsed;
        _dismiss.Content = status == DownloadStatus.Queued ? "Cancel" : "Close";
    }
}
