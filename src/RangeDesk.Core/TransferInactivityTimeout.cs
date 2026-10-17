using System.Net.Http;

namespace RangeDesk.Core;

/// <summary>
/// Bounds a single network wait rather than the entire download duration.
/// The timer restarts for each received chunk; intentional bandwidth throttling
/// and file-system operations cannot trigger a false network stall.
/// </summary>
public static class TransferInactivityTimeout
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    public static TimeSpan Validate(TimeSpan? value)
    {
        var timeout = value ?? Default;
        if (timeout < TimeSpan.FromMilliseconds(20) || timeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(value), "Network idle timeout must be between 20 ms and 10 minutes.");
        return timeout;
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellation,
        TimeSpan? timeout = null)
    {
        var duration = Validate(timeout);
        using var scoped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        scoped.CancelAfter(duration);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, scoped.Token);
        }
        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested && scoped.IsCancellationRequested)
        {
            throw new TimeoutException("Server did not respond within the network inactivity limit.", ex);
        }
    }

    /// <summary>
    /// A failure while reading an HTTP response body is a remote transport
    /// failure, not a local disk write failure. Only use this at HTTP-response
    /// input sites; ordinary ReadAsync retains its original exception types.
    /// </summary>
    public static async ValueTask<int> ReadHttpBodyAsync(
        Stream input, Memory<byte> buffer, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        try { return await ReadAsync(input, buffer, cancellation, timeout); }
        catch (IOException ex) when (ex is not EndOfStreamException)
        {
            throw new EndOfStreamException(
                "HTTP response body disconnected before validation.", ex);
        }
    }

    public static async ValueTask<int> ReadAsync(
        Stream input, Memory<byte> buffer, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        var duration = Validate(timeout);
        using var scoped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        scoped.CancelAfter(duration);
        try
        {
            return await input.ReadAsync(buffer, scoped.Token);
        }
        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested && scoped.IsCancellationRequested)
        {
            throw new TimeoutException("No download data arrived within the network inactivity limit.", ex);
        }
    }
}
