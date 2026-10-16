namespace RangeDesk.Core;

/// <summary>
/// Ensures a range whose final HTTP read timed out is not mistaken for a
/// completed piece solely because it has the expected number of bytes.
/// A later retry re-downloads the missing last byte under the same validator.
/// </summary>
internal static class UnconfirmedRangeTail
{
    public static void RewindOneByte(string path, long expected)
    {
        if (expected < 1)
            throw new InvalidDataException("Cannot revalidate an empty range.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        if (file.Length != expected)
            throw new InvalidDataException("Range checkpoint changed during EOF recovery.");
        file.SetLength(expected - 1);
        file.Flush(flushToDisk: true);
    }
}
