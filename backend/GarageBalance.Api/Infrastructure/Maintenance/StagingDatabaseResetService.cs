using System.Security.Cryptography;
using System.Text;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Maintenance;

public sealed class StagingDatabaseResetService(
    GarageBalanceDbContext context,
    IDatabaseBackupService backupService,
    IConfiguration configuration,
    IOptions<StagingDatabaseResetOptions> options,
    ILogger<StagingDatabaseResetService> logger) : IStagingDatabaseResetService
{
    public const string RequiredConfirmation = "ОЧИСТИТЬ БАЗУ";
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private readonly StagingDatabaseResetOptions _options = options.Value;

    public async Task<StagingDatabaseResetResult> ResetAsync(
        StagingDatabaseResetRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Password))
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_disabled",
                "Очистка базы данных отключена в конфигурации сервера.");
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var databaseName = string.IsNullOrWhiteSpace(connectionString)
            ? string.Empty
            : new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (!string.Equals(databaseName, "garagebalance_staging", StringComparison.Ordinal))
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_wrong_target",
                "Очистка разрешена только для тестовой базы данных.");
        }

        if (!PasswordMatches(request.Password, _options.Password))
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_password_invalid",
                "Неверный пароль очистки базы данных.");
        }

        if (!string.Equals(request.Confirmation?.Trim(), RequiredConfirmation, StringComparison.Ordinal))
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_confirmation_invalid",
                $"Введите точную фразу «{RequiredConfirmation}».");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 500)
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_reason_invalid",
                "Укажите причину очистки длиной от 3 до 500 символов.");
        }

        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            return StagingDatabaseResetResult.Failure(
                "database_reset_in_progress",
                "Очистка базы данных уже выполняется.");
        }

        try
        {
            var backup = await backupService.CreateAsync(
                DatabaseBackupKind.PreUpdate,
                reason,
                actorUserId,
                cancellationToken);
            if (!backup.Succeeded || backup.Value is null)
            {
                return StagingDatabaseResetResult.Failure(
                    "database_reset_backup_failed",
                    backup.ErrorMessage ?? "Не удалось создать проверенную резервную копию перед очисткой.");
            }

            var reset = await new WorkingDataResetExecutor(context).ResetAsync(cancellationToken);
            logger.LogWarning(
                "Staging database working data was reset after verified backup {BackupFileName}. ClearedRows={ClearedRows}; PreservedUsers={PreservedUsers}.",
                backup.Value.FileName,
                reset.ClearedRowCount,
                reset.PreservedAfter.Users);
            return StagingDatabaseResetResult.Success(new StagingDatabaseResetDto(
                backup.Value.FileName,
                reset.ClearedRowCount,
                reset.PreservedAfter.Users,
                reset.PreservedAfter.Tariffs,
                reset.PreservedAfter.IrregularPayments,
                reset.PreservedAfter.Funds,
                reset.FundBalance,
                reset.GeneralPoolBalance));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Staging database reset failed. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return StagingDatabaseResetResult.Failure(
                "database_reset_failed",
                "Очистка не выполнена. Изменения базы данных отменены, резервная копия сохранена.");
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private static bool PasswordMatches(string? suppliedPassword, string configuredPassword)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedPassword ?? string.Empty));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredPassword));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }
}
