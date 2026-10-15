using System.Net;
using System.Net.Http.Headers;
using RangeDesk.Core;
using Xunit;

namespace RangeDesk.Tests;

public sealed class HttpRetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void TemporaryResponsesAreRetried(HttpStatusCode status) =>
        Assert.True(HttpRetryPolicy.IsTemporary(status));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable)]
    public void PermanentResponsesAreNeverAutomaticallyRetried(HttpStatusCode status) =>
        Assert.False(HttpRetryPolicy.IsTemporary(status));

    [Fact]
    public void ServerRetryAfterIsPreferredAndClamped()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), HttpRetryPolicy.Delay(1,
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(3))));
        Assert.Equal(TimeSpan.FromMinutes(5), HttpRetryPolicy.Delay(3,
            new RetryConditionHeaderValue(TimeSpan.FromMinutes(20))));
        var when = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(7), HttpRetryPolicy.Delay(2,
            new RetryConditionHeaderValue(when.AddSeconds(7)), when));
    }

    [Fact]
    public void RetryBudgetIsFinite()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), HttpRetryPolicy.Delay(1, null));
        Assert.Equal(TimeSpan.FromSeconds(4), HttpRetryPolicy.Delay(4, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => HttpRetryPolicy.Delay(5, null));
    }
}
