using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RangeDesk.Core;

/// <summary>Measured HTTP range telemetry, not simulated segments.</summary>
public readonly record struct WorkerMetric(int Index, long Received, long Total, double BytesPerSecond, bool Active);

public sealed class DownloadWorkerState : INotifyPropertyChanged
{
    private long _received;
    private long _total;
    private double _rate;
    private bool _active;

    public DownloadWorkerState(int index) => Index = index;
    public int Index { get; }
    public string Title => $"#{Index + 1}";
    public long Received => _received;
    public long Total => _total;
    public double Percent => _total == 0 ? 0 : Math.Clamp(_received * 100.0 / _total, 0, 100);
    public double Rate => _rate;
    public bool Active => _active;
    public string SpeedLabel => _active ? DownloadEntry.FormatSize(_rate) + "/s" : "—";
    public string StateLabel => _total > 0 && _received >= _total ? "Completed" :
        _active ? "Downloading" : "Pending";
    public string BytesLabel => $"{DownloadEntry.FormatSize(_received)} / {DownloadEntry.FormatSize(_total)}";
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(WorkerMetric metric)
    {
        _received = metric.Received;
        _total = metric.Total;
        _rate = Math.Max(0, metric.BytesPerSecond);
        _active = metric.Active;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
