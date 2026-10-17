namespace RangeDesk.Core;

/// <summary>Small, local extension hints. Explicitly chosen categories always win.</summary>
public static class DownloadCategory
{
    private static readonly HashSet<string> Software = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msix", ".appx", ".deb", ".rpm", ".apk" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv", ".ts" };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg" };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv" };
    private static readonly HashSet<string> Music = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus" };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".gz", ".tar", ".xz", ".iso" };

    public static string Guess(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (Software.Contains(extension)) return "Software";
        if (Videos.Contains(extension)) return "Videos";
        if (Images.Contains(extension)) return "Images";
        if (Documents.Contains(extension)) return "Documents";
        if (Archives.Contains(extension)) return "Archives";
        return "Other";
    }
}
