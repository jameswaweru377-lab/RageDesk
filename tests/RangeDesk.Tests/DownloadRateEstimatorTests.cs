using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class DownloadRateEstimatorTests
{
    [Fact]
    public void ShortBurstDoesNotCauseViolentUiRateDrop()
    {
        var rate = new DownloadRateEstimator();
        Assert.Equal(0, rate.Update(TimeSpan.Zero, 0));
        const long perHalfSecond = 500_000;
        for (int i = 1; i <= 8; i++)
        {
            var value = rate.Update(TimeSpan.FromMilliseconds(i * 500), i * perHalfSecond);
            Assert.InRange(value, 999_999, 1_000_001);
        }

        double afterOneQuietHalfSecond = rate.Update(TimeSpan.FromMilliseconds(4500), 4_000_000);
        Assert.InRange(afterOneQuietHalfSecond, 850_000, 950_000);
        double afterOneMoreQuietHalfSecond = rate.Update(TimeSpan.FromMilliseconds(5000), 4_000_000);
        Assert.InRange(afterOneMoreQuietHalfSecond, 750_000, 850_000);
    }

    [Fact]
    public void EightSecondStallDecaysToZeroInsteadOfFreezingFastSpeed()
    {
        var rate = new DownloadRateEstimator();
        rate.Update(TimeSpan.Zero, 0);
        for (int i = 1; i <= 8; i++)
            rate.Update(TimeSpan.FromMilliseconds(i * 500), i * 500_000);

        double last = double.PositiveInfinity;
        for (int i = 9; i <= 26; i++)
        {
            double measured = rate.Update(TimeSpan.FromMilliseconds(i * 500), 4_000_000);
            Assert.True(measured <= last, "A stall should not make the reported rate rise.");
            last = measured;
        }
        Assert.Equal(0, last);
    }

    [Fact]
    public void HandlesIrregularSampleIntervalsAndRestart()
    {
        var rate = new DownloadRateEstimator();
        rate.Update(TimeSpan.Zero, 0);
        Assert.Equal(0, rate.Update(TimeSpan.FromMilliseconds(300), 10_000));
        double first = rate.Update(TimeSpan.FromMilliseconds(750), 75_000);
        Assert.InRange(first, 99_999, 100_001);
        double second = rate.Update(TimeSpan.FromMilliseconds(1650), 165_000);
        Assert.InRange(second, 99_999, 100_001);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => rate.Update(TimeSpan.FromMilliseconds(1600), 100_000));
        rate.Reset();
        Assert.Equal(0, rate.Update(TimeSpan.Zero, 0));
    }
}
