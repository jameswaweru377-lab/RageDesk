using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Explicit opt-in front end for a separately installed yt-dlp and FFmpeg.
/// This component does not bypass DRM, read cookies, authenticate websites,
/// load external plugins or download tools. It handles one user-supplied page
/// URL and only surfaces actual formats reported by the extractor.
/// </summary>
public static class OptionalMediaExtractor
{
    public sealed record Format(string Id, string Label, bool NeedsAudioMerge, int Height);
    public sealed record Probe(string Title, string VideoId, IReadOnlyList<Format> Formats);

    public static string DefaultToolsFolder(string appFolder) =>
        Path.Combine(Path.GetFullPath(appFolder), "MediaTools");

    public static string ExtractorExecutable(string appFolder) =>
        Path.Combine(DefaultToolsFolder(appFolder), "yt-dlp.exe");

    public static string FfmpegExecutable(string appFolder) =>
        Path.Combine(DefaultToolsFolder(appFolder), "ffmpeg.exe");

    public static bool IsReady(string appFolder) =>
        File.Exists(ExtractorExecutable(appFolder)) && File.Exists(FfmpegExecutable(appFolder));

    public static bool IsPageCandidate(string url)
    {
        if (!DownloadEngine.IsAllowedUrl(url)) return false;
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        // Direct transfers still go through the existing optimized RangeDesk
        // engine, rather than an external extractor.
        return extension is not (".mp4" or ".webm" or ".m4v" or ".mov" or
            ".ts" or ".m3u8" or ".zip" or ".exe" or ".pdf" or ".msi" or
            ".mkv" or ".mp3" or ".7z" or ".rar" or ".iso");
    }

    public static Probe ParseProbe(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Extractor returned an invalid media description.");
        if (!root.TryGetProperty("formats", out var formats) ||
            formats.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("This webpage did not expose supported media formats.");
        string title = ReadText(root, "title") ?? "Untitled media";
        string id = ReadText(root, "id") ?? "";
        var result = new List<Format>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in formats.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("has_drm", out var drm) &&
                drm.ValueKind is JsonValueKind.True or JsonValueKind.String) continue;
            string? formatId = ReadText(item, "format_id");
            if (string.IsNullOrWhiteSpace(formatId) || formatId.Length > 64 ||
                !formatId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
                continue;
            string video = ReadText(item, "vcodec") ?? "none";
            string audio = ReadText(item, "acodec") ?? "none";
            if (video == "none" || !seen.Add(formatId)) continue;
            string ext = ReadText(item, "ext") ?? "media";
            int height = item.TryGetProperty("height", out var h) &&
                h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var ht) &&
                ht is > 0 and <= 8192 ? ht : 0;
            bool separateAudio = audio == "none";
            string quality = height > 0 ? $"{height}p" : "Unknown resolution";
            string label = $"{quality} · {ext.ToUpperInvariant()} · {formatId}" +
                (separateAudio ? " (merge separate audio)" : " (video + audio)");
            result.Add(new Format(formatId, label, separateAudio, height));
            if (result.Count >= 80) break;
        }
        if (result.Count == 0)
            throw new NotSupportedException("No unprotected video formats were reported by this page.");
        return new Probe(title.Length > 160 ? title[..160] : title,
            id.Length > 80 ? id[..80] : id,
            result.OrderByDescending(x => x.Height).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }

    private static string? ReadText(JsonElement item, string name) =>
        item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

    public static IReadOnlyList<string> ProbeArguments(string url)
    {
        ValidatePage(url);
        return ["--ignore-config", "--no-plugin-dirs", "--no-remote-components",
            "--no-playlist", "--skip-download", "--dump-single-json",
            "--no-warnings", "--", url];
    }

