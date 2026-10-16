using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// User-clicked, browser-originated media format choice. Enqueued locally
/// only when the visible app is running; the app re-probes the page and
/// verifies that the chosen format really exists before downloading.
/// No cookies, authentication headers or commands are accepted.
/// </summary>
public sealed record BrowserMediaRequest(string Action, string Url, string FormatId,
    string? JobId = null, bool QueueOnly = false,
    string? FileStem = null, string SaveLocation = "default")
{
    public void Validate()
    {
        if (Action != "media_download" ||
            Url is null || Url.Length > 4096 ||
            !OptionalMediaExtractor.IsPageCandidate(Url) ||
            string.IsNullOrWhiteSpace(FormatId) || FormatId.Length > 64 ||
            !FormatId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ||
            (JobId is not null && !Guid.TryParseExact(JobId, "N", out _)) ||
            SaveLocation is not ("default" or "videos") ||
            !IsValidStem(FileStem))
            throw new ArgumentException("Invalid user-selected media download request.");
    }

    public static bool IsValidStem(string? value) =>
        value is null || (value.Length is >= 1 and <= 80 &&
            value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'));
}

public static class BrowserMediaInbox
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "browser-media-inbox");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Enqueue(string folder, string json)
    {
        if (json.Length > 8192) throw new ArgumentException("Media request too large.");
        var item = JsonSerializer.Deserialize<BrowserMediaRequest>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty media request.");
        item.Validate();
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder, "*.json").Take(20).Count() >= 20)
            throw new InvalidOperationException("Too many pending media requests.");
        string name = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(folder, name + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(item));
            File.Move(temporary, Path.Combine(folder, name + ".json"));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static BrowserMediaRequest? TryTake(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        foreach (string file in Directory.EnumerateFiles(folder, "*.json").Take(20))
        {
            try
            {
                if (new FileInfo(file).Length > 8192) continue;
                var item = JsonSerializer.Deserialize<BrowserMediaRequest>(
                    File.ReadAllText(file), JsonOptions);
                item?.Validate();
                if (item is not null) return item;
            }
            catch (Exception ex) when (
                ex is JsonException or ArgumentException or IOException)
            {
                // Never display signed URLs or secrets from a corrupt request.
            }
            finally
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        return null;
    }
}
