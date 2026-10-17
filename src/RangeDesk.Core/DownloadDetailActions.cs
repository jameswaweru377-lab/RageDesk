namespace RangeDesk.Core;

/// <summary>Allowed detail-window actions for an immutable download task.</summary>
public static class DownloadDetailActions
{
    public static bool CanPause(DownloadStatus state, bool isMedia) =>
        !isMedia && state == DownloadStatus.Downloading;
    public static bool CanResume(DownloadStatus state, bool isMedia) =>
        !isMedia && state is DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled;
    public static bool CanStart(DownloadStatus state, bool isMedia) =>
        !isMedia && state == DownloadStatus.Queued;
    public static bool CanOpen(DownloadStatus state) => state == DownloadStatus.Completed;
    public static bool CanDownloadAgain(DownloadStatus state, bool isMedia) =>
        !isMedia && state == DownloadStatus.Completed;
    public static bool CanStop(DownloadStatus state) => state == DownloadStatus.Downloading;
}

/// <summary>
/// Re-downloads are new independent tasks with new filenames. Never overwrite
/// the completed original, an existing user file, or resumable sidecars.
/// </summary>
public static class DownloadAgainPath
{
    public static string Create(string original, IEnumerable<string> trackedPaths)
    {
        if (string.IsNullOrWhiteSpace(original) || !Path.IsPathFullyQualified(original))
            throw new ArgumentException("A full destination path is required.", nameof(original));
        string full = Path.GetFullPath(original);
        var names = new HashSet<string>(trackedPaths.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        string directory = Path.GetDirectoryName(full)!;
        string stem = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);
        for (int i = 1; i <= 10000; i++)
        {
            string destination = Path.Combine(directory, $"{stem} (again {i}){ext}");
            if (names.Contains(destination) || File.Exists(destination) ||
                Directory.Exists(destination) ||
                File.Exists(destination + ".rdpartial") ||
                File.Exists(destination + ".rdstate") ||
                Directory.Exists(destination + ".rdsegments") ||
                Directory.Exists(destination + ".rdadaptive"))
                continue;
            return destination;
        }
        throw new IOException("There are too many versions of this download. Select a different folder.");
    }
}
