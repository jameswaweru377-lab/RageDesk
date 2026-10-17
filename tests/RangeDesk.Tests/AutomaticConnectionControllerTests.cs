using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class AutomaticConnectionControllerTests
{
    private const long MiB = 1024 * 1024;

    [Fact]
    public void RequiresTwoStablePostExpansionWindowsToGrowAgain()
    {
        var ctrl = new AutomaticConnectionController(16);
        Assert.Equal(2, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(2), MiB / 2, 128 * MiB));
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(3), MiB, 128 * MiB));
        Assert.Equal(4, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(6), 3 * MiB, 128 * MiB));
        Assert.Contains("1/2", ctrl.Decision);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(9), 5 * MiB, 128 * MiB));
        Assert.Equal(4, ctrl.TargetWorkers);
        Assert.Contains("faster", ctrl.Decision, StringComparison.OrdinalIgnoreCase);
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(12), 7 * MiB, 128 * MiB));
        Assert.Equal(8, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(15), 11 * MiB, 128 * MiB));
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(18), 15 * MiB, 128 * MiB));
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(21), 19 * MiB, 128 * MiB));
        Assert.Equal(16, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(24), 27 * MiB, 128 * MiB));
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(27), 35 * MiB, 128 * MiB));
        Assert.True(ctrl.StoppedExpanding || ctrl.TargetWorkers == 16);
    }

    [Fact]
    public void PlateauRetiresSurplusAndReprobesLaterNotImmediately()
    {
        var ctrl = new AutomaticConnectionController(16);
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(3), MiB, 128 * MiB)); // 2 -> 4
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(6), 2 * MiB, 128 * MiB));
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(9), 3 * MiB, 128 * MiB)); // 4 -> 2
        Assert.Equal(2, ctrl.TargetWorkers);
        Assert.False(ctrl.StoppedExpanding);
        Assert.Contains("later", ctrl.Decision, StringComparison.OrdinalIgnoreCase);

        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(12), 4 * MiB, 128 * MiB));
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(21), 7 * MiB, 128 * MiB));
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(24), 8 * MiB, 128 * MiB)); // delayed 2 -> 4
        Assert.Equal(4, ctrl.TargetWorkers);
        Assert.Contains("Re-testing", ctrl.Decision);

        // Improved conditions at the new worker target.
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(27), 10 * MiB, 128 * MiB));
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(30), 12 * MiB, 128 * MiB));
        Assert.False(ctrl.StoppedExpanding);
        Assert.Equal(4, ctrl.TargetWorkers);
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(33), 14 * MiB, 128 * MiB));
        Assert.Equal(8, ctrl.TargetWorkers);
    }

    [Fact]
    public void GlobalBandwidthPlateauStopsAfterTwoReprobes()
    {
        var ctrl = new AutomaticConnectionController(16);
        long amount = 0;
        void Sample(int seconds)
        {
            amount += MiB;
            ctrl.Observe(TimeSpan.FromSeconds(seconds), amount, 128 * MiB);
        }
        Sample(3); // grow to 4
        Sample(6);
        Sample(9); // rollback to 2 and wait 15 seconds
        Assert.Equal(2, ctrl.TargetWorkers);
        Sample(12);
        Sample(15);
        Sample(18);
        Sample(21);
        Sample(24); // re-test 4 (attempt 1)
        Assert.Equal(4, ctrl.TargetWorkers);
        Sample(27);
        Sample(30); // rollback 2
        Assert.Equal(2, ctrl.TargetWorkers);
        Sample(33);
        Sample(36);
        Sample(39);
        Sample(42);
        Sample(45); // re-test 4 (attempt 2)
        Assert.Equal(4, ctrl.TargetWorkers);
        Sample(48);
        Sample(51); // rollback permanently
        Assert.True(ctrl.StoppedExpanding);
        Assert.Equal(2, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(100), 100 * MiB, 128 * MiB));
    }

    [Fact]
    public void RespectsOddSegmentCeilingAndUsefulRemainingBytes()
    {
        var odd = new AutomaticConnectionController(5);
        Assert.True(odd.Observe(TimeSpan.FromSeconds(3), MiB, 128 * MiB));
        Assert.False(odd.Observe(TimeSpan.FromSeconds(6), 3 * MiB, 128 * MiB));
        Assert.False(odd.Observe(TimeSpan.FromSeconds(9), 5 * MiB, 128 * MiB));
        Assert.True(odd.Observe(TimeSpan.FromSeconds(12), 7 * MiB, 128 * MiB));
        Assert.Equal(5, odd.TargetWorkers);

        var small = new AutomaticConnectionController(32);
        Assert.False(small.Observe(TimeSpan.FromSeconds(3), MiB, 2 * MiB));
        Assert.True(small.StoppedExpanding);
        Assert.Equal(2, small.TargetWorkers);
    }

    [Fact]
    public void InconsistentOrStalledProgressCannotCauseAggressiveExpansion()
    {
        var ctrl = new AutomaticConnectionController(32);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(3), 0, 128 * MiB));
        Assert.Equal(2, ctrl.TargetWorkers);
        Assert.True(ctrl.Observe(TimeSpan.FromSeconds(6), MiB, 128 * MiB)); // First useful window
        Assert.Equal(4, ctrl.TargetWorkers);
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(9), 2 * MiB, 128 * MiB));
        Assert.False(ctrl.Observe(TimeSpan.FromSeconds(12), MiB, 128 * MiB));
        Assert.True(ctrl.StoppedExpanding);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(64)]
    public void InvalidCeilingIsRejected(int max) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomaticConnectionController(max));

    [Fact]
    public void OnlyExplicitOptInUsesV71AndDisplaysDecisionSafely()
    {
        using var engine = new DownloadEngine();
        var legacy = engine.Create("https://example.org/file",
            Path.Combine(Path.GetTempPath(), "rd-v71-fixed.bin"), connections: 8);
        Assert.False(legacy.AutomaticConnections);
        Assert.False(legacy.Adaptive);
        var automatic = engine.Create("https://example.org/file",
            Path.Combine(Path.GetTempPath(), "rd-v71-auto.bin"),
            connections: 16, automaticConnections: true);
        Assert.True(automatic.AutomaticConnections);
        Assert.True(automatic.Adaptive);
        automatic.ReportOptimization("Testing 4 / 16 connections");
        Assert.Equal("Testing 4 / 16 connections", automatic.ConnectionOptimizationStatus);
        Assert.Contains("V7.1 auto", automatic.ConnectionSummary);
        Assert.Throws<ArgumentException>(() => engine.Create(
            "https://example.org/file",
            Path.Combine(Path.GetTempPath(), "rd-v71-bad.bin"),
            connections: 1, automaticConnections: true));
    }
}
