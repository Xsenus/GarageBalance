using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ApiSecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsNoStoreAndSecurityHeadersForApiResponses()
    {
        var context = CreateHttpContext("/api/auth/me");
        var middleware = new ApiSecurityHeadersMiddleware(async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsync("{}");
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("none", context.Response.Headers["X-Permitted-Cross-Domain-Policies"]);
        Assert.Equal("default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("no-store, no-cache, max-age=0", context.Response.Headers.CacheControl);
        Assert.Equal("no-cache", context.Response.Headers.Pragma);
        Assert.Equal("0", context.Response.Headers.Expires);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotDisableCacheForNonApiResponses()
    {
        var context = CreateHttpContext("/health");
        var middleware = new ApiSecurityHeadersMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"]);
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
        Assert.False(context.Response.Headers.ContainsKey("Pragma"));
        Assert.False(context.Response.Headers.ContainsKey("Expires"));
    }

    [Fact]
    public async Task InvokeAsync_KeepsExistingSecurityHeaders()
    {
        var context = CreateHttpContext("/api/reports/consolidated");
        context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'";
        var middleware = new ApiSecurityHeadersMiddleware(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("SAMEORIGIN", context.Response.Headers.XFrameOptions);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("default-src 'self'", context.Response.Headers["Content-Security-Policy"]);
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
