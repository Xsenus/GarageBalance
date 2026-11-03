using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Controllers;

public sealed class ApiExceptionHandlingMiddleware(RequestDelegate next, ILogger<ApiExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "HTTP request {Method} {Path} was cancelled by the client.",
                context.Request.Method,
                context.Request.Path);
        }
        catch (OptimisticConcurrencyException) when (!context.Response.HasStarted)
        {
            logger.LogWarning(
                "Optimistic concurrency conflict for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
            await WriteProblemAsync(context, ApiProblemDetails.CreateConcurrentWriteConflict());
        }
        catch (DbUpdateConcurrencyException) when (!context.Response.HasStarted)
        {
            logger.LogWarning(
                "Optimistic concurrency conflict for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
            await WriteProblemAsync(context, ApiProblemDetails.CreateConcurrentWriteConflict());
        }
        catch (Exception exception) when (!context.Response.HasStarted && FindGuardViolation(exception) is not null)
        {
            var held = FindGuardViolation(exception) == "gb_annual_accrual_held";
            await WriteProblemAsync(context, ApiProblemDetails.Create(
                held ? "annual_accrual_generation_held" : "annual_accrual_guard_needs_review",
                held ? "Годовое начисление приостановлено. Обновите данные и проверьте период перед повтором." :
                    "Не удалось безопасно определить годовой период начисления. Данные не сохранены; обратитесь к администратору.",
                StatusCodes.Status409Conflict));
        }
        catch (Exception exception) when (!context.Response.HasStarted && IsSerializationFailure(exception))
        {
            // Never replay a financial operation automatically. Re-read current holds in a
            // fresh scope, bounded by a short deadline, and require the user to refresh.
            _ = await TryReadCurrentHoldsAsync(context);
            await WriteProblemAsync(context, ApiProblemDetails.CreateConcurrentWriteConflict());
        }
        catch (DbUpdateException exception) when (!context.Response.HasStarted && IsUniqueConstraintViolation(exception))
        {
            logger.LogWarning(
                "Concurrent unique write conflict for {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
            await WriteProblemAsync(context, ApiProblemDetails.CreateConcurrentWriteConflict());
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(
                "Unhandled API exception {ErrorId} ({ExceptionType}) for {Method} {Path}. Diagnostic={Diagnostic}",
                context.TraceIdentifier,
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                DiagnosticLogSanitizer.SanitizeException(exception));
            await WriteProblemAsync(context, ApiProblemDetails.CreateInternalError(context.TraceIdentifier));
        }
    }

    private static Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.Clear();
        context.Response.Headers[RequestCorrelationMiddleware.HeaderName] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindGuardViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } postgres &&
                postgres.ConstraintName is "gb_annual_accrual_held" or "gb_annual_guard_invalid" or "gb_annual_guard_ambiguous")
                return postgres.ConstraintName;
        return null;
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }) return true;
        return false;
    }

    private static async Task<string[]> TryReadCurrentHoldsAsync(HttpContext context)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            await using var scope = context.RequestServices.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
            var keys = await db.ApplicationSettings.AsNoTracking().Where(setting => setting.BooleanValue &&
                    setting.Key.StartsWith(AnnualAccrualHoldPolicy.KeyPrefix))
                .Select(setting => setting.Key).ToArrayAsync(deadline.Token);
            return keys.Select(key => key[AnnualAccrualHoldPolicy.KeyPrefix.Length..])
                .Where(year => year.Length == 4 && year.All(char.IsAsciiDigit)).Order().ToArray();
        }
        catch { return []; }
    }
}
