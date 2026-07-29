using System.Data.Common;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Diagnostics;

public sealed class DatabaseCommandPerformanceOptions
{
    public const string SectionName = "DatabasePerformance";

    [Range(50, 300_000)]
    public int SlowCommandThresholdMilliseconds { get; init; } = 500;
}

public sealed class DatabaseCommandPerformanceInterceptor(
    IHttpContextAccessor httpContextAccessor,
    IOptions<DatabaseCommandPerformanceOptions> options,
    ILogger<DatabaseCommandPerformanceInterceptor> logger) : DbCommandInterceptor
{
    private static readonly EventId SlowDatabaseCommandEvent = new(3201, "SlowDatabaseCommand");
    private static readonly EventId FailedDatabaseCommandEvent = new(3202, "FailedDatabaseCommand");
    private readonly double slowCommandThresholdMilliseconds = options.Value.SlowCommandThresholdMilliseconds;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteReader);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteReader);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteScalar);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteScalar);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteNonQuery);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, eventData.CommandSource, DbCommandMethod.ExecuteNonQuery);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        RecordCommand(
            eventData.Duration,
            eventData.CommandSource,
            eventData.ExecuteMethod,
            failed: true,
            eventData.Exception.GetType().Name);

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(
            eventData.Duration,
            eventData.CommandSource,
            eventData.ExecuteMethod,
            failed: true,
            eventData.Exception.GetType().Name);
        return Task.CompletedTask;
    }

    internal void RecordCommand(
        TimeSpan duration,
        CommandSource commandSource,
        DbCommandMethod commandMethod,
        bool failed = false,
        string? exceptionType = null)
    {
        var elapsedMilliseconds = duration.TotalMilliseconds;
        if (!failed && elapsedMilliseconds < slowCommandThresholdMilliseconds)
        {
            return;
        }

        var correlationId =
            httpContextAccessor.HttpContext?.TraceIdentifier ??
            Activity.Current?.TraceId.ToString() ??
            "background";

        if (failed)
        {
            logger.LogError(
                FailedDatabaseCommandEvent,
                "Database command {CommandMethod} ({CommandSource}) for request {CorrelationId} failed after {ElapsedMilliseconds:F1} ms with {ExceptionType}.",
                commandMethod,
                commandSource,
                correlationId,
                elapsedMilliseconds,
                exceptionType ?? "DatabaseException");
            return;
        }

        logger.LogWarning(
            SlowDatabaseCommandEvent,
            "Slow database command {CommandMethod} ({CommandSource}) for request {CorrelationId} completed in {ElapsedMilliseconds:F1} ms.",
            commandMethod,
            commandSource,
            correlationId,
            elapsedMilliseconds);
    }
}
