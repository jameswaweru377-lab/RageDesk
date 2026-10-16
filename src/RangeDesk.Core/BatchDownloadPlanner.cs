namespace RangeDesk.Core;

/// <summary>
/// Safely plans a batch of pasted direct HTTP/HTTPS links without writing files
/// or starting transfers. It never silently reuses a destination in the catalog.
/// </summary>
public sealed record BatchDownloadCandidate(string Url, string Destination);
public static class BatchDownloadPlanner
{
    public static IReadOnlyList<BatchDownloadCandidate> Plan(
        string text, string folder, IEnumerable<string> occupiedPaths)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Choose a full download folder.");
        var root = Path.GetFullPath(folder);
        var urls = text.Split(['\r','\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (urls.Length is < 1 or > 100) throw new ArgumentException("Paste between 1 and 100 links.");
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var used = new HashSet<string>(occupiedPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var result = new List<BatchDownloadCandidate>();
        foreach (var link in urls)
        {
            if (!DownloadEngine.IsAllowedUrl(link))
                throw new ArgumentException("The batch contains an invalid or non-HTTP(S) link.");
            if (!seenUrls.Add(link)) continue;
            var uri = new Uri(link, UriKind.Absolute);
            string name = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "download.bin";
            if (HlsPlaylist.IsHlsUrl(link))
                name = Path.ChangeExtension(name, ".ts");
            if (name.Length > 180) name = name[..180];
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            string destination = Path.Combine(root, name);
            for (int n = 2; used.Contains(destination) || File.Exists(destination)
                 || File.Exists(destination + ".rdpartial")
                 || Directory.Exists(destination + ".rdsegments")
                 || Directory.Exists(destination + ".rdadaptive")
                 || File.Exists(destination + ".rdhls-state")
                 || File.Exists(destination + ".rdhls-partial"); n++)
            {
                if (n > 10000) throw new IOException("Could not choose a unique download name.");
                destination = Path.Combine(root, $"{stem} ({n}){ext}");
            }
            used.Add(destination);
            result.Add(new BatchDownloadCandidate(link, destination));
        }
        return result;
    }
}
