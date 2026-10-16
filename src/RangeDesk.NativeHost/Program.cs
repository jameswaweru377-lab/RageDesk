using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using RangeDesk.Core;

// Browser native-messaging host. Runs only when invoked by an explicitly
// permitted installed extension; no page monitoring or cookie access.
try
{
    using var input = Console.OpenStandardInput();
    Span<byte> length = stackalloc byte[4];
    input.ReadExactly(length);
    int bytes = BinaryPrimitives.ReadInt32LittleEndian(length);
    if (bytes is < 2 or > 8192)
        throw new InvalidDataException("Invalid browser request size.");
    var buffer = new byte[bytes];
    input.ReadExactly(buffer);
    string json = Encoding.UTF8.GetString(buffer);
    using var request = JsonDocument.Parse(json);
    string? action = request.RootElement.GetProperty("action").GetString();
    if (action == "ping")
    {
        // Connection test: no URL, permissions, files or user data required.
        WriteResponse(true, "RangeDesk native host is reachable from Chrome.");
    }
    else if (action == "media_probe")
    {
        string url = request.RootElement.GetProperty("url").GetString() ?? "";
        if (!OptionalMediaExtractor.IsPageCandidate(url))
            throw new ArgumentException("Unsupported media page URL.");
        string appFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var probe = await OptionalMediaExtractor.AnalyzeAsync(appFolder, url);
        var formats = probe.Formats.Take(60).Select(f => new
        {
            id = f.Id,
            label = f.Label.Length <= 160 ? f.Label : f.Label[..160],
            height = f.Height,
            needsAudioMerge = f.NeedsAudioMerge
        }).ToArray();
        WriteResponseObject(new { accepted = true, formats });
    }
    else if (action == "media_download")
    {
        if (System.Diagnostics.Process.GetProcessesByName("RangeDesk").Length == 0)
            throw new InvalidOperationException("Open RangeDesk before downloading media.");
        if (!request.RootElement.TryGetProperty("rightsConfirmed", out var consent) ||
            consent.ValueKind != JsonValueKind.True)
            throw new ArgumentException("Explicit download permission is required.");
        string url = request.RootElement.GetProperty("url").GetString() ?? "";
        string formatId = request.RootElement.GetProperty("formatId").GetString() ?? "";
        string jobId = Guid.NewGuid().ToString("N");
        var item = new BrowserMediaRequest("media_download", url, formatId, jobId);
        item.Validate();
        BrowserMediaJobState.Update(jobId, "queued", "Selection received. Waiting for RangeDesk.");
        try
        {
            BrowserMediaInbox.Enqueue(BrowserMediaInbox.DefaultDirectory,
                JsonSerializer.Serialize(item));
        }
        catch
        {
            BrowserMediaJobState.TryUpdate(jobId, "failed", "Media request could not be queued.");
            throw;
        }
        WriteResponseObject(new { accepted = true, jobId, state = "queued",
            message = "Selection received by RangeDesk." });
    }
    else if (action == "media_status")
    {
        string jobId = request.RootElement.GetProperty("jobId").GetString() ?? "";
        var snapshot = BrowserMediaJobState.Read(jobId);
        if (snapshot is null)
            WriteResponseObject(new { accepted = false, errorCode = "JOB_NOT_FOUND",
                message = "Media job status is not available." });
        else
            WriteResponseObject(new { accepted = true, jobId, state = snapshot.State,
                message = snapshot.Message, downloadedBytes = snapshot.DownloadedBytes,
                totalBytes = snapshot.TotalBytes, estimated = snapshot.Estimated });
    }
    else if (action == "capture_download")
    {
        // No hidden service and no silently launched GUI: if RangeDesk is not
        // already running, tell the extension to resume its browser download.
        if (System.Diagnostics.Process.GetProcessesByName("RangeDesk").Length == 0)
            throw new InvalidOperationException("The visible RangeDesk app is not running.");
        var job = BrowserAutomaticCapture.Queue(BrowserAutomaticCapture.DefaultDirectory, json);
        bool started = await BrowserAutomaticCapture.WaitForDecisionAsync(
            BrowserAutomaticCapture.DefaultDirectory, job);
        WriteResponse(started, started
            ? "RangeDesk accepted and started the transfer."
            : "RangeDesk did not start the transfer. Continue in the browser.");
    }
    else
    {
        BrowserLinkInbox.Enqueue(BrowserLinkInbox.DefaultDirectory, json);
        WriteResponse(true, "Sent to RangeDesk for confirmation. Open RangeDesk to review the link.");
    }
}
catch (MediaAnalyzerException ex)
{
    WriteResponseObject(new { accepted = false, errorCode = ex.Code, message = ex.Message });
}
catch (Exception)
{
    // Do not leak URLs, tokens or native-host protocol text into diagnostics.
    WriteResponseObject(new { accepted = false, errorCode = "NATIVE_REQUEST_FAILED",
        message = "RangeDesk could not complete this request. Check the application and MediaTools." });
}

static void WriteResponse(bool accepted, string message) =>
    WriteResponseObject(new { accepted, message });

static void WriteResponseObject<T>(T value)
{
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(value);
    using var output = Console.OpenStandardOutput();
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
    output.Write(length);
    output.Write(json);
    output.Flush();
}
