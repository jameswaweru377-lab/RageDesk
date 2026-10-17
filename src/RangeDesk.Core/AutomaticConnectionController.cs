namespace RangeDesk.Core;

/// <summary>
/// Local, deterministic V7 connection budget. This controls the number of
/// currently launched HTTP range workers, not the user's physical bandwidth.
/// It increases workers only after a measured productive window and freezes
/// expansion when the next window shows insufficient aggregate benefit.
/// Existing workers are never interrupted just to decrease concurrency.
/// </summary>
public sealed class AutomaticConnectionController
{
    public const int StartingWorkers = 2;
    public static readonly TimeSpan SampleWindow = TimeSpan.FromMilliseconds(1500);
    private const long MinimumWindowBytes = 64 * 1024;
    private const long MinimumUsefulRange = 512 * 1024;
    private const double MinimumGain = 1.12;

    private TimeSpan _lastTime;
    private long _lastBytes;
    private double _beforeExpansionBytesPerSecond;
    private bool _checkingExpansion;
    public bool StoppedExpanding { get; private set; }
    public int MaximumWorkers { get; }
    public int TargetWorkers { get; private set; }

    public AutomaticConnectionController(int maximumWorkers)
    {
        if (maximumWorkers is not (2 or 4 or 8 or 16 or 32))
            throw new ArgumentOutOfRangeException(nameof(maximumWorkers));
        MaximumWorkers = maximumWorkers;
        TargetWorkers = StartingWorkers;
    }

    public bool Observe(TimeSpan elapsed, long received, long total)
    {
        if (StoppedExpanding || TargetWorkers >= MaximumWorkers) return false;
        if (elapsed < _lastTime || received < _lastBytes || received > total || total <= 0)
        {
            StoppedExpanding = true; // inconsistent measurements cannot drive requests
            return false;
        }
        var span = elapsed - _lastTime;
        if (span < SampleWindow) return false;
        long delta = received - _lastBytes;
        _lastTime = elapsed;
        _lastBytes = received;
        if (delta < MinimumWindowBytes) return false;
        double rate = delta / span.TotalSeconds;
        if (!double.IsFinite(rate) || rate <= 0) return false;
        if (_checkingExpansion)
        {
            _checkingExpansion = false;
            if (rate < _beforeExpansionBytesPerSecond * MinimumGain)
            {
                StoppedExpanding = true;
                return false; // aggregate bandwidth is probably saturated
            }
        }

        int next = Math.Min(MaximumWorkers, TargetWorkers * 2);
        // Do not create more new work than there is remaining useful payload.
        if (total - received < next * MinimumUsefulRange)
        {
            StoppedExpanding = true;
            return false;
        }
        _beforeExpansionBytesPerSecond = rate;
        _checkingExpansion = true;
        TargetWorkers = next;
        return true;
    }
}
