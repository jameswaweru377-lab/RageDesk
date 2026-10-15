namespace RangeDesk.Core;

/// <summary>
/// Separates stored download state from visible views. A state transition must
/// not remove an active transfer from Queue or from the main Downloads page.
/// </summary>
public static class DownloadListVisibility
{
    public static bool Matches(
        DownloadEntry item,
        string page = "all",
        string? search = null,
        DownloadStatus? status = null,
        string? category = null,
        string? label = null)
    {
        if (page.Equals("completed", StringComparison.OrdinalIgnoreCase)
            && item.Status != DownloadStatus.Completed) return false;

        // "Queue" includes active downloads. Hiding a newly started item made
        // users believe their downloads had disappeared.
        if (page.Equals("queue", StringComparison.OrdinalIgnoreCase)
            && item.Status == DownloadStatus.Completed) return false;

        if (status is DownloadStatus selected && item.Status != selected) return false;
        if (!string.IsNullOrWhiteSpace(category)
            && !string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(label)
            && !item.Labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(label, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(search)
            && !item.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
            && !item.Host.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
