using System.Security.Cryptography;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class Sha256VerificationTests
{
    [Fact]
    public async Task ValidDigestIsAcceptedAndMismatchIsRejectedWithoutDeletingData()
    {
        string path = Path.Combine(Path.GetTempPath(), "RangeDeskHash-" + Guid.NewGuid().ToString("N"));
        byte[] bytes = Enumerable.Range(0, 90_000).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            string valid = Convert.ToHexString(SHA256.HashData(bytes));
            await Sha256Verification.VerifyAsync(path, valid.ToLowerInvariant(), CancellationToken.None);
            await Sha256Verification.VerifyAsync(path, null, CancellationToken.None);
            string wrong = new string('A', 64);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                Sha256Verification.VerifyAsync(path, wrong, CancellationToken.None));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsMalformedExpectedDigests()
    {
        Assert.Throws<ArgumentException>(() => new DownloadEntry("https://example.test/file",
            @"C:\Downloads\file", expectedSha256: "BAD"));
        Assert.Throws<ArgumentException>(() => new DownloadEntry("https://example.test/file",
            @"C:\Downloads\file", expectedSha256: new string('x', 64)));
        var valid = new DownloadEntry("https://example.test/file", @"C:\Downloads\file",
            expectedSha256: new string('a', 64));
        Assert.Equal(new string('A', 64), valid.ExpectedSha256);
    }
}
