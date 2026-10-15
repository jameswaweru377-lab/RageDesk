using System.Net;
using System.Net.Http.Headers;

namespace RangeDesk.Core;

/// <summary>Finite retry policy for temporary HTTP failures. Never retries
/// validator or Content-Range mismatches. Honors server Retry-After up to 5 min.</summary>
public static class HttpRetryPolicy
{
    public const int MaximumAttempts = 5;
    public static bool IsTemporary(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    public static TimeSpan Delay(int attempt, RetryConditionHeaderValue? retryAfter,
        DateTimeOffset? utcNow = null)
    {
        if (attempt is < 1 or >= MaximumAttempts)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        var backoff = TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1)));
        var server = retryAfter?.Delta;
        if (server is null && retryAfter?.Date is DateTimeOffset date)
            server = date - (utcNow ?? DateTimeOffset.UtcNow);
        var selected = server ?? backoff;
        return TimeSpan.FromTicks(Math.Clamp(selected.Ticks, 0, TimeSpan.FromMinutes(5).Ticks));
    }
}
