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
    BrowserLinkInbox.Enqueue(BrowserLinkInbox.DefaultDirectory, Encoding.UTF8.GetString(buffer));
    WriteResponse(true, "Sent to RangeDesk for confirmation. Open RangeDesk to review the link.");
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
