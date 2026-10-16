using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RangeDesk.Core;

// Opt-in CI-only real HTTPS measurements, not an automated performance claim.
// Cloudflare's speed test API explicitly provides short download test bodies.
// All files and results live in the temporary GitHub Actions runner.
const int bytes = 8 * 1024 * 1024;
const string endpoint = "https://speed.cloudflare.com/__down?bytes=8388608";
string folder = Path.Combine(Path.GetTempPath(), "rangedesk-bench-" + Guid.NewGuid().ToString("N"));
string output = args.Length > 0 ? Path.GetFullPath(args[0]) :
    Path.GetFullPath(Path.Combine("artifact", "benchmark"));
Directory.CreateDirectory(output);
Directory.CreateDirectory(folder);
var outcomes = new List<object>();
var hashes = new List<string>();
try
{
    foreach (int requested in new[] { 1, 4, 8 })
    {
        string dest = Path.Combine(folder, $"cloudflare-{requested}.bin");
        using var engine = new DownloadEngine();
        var item = engine.Create(endpoint, dest, connections: requested);
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var state = "Timeout";
        string? sha = null;
        long actual = 0;
        try
        {
            if (!engine.Start(item)) throw new InvalidOperationException("Could not start transfer");
            while (watch.Elapsed < TimeSpan.FromSeconds(75))
            {
                if (item.Status is DownloadStatus.Failed or DownloadStatus.Completed or DownloadStatus.Canceled)
                    break;
                await Task.Delay(100);
            }
            if (item.Status == DownloadStatus.Completed && File.Exists(dest))
            {
                actual = new FileInfo(dest).Length;
                sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dest)));
                state = actual == bytes ? "Completed" : "UnexpectedLength";
                if (actual == bytes) hashes.Add(sha);
            }
            else
            {
                state = item.Status == DownloadStatus.Failed ? "Failed: " + (item.Error ?? "unknown") :
                    "Unavailable: timed out or server refused";
            }
        }
        catch (Exception ex) { state = "Unavailable: " + ex.GetType().Name; }
        finally { if (item.Status != DownloadStatus.Completed) engine.Pause(item); }
        watch.Stop();
        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.001);
        var result = new
        {
            requestedConnections = requested,
            observedWorkerCount = item.WorkerCount,
            source = "Cloudflare public download-test API",
            bytes = actual,
            status = state,
            seconds = Math.Round(seconds, 3),
            throughputMiBs = Math.Round(actual / 1048576d / seconds, 3),
            cpuSeconds = Math.Round((Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds, 3),
            sha256 = sha
        };
        outcomes.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
finally
{
    // Temp data must never be left in the runner or uploaded with the results.
    try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
}
var summary = new
{
    measuredUtc = DateTimeOffset.UtcNow,
    endpointHost = "speed.cloudflare.com",
    requestedMiBPerCase = 8,
    limitation = "Numbers reflect a shared ephemeral Windows GitHub runner and remote CDN load, not user's laptop. Range fallback is allowed, inspect observedWorkerCount.",
    allCasesCompleted = outcomes.Count == 3 && hashes.Count == 3,
    responseHashStableAcrossRuns = hashes.Count == 3 && hashes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1,
    runs = outcomes
};
await File.WriteAllTextAsync(Path.Combine(output, "results.json"),
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("RESULTS " + Path.Combine(output,"results.json"));
