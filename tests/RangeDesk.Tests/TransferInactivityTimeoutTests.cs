using System.Net;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class TransferInactivityTimeoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(600001)]
    public void InvalidTimeoutsRejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransferInactivityTimeout.Validate(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task ReadTimeoutInterruptsStalledStream()
    {
        using var stream = new BlockingStream();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await TransferInactivityTimeout.ReadAsync(stream, new byte[8], CancellationToken.None,
                TimeSpan.FromMilliseconds(60)));
    }

    [Fact]
    public async Task UserCancellationIsNotMisreportedAsTimeout()
    {
        using var stream = new BlockingStream();
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(40));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await TransferInactivityTimeout.ReadAsync(stream, new byte[8], cancel.Token,
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task HeaderWaitTimesOutButRealCancellationIsPreserved()
    {
        using var client = new HttpClient(new BlockingHandler());
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://example.test/file");
        await Assert.ThrowsAsync<TimeoutException>(() =>
            TransferInactivityTimeout.SendAsync(client, req, CancellationToken.None,
                TimeSpan.FromMilliseconds(60)));
    }

    [Fact]
    public async Task BufferedDataReadIsUnaffected()
    {
        await using var data = new MemoryStream([12,13,14]);
        byte[] buffer = new byte[4];
        int n = await TransferInactivityTimeout.ReadAsync(data, buffer, CancellationToken.None,
            TimeSpan.FromMilliseconds(60));
        Assert.Equal(3, n);
        Assert.Equal([12,13,14], buffer[..n]);
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
