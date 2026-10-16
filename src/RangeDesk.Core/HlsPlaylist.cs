using System.Globalization;
using System.Text.RegularExpressions;

namespace RangeDesk.Core;

/// <summary>
/// Strict, deliberately limited HLS playlist reader. Produces a playable
/// MPEG-TS VOD output only; never treats fMP4, encrypted, DRM or live HLS as
/// a plain downloadable MP4. No keys or credentials are acquired.
/// </summary>
public static class HlsPlaylist
{
    public sealed record Variant(Uri PlaylistUrl, int Bandwidth, int? Height);
    public sealed record Playlist(IReadOnlyList<Uri> Segments, IReadOnlyList<Variant> Variants)
    {
        public bool IsMaster => Variants.Count > 0;
    }

    public static bool IsHlsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        DownloadEngine.IsAllowedUrl(url) &&
        parsed.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    private static Uri Link(Uri baseUri, string reference)
    {
        if (!Uri.TryCreate(baseUri, reference.Trim(), out var target) ||
            !DownloadEngine.IsAllowedUrl(target.AbsoluteUri) ||
            target.Fragment.Length > 0)
            throw new InvalidDataException("The playlist contains a non-HTTP(S) or unsafe media link.");
        return target;
    }

    public static Playlist Parse(string content, Uri playlistUrl)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1_048_576 ||
            !content.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("#EXTM3U", StringComparison.Ordinal))
            throw new InvalidDataException("This is not a supported HLS playlist.");

        var lines = content.Replace("\r\n", "\n").Split('\n');
        var variants = new List<Variant>();
        var segments = new List<Uri>();
        bool endList = false;
        bool isMaster = false;
        string? streamInfo = null;
        bool expectingSegment = false;
        foreach (string original in lines)
        {
            var line = original.Trim();
            if (line.Length == 0 || line == "#EXTM3U") continue;
            if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
            {
                if (!Regex.IsMatch(line, @"(?:^|[:,])METHOD=NONE(?:,|$)", RegexOptions.IgnoreCase))
                    throw new NotSupportedException("Encrypted HLS streams are not supported.");
            }
            if (line.StartsWith("#EXT-X-SESSION-KEY:", StringComparison.Ordinal) ||
                line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal) ||
                line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal) ||
                line.StartsWith("#EXT-X-DISCONTINUITY", StringComparison.Ordinal) ||
                line.StartsWith("#EXT-X-PART:", StringComparison.Ordinal) ||
                line.StartsWith("#EXT-X-I-FRAMES-ONLY", StringComparison.Ordinal))
                throw new NotSupportedException("This HLS layout requires a media pipeline not yet supported.");
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                isMaster = true;
                streamInfo = line;
                // External AUDIO groups require separate muxing to avoid silent downloads.
                if (Regex.IsMatch(line, @"(?:^|[:,])AUDIO\s*=", RegexOptions.IgnoreCase))
                    throw new NotSupportedException("Separate HLS audio tracks are not supported yet.");
                continue;
            }
            if (line == "#EXT-X-ENDLIST") { endList = true; continue; }
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                expectingSegment = true;
                continue;
            }
            if (line.StartsWith('#')) continue;
            if (streamInfo is not null)
            {
                var bandwidth = Regex.Match(streamInfo, @"(?:^|[:,])BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                var size = Regex.Match(streamInfo, @"(?:^|[:,])RESOLUTION=\d+x(\d+)", RegexOptions.IgnoreCase);
                int rate = bandwidth.Success && int.TryParse(bandwidth.Groups[1].Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ? b : 0;
                int? height = size.Success && int.TryParse(size.Groups[1].Value, out int h) ? h : null;
                variants.Add(new Variant(Link(playlistUrl, line), rate, height));
                streamInfo = null;
                if (variants.Count > 40) throw new InvalidDataException("Too many playlist variants.");
            }
            else if (expectingSegment)
            {
                Uri segment = Link(playlistUrl, line);
                // Concatenation produces valid MPEG transport stream only.
                if (!segment.AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Only MPEG-TS HLS media segments are supported in this preview.");
                segments.Add(segment);
                if (segments.Count > 20_000) throw new InvalidDataException("Too many HLS segments.");
                expectingSegment = false;
            }
            else
                throw new InvalidDataException("Unexpected non-segment playlist entry.");
        }
        if (streamInfo is not null || expectingSegment)
            throw new InvalidDataException("The playlist ends with an incomplete entry.");
        if (isMaster)
        {
            if (variants.Count == 0 || segments.Count > 0)
                throw new InvalidDataException("Invalid mixed HLS master/media playlist.");
            return new Playlist([], variants);
        }
        if (!endList || segments.Count == 0)
            throw new NotSupportedException("Only finished HLS video-on-demand playlists are supported.");
        return new Playlist(segments, []);
    }
}
