using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class AuthRateLimitPolicyTests
{
    [Fact]
    public void GetPartitionKey_NormalizesMappedIpv4AndHandlesUnknownAddress()
    {
        Assert.Equal(20, AuthRateLimitPolicy.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), AuthRateLimitPolicy.Window);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.15");

        Assert.Equal("192.0.2.15", AuthRateLimitPolicy.GetPartitionKey(context));

        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::15");
        Assert.Equal("2001:db8::15", AuthRateLimitPolicy.GetPartitionKey(context));

        context.Connection.RemoteIpAddress = null;
        Assert.Equal("unknown", AuthRateLimitPolicy.GetPartitionKey(context));
    }

    [Fact]
    public async Task WriteAsync_ReturnsProblemDetailsAndRetryAfterForRejectedLease()
    {
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = false
        });
        using var acceptedLease = limiter.AttemptAcquire();
        using var rejectedLease = limiter.AttemptAcquire();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await RateLimitProblemResponse.WriteAsync(context, rejectedLease, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.True(int.TryParse(context.Response.Headers.RetryAfter, out var retryAfterSeconds));
        Assert.InRange(retryAfterSeconds, 1, 60);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("ip_rate_limited", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status429TooManyRequests, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("ip_rate_limited", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WriteAsync_OmitsRetryAfterWhenLeaseDoesNotProvideIt()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });
        using var acceptedLease = limiter.AttemptAcquire();
        using var rejectedLease = limiter.AttemptAcquire();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await RateLimitProblemResponse.WriteAsync(context, rejectedLease, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
    }
}