    public static IReadOnlyList<string> DownloadArguments(string url, Format format,
        string destinationDirectory, string ffmpegExecutable)
    {
        ValidatePage(url);
        if (format is null || string.IsNullOrWhiteSpace(format.Id) ||
            !format.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            throw new ArgumentException("Select a valid format found by Analyze.");
        if (!Path.IsPathFullyQualified(destinationDirectory) ||
            !Path.IsPathFullyQualified(ffmpegExecutable))
            throw new ArgumentException("A full download folder and FFmpeg path are required.");

        // A video-only track is paired with the best available audio track;
        // FFmpeg muxes both into MKV when compatible. Do not mislabel MPEG-TS
        // or silently manufacture quality levels.
        string selector = format.NeedsAudioMerge ? format.Id + "+ba/" + format.Id : format.Id;
        return ["--ignore-config", "--no-plugin-dirs", "--no-remote-components",
            "--no-playlist", "--no-overwrites", "--windows-filenames",
            "--restrict-filenames", "--continue", "--newline",
            "--format", selector, "--merge-output-format", "mkv",
            "--ffmpeg-location", ffmpegExecutable,
            "--paths", Path.GetFullPath(destinationDirectory),
            "--output", "RangeDesk-%(id).80s.%(ext)s", "--", url];
    }

    private static void ValidatePage(string url)
    {
        if (url is null || url.Length > 4096 || !DownloadEngine.IsAllowedUrl(url))
            throw new ArgumentException("Enter one HTTP(S) page URL without embedded credentials.");
    }

    private static ProcessStartInfo Setup(string appFolder, IReadOnlyList<string> arguments)
    {
        var path = ExtractorExecutable(appFolder);
        if (!File.Exists(path)) throw new FileNotFoundException(
            "Missing MediaTools/yt-dlp.exe. Install the official tool manually.", path);
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = DefaultToolsFolder(appFolder)
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }

    public static async Task<Probe> AnalyzeAsync(string appFolder, string url,
        CancellationToken token = default)
    {
        var info = Setup(appFolder, ProbeArguments(url));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        using var process = Process.Start(info) ??
            throw new InvalidOperationException("yt-dlp could not start.");
        try
        {
            Task<string> output = ReadBoundedAsync(process.StandardOutput, 8_000_000, timeout.Token);
            Task<string> error = ReadBoundedAsync(process.StandardError, 128_000, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string json = await output;
            _ = await error; // Never echo a possibly secret signed media URL.
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "The extractor could not analyze this video. Check permissions, tool version and site support.");
            return ParseProbe(json);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>
    /// Streams into the user-selected folder and reports only high-level
    /// status. No private URLs or yt-dlp debug output enter the UI/catalog.
    /// This preview does not claim integration with native RangeDesk pause/
    /// scheduling checkpoints, which remain separate from yt-dlp .part files.
    /// </summary>
    public static async Task DownloadAsync(string appFolder, string url,
        Format selected, string destinationDirectory, Action<string> status,
        CancellationToken token)
    {
        if (!IsReady(appFolder)) throw new FileNotFoundException(
            "Both MediaTools/yt-dlp.exe and MediaTools/ffmpeg.exe are required.");
        Directory.CreateDirectory(destinationDirectory);
        var info = Setup(appFolder, DownloadArguments(url, selected,
            destinationDirectory, FfmpegExecutable(appFolder)));
        using var process = Process.Start(info) ??
            throw new InvalidOperationException("yt-dlp could not start.");
        try
        {
            // Read lines to avoid deadlocks. Do not surface raw output; it can
            // contain expiring signed media URLs, private titles and paths.
            Task stdout = DrainAsync(process.StandardOutput, token);
            Task stderr = DrainAsync(process.StandardError, token);
            status("Downloading selected video and audio…");
            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Media download failed. Check the source, yt-dlp/FFmpeg compatibility and access rights.");
            status("Media download complete. Check the selected downloads folder.");
        }
        catch (OperationCanceledException)
        {
            status("Media download stopped. yt-dlp partial files may be resumable later.");
            throw;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader,
        int maxChars, CancellationToken token)
    {
        char[] buffer = new char[8192];
        var result = new StringBuilder();
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            if (result.Length > maxChars - count)
                throw new InvalidDataException("Extractor response exceeds its safety limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        char[] buffer = new char[8192];
        while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { }
    }
}
