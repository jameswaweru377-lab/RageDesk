using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using RangeDesk.Core;

namespace RangeDesk.App.Controls;

/// <summary>System Segoe MDL2 Assets glyphs, bundled with Windows 10/11.
/// No remote icon CDN or third-party downloaded icon library.</summary>
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
            DownloadStatus.Downloading => "#DEECFF",
            DownloadStatus.Completed => "#DDF8EA",
            DownloadStatus.Queued => "#FFF0D8",
            DownloadStatus.Paused => "#EBE9FA",
            DownloadStatus.Failed => "#FFE2E5",
            DownloadStatus.Canceled => "#F1F1F5",
            _ => "#F1F1F5"
        } : "#F1F1F5";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LightStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DownloadStatus status ? status switch
        {
            DownloadStatus.Downloading => "#166CDB",
            DownloadStatus.Completed => "#07875C",
            DownloadStatus.Queued => "#B66C19",
            DownloadStatus.Paused => "#7353B4",
            DownloadStatus.Failed => "#B3394F",
            DownloadStatus.Canceled => "#65708B",
            _ => "#65708B"
        } : "#65708B";

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
