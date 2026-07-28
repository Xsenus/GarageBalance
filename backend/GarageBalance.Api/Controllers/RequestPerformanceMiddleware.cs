using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Controllers;

public sealed class RequestPerformanceOptions
{
    public const string SectionName = "RequestPerformance";

    [Range(100, 300_000)]
    public int SlowRequestThresholdMilliseconds { get; init; } = 1_000;
}

public sealed class RequestPerformanceMiddleware(
    RequestDelegate next,
    IOptions<RequestPerformanceOptions> options,
    TimeProvider timeProvider,
    ILogger<RequestPerformanceMiddleware> logger)
{
    public const string ServerTimingHeaderName = "Server-Timing";
    private static readonly EventId SlowRequestEvent = new(3101, "SlowHttpRequest");
    private readonly double slowRequestThresholdMilliseconds = options.Value.SlowRequestThresholdMilliseconds;

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = timeProvider.GetTimestamp();
        context.Response.OnStarting(() =>
        {
            var headerElapsed = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            context.Response.Headers[ServerTimingHeaderName] = FormattableString.Invariant($"app;dur={headerElapsed:F1}");
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMilliseconds = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            if (!context.Response.HasStarted)
            {
                context.Response.Headers[ServerTimingHeaderName] = FormattableString.Invariant($"app;dur={elapsedMilliseconds:F1}");
            }

            if (elapsedMilliseconds >= slowRequestThresholdMilliseconds)
            {
                logger.LogWarning(
                    SlowRequestEvent,
                    "Slow HTTP request {Method} {Path} completed with status {StatusCode} in {ElapsedMilliseconds:F1} ms.",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    elapsedMilliseconds);
            }
        }
    }
}
