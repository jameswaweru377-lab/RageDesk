using System.Diagnostics;

namespace RangeDesk.Core;

/// <summary>
/// One token budget shared by every connection belonging to a download.
/// A zero limit means unlimited. The accumulator is monotonic, asynchronous,
/// and cancellation-aware; workers may not each receive the full allowance.
/// </summary>
public sealed class SharedBandwidthLimiter
{
    private double _bytesPerSecond;
    private int _limitKiBps;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _chargedBytes;

    public SharedBandwidthLimiter(int kibibytesPerSecond)
    {
        if (kibibytesPerSecond < 0 || kibibytesPerSecond > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond));
        _limitKiBps = kibibytesPerSecond;
        _bytesPerSecond = kibibytesPerSecond * 1024d;
    }

    public async ValueTask AccountAsync(int bytes, CancellationToken token)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes == 0 || Volatile.Read(ref _limitKiBps) == 0) return;
        await _gate.WaitAsync(token);
        try
        {
            if (_bytesPerSecond == 0) return;
            _chargedBytes = checked(_chargedBytes + bytes);
            double targetSeconds = _chargedBytes / _bytesPerSecond;
            double remaining = targetSeconds - _clock.Elapsed.TotalSeconds;
            if (remaining > 0)
                await Task.Delay(TimeSpan.FromSeconds(remaining), token);
        }
        finally { _gate.Release(); }
    }

    public int LimitKiBps => Volatile.Read(ref _limitKiBps);

    /// <summary>
    /// Switches an existing transfer's shared cap without restarting workers.
    /// Waiting workers complete their current accounted interval, and the
    /// new limit starts with a fresh monotonic allowance on next account.
    /// </summary>
    public async ValueTask SetLimitAsync(int kibibytesPerSecond, CancellationToken token)
    {
        if (kibibytesPerSecond is < 0 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond));
        if (Volatile.Read(ref _limitKiBps) == kibibytesPerSecond) return;
        await _gate.WaitAsync(token);
        try
        {
            if (_limitKiBps == kibibytesPerSecond) return;
            _bytesPerSecond = kibibytesPerSecond * 1024d;
            _chargedBytes = 0;
            _clock.Restart();
            Volatile.Write(ref _limitKiBps, kibibytesPerSecond);
        }
        finally { _gate.Release(); }
    }

    public long ChargedBytes => Interlocked.Read(ref _chargedBytes);
}
