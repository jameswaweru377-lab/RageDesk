namespace RangeDesk.Core;

/// <summary>
/// V7.1: conservative, local throughput controller. A "connection" is one
/// concurrent validated HTTP range worker, not an independent internet link.
/// Assess 3-second measurements, confirm each increase across two subsequent
/// windows and, on a plateau, restore the previous target without cancelling
/// already-running range requests. At most two delayed re-tests per plateau.
/// </summary>
public sealed class AutomaticConnectionController
{
    public const int StartingWorkers = 2;
    // A 32 MiB transfer may finish before two long measurement windows.
    // Use responsive warm-up on smaller files; reserve long stable windows
    // for downloads where a plateau can cost hours of unnecessary waiting.
    public static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ShortTransferSampleWindow = TimeSpan.FromMilliseconds(1500);
    private const long LongTransferThreshold = 256L * 1024 * 1024;
    public static readonly TimeSpan ReprobeDelay = TimeSpan.FromSeconds(15);
    private const long MinimumWindowBytes = 64 * 1024;
    private const long MinimumUsefulRange = 512 * 1024;
    private const double MinimumGain = 1.10;
    private const int ConfirmationSamples = 2;
    private const int MaximumReprobes = 2;

    private TimeSpan _lastTime;
    private long _lastBytes;
    private double _previousStableRate;
    private double _postExpansionRates;
    private int _postExpansionSamples;
    private int _stableTarget = StartingWorkers;
    private int _reprobeCount;
    private TimeSpan _retryAt;
    private Phase _phase = Phase.Sampling;
    private enum Phase { Sampling, Testing, Holding, Finished }

    public int MaximumWorkers { get; }
    public int TargetWorkers { get; private set; } = StartingWorkers;
    public bool StoppedExpanding => _phase == Phase.Finished;
    public string Decision { get; private set; } =
        "Starting with 2 connections; measuring total download speed.";

    public AutomaticConnectionController(int maximumWorkers)
    {
        if (maximumWorkers is < 2 or > 32)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkers));
        MaximumWorkers = maximumWorkers;
        if (maximumWorkers == StartingWorkers)
        {
            _phase = Phase.Finished;
            Decision = "Using 2 connections (your selected maximum).";
        }
    }

    /// <returns>True only when the desired worker count changes.</returns>
    public bool Observe(TimeSpan elapsed, long received, long total)
    {
        if (_phase == Phase.Finished) return false;
        if (elapsed < _lastTime || received < _lastBytes || total <= 0 ||
            received > total || elapsed < TimeSpan.Zero)
        {
            Finish("Automatic tests stopped because progress measurements were inconsistent.");
            return false;
        }
        TimeSpan window = total >= LongTransferThreshold
            ? SampleWindow : ShortTransferSampleWindow;
        if (elapsed - _lastTime < window) return false;

        double seconds = (elapsed - _lastTime).TotalSeconds;
        long bytes = received - _lastBytes;
        _lastTime = elapsed;
        _lastBytes = received;
        if (bytes < MinimumWindowBytes)
        {
            Decision = $"Using up to {TargetWorkers} / {MaximumWorkers} connections; waiting for enough data to measure.";
            return false;
        }
        double speed = bytes / seconds;
        if (!double.IsFinite(speed) || speed <= 0)
        {
            Finish("Automatic tests stopped because a valid speed could not be measured.");
            return false;
        }

        if (_phase == Phase.Testing)
        {
            _postExpansionRates += speed;
            _postExpansionSamples++;
            Decision = $"Testing {TargetWorkers} / {MaximumWorkers} connections; comparing measured speeds " +
                $"({_postExpansionSamples}/{ConfirmationSamples} windows).";
            if (_postExpansionSamples < ConfirmationSamples) return false;
            double newRate = _postExpansionRates / _postExpansionSamples;
            _postExpansionRates = 0;
            _postExpansionSamples = 0;
            if (newRate < _previousStableRate * MinimumGain)
            {
                TargetWorkers = _stableTarget;
                if (_reprobeCount >= MaximumReprobes)
                {
                    Finish($"Keeping {TargetWorkers} / {MaximumWorkers} connections: further tests did not improve speed.");
                }
                else
                {
                    _retryAt = elapsed + ReprobeDelay;
                    _phase = Phase.Holding;
                    Decision = $"Using {TargetWorkers} / {MaximumWorkers} connections: extra workers did not improve " +
                        "total speed enough. Will test again later.";
                }
                return true; // retire extra workers between pieces, never mid-request
            }
            _stableTarget = TargetWorkers;
            _reprobeCount = 0;
            _previousStableRate = newRate;
            _phase = Phase.Sampling;
            Decision = $"Measured faster download at {TargetWorkers} / {MaximumWorkers} connections.";
            // The next measurement window can consider another increase.
            return false;
        }

        if (_phase == Phase.Holding && elapsed < _retryAt)
        {
            Decision = $"Using {TargetWorkers} / {MaximumWorkers} connections; waiting to re-test extra connections.";
            return false;
        }
        if (TargetWorkers >= MaximumWorkers)
        {
            Finish($"Using {MaximumWorkers} / {MaximumWorkers} connections (your selected maximum).");
            return false;
        }

        int next = Math.Min(MaximumWorkers, TargetWorkers * 2);
        // Don't expand when too little remains for another meaningful window,
        // or when most pending ranges are too small to keep workers productive.
        if (total - received < next * MinimumUsefulRange * 2L)
        {
            Finish($"Using {TargetWorkers} / {MaximumWorkers} connections; file is nearing completion.");
            return false;
        }
        if (_phase == Phase.Holding) _reprobeCount++;
        _stableTarget = TargetWorkers;
        _previousStableRate = speed;
        TargetWorkers = next;
        _phase = Phase.Testing;
        _postExpansionRates = 0;
        _postExpansionSamples = 0;
        Decision = _reprobeCount > 0
            ? $"Re-testing {next} / {MaximumWorkers} connections after a short speed plateau."
            : $"Testing {next} / {MaximumWorkers} connections to see whether total speed improves.";
        return true;
    }

    private void Finish(string message)
    {
        _phase = Phase.Finished;
        Decision = message;
    }
}
