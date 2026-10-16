namespace RangeDesk.Core;

/// <summary>Saved source-host views. These are download filters only; no
/// browser capture, website login, or copyrighted stream extraction.</summary>
public static class SitePresets
{
    public static readonly string[] Names = ["Microsoft", "GitHub", "Google Drive", "YouTube"];
    public static bool Matches(string? host, string preset)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.TrimEnd('.').ToLowerInvariant();
        return preset switch
        {
            "Microsoft" => IsHost(host, "microsoft.com") || IsHost(host, "microsoft.net"),
            "GitHub" => IsHost(host, "github.com") || IsHost(host, "githubusercontent.com"),
            "Google Drive" => IsHost(host, "drive.google.com") || IsHost(host, "googleusercontent.com"),
            "YouTube" => IsHost(host, "youtube.com") || IsHost(host, "googlevideo.com"),
            _ => false
        };
    }

    private static bool IsHost(string host, string suffix) =>
        host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
}
