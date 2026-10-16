using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Explicit opt-in, per-download native-message handoff. The browser MUST
/// retain its paused download until the visible RangeDesk GUI reports that
/// it has accepted and started its own transfer. A queued URL alone is not
/// an acceptance. No cookies, headers or browser history are collected.
/// </summary>
public sealed record BrowserAutomaticCaptureJob(string Id, string Url, DateTimeOffset ExpiresAtUtc);

public static class BrowserAutomaticCapture
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "browser-auto-inbox");

    private static readonly HashSet<string> SupportedExtensions = new(
        [".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".exe", ".msi",
         ".msix", ".iso", ".pdf", ".docx", ".xlsx", ".pptx", ".mp4", ".mp3",
         ".mkv", ".webm", ".png", ".jpg", ".jpeg", ".webp", ".apk"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsEligibleUrl(string url)
    {
        if (!DownloadEngine.IsAllowedUrl(url) || url.Length > 4096) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            SupportedExtensions.Contains(Path.GetExtension(uri.AbsolutePath));
    }

    public static BrowserAutomaticCaptureJob Queue(string folder, string json,
        DateTimeOffset? now = null)
    {
        if (json.Length > 8192) throw new ArgumentException("Browser message is too large.");
        using var payload = JsonDocument.Parse(json);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("action", out var action) ||
            action.GetString() != "capture_download" ||
            !payload.RootElement.TryGetProperty("url", out var url) ||
            url.ValueKind != JsonValueKind.String ||
            url.GetString() is not string directUrl ||
            !IsEligibleUrl(directUrl))
            throw new ArgumentException("Only direct, supported browser downloads can be captured.");
        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.pending").Take(100).Count() >= 100)
            throw new InvalidOperationException("Too many pending browser downloads.");
        var job = new BrowserAutomaticCaptureJob(
            Guid.NewGuid().ToString("N"), directUrl, (now ?? DateTimeOffset.UtcNow).AddSeconds(20));
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, job.Id + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(job));
            File.Move(temp, Path.Combine(folder, job.Id + ".pending"));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return job;
    }

    /// <summary>
    /// Atomically claim one pending request on the GUI thread. Reject expired
    /// or damaged messages without ever starting a network connection.
    /// </summary>
    public static BrowserAutomaticCaptureJob? TryTake(string folder, DateTimeOffset? now = null)
    {
        if (!Directory.Exists(folder)) return null;
        foreach (var pending in Directory.EnumerateFiles(folder, "*.pending").Take(25))
        {
            var claimed = Path.ChangeExtension(pending, ".processing");
            try
            {
                File.Move(pending, claimed);
                if (new FileInfo(claimed).Length > 8192) continue;
                var job = JsonSerializer.Deserialize<BrowserAutomaticCaptureJob>(File.ReadAllText(claimed));
                if (job is null || !Guid.TryParseExact(job.Id, "N", out _) ||
                    !string.Equals(Path.GetFileNameWithoutExtension(claimed), job.Id, StringComparison.Ordinal) ||
                    !IsEligibleUrl(job.Url) || job.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow))
                    continue;
                return job;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // Another process may have claimed it; never leak URL data.
            }
            finally
            {
                // Valid claims remain until Complete writes the acknowledgement.
                if (File.Exists(claimed))
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<BrowserAutomaticCaptureJob>(File.ReadAllText(claimed));
                        if (item is null || item.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow) ||
                            !IsEligibleUrl(item.Url) || item.Id != Path.GetFileNameWithoutExtension(claimed))
                            File.Delete(claimed);
                    }
                    catch (Exception) { try { File.Delete(claimed); } catch (IOException) { } }
                }
            }
        }
        return null;
    }

    /// <summary>Acknowledgement is intentionally URL-free.</summary>
    public static void Complete(string folder, BrowserAutomaticCaptureJob job, bool started)
    {
        if (!Guid.TryParseExact(job.Id, "N", out _)) throw new ArgumentException("Invalid capture id.");
        var processing = Path.Combine(folder, job.Id + ".processing");
        var temp = Path.Combine(folder, job.Id + ".reply.tmp");
        var ready = Path.Combine(folder, job.Id + ".reply");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new { accepted = started }));
            File.Move(temp, ready, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(processing)) File.Delete(processing);
        }
    }

    public static async Task<bool> WaitForDecisionAsync(string folder, BrowserAutomaticCaptureJob job,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(job.Id, "N", out _)) return false;
        var reply = Path.Combine(folder, job.Id + ".reply");
        while (DateTimeOffset.UtcNow < job.ExpiresAtUtc.AddSeconds(2))
        {
            if (File.Exists(reply))
            {
                try
                {
                    using var payload = JsonDocument.Parse(await File.ReadAllTextAsync(reply, cancellationToken));
                    return payload.RootElement.TryGetProperty("accepted", out var accepted) &&
                        accepted.ValueKind == JsonValueKind.True;
                }
                catch (Exception ex) when (ex is IOException or JsonException) { return false; }
                finally { try { File.Delete(reply); } catch (IOException) { } }
            }
            await Task.Delay(100, cancellationToken);
        }
        // The GUI must check ExpiresAtUtc before calling engine.Start.
        try { File.Delete(Path.Combine(folder, job.Id + ".pending")); }
        catch (IOException) { }
        return false;
    }
}
