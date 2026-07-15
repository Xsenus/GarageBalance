using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class RequestCorrelationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_GeneratesErrorIdAndReturnsItInResponseHeader()
    {
        var context = new DefaultHttpContext();
        string? observedId = null;
        var middleware = new RequestCorrelationMiddleware(next =>
        {
            observedId = next.TraceIdentifier;
            return Task.CompletedTask;
        }, NullLogger<RequestCorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.NotNull(observedId);
        Assert.Matches("^[a-f0-9]{16}$", observedId);
        Assert.Equal(observedId, context.Response.Headers[RequestCorrelationMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData("customer-error_123")]
    [InlineData("support-case-20260715")]
    public async Task InvokeAsync_PreservesSafeCallerIdentifier(string requestedId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestCorrelationMiddleware.HeaderName] = requestedId;
        var middleware = new RequestCorrelationMiddleware(_ => Task.CompletedTask, NullLogger<RequestCorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(requestedId, context.TraceIdentifier);
    }

    [Fact]
    public async Task InvokeAsync_ReplacesUnsafeIdentifier()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestCorrelationMiddleware.HeaderName] = "password=secret value";
        var middleware = new RequestCorrelationMiddleware(_ => Task.CompletedTask, NullLogger<RequestCorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.DoesNotContain("secret", context.TraceIdentifier, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[a-f0-9]{16}$", context.TraceIdentifier);
    }
}
