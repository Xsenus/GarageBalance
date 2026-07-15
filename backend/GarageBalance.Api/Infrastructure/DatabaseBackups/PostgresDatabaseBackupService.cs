using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Backups;

public sealed partial class PostgresDatabaseBackupService(
    IConfiguration configuration,
    IOptions<DatabaseBackupOptions> options,
    IBackupCommandRunner commandRunner,
    IBackupToolLocator toolLocator,
    IAuditEventWriter auditEventWriter,
    IApplicationUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PostgresDatabaseBackupService> logger) : IDatabaseBackupService
{
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static DateTimeOffset? _lastSuccessfulBackupAtUtc;
    private static string? _lastError;
    private readonly DatabaseBackupOptions _options = options.Value;
    private readonly string _directory = ResolveBackupDirectory(options.Value.Directory);

    public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backups = EnumerateBackups(20);
        var lastSuccessful = backups.FirstOrDefault()?.CreatedAtUtc ?? _lastSuccessfulBackupAtUtc;
        var toolError = _options.Enabled ? GetToolAvailabilityError() : null;
        return Task.FromResult(new DatabaseBackupStatusDto(
            _options.Enabled,
            _options.AutomaticEnabled,
            _options.IntervalHours,
            _options.RetentionCount,
            _directory,
            OperationLock.CurrentCount == 0,
            lastSuccessful,
            toolError ?? _lastError,
            backups));
    }

    public async Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(
        DatabaseBackupKind kind,
        string? reason,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_disabled",
                "Резервное копирование отключено в конфигурации сервера.");
        }

        if (kind == DatabaseBackupKind.Manual)
        {
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                    "database_backup_reason_required",
                    "Укажите причину создания резервной копии.");
            }

            if (reason.Length is < 3 or > 500)
            {
                return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                    "database_backup_reason_invalid",
                    "Причина должна содержать от 3 до 500 символов.");
            }
        }

        var pgDumpPath = toolLocator.Resolve(_options.PgDumpPath);
        var pgRestorePath = toolLocator.Resolve(_options.PgRestorePath);
        if (pgDumpPath is null || pgRestorePath is null)
        {
            return Fail(
                "database_backup_tools_unavailable",
                "Не найдены утилиты PostgreSQL pg_dump и pg_restore. Установите клиентские инструменты PostgreSQL или задайте POSTGRESQL_BIN.");
        }

        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_in_progress",
                "Другая резервная копия уже создается. Дождитесь ее завершения.");
        }

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var now = timeProvider.GetUtcNow();
            var kindName = FormatKind(kind);
            var fileName = $"garagebalance_{kindName}_{now:yyyyMMdd_HHmmss_fff}.pgdump";
            var finalPath = Path.Combine(_directory, fileName);
            temporaryPath = finalPath + ".tmp";
            var connection = BuildConnectionSettings();

            var dumpResult = await commandRunner.RunAsync(new BackupCommand(
                pgDumpPath,
                BuildDumpArguments(connection, temporaryPath),
                BuildPasswordEnvironment(connection)), cancellationToken);
            if (dumpResult.ExitCode != 0)
            {
                return Fail("database_backup_dump_failed", "PostgreSQL не смог создать резервную копию.", dumpResult.StandardError);
            }

            var temporaryFile = new FileInfo(temporaryPath);
            if (!temporaryFile.Exists || temporaryFile.Length == 0)
            {
                return Fail("database_backup_empty", "Созданная резервная копия пуста и была отклонена.");
            }

            var verifyResult = await commandRunner.RunAsync(new BackupCommand(
                pgRestorePath,
                ["--list", temporaryPath],
                new Dictionary<string, string>()), cancellationToken);
            if (verifyResult.ExitCode != 0)
            {
                return Fail("database_backup_verification_failed", "Не удалось проверить структуру резервной копии.", verifyResult.StandardError);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            temporaryPath = null;
            var file = new FileInfo(finalPath);
            var dto = new DatabaseBackupFileDto(file.Name, file.Length, now, kindName);
            _lastSuccessfulBackupAtUtc = now;
            _lastError = null;
            DeleteExpiredBackups();

            if (kind != DatabaseBackupKind.PreUpdate)
            {
                auditEventWriter.Add(new AuditEventWriteRequest(
                    actorUserId,
                    "database.backup_created",
                    "database_backup",
                    file.Name,
                    Summary: kind == DatabaseBackupKind.Manual
                        ? "Администратор создал резервную копию базы данных."
                        : "Система создала автоматическую резервную копию базы данных.",
                    Section: "settings",
                    ActionKind: "create",
                    EntityDisplayName: file.Name,
                    Reason: reason,
                    Metadata: new Dictionary<string, object?>
                    {
                        ["kind"] = kindName,
                        ["sizeBytes"] = file.Length
                    }));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation("Database backup {BackupFileName} was created and verified.", file.Name);
            return DatabaseBackupResult<DatabaseBackupFileDto>.Success(dto);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Database backup failed. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return Fail("database_backup_failed", "Не удалось создать резервную копию базы данных.");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

            OperationLock.Release();
        }
    }

    private NpgsqlConnectionStringBuilder BuildConnectionSettings()
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Default PostgreSQL connection string is not configured.");
        }

        return new NpgsqlConnectionStringBuilder(connectionString);
    }

    private static IReadOnlyList<string> BuildDumpArguments(NpgsqlConnectionStringBuilder connection, string outputPath)
    {
        var host = connection.Host ?? throw new InvalidOperationException("PostgreSQL host is not configured.");
        var username = connection.Username ?? throw new InvalidOperationException("PostgreSQL username is not configured.");
        var database = connection.Database ?? throw new InvalidOperationException("PostgreSQL database is not configured.");
        return [
            "--format=custom",
            "--no-owner",
            "--no-privileges",
            "--no-password",
            "--host", host,
            "--port", connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--username", username,
            "--dbname", database,
            "--file", outputPath
        ];
    }

    private static IReadOnlyDictionary<string, string> BuildPasswordEnvironment(NpgsqlConnectionStringBuilder connection)
    {
        return string.IsNullOrEmpty(connection.Password)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["PGPASSWORD"] = connection.Password };
    }

    private IReadOnlyList<DatabaseBackupFileDto> EnumerateBackups(int limit)
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "garagebalance_*.pgdump", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => ManagedBackupName().IsMatch(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(limit)
            .Select(file => new DatabaseBackupFileDto(
                file.Name,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                ParseKind(file.Name)))
            .ToArray();
    }

    private void DeleteExpiredBackups()
    {
        var expired = EnumerateBackups(int.MaxValue).Skip(_options.RetentionCount);
        foreach (var backup in expired)
        {
            File.Delete(Path.Combine(_directory, backup.FileName));
        }
    }

    private string? GetToolAvailabilityError()
    {
        return toolLocator.Resolve(_options.PgDumpPath) is not null && toolLocator.Resolve(_options.PgRestorePath) is not null
            ? null
            : "Не найдены утилиты PostgreSQL pg_dump и pg_restore. Установите клиентские инструменты PostgreSQL или задайте POSTGRESQL_BIN.";
    }

    private static string ResolveBackupDirectory(string configuredDirectory)
    {
        if (!string.Equals(configuredDirectory, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = AppContext.BaseDirectory;
        }

        return Path.Combine(localData, "GarageBalance", "backups");
    }

    private DatabaseBackupResult<DatabaseBackupFileDto> Fail(string code, string message, string? diagnostic = null)
    {
        _lastError = message;
        if (!string.IsNullOrWhiteSpace(diagnostic))
        {
            logger.LogWarning("Database backup command failed with code {BackupErrorCode}.", code);
        }

        return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(code, message);
    }

    private static string FormatKind(DatabaseBackupKind kind) => kind switch
    {
        DatabaseBackupKind.Manual => "manual",
        DatabaseBackupKind.Automatic => "automatic",
        DatabaseBackupKind.PreUpdate => "pre_update",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string ParseKind(string fileName)
    {
        var match = ManagedBackupName().Match(fileName);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    [GeneratedRegex("^garagebalance_(manual|automatic|pre_update)_\\d{8}_\\d{6}_\\d{3}\\.pgdump$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedBackupName();
}
