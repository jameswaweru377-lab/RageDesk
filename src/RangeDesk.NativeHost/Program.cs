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
    if (action == "capture_download")
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
catch (Exception)
{
    // Do not leak URLs, tokens or native-host protocol text into diagnostics.
    WriteResponse(false, "RangeDesk could not accept this link. Check that the application is installed.");
}

static void WriteResponse(bool accepted, string message)
{
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { accepted, message });
    using var output = Console.OpenStandardOutput();
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
    output.Write(length);
    output.Write(json);
    output.Flush();
}
