using System.IO;
using RangeDesk.Core;

namespace RangeDesk.App;

/// <summary>
/// Minimum read-only projection of the *existing* MainWindow state for the optional React preview.
/// No URL, query string, local path, auth headers, browser content or user preference secrets cross the bridge.
/// </summary>
internal sealed record ReactDashboardSnapshot(
    string Theme,
    ReactSummary Summary,
    ReactCounts Counts,
    Dictionary<string, int> CategoryCounts,
    Dictionary<string, int> SiteCounts,
    int ScheduledCount,
    ReactDownloadRow[] Rows,
    ReactSource[] Sources,
    ReactRecent[] Recent,
    double[] SpeedSamples,
    ReactDiskStorage? Storage,
    ReactSettingsDisplay Settings)
{
    public static ReactDashboardSnapshot Capture(
        IReadOnlyCollection<DownloadEntry> downloads,
        IReadOnlyDictionary<Guid, DateTimeOffset> knownCompletions,
        DownloadEntry? selected,
        string theme,
        int scheduledCount,
        string currentSpeed,
        string remainingTime,
        RangeDeskPreferences preferences)
    {
        var entries = downloads.ToArray();
        var counts = new ReactCounts(
            entries.Length,
            entries.Count(d => d.Status == DownloadStatus.Downloading),
            entries.Count(d => d.Status == DownloadStatus.Completed),
            entries.Count(d => d.Status == DownloadStatus.Paused),
            entries.Count(d => d.Status == DownloadStatus.Queued),
            entries.Count(d => d.Status == DownloadStatus.Failed));
        double todayBytes = entries.Where(d => d.Status == DownloadStatus.Completed &&
                knownCompletions.TryGetValue(d.Id, out var completed) &&
                completed.ToLocalTime().Date == DateTime.Today)
            .Sum(d => (double)Math.Max(0, d.Received));
        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["All Downloads"] = entries.Length,
            ["Programs"] = entries.Count(d => d.Category.Equals("Software", StringComparison.OrdinalIgnoreCase) ||
                                               d.Category.Equals("Programs", StringComparison.OrdinalIgnoreCase)),
            ["Videos"] = Count("Videos"),
            ["Music"] = Count("Music"),
            ["Documents"] = Count("Documents"),
            ["Compressed"] = Count("Archives"),
            ["Images"] = Count("Images")
        };
        int Count(string category) => entries.Count(d =>
            d.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var sites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft"] = entries.Count(d => SitePresets.Matches(d.Host, "Microsoft")),
            ["GitHub"] = entries.Count(d => SitePresets.Matches(d.Host, "GitHub")),
            ["Google Drive"] = entries.Count(d => SitePresets.Matches(d.Host, "Google Drive")),
            ["YouTube"] = entries.Count(d => SitePresets.Matches(d.Host, "YouTube"))
        };
        var sources = entries.Where(d => d.Received > 0 && d.Host.Length > 0)
            .GroupBy(d => d.Host, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReactSource(Trim(group.Key, 100),
                group.Sum(d => (double)Math.Max(0, d.Received))))
            .OrderByDescending(s => s.Bytes).Take(6).ToArray();
        var recent = entries.OrderByDescending(d => d.AddedAt).Take(6)
            .Select(d => new ReactRecent(d.Id.ToString("N"),
                d.AddedAt.ToLocalTime().ToString("HH:mm"),
                Trim(d.Status + " · " + d.Name, 180))).ToArray();
        // Exact bridge surface: a download ID allows stable selection in React only; there are no native commands.
        var rows = entries.Take(500)
            .Select(d => new ReactDownloadRow(
                d.Id.ToString("N"), Trim(d.Name, 180), Trim(d.Host, 100),
                Trim(d.Category, 50), d.Status.ToString(), Math.Max(0, d.Received),
                d.Total is long total ? Math.Max(0, total) : null,
                d.Percent is double pct && double.IsFinite(pct) ? Math.Clamp(pct, 0, 100) : null,
                double.IsFinite(d.BytesPerSecond) ? Math.Max(0, d.BytesPerSecond) : 0,
                d.EtaLabel, Math.Max(0, d.Connections), d.RequestedConnections,
                d.AddedLabel, d.AddedAt.ToUniversalTime().ToString("O"))).ToArray();
        double[] history = selected?.SpeedSamples.TakeLast(60)
            .Select(value => double.IsFinite(value) ? Math.Max(0, value) : 0).ToArray()
            ?? Array.Empty<double>();

        return new ReactDashboardSnapshot(
            theme == RangeDeskTheme.Arctic ? RangeDeskTheme.Arctic : RangeDeskTheme.Cobalt,
            new ReactSummary(currentSpeed, DownloadEntry.FormatSize(todayBytes), remainingTime),
            counts, categories, sites, Math.Max(0, scheduledCount), rows, sources, recent, history,
            ReadStorage(preferences.DownloadFolder),
            new ReactSettingsDisplay(preferences.StartImmediately, preferences.DefaultConnections,
                preferences.MaxConcurrentDownloads, preferences.DefaultBandwidthKiBps,
                !string.IsNullOrWhiteSpace(preferences.ProxyUri)));
    }

    private static ReactDiskStorage? ReadStorage(string downloadFolder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(downloadFolder));
            if (string.IsNullOrEmpty(root)) return null;
            DriveInfo disk = new(root);
            if (!disk.IsReady) return null;
            // Totals only; never disclose any path, folder name, drive label or account data to JS.
            return new ReactDiskStorage(disk.AvailableFreeSpace, disk.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}

internal sealed record ReactSummary(string CurrentSpeed, string DownloadedToday, string TimeRemaining);
internal sealed record ReactCounts(int All, int Downloading, int Completed, int Paused, int Queued, int Failed);
internal sealed record ReactDownloadRow(
    string Id, string Name, string Host, string Category, string Status, long ReceivedBytes,
    long? TotalBytes, double? Percent, double BytesPerSecond, string Eta,
    int Connections, int RequestedConnections, string Added, string AddedAt);
internal sealed record ReactSource(string Host, double Bytes);
internal sealed record ReactRecent(string Id, string Added, string Description);

internal sealed record ReactDiskStorage(long FreeBytes, long TotalBytes);
internal sealed record ReactSettingsDisplay(bool StartImmediately, int DefaultConnections,
    int MaxConcurrentDownloads, int DefaultBandwidthKiBps, bool ProxyEnabled);
