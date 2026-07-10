using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Import;

public sealed class ImportService(
    GarageBalanceDbContext dbContext,
    IAccessImportReader accessImportReader,
    IAuditEventWriter auditEventWriter) : IImportService
{
    private const long MaxDryRunFileSizeBytes = 512L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<AccessImportReaderStatusDto> GetAccessImportReaderStatusAsync(CancellationToken cancellationToken)
    {
        return accessImportReader.GetStatusAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(AccessImportRunListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit, 50, 200);
        var query = dbContext.AccessImportRuns.AsNoTracking();

        List<AccessImportRun> runs;
        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            runs = (await query.ToListAsync(cancellationToken))
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .Take(limit)
                .ToList();
        }
        else
        {
            runs = await query
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        return runs.Select(ToDto).ToList();
    }

    public async Task<ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLogEntriesAsync(Guid runId, AccessImportRunLogListRequest request, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.AccessImportRuns.AsNoTracking().AnyAsync(run => run.Id == runId, cancellationToken);
        if (!runExists)
        {
            return ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        var limit = NormalizeLimit(request.Limit, 100, 500);
        var query = dbContext.AccessImportRunLogEntries
            .AsNoTracking()
            .Where(entry => entry.AccessImportRunId == runId);

        List<AccessImportRunLogEntry> entries;
        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            entries = (await query.ToListAsync(cancellationToken))
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.Id)
                .Take(limit)
                .ToList();
        }
        else
        {
            entries = await query
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        return ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>.Success(entries.Select(ToLogEntryDto).ToList());
    }

    public async Task<ImportResult<IReadOnlyList<AccessImportCreatedRecordDto>>> GetAccessImportCreatedRecordsAsync(Guid runId, AccessImportCreatedRecordListRequest request, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.AccessImportRuns.AsNoTracking().AnyAsync(run => run.Id == runId, cancellationToken);
        if (!runExists)
        {
            return ImportResult<IReadOnlyList<AccessImportCreatedRecordDto>>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        var query = dbContext.AccessImportCreatedRecords
            .AsNoTracking()
            .Where(record => record.AccessImportRunId == runId);

        List<AccessImportCreatedRecord> records;
        if (dbContext.Database.IsNpgsql())
        {
            records = await query
                .OrderByDescending(record => record.CreatedAtUtc)
                .ThenBy(record => record.TargetEntityType)
                .ThenBy(record => record.TargetEntityId)
                .Take(request.Limit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            records = await query.ToListAsync(cancellationToken);
            records = records
                .OrderByDescending(record => record.CreatedAtUtc)
                .ThenBy(record => record.TargetEntityType, StringComparer.Ordinal)
                .ThenBy(record => record.TargetEntityId, StringComparer.Ordinal)
                .Take(request.Limit)
                .ToList();
        }

        return ImportResult<IReadOnlyList<AccessImportCreatedRecordDto>>.Success(records.Select(ToCreatedRecordDto).ToList());
    }

    public async Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var run = await dbContext.AccessImportRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return ImportResult<ImportReportFileDto>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        var dto = ToDto(run);
        var report = new
        {
            dto.Id,
            dto.Mode,
            dto.Status,
            dto.OriginalFileName,
            dto.FileExtension,
            dto.FileSizeBytes,
            dto.ContentSha256,
            dto.StartedAtUtc,
            dto.FinishedAtUtc,
            dto.TotalChecks,
            dto.PassedChecks,
            dto.WarningCount,
            dto.ErrorCount,
            dto.Summary,
            dto.Checks
        };
        var content = JsonSerializer.SerializeToUtf8Bytes(report, ReportJsonOptions);
        var safeFileName = Path.GetFileNameWithoutExtension(dto.OriginalFileName)
            .Replace(' ', '-')
            .ToLowerInvariant();
        var timestamp = dto.StartedAtUtc.ToString("yyyyMMdd-HHmmss");
        var fileName = $"garagebalance-access-dry-run-{safeFileName}-{timestamp}.json";

        auditEventWriter.Add(new AuditEventWriteRequest(
            ActorUserId: actorUserId,
            Action: "import.access_dry_run_report_exported",
            EntityType: "access_import_run",
            EntityId: run.Id.ToString(),
            Summary: $"Экспортирован отчет dry-run импорта Access: {dto.OriginalFileName}.",
            ActionKind: "export",
            EntityDisplayName: dto.OriginalFileName,
            RelatedDocumentId: run.Id.ToString(),
            RelatedDocumentNumber: dto.OriginalFileName,
            Metadata: new Dictionary<string, object?>
            {
                ["mode"] = run.Mode,
                ["status"] = run.Status,
                ["originalFileName"] = dto.OriginalFileName,
                ["fileExtension"] = dto.FileExtension,
                ["reportFileName"] = fileName,
                ["totalChecks"] = dto.TotalChecks,
                ["warningCount"] = dto.WarningCount,
                ["errorCount"] = dto.ErrorCount
            }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportResult<ImportReportFileDto>.Success(new ImportReportFileDto(
            fileName,
            "application/json; charset=utf-8",
            content));
    }

    public async Task<ImportResult<AccessImportRunDto>> RequestAccessImportRollbackAsync(
        Guid runId,
        AccessImportRollbackRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ImportResult<AccessImportRunDto>.Failure("import_rollback_reason_required", "Укажите причину rollback импорта.");
        }

        if (reason.Length > 1000)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_rollback_reason_too_long", "Причина rollback импорта превышает допустимую длину.");
        }

        var run = await dbContext.AccessImportRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        if (run.Status == "rollback_requested")
        {
            return ImportResult<AccessImportRunDto>.Success(ToDto(run));
        }

        if (run.Status == "import_requested")
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_import_requested", "Нельзя запрашивать rollback после заявки на фактический импорт.");
        }

        run.Status = "rollback_requested";
        run.Summary = "Rollback запрошен: фактических данных для отката нет, пока запуск импорта находится в режиме dry-run.";

        AddRunLog(run, "warning", "rollback_requested", "Запрошен rollback импорта Access. Фактический откат данных не выполнялся: запуск был dry-run.", new
        {
            reason,
            mode = run.Mode,
            status = run.Status
        });
        var auditMetadata = await BuildAccessImportRunAuditMetadataAsync(run, cancellationToken, new Dictionary<string, object?>
        {
            ["rollbackExecuted"] = false,
            ["rollbackState"] = "dry_run_no_created_records"
        });
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "import.rollback_requested",
            "access_import_run",
            run.Id.ToString(),
            Summary: $"Запрошен rollback импорта Access: {run.OriginalFileName}.",
            ActionKind: "cancel",
            EntityDisplayName: run.OriginalFileName,
            Reason: reason,
            RelatedDocumentId: run.Id.ToString(),
            RelatedDocumentNumber: run.OriginalFileName,
            Metadata: auditMetadata));

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportRunDto>.Success(ToDto(run));
    }

    public async Task<ImportResult<AccessImportRunDto>> RequestAccessImportApplyAsync(
        Guid runId,
        AccessImportApplyRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_reason_required", "Укажите причину фактического импорта.");
        }

        if (reason.Length > 1000)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_reason_too_long", "Причина фактического импорта превышает допустимую длину.");
        }

        if (!request.BackupConfirmed)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_backup_confirmation_required", "Подтвердите, что перед фактическим импортом создан backup PostgreSQL.");
        }

        var run = await dbContext.AccessImportRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        if (run.Status == "import_requested")
        {
            return ImportResult<AccessImportRunDto>.Success(ToDto(run));
        }

        if (run.Status == "blocked")
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_blocked", "Нельзя запрашивать фактический импорт, пока dry-run завершен с ошибками.");
        }

        if (run.Status == "rollback_requested")
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_rollback_requested", "Нельзя запрашивать фактический импорт после rollback-заявки по этому запуску.");
        }

        if (run.Status is not "completed" and not "import_request_cancelled")
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_not_ready", "Фактический импорт можно запросить только после успешного dry-run.");
        }

        run.Status = "import_requested";
        run.Summary = "Фактический импорт запрошен: перенос будет выполнен после подключения reader Access и проверки backup.";

        AddRunLog(run, "warning", "import_requested", "Запрошен фактический импорт Access. Данные пока не переносились: reader Access еще не подключен.", new
        {
            reason,
            backupConfirmed = request.BackupConfirmed,
            mode = run.Mode,
            status = run.Status
        });
        var auditMetadata = await BuildAccessImportRunAuditMetadataAsync(run, cancellationToken, new Dictionary<string, object?>
        {
            ["backupConfirmed"] = request.BackupConfirmed,
            ["importExecuted"] = false,
            ["importState"] = "pending_access_reader"
        });
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "import.apply_requested",
            "access_import_run",
            run.Id.ToString(),
            Summary: $"Запрошен фактический импорт Access: {run.OriginalFileName}.",
            ActionKind: "import",
            EntityDisplayName: run.OriginalFileName,
            Reason: reason,
            RelatedDocumentId: run.Id.ToString(),
            RelatedDocumentNumber: run.OriginalFileName,
            Metadata: auditMetadata));

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportRunDto>.Success(ToDto(run));
    }

    public async Task<ImportResult<AccessImportRunDto>> CancelAccessImportApplyRequestAsync(
        Guid runId,
        AccessImportApplyCancelRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_cancel_reason_required", "Укажите причину отмены заявки на импорт.");
        }

        if (reason.Length > 1000)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_cancel_reason_too_long", "Причина отмены заявки на импорт превышает допустимую длину.");
        }

        var run = await dbContext.AccessImportRuns.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null)
        {
            return ImportResult<AccessImportRunDto>.Failure("import_run_not_found", "Запуск dry-run импорта не найден.");
        }

        if (run.Status == "import_request_cancelled")
        {
            return ImportResult<AccessImportRunDto>.Success(ToDto(run));
        }

        if (run.Status != "import_requested")
        {
            return ImportResult<AccessImportRunDto>.Failure("import_apply_request_not_active", "Нет активной заявки на фактический импорт для отмены.");
        }

        run.Status = "import_request_cancelled";
        run.Summary = "Заявка на фактический импорт отменена. Dry-run остается доступным для повторной заявки или rollback-заявки.";

        AddRunLog(run, "warning", "import_request_cancelled", "Отменена заявка на фактический импорт Access. Данные не переносились.", new
        {
            reason,
            mode = run.Mode,
            status = run.Status
        });
        var auditMetadata = await BuildAccessImportRunAuditMetadataAsync(run, cancellationToken, new Dictionary<string, object?>
        {
            ["importExecuted"] = false,
            ["importState"] = "apply_request_cancelled"
        });
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "import.apply_request_cancelled",
            "access_import_run",
            run.Id.ToString(),
            Summary: $"Отменена заявка на фактический импорт Access: {run.OriginalFileName}.",
            ActionKind: "cancel",
            EntityDisplayName: run.OriginalFileName,
            Reason: reason,
            RelatedDocumentId: run.Id.ToString(),
            RelatedDocumentNumber: run.OriginalFileName,
            Metadata: auditMetadata));

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportRunDto>.Success(ToDto(run));
    }

    public async Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return ImportResult<AccessImportRunDto>.Failure("file_name_required", "Имя файла Access обязательно.");
        }

        var fileName = Path.GetFileName(request.FileName.Trim());
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".accdb" and not ".mdb")
        {
            return ImportResult<AccessImportRunDto>.Failure("access_extension_required", "Для dry-run импорта нужен файл .accdb или .mdb.");
        }

        await using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return ImportResult<AccessImportRunDto>.Failure("file_empty", "Файл Access пустой.");
        }

        if (buffer.Length > MaxDryRunFileSizeBytes)
        {
            return ImportResult<AccessImportRunDto>.Failure("file_too_large", "Файл Access слишком большой для первичной проверки.");
        }

        var bytes = buffer.ToArray();
        var contentSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sameContentQuery = dbContext.AccessImportRuns
            .AsNoTracking()
            .Where(item => item.ContentSha256 == contentSha256);
        var previousRunWithSameContent = string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal)
            ? (await sameContentQuery.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.StartedAtUtc)
                .ThenByDescending(item => item.Id)
                .Select(item => new { item.Id, item.OriginalFileName, item.StartedAtUtc })
                .FirstOrDefault()
            : await sameContentQuery
                .OrderByDescending(item => item.StartedAtUtc)
                .ThenByDescending(item => item.Id)
                .Select(item => new { item.Id, item.OriginalFileName, item.StartedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
        var checks = BuildChecks(extension, bytes);
        if (previousRunWithSameContent is not null)
        {
            checks.Add(new AccessImportCheckDto(
                "duplicate_content",
                "Повторная проверка файла",
                "warning",
                $"Файл уже проверялся в запуске {previousRunWithSameContent.OriginalFileName} от {previousRunWithSameContent.StartedAtUtc:dd.MM.yyyy HH:mm}. Перед фактическим импортом убедитесь, что это осознанная повторная загрузка."));
        }

        var errors = checks.Count(check => check.Status == "error");
        var warnings = checks.Count(check => check.Status == "warning");
        var passed = checks.Count(check => check.Status == "passed");
        var status = errors > 0 ? "blocked" : "completed";
        var summary = errors > 0
            ? "Dry-run остановлен: есть ошибки, которые нужно исправить до импорта."
            : warnings > 0
                ? "Dry-run завершен с предупреждениями: можно продолжать подготовку, но нужен драйвер/конвертация Access."
                : "Dry-run завершен: файл прошел первичные проверки.";

        var run = new AccessImportRun
        {
            Mode = "dry_run",
            Status = status,
            OriginalFileName = fileName,
            FileExtension = extension,
            FileSizeBytes = buffer.Length,
            ContentSha256 = contentSha256,
            ActorUserId = actorUserId,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            TotalChecks = checks.Count,
            PassedChecks = passed,
            WarningCount = warnings,
            ErrorCount = errors,
            Summary = summary,
            ReportJson = JsonSerializer.Serialize(checks, JsonOptions)
        };

        dbContext.AccessImportRuns.Add(run);
        AddRunLog(run, "info", "file_received", $"Файл {fileName} получен для dry-run проверки.", new
        {
            fileName,
            extension,
            fileSizeBytes = buffer.Length
        });
        AddRunLog(run, "info", "hash_calculated", "SHA-256 файла рассчитан для сверки и повторяемости dry-run.", new
        {
            contentSha256 = run.ContentSha256
        });
        if (previousRunWithSameContent is not null)
        {
            AddRunLog(run, "warning", "duplicate_content_detected", "Найден предыдущий dry-run с тем же содержимым файла Access.", new
            {
                previousRunId = previousRunWithSameContent.Id,
                previousFileName = previousRunWithSameContent.OriginalFileName,
                previousStartedAtUtc = previousRunWithSameContent.StartedAtUtc
            });
        }

        foreach (var check in checks)
        {
            AddRunLog(run, check.Status == "error" ? "error" : check.Status == "warning" ? "warning" : "info", $"check_{check.Code}", check.Message, new
            {
                check.Code,
                check.Status
            });
        }
        AddRunLog(run, status == "blocked" ? "error" : warnings > 0 ? "warning" : "info", "dry_run_finished", summary, new
        {
            status,
            totalChecks = checks.Count,
            passed,
            warnings,
            errors
        });
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "import.access_dry_run",
            "access_import_run",
            run.Id.ToString(),
            Summary: $"Dry-run импорта Access: {fileName}, статус {status}, проверок {checks.Count}.",
            ActionKind: "import",
            EntityDisplayName: fileName,
            RelatedDocumentId: run.Id.ToString(),
            RelatedDocumentNumber: fileName,
            Metadata: new Dictionary<string, object?>
            {
                ["mode"] = run.Mode,
                ["status"] = status,
                ["originalFileName"] = fileName,
                ["fileExtension"] = extension,
                ["fileSizeBytes"] = buffer.Length,
                ["contentSha256"] = run.ContentSha256,
                ["duplicateContentDetected"] = previousRunWithSameContent is not null,
                ["duplicateContentRunId"] = previousRunWithSameContent?.Id,
                ["duplicateContentFileName"] = previousRunWithSameContent?.OriginalFileName,
                ["totalChecks"] = checks.Count,
                ["passedChecks"] = passed,
                ["warningCount"] = warnings,
                ["errorCount"] = errors
            }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportRunDto>.Success(ToDto(run));
    }

    private async Task<Dictionary<string, object?>> BuildAccessImportRunAuditMetadataAsync(
        AccessImportRun run,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["accessImportRunId"] = run.Id,
            ["mode"] = run.Mode,
            ["status"] = run.Status,
            ["originalFileName"] = run.OriginalFileName,
            ["fileExtension"] = run.FileExtension,
            ["contentSha256"] = run.ContentSha256
        };

        if (additionalMetadata is not null)
        {
            foreach (var (key, value) in additionalMetadata)
            {
                metadata[key] = value;
            }
        }

        var createdRecordsQuery = dbContext.AccessImportCreatedRecords
            .AsNoTracking()
            .Where(record => record.AccessImportRunId == run.Id);
        var createdRecordCount = await createdRecordsQuery.CountAsync(cancellationToken);
        var pendingRollbackRecordCount = await createdRecordsQuery
            .CountAsync(record => record.RollbackStatus == "created", cancellationToken);
        var sourceRowFingerprintCount = await createdRecordsQuery
            .Select(record => record.SourceRowHash)
            .Where(rowHash => rowHash != string.Empty)
            .Distinct()
            .CountAsync(cancellationToken);
        var targetEntityTypes = await createdRecordsQuery
            .Select(record => record.TargetEntityType)
            .Where(targetEntityType => targetEntityType != string.Empty)
            .Distinct()
            .OrderBy(targetEntityType => targetEntityType)
            .Take(10)
            .ToListAsync(cancellationToken);
        var sourceRowFingerprints = (await createdRecordsQuery
                .OrderBy(record => record.TargetEntityType)
                .ThenBy(record => record.TargetEntityId)
                .Select(record => record.SourceRowHash)
                .Where(rowHash => rowHash != string.Empty)
                .Take(20)
                .ToListAsync(cancellationToken))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();

        metadata["importCreatedRecordCount"] = createdRecordCount;
        metadata["importPendingRollbackRecordCount"] = pendingRollbackRecordCount;
        metadata["sourceRowFingerprintCount"] = sourceRowFingerprintCount;
        if (targetEntityTypes.Count > 0)
        {
            metadata["targetEntityTypes"] = string.Join(", ", targetEntityTypes);
        }

        if (sourceRowFingerprints.Count > 0)
        {
            metadata["sourceRowFingerprints"] = string.Join(", ", sourceRowFingerprints);
        }

        return metadata;
    }

    private void AddRunLog(AccessImportRun run, string level, string stepCode, string message, object details)
    {
        dbContext.AccessImportRunLogEntries.Add(new AccessImportRunLogEntry
        {
            AccessImportRunId = run.Id,
            Level = level,
            StepCode = stepCode,
            Message = message,
            DetailsJson = JsonSerializer.Serialize(details, JsonOptions)
        });
    }

    private static int NormalizeLimit(int limit, int defaultLimit, int maxLimit)
    {
        if (limit <= 0)
        {
            return defaultLimit;
        }

        return Math.Min(limit, maxLimit);
    }

    private static List<AccessImportCheckDto> BuildChecks(string extension, byte[] bytes)
    {
        var checks = new List<AccessImportCheckDto>
        {
            new("extension", "Формат файла", "passed", $"Расширение {extension} поддерживается для импорта Access."),
            new("size", "Размер файла", "passed", $"Файл содержит {bytes.LongLength:N0} байт и может быть проверен."),
            HasOleSignature(bytes)
                ? new AccessImportCheckDto("signature", "Сигнатура Access", "passed", "Файл похож на OLE Compound документ Access.")
                : new AccessImportCheckDto("signature", "Сигнатура Access", "warning", "Не найдена стандартная OLE-сигнатура. Возможно, файл поврежден или требует конвертации."),
            new("native_reader", "Драйвер чтения .accdb", "warning", "На текущей машине прямое чтение Access не подтверждено. Для фактического переноса нужен ACE-драйвер, Microsoft Access или конвертация в промежуточный формат.")
        };

        var discoveredHints = DiscoverSchemaHints(bytes);
        checks.Add(discoveredHints.Count == 0
            ? new AccessImportCheckDto("schema_hints", "Ориентиры схемы", "warning", "В бинарном файле не найдены читаемые названия таблиц. Карта Access -> PostgreSQL потребует ACE-драйвер или экспорт.")
            : new AccessImportCheckDto("schema_hints", "Ориентиры схемы", "passed", $"Найдены текстовые ориентиры: {string.Join(", ", discoveredHints.Take(8))}."));

        return checks;
    }

    private static bool HasOleSignature(byte[] bytes)
    {
        var oleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        return bytes.Length >= oleSignature.Length && bytes.Take(oleSignature.Length).SequenceEqual(oleSignature);
    }

    private static IReadOnlyList<string> DiscoverSchemaHints(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes) + " " + Encoding.Unicode.GetString(bytes);
        var candidates = new[] { "гараж", "владел", "плат", "счет", "счёт", "тариф", "постав", "контраг", "garage", "owner", "payment", "tariff", "meter" };
        return candidates
            .Where(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AccessImportRunDto ToDto(AccessImportRun run)
    {
        var checks = JsonSerializer.Deserialize<List<AccessImportCheckDto>>(run.ReportJson, JsonOptions) ?? [];
        return new AccessImportRunDto(
            run.Id,
            run.Mode,
            run.Status,
            run.OriginalFileName,
            run.FileExtension,
            run.FileSizeBytes,
            run.ContentSha256,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.TotalChecks,
            run.PassedChecks,
            run.WarningCount,
            run.ErrorCount,
            run.Summary,
            checks);
    }

    private static AccessImportRunLogEntryDto ToLogEntryDto(AccessImportRunLogEntry entry)
    {
        return new AccessImportRunLogEntryDto(
            entry.Id,
            entry.AccessImportRunId,
            entry.CreatedAtUtc,
            entry.Level,
            entry.StepCode,
            entry.Message);
    }

    private static AccessImportCreatedRecordDto ToCreatedRecordDto(AccessImportCreatedRecord record)
    {
        return new AccessImportCreatedRecordDto(
            record.Id,
            record.AccessImportRunId,
            record.SourceSystem,
            record.SourceEntityType,
            record.SourceExternalId,
            record.SourceRowHash,
            record.TargetEntityType,
            record.TargetEntityId,
            record.TargetDisplayName,
            record.RollbackStatus,
            record.CreatedAtUtc,
            record.CreatedByUserId,
            record.RolledBackAtUtc,
            record.RolledBackByUserId,
            record.RollbackReason);
    }
}
