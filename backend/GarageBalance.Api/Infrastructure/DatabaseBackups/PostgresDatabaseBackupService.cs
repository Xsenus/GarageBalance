using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Storage;
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
    ILogger<PostgresDatabaseBackupService> logger,
    IStorageProviderRegistry? storageProviderRegistry = null,
    StorageConfigurationResolver? storageConfigurationResolver = null,
    IStorageCatalog? storageCatalog = null,
    IStorageReadRouter? storageReadRouter = null,
    StorageReconciliationRunner? storageReconciliationRunner = null) : IDatabaseBackupService
{
    private const int ManifestSchemaVersion = 2;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static DateTimeOffset? _lastSuccessfulBackupAtUtc;
    private static string? _lastError;
    private readonly DatabaseBackupOptions _options = options.Value;
    private readonly EffectiveStorageConfiguration? _storageConfiguration = storageConfigurationResolver?.Resolve();
    private readonly string _directory = DatabaseBackupPathResolver.Resolve(
        storageConfigurationResolver?.Resolve().Destinations
            .FirstOrDefault(destination => destination.Type == StorageProviderType.LocalFileSystem)?.RootPath
        ?? options.Value.Directory);

    public async Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DatabaseBackupFileDto> backups = EnumerateBackups(20, verifyChecksum: true);
        await ReconcileCatalogAsync(backups, cancellationToken);
        backups = await MergeCatalogBackupsAsync(backups, cancellationToken);
        backups = await ApplyCatalogProtectionAsync(backups, cancellationToken);
        var lastSuccessful = backups.FirstOrDefault()?.CreatedAtUtc ?? _lastSuccessfulBackupAtUtc;
        var freshnessThresholdHours = _options.IntervalHours + _options.FreshnessGraceHours;
        var isStale = _options.Enabled &&
            (lastSuccessful is null || timeProvider.GetUtcNow() - lastSuccessful.Value > TimeSpan.FromHours(freshnessThresholdHours));
        var toolError = _options.Enabled ? GetToolAvailabilityError() : null;
        return new DatabaseBackupStatusDto(
            _options.Enabled,
            _options.AutomaticEnabled,
            _options.IntervalHours,
            _options.RetentionCount,
            string.Empty,
            OperationLock.CurrentCount == 0,
            lastSuccessful,
            toolError ?? GetStorageCapacityWarning() ?? _lastError,
            backups,
            isStale,
            freshnessThresholdHours,
            _storageConfiguration?.Mode == StorageMode.AsyncMirror
                ? "Локальное и удалённое хранилища"
                : "Локальное хранилище");
    }

    public Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latest = EnumerateBackups(int.MaxValue, verifyChecksum: false)
            .FirstOrDefault(backup => backup.Kind == "automatic")
            ?.CreatedAtUtc;
        return Task.FromResult(latest);
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

        if (_storageConfiguration?.Mode == StorageMode.AsyncMirror &&
            GetLocalBackupBytes() >= _storageConfiguration.Replication.MaximumPendingBytes)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_storage_capacity",
                "Локальная очередь резервных копий заполнена. Проверьте удалённое хранилище и освободите место безопасным способом.");
        }

        if (kind == DatabaseBackupKind.Manual)
        {
            reason = reason?.Trim() ?? string.Empty;

            if (ActionCommentRequirementContext.IsRequired && string.IsNullOrWhiteSpace(reason))
            {
                return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                    "database_backup_reason_required",
                    "Укажите причину создания резервной копии.");
            }

            if (reason.Length is > 0 and < 3 or > 500)
            {
                return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                    "database_backup_reason_invalid",
                    "Комментарий не должен превышать 500 символов.");
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

            var backupId = Guid.NewGuid();
            var hash = await ComputeSha256Async(temporaryPath, cancellationToken);
            var localProvider = GetLocalProvider();
            var storageWriteRequest = new StorageWriteRequest(
                backupId,
                fileName,
                1,
                temporaryFile.Length,
                hash,
                new Dictionary<string, string>
                {
                    ["backup-kind"] = kindName,
                    ["manifest-schema"] = ManifestSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            if (localProvider is ILocalFileStorageProvider localFileProvider)
            {
                await localFileProvider.CommitVerifiedFileAsync(storageWriteRequest, temporaryPath, cancellationToken);
            }
            else
            {
                await using var source = new FileStream(
                    temporaryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await localProvider.WriteAsync(storageWriteRequest, source, cancellationToken);
                File.Delete(temporaryPath);
            }
            temporaryPath = null;
            var file = new FileInfo(finalPath);
            var (policyId, policyRevision) = GetDatabaseBackupPolicy();
            var manifest = await CreateManifestAsync(
                file,
                backupId,
                generation: 1,
                hash,
                kindName,
                now,
                policyId,
                policyRevision,
                cancellationToken);
            await RegisterCatalogAsync(file, manifest, cancellationToken);
            var dto = ToDto(file, manifest, verifyChecksum: true, manifestAlreadyVerified: true);
            if (_storageConfiguration?.Mode == StorageMode.AsyncMirror)
            {
                dto = dto with { ProtectionState = "protection_pending" };
            }
            _lastSuccessfulBackupAtUtc = now;
            _lastError = null;
            await DeleteExpiredBackupsAsync(cancellationToken);

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
                        ["sizeBytes"] = file.Length,
                        ["sha256"] = manifest.Sha256
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

    public async Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backup = await FindManagedBackupAsync(fileName, requireVerifiedIntegrity: true, cancellationToken);
        if (!backup.Succeeded || backup.Value is null)
        {
            return DatabaseBackupResult<DatabaseBackupDownloadDto>.Failure(
                backup.ErrorCode!,
                backup.ErrorMessage!);
        }

        Stream? stream = null;
        try
        {
            stream = _storageConfiguration?.Mode == StorageMode.AsyncMirror && storageReadRouter is not null
                ? (await storageReadRouter.OpenByLogicalKeyAsync(
                    _storageConfiguration.TenantId,
                    StorageDataClass.DatabaseBackup,
                    backup.Value.FileName,
                    cancellationToken)).Content
                : await GetLocalProvider().OpenReadAsync(backup.Value.FileName, cancellationToken);

            auditEventWriter.Add(new AuditEventWriteRequest(
                actorUserId,
                "database.backup_downloaded",
                "database_backup",
                backup.Value.FileName,
                Summary: "Администратор скачал резервную копию базы данных.",
                Section: "settings",
                ActionKind: "export",
                EntityDisplayName: backup.Value.FileName,
                Metadata: new Dictionary<string, object?>
                {
                    ["kind"] = backup.Value.Kind,
                    ["sizeBytes"] = backup.Value.SizeBytes
                }));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var result = DatabaseBackupResult<DatabaseBackupDownloadDto>.Success(
                new DatabaseBackupDownloadDto(backup.Value.FileName, backup.Value.SizeBytes, stream));
            stream = null;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Database backup download failed. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return DatabaseBackupResult<DatabaseBackupDownloadDto>.Failure(
                "database_backup_download_failed",
                "Не удалось подготовить резервную копию к скачиванию.");
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public async Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(
        string fileName,
        string? reason,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;

        if (ActionCommentRequirementContext.IsRequired && string.IsNullOrWhiteSpace(reason))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_delete_reason_required",
                "Укажите причину удаления резервной копии.");
        }

        if (reason.Length is > 0 and < 3 or > 500)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_delete_reason_invalid",
                "Комментарий не должен превышать 500 символов.");
        }

        var backup = await FindManagedBackupAsync(fileName, requireVerifiedIntegrity: false, cancellationToken);
        if (!backup.Succeeded || backup.Value is null)
        {
            return backup;
        }

        if (!await OperationLock.WaitAsync(0, cancellationToken))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_in_progress",
                "Сейчас создается резервная копия. Дождитесь завершения операции.");
        }

        try
        {
            if (_storageConfiguration?.Mode == StorageMode.AsyncMirror)
            {
                if (storageCatalog is null || await storageCatalog.TombstoneAndScheduleDeleteAsync(
                    _storageConfiguration.TenantId,
                    StorageDataClass.DatabaseBackup,
                    backup.Value.FileName,
                    _storageConfiguration.Replication.MaximumAttempts,
                    timeProvider.GetUtcNow(),
                    cancellationToken) is null)
                {
                    return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                        "database_backup_not_found",
                        "Резервная копия не найдена или уже удалена.");
                }
            }
            else
            {
                await GetLocalProvider().DeleteAsync(backup.Value.FileName, cancellationToken);
                await GetLocalProvider().DeleteAsync(GetManifestKey(backup.Value.FileName), cancellationToken);
            }
            auditEventWriter.Add(new AuditEventWriteRequest(
                actorUserId,
                "database.backup_deleted",
                "database_backup",
                backup.Value.FileName,
                Summary: "Администратор удалил резервную копию базы данных.",
                Section: "settings",
                ActionKind: "delete",
                EntityDisplayName: backup.Value.FileName,
                Reason: reason,
                Metadata: new Dictionary<string, object?>
                {
                    ["kind"] = backup.Value.Kind,
                    ["sizeBytes"] = backup.Value.SizeBytes
                }));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Database backup {BackupFileName} was deleted.", backup.Value.FileName);
            return DatabaseBackupResult<DatabaseBackupFileDto>.Success(
                _storageConfiguration?.Mode == StorageMode.AsyncMirror
                    ? backup.Value with { ProtectionState = "deleting" }
                    : backup.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Database backup deletion failed. ExceptionType={ExceptionType}; Diagnostic={Diagnostic}",
                exception.GetType().Name,
                DiagnosticLogSanitizer.SanitizeException(exception));
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_delete_failed",
                "Не удалось удалить резервную копию.");
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<DatabaseBackupResult<DatabaseBackupFileDto>> RetryProtectionAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var backup = await FindManagedBackupAsync(fileName, false, cancellationToken);
        if (!backup.Succeeded || backup.Value is null)
        {
            return backup;
        }
        if (_storageConfiguration?.Mode != StorageMode.AsyncMirror || storageCatalog is null)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_protection_not_configured",
                "Удалённая защита резервных копий не настроена.");
        }
        var storageObject = await storageCatalog.FindByLogicalKeyAsync(
            _storageConfiguration.TenantId,
            StorageDataClass.DatabaseBackup,
            backup.Value.FileName,
            cancellationToken);
        if (storageObject is null)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure("database_backup_not_found", "Резервная копия не найдена или уже удалена.");
        }
        var policy = _storageConfiguration.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var scheduled = await storageCatalog.RetryProtectionAsync(
            storageObject.Id,
            policy.RequiredIndependentCopies,
            policy.DesiredCopies,
            _storageConfiguration.Replication.MaximumAttempts,
            timeProvider.GetUtcNow(),
            cancellationToken);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "database.backup_protection_retried",
            "database_backup",
            backup.Value.FileName,
            Summary: "Администратор повторно запустил защиту резервной копии.",
            Section: "settings",
            ActionKind: "update",
            EntityDisplayName: backup.Value.FileName,
            Metadata: new Dictionary<string, object?> { ["scheduled"] = scheduled }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DatabaseBackupResult<DatabaseBackupFileDto>.Success(
            backup.Value with { ProtectionState = scheduled ? "protection_pending" : backup.Value.ProtectionState });
    }

    public async Task<DatabaseBackupResult<DatabaseBackupFileDto>> VerifyProtectionAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var backup = await FindManagedBackupAsync(fileName, false, cancellationToken);
        if (!backup.Succeeded || backup.Value is null)
        {
            return backup;
        }
        if (_storageConfiguration?.Mode != StorageMode.AsyncMirror || storageReconciliationRunner is null)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_protection_not_configured",
                "Удалённая защита резервных копий не настроена.");
        }
        await storageReconciliationRunner.ReconcileObjectAsync(backup.Value.FileName, cancellationToken);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "database.backup_protection_verified",
            "database_backup",
            backup.Value.FileName,
            Summary: "Администратор проверил доступность копий резервной копии.",
            Section: "settings",
            ActionKind: "view",
            EntityDisplayName: backup.Value.FileName));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var refreshed = await FindManagedBackupAsync(fileName, false, cancellationToken);
        return refreshed.Succeeded && refreshed.Value is not null
            ? DatabaseBackupResult<DatabaseBackupFileDto>.Success((await ApplyCatalogProtectionAsync([refreshed.Value], cancellationToken))[0])
            : refreshed;
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

    private IReadOnlyList<DatabaseBackupFileDto> EnumerateBackups(int limit, bool verifyChecksum = true)
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
            .Select(file => ToDto(file, TryReadManifest(file), verifyChecksum))
            .ToArray();
    }

    private DatabaseBackupResult<DatabaseBackupFileDto> FindManagedBackup(
        string fileName,
        bool requireVerifiedIntegrity = false)
    {
        fileName = fileName?.Trim() ?? string.Empty;
        if (!ManagedBackupName().IsMatch(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_file_invalid",
                "Указано недопустимое имя резервной копии.");
        }

        var backup = EnumerateBackups(int.MaxValue, verifyChecksum: requireVerifiedIntegrity)
            .FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
        if (backup is null)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_not_found",
                "Резервная копия не найдена или уже удалена.");
        }

        if (requireVerifiedIntegrity && backup.ProtectionState == "failed")
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_integrity_failed",
                "Контрольная сумма резервной копии не совпадает с манифестом. Скачивание заблокировано.");
        }

        return DatabaseBackupResult<DatabaseBackupFileDto>.Success(backup);
    }

    private async Task<DatabaseBackupResult<DatabaseBackupFileDto>> FindManagedBackupAsync(
        string fileName,
        bool requireVerifiedIntegrity,
        CancellationToken cancellationToken)
    {
        fileName = fileName?.Trim() ?? string.Empty;
        if (!ManagedBackupName().IsMatch(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure("database_backup_file_invalid", "Указано недопустимое имя резервной копии.");
        }
        if (_storageConfiguration?.Mode != StorageMode.AsyncMirror || storageCatalog is null)
        {
            return FindManagedBackup(fileName, requireVerifiedIntegrity);
        }
        var storageObject = await storageCatalog.FindByLogicalKeyAsync(
            _storageConfiguration.TenantId,
            StorageDataClass.DatabaseBackup,
            fileName,
            cancellationToken);
        if (storageObject is null || storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure("database_backup_not_found", "Резервная копия не найдена или уже удалена.");
        }
        if (requireVerifiedIntegrity && storageObject.Replicas.All(item => item.State != StorageReplicaState.Available))
        {
            return DatabaseBackupResult<DatabaseBackupFileDto>.Failure("database_backup_integrity_failed", "Нет доступной проверенной копии резервной копии.");
        }
        var protectionState = storageObject.State switch
        {
            StorageObjectState.Protected => "protected",
            StorageObjectState.ProtectionDegraded => "protection_degraded",
            StorageObjectState.Failed => "failed",
            _ => "protection_pending"
        };
        return DatabaseBackupResult<DatabaseBackupFileDto>.Success(new DatabaseBackupFileDto(
            fileName,
            storageObject.SizeBytes,
            storageObject.CreatedAtUtc,
            ParseKind(fileName),
            storageObject.Sha256,
            protectionState,
            storageObject.Replicas.Where(item => item.State == StorageReplicaState.Available).Max(item => item.LastVerifiedAtUtc)));
    }

    private async Task<IReadOnlyList<DatabaseBackupFileDto>> MergeCatalogBackupsAsync(
        IReadOnlyList<DatabaseBackupFileDto> localBackups,
        CancellationToken cancellationToken)
    {
        if (_storageConfiguration?.Mode != StorageMode.AsyncMirror || storageCatalog is null)
        {
            return localBackups;
        }
        var byName = localBackups.ToDictionary(item => item.FileName, StringComparer.Ordinal);
        var manifest = await storageCatalog.ExportManifestAsync(StorageDataClass.DatabaseBackup, 100, cancellationToken);
        foreach (var item in manifest.Where(item => item.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted)))
        {
            byName.TryAdd(item.LogicalKey, new DatabaseBackupFileDto(
                item.LogicalKey,
                item.SizeBytes,
                item.CreatedAtUtc,
                ParseKind(item.LogicalKey),
                item.Sha256));
        }
        return byName.Values.OrderByDescending(item => item.CreatedAtUtc).Take(20).ToArray();
    }

    private async Task DeleteExpiredBackupsAsync(CancellationToken cancellationToken)
    {
        if (_storageConfiguration?.Mode == StorageMode.AsyncMirror)
        {
            if (storageCatalog is null)
            {
                return;
            }
            foreach (var backup in EnumerateBackups(int.MaxValue, verifyChecksum: false).Skip(_options.RetentionCount))
            {
                var storageObject = await storageCatalog.FindByLogicalKeyAsync(
                    _storageConfiguration.TenantId,
                    StorageDataClass.DatabaseBackup,
                    backup.FileName,
                    cancellationToken);
                if (storageObject?.State == StorageObjectState.Protected)
                {
                    await storageCatalog.TombstoneAndScheduleDeleteAsync(
                        _storageConfiguration.TenantId,
                        StorageDataClass.DatabaseBackup,
                        backup.FileName,
                        _storageConfiguration.Replication.MaximumAttempts,
                        timeProvider.GetUtcNow(),
                        cancellationToken);
                }
            }
            return;
        }
        var expired = EnumerateBackups(int.MaxValue, verifyChecksum: false).Skip(_options.RetentionCount);
        foreach (var backup in expired)
        {
            await GetLocalProvider().DeleteAsync(backup.FileName, cancellationToken);
            await GetLocalProvider().DeleteAsync(GetManifestKey(backup.FileName), cancellationToken);
        }
    }

    private async Task<DatabaseBackupManifest> CreateManifestAsync(
        FileInfo file,
        Guid backupId,
        long generation,
        string sha256,
        string kind,
        DateTimeOffset createdAtUtc,
        string policyId,
        int policyRevision,
        CancellationToken cancellationToken)
    {
        var manifest = new DatabaseBackupManifest(
            ManifestSchemaVersion,
            backupId,
            generation,
            file.Name,
            file.Length,
            sha256,
            kind,
            createdAtUtc,
            typeof(PostgresDatabaseBackupService).Assembly.GetName().Version?.ToString() ?? "unknown",
            policyId,
            policyRevision);

        await using var content = new MemoryStream();
        await JsonSerializer.SerializeAsync(content, manifest, ManifestJsonOptions, cancellationToken);
        var bytes = content.ToArray();
        content.Position = 0;
        await GetLocalProvider().WriteAsync(
            new StorageWriteRequest(
                backupId,
                GetManifestKey(file.Name),
                generation,
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                new Dictionary<string, string> { ["content"] = "database-backup-manifest" }),
            content,
            cancellationToken);
        return manifest;
    }

    private DatabaseBackupFileDto ToDto(
        FileInfo file,
        DatabaseBackupManifest? manifest,
        bool verifyChecksum,
        bool manifestAlreadyVerified = false)
    {
        var createdAtUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        var manifestMatches = manifest is not null &&
            string.Equals(manifest.FileName, file.Name, StringComparison.Ordinal) &&
            manifest.SizeBytes == file.Length &&
            manifest.SchemaVersion == ManifestSchemaVersion &&
            manifest.Generation > 0 &&
            manifest.PolicyRevision > 0 &&
            StorageObjectKey.IsValidId(manifest.PolicyId) &&
            IsSha256(manifest.Sha256);
        var checksumMatches = manifestMatches &&
            (manifestAlreadyVerified || !verifyChecksum || HasMatchingChecksum(file, manifest!.Sha256));
        var protectionState = manifest switch
        {
            null => "manifest_missing",
            _ when !manifestMatches => "failed",
            _ when verifyChecksum && !checksumMatches => "failed",
            _ when verifyChecksum || manifestAlreadyVerified => "local_verified",
            _ => "protection_pending"
        };
        return new DatabaseBackupFileDto(
            file.Name,
            file.Length,
            manifestMatches ? manifest!.CreatedAtUtc : createdAtUtc,
            manifestMatches ? manifest!.Kind : ParseKind(file.Name),
            manifestMatches ? manifest!.Sha256 : null,
            protectionState,
            checksumMatches && (verifyChecksum || manifestAlreadyVerified)
                ? (manifestAlreadyVerified ? manifest!.CreatedAtUtc : timeProvider.GetUtcNow())
                : null);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool HasMatchingChecksum(FileInfo file, string expectedSha256)
    {
        try
        {
            using var content = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            var actual = SHA256.HashData(content);
            var expected = Convert.FromHexString(expectedSha256);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static DatabaseBackupManifest? TryReadManifest(FileInfo file)
    {
        var path = GetManifestPath(file.FullName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DatabaseBackupManifest>(File.ReadAllText(path), ManifestJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetManifestPath(string backupPath) => backupPath + ".manifest.json";
    private static string GetManifestKey(string backupKey) => backupKey + ".manifest.json";

    private string? GetToolAvailabilityError()
    {
        return toolLocator.Resolve(_options.PgDumpPath) is not null && toolLocator.Resolve(_options.PgRestorePath) is not null
            ? null
            : "Не найдены утилиты PostgreSQL pg_dump и pg_restore. Установите клиентские инструменты PostgreSQL или задайте POSTGRESQL_BIN.";
    }

    private string? GetStorageCapacityWarning()
    {
        if (_storageConfiguration?.Mode != StorageMode.AsyncMirror)
        {
            return null;
        }
        var used = GetLocalBackupBytes();
        return used >= _storageConfiguration.Replication.MaximumPendingBytes * 8 / 10
            ? "Локальная очередь резервных копий близка к установленному пределу. Проверьте состояние удалённой защиты."
            : null;
    }

    private long GetLocalBackupBytes()
    {
        if (!Directory.Exists(_directory))
        {
            return 0;
        }
        try
        {
            return Directory.EnumerateFiles(_directory, "garagebalance_*.pgdump", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => ManagedBackupName().IsMatch(file.Name))
                .Sum(file => file.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return _storageConfiguration?.Replication.MaximumPendingBytes ?? long.MaxValue;
        }
    }

    private IStorageProvider GetLocalProvider()
    {
        var localDestinationId = _storageConfiguration?.Destinations
            .FirstOrDefault(destination => destination.Type == StorageProviderType.LocalFileSystem)?.Id
            ?? "local-hot";
        return storageProviderRegistry?.GetRequired(localDestinationId)
            ?? new LocalFileStorageProvider(localDestinationId, _directory);
    }

    private (string PolicyId, int Revision) GetDatabaseBackupPolicy()
    {
        var policy = _storageConfiguration?.Policies
            .SingleOrDefault(item => item.DataClass == StorageDataClass.DatabaseBackup);
        return policy is null ? ("database-backups", 1) : (policy.Id, policy.Revision);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var content = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));
    }

    private async Task RegisterCatalogAsync(
        FileInfo file,
        DatabaseBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        if (storageCatalog is null)
        {
            return;
        }

        var local = _storageConfiguration?.Destinations
            .FirstOrDefault(destination => destination.Type == StorageProviderType.LocalFileSystem);
        var localDestinationId = local?.Id ?? "local-hot";
        var localFailureDomain = local?.FailureDomain ?? "local-host";
        var policy = _storageConfiguration?.Policies
            .SingleOrDefault(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var pool = policy is null
            ? null
            : _storageConfiguration?.Pools.Single(item => item.Id == policy.PoolId);
        var targets = pool?.DestinationIds
            .Where(destinationId => !string.Equals(destinationId, localDestinationId, StringComparison.Ordinal))
            .Select(destinationId => _storageConfiguration!.Destinations.Single(destination => destination.Id == destinationId))
            .Select(destination => new StorageReplicationTarget(destination.Id, destination.FailureDomain))
            .ToArray() ?? [];
        await storageCatalog.RegisterCommittedObjectAsync(
            new RegisterCommittedStorageObjectRequest(
                manifest.BackupId,
                _storageConfiguration?.TenantId ?? "garagebalance",
                StorageDataClass.DatabaseBackup,
                file.Name,
                manifest.PolicyId,
                manifest.PolicyRevision,
                manifest.Generation,
                manifest.SizeBytes,
                manifest.Sha256,
                manifest.FileName,
                "application/vnd.postgresql.custom-dump",
                localDestinationId,
                localFailureDomain,
                file.Name,
                targets,
                _storageConfiguration?.Replication.MaximumAttempts ?? 12,
                manifest.CreatedAtUtc),
            cancellationToken);
    }

    private async Task ReconcileCatalogAsync(
        IReadOnlyList<DatabaseBackupFileDto> backups,
        CancellationToken cancellationToken)
    {
        if (storageCatalog is null)
        {
            return;
        }

        foreach (var backup in backups.Where(item => item.ProtectionState == "local_verified"))
        {
            var file = new FileInfo(Path.Combine(_directory, backup.FileName));
            var manifest = TryReadManifest(file);
            if (manifest is not null && manifest.SchemaVersion == ManifestSchemaVersion)
            {
                await RegisterCatalogAsync(file, manifest, cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<DatabaseBackupFileDto>> ApplyCatalogProtectionAsync(
        IReadOnlyList<DatabaseBackupFileDto> backups,
        CancellationToken cancellationToken)
    {
        if (storageCatalog is null || _storageConfiguration?.Mode != StorageMode.AsyncMirror)
        {
            return backups;
        }

        var result = new List<DatabaseBackupFileDto>(backups.Count);
        foreach (var backup in backups)
        {
            var storageObject = await storageCatalog.FindByLogicalKeyAsync(
                _storageConfiguration.TenantId,
                StorageDataClass.DatabaseBackup,
                backup.FileName,
                cancellationToken);
            if (storageObject is null)
            {
                result.Add(backup with { ProtectionState = "protection_pending" });
                continue;
            }
            var protectionState = storageObject.State switch
            {
                StorageObjectState.Protected => "protected",
                StorageObjectState.ProtectionDegraded => "protection_degraded",
                StorageObjectState.Failed => "failed",
                StorageObjectState.Deleting => "deleting",
                StorageObjectState.Deleted => "deleted",
                _ => "protection_pending"
            };
            var lastVerified = storageObject.Replicas
                .Where(replica => replica.State == StorageReplicaState.Available)
                .Select(replica => replica.LastVerifiedAtUtc)
                .Max();
            result.Add(backup with
            {
                ProtectionState = protectionState,
                LastVerifiedAtUtc = lastVerified ?? backup.LastVerifiedAtUtc
            });
        }
        return result;
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
