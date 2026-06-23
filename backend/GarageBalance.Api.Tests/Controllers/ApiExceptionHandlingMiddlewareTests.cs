using System.Text.Json;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ApiExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsInternalErrorProblemForUnhandledException()
    {
        var context = CreateHttpContext();
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Database password leaked in exception message."),
            NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", context.Response.ContentType);
        Assert.Equal(ApiProblemDetails.InternalErrorCode, problem.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.GetProperty("status").GetInt32());
        Assert.Equal(ApiProblemDetails.InternalErrorCode, problem.GetProperty(ApiProblemDetails.CodeExtensionKey).GetString());
        Assert.DoesNotContain("password", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextWhenNoExceptionIsThrown()
    {
        var nextCalled = false;
        var context = CreateHttpContext();
        var middleware = new ApiExceptionHandlingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotMaskExceptionWhenResponseHasStarted()
    {
        var context = CreateHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature(context.Response.Body));
        var middleware = new ApiExceptionHandlingMiddleware(_ =>
        {
            throw new InvalidOperationException("Response has already started.");
        }, NullLogger<ApiExceptionHandlingMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    private sealed class StartedResponseFeature(Stream body) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = body;
        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
