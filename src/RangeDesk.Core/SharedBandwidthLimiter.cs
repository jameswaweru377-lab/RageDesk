using System.Diagnostics;

namespace RangeDesk.Core;

/// <summary>
/// One token budget shared by every connection belonging to a download.
/// A zero limit means unlimited. The accumulator is monotonic, asynchronous,
/// and cancellation-aware; workers may not each receive the full allowance.
/// </summary>
public sealed class SharedBandwidthLimiter
{
    private readonly double _bytesPerSecond;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _chargedBytes;

    public SharedBandwidthLimiter(int kibibytesPerSecond)
    {
        if (kibibytesPerSecond < 0 || kibibytesPerSecond > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond));
        _bytesPerSecond = kibibytesPerSecond * 1024d;
    }

    public async ValueTask AccountAsync(int bytes, CancellationToken token)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (_bytesPerSecond == 0 || bytes == 0) return;
        await _gate.WaitAsync(token);
        try
        {
            _chargedBytes = checked(_chargedBytes + bytes);
            double targetSeconds = _chargedBytes / _bytesPerSecond;
            double remaining = targetSeconds - _clock.Elapsed.TotalSeconds;
            if (remaining > 0)
                await Task.Delay(TimeSpan.FromSeconds(remaining), token);
        }
        finally { _gate.Release(); }
    }

    public long ChargedBytes => Interlocked.Read(ref _chargedBytes);
}
