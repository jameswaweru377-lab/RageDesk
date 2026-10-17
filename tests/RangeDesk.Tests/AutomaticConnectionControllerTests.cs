using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class AutomaticConnectionControllerTests
{
    private const long MiB = 1024 * 1024;

    [Fact]
    public void GraduallyRampsOnlyWhenTotalMeasuredThroughputImproves()
    {
        var controller = new AutomaticConnectionController(32);
        Assert.Equal(2, controller.TargetWorkers);
        Assert.False(controller.Observe(TimeSpan.FromSeconds(1), MiB / 2, 64 * MiB));
        Assert.True(controller.Observe(TimeSpan.FromSeconds(1.5), MiB, 64 * MiB));
        Assert.Equal(4, controller.TargetWorkers);
        Assert.True(controller.Observe(TimeSpan.FromSeconds(3), 3 * MiB, 64 * MiB));
        Assert.Equal(8, controller.TargetWorkers);
        Assert.True(controller.Observe(TimeSpan.FromSeconds(4.5), 7 * MiB, 64 * MiB));
        Assert.Equal(16, controller.TargetWorkers);
        Assert.True(controller.Observe(TimeSpan.FromSeconds(6), 15 * MiB, 64 * MiB));
        Assert.Equal(32, controller.TargetWorkers);
        Assert.False(controller.Observe(TimeSpan.FromSeconds(8), 23 * MiB, 64 * MiB));
    }

    [Fact]
    public void SharedBandwidthPlateauStopsAddingConnections()
    {
        var controller = new AutomaticConnectionController(32);
        Assert.True(controller.Observe(TimeSpan.FromSeconds(1.5), MiB, 64 * MiB));
        Assert.False(controller.Observe(TimeSpan.FromSeconds(3), 2 * MiB, 64 * MiB));
        Assert.True(controller.StoppedExpanding);
        Assert.Equal(4, controller.TargetWorkers);
        Assert.False(controller.Observe(TimeSpan.FromSeconds(10), 10 * MiB, 64 * MiB));
    }

    [Fact]
    public void NeverExceedsUserMaximumOrUsefulRemainingBytes()
    {
        var capped = new AutomaticConnectionController(4);
        Assert.True(capped.Observe(TimeSpan.FromSeconds(1.5), MiB, 64 * MiB));
        Assert.False(capped.Observe(TimeSpan.FromSeconds(3), 3 * MiB, 64 * MiB));
        Assert.Equal(4, capped.TargetWorkers);

        var small = new AutomaticConnectionController(32);
        Assert.False(small.Observe(TimeSpan.FromSeconds(1.5), MiB, 2 * MiB));
        Assert.Equal(2, small.TargetWorkers);
    }

    [Fact]
    public void SuspiciousMeasurementsCannotCauseAggressiveExpansion()
    {
        var controller = new AutomaticConnectionController(32);
        Assert.False(controller.Observe(TimeSpan.FromSeconds(1.5), 100, 1000));
        Assert.False(controller.Observe(TimeSpan.FromSeconds(3), 50, 1000));
        Assert.True(controller.StoppedExpanding);
        Assert.Equal(2, controller.TargetWorkers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(64)]
    public void InvalidConnectionCeilingsAreRejected(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutomaticConnectionController(limit));
    }

    [Fact]
    public void OptInIsExplicitAndActivatesValidatedAdaptiveEngine()
    {
        using var engine = new DownloadEngine();
        var legacy = engine.Create("https://example.org/file.bin",
            Path.Combine(Path.GetTempPath(), "auto-legacy.bin"), connections: 8);
        Assert.False(legacy.AutomaticConnections);
        Assert.False(legacy.Adaptive);

        var automatic = engine.Create("https://example.org/file.bin",
            Path.Combine(Path.GetTempPath(), "auto-enabled.bin"),
            connections: 16, automaticConnections: true);
        Assert.True(automatic.AutomaticConnections);
        Assert.True(automatic.Adaptive);
        Assert.Equal(16, automatic.RequestedConnections);
        Assert.Throws<ArgumentException>(() => engine.Create(
            "https://example.org/file.bin",
            Path.Combine(Path.GetTempPath(), "auto-invalid.bin"),
            connections: 1, automaticConnections: true));
    }
}
