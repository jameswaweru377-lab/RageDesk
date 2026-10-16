using System.Security.Cryptography;

namespace RangeDesk.Core;

public static class Sha256Verification
{
    /// <summary>Never mark an unverified download completed. Existing partials
    /// are retained on mismatch for investigation and safe re-download.</summary>
    public static async Task VerifyAsync(string path, string? expected, CancellationToken token)
    {
        if (expected is null) return;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 checksum does not match. File was not published as complete.");
    }
}
