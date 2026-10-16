using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RangeDesk.Core;

/// <summary>
/// Real loopback TCP HTTP/1.1 range-capable server, not mocked HttpClient
/// messages. Each response is throttled independently to measure the
/// downloader's parallel range path with a known immutable body and ETag.
/// These numbers reflect loopback+CPU, NOT external ISP/CDN speeds.
/// </summary>
public static class ControlledLoopbackBenchmark
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        string folder = Path.Combine(Path.GetTempPath(),"RangeDesk-Controlled-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            byte[] original = new byte[8 * 1024 * 1024];
            for (int i=0; i<original.Length; i++) original[i] = (byte)(i * 131 + 17);
            string expected = Convert.ToHexString(SHA256.HashData(original));
            await using var server = new ThrottledRangeServer(original);
            var measures = new List<object>();
            foreach (var requested in new [] { 1, 4, 8 })
            {
                int requestsBefore = server.RangeRequests;
                string destination = Path.Combine(folder, "local-"+requested+".bin");
                using var engine = new DownloadEngine();
                var item = engine.Create(server.Url, destination, connections:requested);
                var watch=Stopwatch.StartNew();
                if (!engine.Start(item)) throw new InvalidOperationException("Could not start controlled transfer");
                while (item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled) &&
                    watch.Elapsed < TimeSpan.FromSeconds(45))
                    await Task.Delay(50);
                watch.Stop();
                if (item.Status != DownloadStatus.Completed)
                {
                    engine.Pause(item);
                    throw new InvalidDataException($"Controlled {requested}-connection test did not complete: {item.Status} {item.Error}");
                }
                long bytes = new FileInfo(destination).Length;
                string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(destination)));
                int ranges = server.RangeRequests - requestsBefore;
                if (bytes != original.Length || hash != expected)
                    throw new InvalidDataException("Controlled download output failed known checksum or length.");
                if (requested > 1 && ranges < requested)
                    throw new InvalidDataException("Controlled server did not observe the requested parallel range connections.");
                var measured = new {
                    requestedConnections = requested, observedRangeRequests = ranges,
                    item.WorkerCount, bytes, sha256 = hash,
                    seconds = Math.Round(watch.Elapsed.TotalSeconds,3),
                    throughputMiBs = Math.Round(bytes / 1048576.0 / watch.Elapsed.TotalSeconds,3)
                };
                measures.Add(measured);
                Console.WriteLine("CONTROLLED "+JsonSerializer.Serialize(measured));
            }
            var report=new {
                name="Rangedesk controlled HTTP localhost test",
                source="127.0.0.1 ephemeral real TCP server (32KiB bursts, 20ms delay per connection)",
                expectedSha256=expected,
                meaning="Deterministic verification of ranged transfers and throughput under imposed per-connection delay; not internet or device benchmark.",
                runs=measures
            };
            await File.WriteAllTextAsync(Path.Combine(output,"controlled-results.json"),
                JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            try {Directory.Delete(folder,true);} catch(IOException) {}
        }
    }

    private sealed class ThrottledRangeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _body;
        private readonly Task _accept;
        private int _rangeRequests;
        public int RangeRequests => Volatile.Read(ref _rangeRequests);
        public string Url {get;}
        public ThrottledRangeServer(byte[] body)
        {
            _body = body;
            _listener.Start();
            int port=((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/controlled-test.bin";
            _accept = Task.Run(AcceptAsync);
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client=await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Task.Run(async () => {
                        try {await HandleAsync(client,_stop.Token);}
                        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) {}
                        finally { client.Dispose(); }
                    });
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) {}
            catch (SocketException) when (_stop.IsCancellationRequested) {}
        }
        private async Task HandleAsync(TcpClient client, CancellationToken cancel)
        {
            await using var socket=client.GetStream();
            using var reader=new StreamReader(socket,Encoding.ASCII, false, 4096,leaveOpen:true);
            string? request=await reader.ReadLineAsync(cancel);
            if (request is null) return;
            bool head=request.StartsWith("HEAD ",StringComparison.Ordinal);
            if (!head && !request.StartsWith("GET ",StringComparison.Ordinal))
                return;
            long start=0,end=_body.Length-1;
            bool isRange=false;
            string? rangeValue=null;
            for (;;)
            {
                string? line=await reader.ReadLineAsync(cancel);
                if (line is null || line.Length==0) break;
                if (line.StartsWith("Range: ",StringComparison.OrdinalIgnoreCase))
                    rangeValue=line["Range: ".Length..].Trim();
            }
            if (rangeValue is not null && rangeValue.StartsWith("bytes=",StringComparison.OrdinalIgnoreCase))
            {
                var parts=rangeValue[6..].Split('-',2);
                if (parts.Length !=2 || !long.TryParse(parts[0],out start) ||
                    !long.TryParse(parts[1],out end) || start<0 || end<start || end>=_body.Length)
                    return;
                isRange = true;
                Interlocked.Increment(ref _rangeRequests);
            }
            int size=checked((int)(end-start+1));
            string headers=(isRange?"HTTP/1.1 206 Partial Content\r\n":"HTTP/1.1 200 OK\r\n") +
                "Content-Type: application/octet-stream\r\n" +
                "Accept-Ranges: bytes\r\n" +
                "ETag: \"rangedesk-controlled-v1\"\r\n" +
                (isRange?$"Content-Range: bytes {start}-{end}/{_body.Length}\r\n":"") +
                $"Content-Length: {size}\r\nConnection: close\r\n\r\n";
            await socket.WriteAsync(Encoding.ASCII.GetBytes(headers),cancel);
            if (head) return;
            for (int at=(int)start; at<=end; )
            {
                int n=(int)Math.Min(32768,end-at+1);
                await socket.WriteAsync(_body.AsMemory(at,n),cancel);
                at+=n;
                if(at<=end) await Task.Delay(20,cancel);
            }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try {await _accept;} catch (ObjectDisposedException){}
            _stop.Dispose();
        }
    }
}
