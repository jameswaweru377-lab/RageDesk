namespace RangeDesk.Core;

/// <summary>
/// A monotonic, bounded rolling measurement of received bytes (not bytes saved to
/// a checkpoint). This is a display estimate, never an artificial speed boost.
/// A quiet connection continues contributing zero-byte samples so real stalls
/// gradually decay to 0 rather than remaining frozen at the last fast burst.
/// </summary>
public sealed class DownloadRateEstimator
{
    private readonly TimeSpan _window;
    private readonly TimeSpan _warmup;
    private readonly Queue<Sample> _samples = new();
    private TimeSpan _lastTime;
    private long _lastBytes;
    private bool _started;

    private readonly record struct Sample(TimeSpan At, long Bytes);

    public DownloadRateEstimator(TimeSpan? window = null, TimeSpan? warmup = null)
    {
        _window = window ?? TimeSpan.FromSeconds(8);
        _warmup = warmup ?? TimeSpan.FromMilliseconds(500);
        if (_window <= TimeSpan.Zero || _warmup < TimeSpan.Zero || _warmup >= _window)
            throw new ArgumentOutOfRangeException(nameof(window));
    }

    public double Update(TimeSpan elapsed, long receivedBytes)
    {
        if (elapsed < TimeSpan.Zero || receivedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (_started && (elapsed < _lastTime || receivedBytes < _lastBytes))
            throw new ArgumentOutOfRangeException(nameof(receivedBytes),
                "Observations must be monotonic; restart a new estimator for every transfer.");

        _started = true;
        _lastTime = elapsed;
        _lastBytes = receivedBytes;
        _samples.Enqueue(new Sample(elapsed, receivedBytes));

        var cutoff = elapsed - _window;
        // Keep the most recent sample at or before the window boundary, or the
        // earliest observation while the stream has been active < 8 seconds.
        while (_samples.Count > 1 && _samples.ElementAt(1).At <= cutoff)
            _samples.Dequeue();

        var first = _samples.Peek();
        var span = elapsed - first.At;
        if (elapsed < _warmup || span <= TimeSpan.Zero) return 0;

        return Math.Max(0, receivedBytes - first.Bytes) / span.TotalSeconds;
    }

    public void Reset()
    {
        _samples.Clear();
        _lastBytes = 0;
        _lastTime = TimeSpan.Zero;
        _started = false;
    }
}
