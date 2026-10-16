using System.Net;
using System.Net.Http;
using System.Text;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class HlsVodTests
{
    private const string Base = "https://media.example.test/";
    private static byte[] Ts(byte marker)
    {
        var bytes = new byte[188 * 2];
        for (int i = 0; i < bytes.Length; i += 188)
        {
            bytes[i] = 0x47;
            for (int j = 1; j < 188; j++) bytes[i + j] = marker;
        }
        return bytes;
    }

    private const string Media =
        "#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:4.0,\npart1.ts\n#EXTINF:4.0,\npart2.ts\n#EXT-X-ENDLIST\n";

    [Fact]
    public void HlsManifestDiscoversActualQualitiesAndSegments()
    {
        var master = HlsPlaylist.Parse(
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=640x360\nsmall.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720\nhd.m3u8\n",
            new Uri(Base + "master.m3u8"));
        Assert.True(master.IsMaster);
        Assert.Equal(2, master.Variants.Count);
        Assert.Equal(360, master.Variants[0].Height);
        Assert.Equal(720, master.Variants[1].Height);
        Assert.Equal(2_000_000, master.Variants[1].Bandwidth);
        Assert.Equal(Base + "hd.m3u8", master.Variants[1].PlaylistUrl.AbsoluteUri);
        var media = HlsPlaylist.Parse(Media, new Uri(Base + "hd.m3u8"));
        Assert.False(media.IsMaster);
        Assert.Equal(2, media.Segments.Count);
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"secret.key\"\n#EXTINF:4,\na.ts\n#EXT-X-ENDLIST")]
    [InlineData("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\na.m4s\n#EXT-X-ENDLIST")]
    [InlineData("#EXTM3U\n#EXT-X-BYTERANGE:188@0\n#EXTINF:4,\na.ts\n#EXT-X-ENDLIST")]
    [InlineData("#EXTM3U\n#EXTINF:4,\na.ts\n")]
    public void UnsupportedOrLivePlaylistsFailClosed(string content)
    {
        Assert.ThrowsAny<Exception>(() => HlsPlaylist.Parse(content, new Uri(Base + "stream.m3u8")));
    }

    [Fact]
    public void HlsNeverAcceptsFileOrCredentialsInSegmentReferences()
    {
        Assert.Throws<InvalidDataException>(() => HlsPlaylist.Parse(
            "#EXTM3U\n#EXTINF:4,\nfile:///C:/secret.ts\n#EXT-X-ENDLIST",
            new Uri(Base + "stream.m3u8")));
        Assert.Throws<InvalidDataException>(() => HlsPlaylist.Parse(
            "#EXTM3U\n#EXTINF:4,\nhttps://user:pass@media.example.test/a.ts\n#EXT-X-ENDLIST",
            new Uri(Base + "stream.m3u8")));
    }

    [Fact]
    public async Task MasterSelectsHighestVariantAndAssemblesExactMpegTsBytes()
    {
        await InFolder(async folder =>
        {
            var a = Ts(12); var b = Ts(21);
            var requested = new List<string>();
            using var http = new HttpClient(new ReplyHandler(uri =>
            {
                lock (requested) requested.Add(uri.AbsolutePath);
                return uri.AbsolutePath switch
                {
                    "/master.m3u8" => Text("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=150000,RESOLUTION=640x360\nlo.m3u8\n" +
                        "#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720\nhi.m3u8\n"),
                    "/hi.m3u8" => Text(Media),
                    "/part1.ts" => Bytes(a),
                    "/part2.ts" => Bytes(b),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }));
            using var engine = new DownloadEngine(client: http);
            var entry = engine.Create(Base + "master.m3u8", Path.Combine(folder, "movie.ts"));
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Completed, entry.Status);
            Assert.Equal(a.Concat(b).ToArray(), await File.ReadAllBytesAsync(entry.Destination));
            Assert.Contains("/hi.m3u8", requested);
            Assert.DoesNotContain("/lo.m3u8", requested);
            Assert.False(File.Exists(entry.Destination + ".rdhls-state"));
            Assert.False(File.Exists(entry.Destination + ".rdhls-partial"));
        });
    }

    [Fact]
    public async Task FailedSecondSegmentResumesWholeFirstSegmentWithoutReDownloading()
    {
        await InFolder(async folder =>
        {
            var a = Ts(4); var b = Ts(9);
            bool failSecond = true;
            int firstRequests = 0;
            using var http = new HttpClient(new ReplyHandler(uri => uri.AbsolutePath switch
            {
                "/movie.m3u8" => Text(Media),
                "/part1.ts" => First(),
                "/part2.ts" => failSecond
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Bytes(b),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            }));
            HttpResponseMessage First()
            {
                Interlocked.Increment(ref firstRequests);
                return Bytes(a);
            }

            using var engine = new DownloadEngine(client: http);
            var entry = engine.Create(Base + "movie.m3u8", Path.Combine(folder, "resume.ts"));
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.False(File.Exists(entry.Destination));
            Assert.Equal(1, firstRequests);
            Assert.Equal(a, await File.ReadAllBytesAsync(entry.Destination + ".rdhls-partial"));
            failSecond = false;
            var resumed = engine.Create(Base + "movie.m3u8", entry.Destination);
            Assert.True(engine.Start(resumed));
            await Wait(resumed);
            Assert.Equal(DownloadStatus.Completed, resumed.Status);
            Assert.Equal(1, firstRequests);
            Assert.Equal(a.Concat(b).ToArray(), await File.ReadAllBytesAsync(entry.Destination));
        });
    }

    [Fact]
    public async Task ChangedPlaylistRejectsResumeAndPreservesExistingSegments()
    {
        await InFolder(async folder =>
        {
            var a = Ts(8);
            string playlist = Media;
            using var http = new HttpClient(new ReplyHandler(uri => uri.AbsolutePath switch
            {
                "/movie.m3u8" => Text(playlist),
                "/part1.ts" => Bytes(a),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            }));
            using var engine = new DownloadEngine(client: http);
            var entry = engine.Create(Base + "movie.m3u8", Path.Combine(folder, "movie.ts"));
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            playlist = Media.Replace("part2.ts", "different.ts");
            Assert.True(engine.Start(entry));
            await Wait(entry);
            Assert.Equal(DownloadStatus.Failed, entry.Status);
            Assert.Equal(a, await File.ReadAllBytesAsync(entry.Destination + ".rdhls-partial"));
            Assert.False(File.Exists(entry.Destination));
        });
    }

    [Fact]
    public void BatchPlanUsesTransportStreamFilenameAndAvoidsSavedCheckpoints()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rd-hls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = BatchDownloadPlanner.Plan(Base + "movie.m3u8", folder, [])[0];
            Assert.EndsWith("movie.ts", first.Destination);
            File.WriteAllText(first.Destination + ".rdhls-state", "{}");
            var next = BatchDownloadPlanner.Plan(Base + "movie.m3u8", folder, [])[0];
            Assert.EndsWith("movie (2).ts", next.Destination);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static HttpResponseMessage Text(string source) =>
        new(HttpStatusCode.OK) { Content = new StringContent(source, Encoding.UTF8) };
    private static HttpResponseMessage Bytes(byte[] source) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(source) };

    private sealed class ReplyHandler(Func<Uri, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(reply(request.RequestUri!));
    }

    private static async Task Wait(DownloadEntry item)
    {
        for (int i = 0; i < 300; i++)
        {
            if (item.Status is DownloadStatus.Completed or DownloadStatus.Failed) return;
            await Task.Delay(30);
        }
        throw new TimeoutException("HLS transfer did not finish in the allotted test window.");
    }

    private static async Task InFolder(Func<string, Task> act)
    {
        string folder = Path.Combine(Path.GetTempPath(), "rd-hls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { await act(folder); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
