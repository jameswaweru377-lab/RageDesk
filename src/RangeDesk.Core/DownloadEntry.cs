using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace RangeDesk.Core;

public enum DownloadStatus { Queued, Downloading, Paused, Canceled, Completed, Failed }

/// <summary>Only actual network and file measurements are published; missing size is indeterminate.</summary>
public sealed class DownloadEntry : INotifyPropertyChanged
{
    private DownloadStatus _status = DownloadStatus.Queued;
    private long _received;
    private long? _total;
    private double _bytesPerSecond;
    private int _connections;
    private string? _error;
    private readonly List<double> _speedSamples = new(64);
    private readonly ObservableCollection<string> _events = new();
    private readonly ObservableCollection<DownloadWorkerState> _workers = new();

    public DownloadEntry(string url, string destination, string category = "Other", string labels = "", int connections = 1, int bandwidthKiBps = 0, string? expectedSha256 = null)
    {
        if (connections is not (1 or 2 or 4 or 8 or 16 or 32))
            throw new ArgumentOutOfRangeException(nameof(connections));
        if (bandwidthKiBps < 0 || bandwidthKiBps > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bandwidthKiBps));
        if (expectedSha256 is { Length: > 0 } &&
            (expectedSha256.Length != 64 || expectedSha256.Any(c => !Uri.IsHexDigit(c))))
            throw new ArgumentException("SHA-256 must contain 64 hexadecimal characters.", nameof(expectedSha256));
        RequestedConnections = connections;
        BandwidthKiBps = bandwidthKiBps;
        ExpectedSha256 = string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256.ToUpperInvariant();
        Id = Guid.NewGuid();
        Url = url;
        Destination = destination;
        Category = category;
        Labels = labels;
        AddedAt = DateTimeOffset.Now;
        Events = new ReadOnlyObservableCollection<string>(_events);
        Workers = new ReadOnlyObservableCollection<DownloadWorkerState>(_workers);
        _events.Add($"{DateTime.Now:HH:mm:ss}  Added to downloads");
    }

    public int RequestedConnections { get; }
    public int BandwidthKiBps { get; }
    public string? ExpectedSha256 { get; }
    public string ChecksumLabel => ExpectedSha256 is null ? "Not configured" :
        Status == DownloadStatus.Completed ? "SHA-256 verified" : "SHA-256 required";
    public Guid Id { get; }
    public string Url { get; }
    public string Destination { get; }
    public string Category { get; set; }
    public string Labels { get; set; }
    public DateTimeOffset AddedAt { get; }
    public string Name => Path.GetFileName(Destination);
    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "";
    public DownloadStatus Status => _status;
    public long Received => _received;
    public long? Total => _total;
    public double BytesPerSecond => _bytesPerSecond;
    public int Connections => _connections;
    public string? Error => _error;
    /// <summary>Most recent measured receive-rate samples, bytes per second, max 60.</summary>
    public IReadOnlyList<double> SpeedSamples => _speedSamples;
    /// <summary>Locally held state changes only; never includes signed URLs, tokens or headers.</summary>
    public ReadOnlyObservableCollection<string> Events { get; private set; } = null!;
    /// <summary>Actual measured per-worker telemetry; no synthetic connection cards.</summary>
    public ReadOnlyObservableCollection<DownloadWorkerState> Workers { get; private set; } = null!;

    public void ReportWorkers(IReadOnlyList<WorkerMetric> metrics)
    {
        foreach (var metric in metrics)
        {
            while (_workers.Count <= metric.Index)
                _workers.Add(new DownloadWorkerState(_workers.Count));
            _workers[metric.Index].Update(metric);
        }
    }
    public double? Percent => _total is > 0 ? Math.Min(100, 100.0 * _received / _total.Value) : null;
    public bool IsIndeterminate => _total is null && _status == DownloadStatus.Downloading;
    public string ProgressLabel => Percent is double p ? $"{p:0.0}%" : "—";
    public string TransferredLabel => _total is long total
        ? $"{FormatSize(_received)} / {FormatSize(total)}" : FormatSize(_received);
    public string SpeedLabel => _status == DownloadStatus.Downloading && _bytesPerSecond > 0
        ? $"{FormatSize(_bytesPerSecond)}/s" : "—";
    public string EtaLabel
    {
        get
        {
            if (_status != DownloadStatus.Downloading || _total is not long size || _bytesPerSecond <= 0)
                return "—";
            double seconds = (size - _received) / _bytesPerSecond;
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
                return "—";
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                : $"{span.Seconds}s";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Report(DownloadStatus status, long received, long? total,
        double bytesPerSecond = 0, int activeConnections = 0, string? error = null)
    {
        if (_status != status)
        {
            _events.Add($"{DateTime.Now:HH:mm:ss}  {status}");
            if (_events.Count > 100) _events.RemoveAt(0);
        }
        _status = status;
        _received = Math.Max(0, received);
        _total = total;
        _bytesPerSecond = double.IsFinite(bytesPerSecond) ? Math.Max(0, bytesPerSecond) : 0;
        _connections = Math.Max(0, activeConnections);
        _error = error;
        if (status == DownloadStatus.Downloading && double.IsFinite(bytesPerSecond) && bytesPerSecond >= 0)
        {
            _speedSamples.Add(bytesPerSecond);
            if (_speedSamples.Count > 60) _speedSamples.RemoveAt(0);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public static string FormatSize(double bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000:0.00} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000:0.0} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000:0.0} KB";
        return $"{Math.Max(0, bytes):0} B";
    }
}
