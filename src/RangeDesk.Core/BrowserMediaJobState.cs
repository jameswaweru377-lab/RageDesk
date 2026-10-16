using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>Small local, URL-free media job snapshots for browser status queries.</summary>
public sealed record BrowserMediaJobSnapshot(string JobId, string State, string Message,
    long? DownloadedBytes = null, long? TotalBytes = null, bool Estimated = false);

public static class BrowserMediaJobState
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "browser-media-status");

    private static string FileFor(string jobId)
    {
        if (!Guid.TryParseExact(jobId, "N", out _))
            throw new ArgumentException("Invalid media job ID.");
        return Path.Combine(DefaultDirectory, jobId + ".json");
    }

    public static void Update(string? jobId, string state, string message,
        long? downloadedBytes = null, long? totalBytes = null, bool estimated = false)
    {
        if (jobId is null) return;
        string file = FileFor(jobId);
        Directory.CreateDirectory(DefaultDirectory);
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var snapshot = new BrowserMediaJobSnapshot(jobId, state, message,
            downloadedBytes, totalBytes, estimated);
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static BrowserMediaJobSnapshot? Read(string jobId)
    {
        string path = FileFor(jobId);
        if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
        return JsonSerializer.Deserialize<BrowserMediaJobSnapshot>(File.ReadAllText(path));
    }

    public static void TryUpdate(string? jobId, string state, string message,
        long? downloadedBytes = null, long? totalBytes = null, bool estimated = false)
    {
        try { Update(jobId, state, message, downloadedBytes, totalBytes, estimated); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
