using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// User-initiated native-messaging handoff only. No browser cookies,
/// Authorization headers, page content or automatic interception.
/// </summary>
public sealed record BrowserLinkRequest(string Action, string Url)
{
    public string ValidateUrl()
    {
        if (!string.Equals(Action, "add_link", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Url) || Url.Length > 4096 ||
            !DownloadEngine.IsAllowedUrl(Url))
            throw new ArgumentException("Only user-requested direct HTTP/HTTPS links are accepted.");
        return Url;
    }
}

public static class BrowserLinkInbox
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "browser-inbox");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Enqueue(string folder, string json)
    {
        if (json.Length > 8192) throw new ArgumentException("Browser message is too large.");
        var request = JsonSerializer.Deserialize<BrowserLinkRequest>(json, JsonOptions)
            ?? throw new InvalidDataException("Browser message is empty.");
        request.ValidateUrl();
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder,"*.json").Take(100).Count() >= 100)
            throw new InvalidOperationException("Too many pending browser links. Open RangeDesk to review them.");
        string id = Guid.NewGuid().ToString("N");
        string pending = Path.Combine(folder, id + ".tmp");
        string ready = Path.Combine(folder, id + ".json");
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(request));
            File.Move(pending, ready);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    /// <summary>
    /// Returns one validated link for user confirmation, never starts
    /// a download. A malicious or corrupt pending request is discarded.
    /// </summary>
    public static string? TryTake(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(25))
        {
            try
            {
                if (new FileInfo(file).Length > 8192) continue;
                var request = JsonSerializer.Deserialize<BrowserLinkRequest>(
                    File.ReadAllText(file), JsonOptions);
                var url = request?.ValidateUrl();
                if (url is not null) return url;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
            {
                // Untrusted request is discarded without logging its URL.
            }
            finally
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        return null;
    }
}
