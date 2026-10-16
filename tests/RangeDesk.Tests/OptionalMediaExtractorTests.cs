using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class OptionalMediaExtractorTests
{
    private const string Example = "https://example.org/watch?v=safe";
    private const string Probe = """
    {
      "id": "sample-01", "title": "Test video",
      "formats": [
        {"format_id":"hls-360", "vcodec":"avc1.42", "acodec":"none", "height":360,"ext":"mp4"},
        {"format_id":"720p", "vcodec":"avc1.64", "acodec":"mp4a.40.2", "height":720,"ext":"mp4"},
        {"format_id":"hd1080", "vcodec":"vp09", "acodec":"none", "height":1080,"ext":"webm"},
        {"format_id":"audio", "vcodec":"none", "acodec":"opus","ext":"webm"},
        {"format_id":"protected", "vcodec":"avc1", "acodec":"none","has_drm":true, "height":4320},
        {"format_id":"../../bad", "vcodec":"avc1", "acodec":"none", "height":2160}
      ]
    }
    """;

    [Fact]
    public void FormatsReflectActualExtractedQualitiesAndRejectDrm()
    {
        var info = OptionalMediaExtractor.ParseProbe(Probe);
        Assert.Equal("Test video", info.Title);
        Assert.Equal("sample-01", info.VideoId);
        Assert.Equal(3, info.Formats.Count);
        Assert.Equal([1080, 720, 360], info.Formats.Select(f => f.Height).ToArray());
        Assert.True(info.Formats[0].NeedsAudioMerge);
        Assert.False(info.Formats[1].NeedsAudioMerge);
        Assert.DoesNotContain(info.Formats, f => f.Id == "protected");
    }

    [Fact]
    public void ProtectedOnlyMediaHasNoDownloadableFormats()
    {
        const string source = """
        {"id":"drm-1","formats":[{"format_id":"1","has_drm":true,"vcodec":"avc1","acodec":"none"}]}
        """;
        Assert.Throws<NotSupportedException>(() => OptionalMediaExtractor.ParseProbe(source));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@site.test/watch")]
    [InlineData("javascript:alert(1)")]
    public void UnsafeUrlNotPassedToExternalTools(string url)
    {
        Assert.Throws<ArgumentException>(() => OptionalMediaExtractor.ProbeArguments(url));
    }

    [Fact]
    public void UserSelectedFormatAndAudioMergeAreExplicitInCliArguments()
    {
        var selected = OptionalMediaExtractor.ParseProbe(Probe).Formats[0];
        var outDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RangeDeskTests"));
        var ffmpeg = Path.Combine(outDir, "ffmpeg.exe");
        var argv = OptionalMediaExtractor.DownloadArguments(Example, selected, outDir, ffmpeg);
        Assert.Equal("hd1080+ba/hd1080", argv[argv.ToList().IndexOf("--format") + 1]);
        Assert.Equal("mkv", argv[argv.ToList().IndexOf("--merge-output-format") + 1]);
        Assert.Contains("--no-playlist", argv);
        Assert.Contains("--no-overwrites", argv);
        Assert.Contains("--ignore-config", argv);
        Assert.Contains("--no-plugin-dirs", argv);
        Assert.Contains("--no-remote-components", argv);
        Assert.Equal(Example, argv[^1]);
        Assert.Equal("--", argv[^2]);
    }

    [Fact]
    public void PageAndDirectDownloadsUseDifferentEntryPoints()
    {
        Assert.True(OptionalMediaExtractor.IsPageCandidate("https://www.youtube.com/watch?v=abc"));
        Assert.True(OptionalMediaExtractor.IsPageCandidate("https://example.org/videos/trailer"));
        Assert.False(OptionalMediaExtractor.IsPageCandidate("https://example.org/movie.mp4"));
        Assert.False(OptionalMediaExtractor.IsPageCandidate("https://example.org/live.m3u8"));
        Assert.False(OptionalMediaExtractor.IsPageCandidate("https://example.org/movie.webm"));
    }
}
