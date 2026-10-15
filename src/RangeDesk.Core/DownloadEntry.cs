using System.ComponentModel;
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

    public DownloadEntry(string url, string destination, string category = "Other", string labels = "")
    {
        Id = Guid.NewGuid();
        Url = url;
        Destination = destination;
        Category = category;
        Labels = labels;
        AddedAt = DateTimeOffset.Now;
    }

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
        _status = status;
        _received = Math.Max(0, received);
        _total = total;
        _bytesPerSecond = double.IsFinite(bytesPerSecond) ? Math.Max(0, bytesPerSecond) : 0;
        _connections = Math.Max(0, activeConnections);
        _error = error;
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
