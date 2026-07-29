using GarageBalance.Api.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Diagnostics;

public sealed class DatabaseCommandPerformanceInterceptorTests
{
    [Fact]
    public void RecordCommand_LogsSlowCommandWithRequestCorrelationWithoutSqlText()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "nginx-request-20260730"
        };
        var logger = new RecordingLogger();
        var interceptor = CreateInterceptor(context, logger);

        interceptor.RecordCommand(
            TimeSpan.FromMilliseconds(750),
            CommandSource.LinqQuery,
            DbCommandMethod.ExecuteReader);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(3201, entry.EventId.Id);
        Assert.Contains("nginx-request-20260730", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ExecuteReader", entry.Message, StringComparison.Ordinal);
        Assert.Contains("LinqQuery", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordCommand_DoesNotLogFastSuccessfulCommand()
    {
        var logger = new RecordingLogger();
        var interceptor = CreateInterceptor(new DefaultHttpContext(), logger);

        interceptor.RecordCommand(
            TimeSpan.FromMilliseconds(499),
            CommandSource.SaveChanges,
            DbCommandMethod.ExecuteNonQuery);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void RecordCommand_LogsFastFailureWithoutSensitiveExceptionMessage()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "failed-request-20260730"
        };
        var logger = new RecordingLogger();
        var interceptor = CreateInterceptor(context, logger);

        interceptor.RecordCommand(
            TimeSpan.FromMilliseconds(12),
            CommandSource.SaveChanges,
            DbCommandMethod.ExecuteNonQuery,
            failed: true,
            exceptionType: nameof(InvalidOperationException));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(3202, entry.EventId.Id);
        Assert.Contains("failed-request-20260730", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseCommandPerformanceInterceptor CreateInterceptor(
        HttpContext context,
        RecordingLogger logger) =>
        new(
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new DatabaseCommandPerformanceOptions
            {
                SlowCommandThresholdMilliseconds = 500
            }),
            logger);

    private sealed class RecordingLogger : ILogger<DatabaseCommandPerformanceInterceptor>
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
