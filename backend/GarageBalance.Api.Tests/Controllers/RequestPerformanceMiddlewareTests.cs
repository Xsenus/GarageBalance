using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class RequestPerformanceMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsServerTimingWithoutQueryString()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/dictionaries/owners";
        context.Request.QueryString = new QueryString("?search=personal-data");
        var timeProvider = new ManualTimeProvider();
        var middleware = CreateMiddleware(next =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(24.5));
            return Task.CompletedTask;
        }, timeProvider, new RecordingLogger());

        await middleware.InvokeAsync(context);

        Assert.Equal("app;dur=24.5", context.Response.Headers[RequestPerformanceMiddleware.ServerTimingHeaderName]);
    }

    [Fact]
    public async Task InvokeAsync_LogsOnlySlowRequestPathWithoutSensitiveQuery()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/dictionaries/owners";
        context.Request.QueryString = new QueryString("?search=secret-owner");
        context.Response.StatusCode = StatusCodes.Status200OK;
        var timeProvider = new ManualTimeProvider();
        var logger = new RecordingLogger();
        var middleware = CreateMiddleware(_ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(1_250));
            return Task.CompletedTask;
        }, timeProvider, logger);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(3101, entry.EventId.Id);
        Assert.Contains("/api/dictionaries/owners", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-owner", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotLogFastRequest()
    {
        var context = new DefaultHttpContext();
        var timeProvider = new ManualTimeProvider();
        var logger = new RecordingLogger();
        var middleware = CreateMiddleware(_ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(999));
            return Task.CompletedTask;
        }, timeProvider, logger);

        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task InvokeAsync_LogsSlowFailedRequestAndRethrows()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reports/income";
        var timeProvider = new ManualTimeProvider();
        var logger = new RecordingLogger();
        var expected = new OperationCanceledException("request cancelled");
        var middleware = CreateMiddleware(_ =>
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(1_100));
            throw expected;
        }, timeProvider, logger);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Single(logger.Entries);
    }

    private static RequestPerformanceMiddleware CreateMiddleware(
        RequestDelegate next,
        TimeProvider timeProvider,
        RecordingLogger logger) =>
        new(
            next,
            Options.Create(new RequestPerformanceOptions { SlowRequestThresholdMilliseconds = 1_000 }),
            timeProvider,
            logger);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }

    private sealed class RecordingLogger : ILogger<RequestPerformanceMiddleware>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);
}
