using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using RangeDesk.Core;

namespace RangeDesk.App.Controls;

/// <summary>System Segoe MDL2 Assets glyphs, bundled with Windows 10/11.
/// No remote icon CDN or third-party downloaded icon library.</summary>
/// <summary>Show actions only when they make sense for the inspected task.
/// Bind both Status and IsMedia, so completed tasks never show Pause/Resume.</summary>
public sealed class DownloadDetailActionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not DownloadStatus state ||
            values[1] is not bool isMedia)
            return System.Windows.Visibility.Collapsed;
        string action = parameter?.ToString() ?? "";
        bool show = action switch
        {
            "Pause" => DownloadDetailActions.CanPause(state, isMedia),
            "Resume" => DownloadDetailActions.CanResume(state, isMedia),
            "Start" => DownloadDetailActions.CanStart(state, isMedia),
            "Open" => DownloadDetailActions.CanOpen(state),
            "Again" => DownloadDetailActions.CanDownloadAgain(state, isMedia),
            "Stop" => DownloadDetailActions.CanStop(state),
            _ => false
        };
        return show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FileIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string ext = Path.GetExtension(value as string ?? "").ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".msi" or ".appx" => "\uE8F1",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8B7",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "\uE714",
            ".jpg" or ".png" or ".gif" or ".webp" or ".jpeg" => "\uE91B",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" => "\uE8A5",
            ".iso" or ".img" => "\uE7F4",
            ".mp3" or ".flac" or ".wav" => "\uE8D6",
            _ => "\uE8A5"
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class FileIconColorConverter : IValueConverter
{
    private static readonly Brush Blue = Brush("#56B5FF");
    private static readonly Brush Violet = Brush("#B78CFF");
    private static readonly Brush Orange = Brush("#FFC06A");
    private static readonly Brush Green = Brush("#61DABB");

    private static Brush Brush(string hex)
    {
        var color = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        color.Freeze();
        return color;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ext = Path.GetExtension(value as string ?? "").ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".rar" or ".7z" => Violet,
            ".exe" or ".msi" => Blue,
            ".mp4" or ".mkv" or ".avi" => Orange,
            ".jpg" or ".png" or ".gif" or ".webp" => Green,
            _ => Blue
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StatusColorConverter : IValueConverter
{
    private static readonly Brush Running = Solid("#0D66D1");
    private static readonly Brush Completed = Solid("#007C67");
    private static readonly Brush Waiting = Solid("#5C25B9");
    private static readonly Brush Paused = Solid("#405F83");
    private static readonly Brush Warning = Solid("#8A551F");
    private static readonly Brush Danger = Solid("#A4354B");

    private static Brush Solid(string hex)
    {
        var color = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        color.Freeze();
        return color;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DownloadStatus status ? status switch
        {
            DownloadStatus.Downloading => Running,
            DownloadStatus.Completed => Completed,
            DownloadStatus.Queued => Waiting,
            DownloadStatus.Paused => Paused,
            DownloadStatus.Canceled => Warning,
            DownloadStatus.Failed => Danger,
            _ => Paused
        } : Paused;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LightStatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DownloadStatus status ? status switch
        {
            DownloadStatus.Downloading => "#E6F2FF",
            DownloadStatus.Completed => "#E3F8EF",
            DownloadStatus.Queued => "#FFF4E2",
            DownloadStatus.Paused => "#F0E9FF",
            DownloadStatus.Failed => "#FCE9EE",
            DownloadStatus.Canceled => "#EDF2F8",
            _ => "#EDF2F8"
        } : "#EDF2F8";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LightStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DownloadStatus status ? status switch
        {
            DownloadStatus.Downloading => "#126EC8",
            DownloadStatus.Completed => "#10745E",
            DownloadStatus.Queued => "#8E5A0E",
            DownloadStatus.Paused => "#6542A6",
            DownloadStatus.Failed => "#B33B56",
            DownloadStatus.Canceled => "#516A87",
            _ => "#516A87"
        } : "#516A87";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class DownloadActionGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DownloadStatus status ? status switch
        {
            DownloadStatus.Completed => "\uE8B7", // Folder
            DownloadStatus.Downloading => "\uE769", // Pause
            DownloadStatus.Queued or DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled => "\uE768", // Play
            _ => "\uE768"
        } : "\uE768";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
