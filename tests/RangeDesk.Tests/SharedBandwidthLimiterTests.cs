using System.Diagnostics;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class SharedBandwidthLimiterTests
{
    [Fact]
    public async Task ConcurrentWorkersShareOneBudgetInsteadOfMultiplyingIt()
    {
        var limiter = new SharedBandwidthLimiter(64);
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => limiter.AccountAsync(16 * 1024, CancellationToken.None).AsTask()));
        clock.Stop();
        Assert.Equal(64 * 1024, limiter.ChargedBytes);
        // 64 KiB total charged at 64 KiB/s: approximately one second,
        // not one quarter second as four independent budgets would permit.
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(800),
            $"Shared rate budget not enforced: {clock.Elapsed}");
    }

    [Fact]
    public async Task UnlimitedBudgetDoesNotThrottle()
    {
        var limiter = new SharedBandwidthLimiter(0);
        await limiter.AccountAsync(1024 * 1024, CancellationToken.None);
        Assert.Equal(0, limiter.ChargedBytes);
    }

    [Fact]
    public async Task CancellationInterruptsThrottleWithoutDeadlock()
    {
        var limiter = new SharedBandwidthLimiter(128);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.AccountAsync(1024 * 1024, stop.Token).AsTask());
    }

    [Fact]
    public async Task LiveCalendarCanThrottleAndRestoreUnlimitedRate()
    {
        var limiter = new SharedBandwidthLimiter(0);
        Assert.Equal(0, limiter.LimitKiBps);
        await limiter.SetLimitAsync(256, CancellationToken.None);
        Assert.Equal(256, limiter.LimitKiBps);
        var timer=Stopwatch.StartNew();
        await limiter.AccountAsync(64 * 1024, CancellationToken.None);
        Assert.True(timer.ElapsedMilliseconds >= 150);
        await limiter.SetLimitAsync(0, CancellationToken.None);
        Assert.Equal(0, limiter.LimitKiBps);
        Assert.Equal(0, limiter.ChargedBytes);
        await limiter.AccountAsync(1024 * 1024, CancellationToken.None);
        Assert.Equal(0, limiter.ChargedBytes);
    }

    [Fact]
    public async Task InvalidLiveCapDoesNotChangeTheCurrentLimit()
    {
        var limiter=new SharedBandwidthLimiter(128);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await limiter.SetLimitAsync(-2, CancellationToken.None));
        Assert.Equal(128,limiter.LimitKiBps);
    }

    [Fact]
    public void NegativeBudgetIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SharedBandwidthLimiter(-1));
}
