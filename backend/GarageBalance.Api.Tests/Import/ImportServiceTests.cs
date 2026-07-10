using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Import;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task DryRunAccessImportAsync_PersistsReportAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        await using var stream = CreateAccessLikeStream("гараж владелец платеж счетчик");

        var result = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("ГСК.accdb", stream), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Value!.Status);
        Assert.Equal(".accdb", result.Value.FileExtension);
        Assert.Equal(64, result.Value.ContentSha256.Length);
        Assert.Contains(result.Value.Checks, check => check.Code == "schema_hints" && check.Status == "passed");
        Assert.Single(database.Context.AccessImportRuns);
        Assert.Contains(database.Context.AccessImportRunLogEntries, item => item.StepCode == "file_received" && item.AccessImportRunId == result.Value.Id);
        Assert.Contains(database.Context.AccessImportRunLogEntries, item => item.StepCode == "dry_run_finished" && item.AccessImportRunId == result.Value.Id);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.access_dry_run");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("import", auditEvent.ActionKind);
        Assert.Equal("ГСК.accdb", auditEvent.EntityDisplayName);
        Assert.Equal(result.Value.Id.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("ГСК.accdb", auditEvent.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("dry_run", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("completed", metadata.RootElement.GetProperty("status").GetString());
        Assert.Equal(".accdb", metadata.RootElement.GetProperty("fileExtension").GetString());
        Assert.Equal(result.Value.ContentSha256, metadata.RootElement.GetProperty("contentSha256").GetString());
        Assert.Equal("0", metadata.RootElement.GetProperty("errorCount").GetString());
    }

    [Fact]
    public async Task DryRunAccessImportAsync_RejectsUnsupportedExtension()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("data.xlsx", stream), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("access_extension_required", result.ErrorCode);
        Assert.Empty(database.Context.AccessImportRuns);
    }

    [Fact]
    public async Task GetAccessImportRunsAsync_ReturnsLatestRunsFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("first.accdb", CreateAccessLikeStream("garage")), null, CancellationToken.None);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("second.accdb", CreateAccessLikeStream("owner")), null, CancellationToken.None);

        var result = await service.GetAccessImportRunsAsync(new AccessImportRunListRequest(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("second.accdb", result[0].OriginalFileName);
    }

    [Fact]
    public async Task GetAccessImportRunsAsync_AppliesLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("first.accdb", CreateAccessLikeStream("garage")), null, CancellationToken.None);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("second.accdb", CreateAccessLikeStream("owner")), null, CancellationToken.None);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("third.accdb", CreateAccessLikeStream("payment")), null, CancellationToken.None);

        var result = await service.GetAccessImportRunsAsync(new AccessImportRunListRequest { Limit = 2 }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, run => run.OriginalFileName == "first.accdb");
    }

    [Fact]
    public async Task ExportAccessImportRunReportAsync_ReturnsJsonReportFile()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.ExportAccessImportRunReportAsync(dryRun.Value!.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("application/json; charset=utf-8", result.Value!.ContentType);
        Assert.StartsWith("garagebalance-access-dry-run-gsk-archive-", result.Value.FileName);
        var text = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains("\"originalFileName\": \"GSK archive.accdb\"", text);
        Assert.Contains("\"checks\":", text);
        Assert.Contains("\"schema_hints\"", text);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.access_dry_run_report_exported");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("export", auditEvent.ActionKind);
        Assert.Equal(dryRun.Value!.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("GSK archive.accdb", auditEvent.EntityDisplayName);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("GSK archive.accdb", auditEvent.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("dry_run", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("GSK archive.accdb", metadata.RootElement.GetProperty("originalFileName").GetString());
        Assert.Equal(result.Value.FileName, metadata.RootElement.GetProperty("reportFileName").GetString());
        Assert.DoesNotContain("schema_hints", auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestAccessImportRollbackAsync_MarksRunAndWritesAuditWithReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.RequestAccessImportRollbackAsync(
            dryRun.Value!.Id,
            new AccessImportRollbackRequest { Reason = "Выбран неверный файл старой базы" },
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("rollback_requested", result.Value!.Status);
        Assert.Contains("Rollback запрошен", result.Value.Summary, StringComparison.Ordinal);
        Assert.Contains(database.Context.AccessImportRunLogEntries, item => item.AccessImportRunId == dryRun.Value.Id && item.StepCode == "rollback_requested");
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.rollback_requested");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("cancel", auditEvent.ActionKind);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("GSK archive.accdb", auditEvent.EntityDisplayName);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("GSK archive.accdb", auditEvent.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("Выбран неверный файл старой базы", metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal("dry_run", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("rollback_requested", metadata.RootElement.GetProperty("status").GetString());
        Assert.Equal("dry_run_no_created_records", metadata.RootElement.GetProperty("rollbackState").GetString());
    }

    [Fact]
    public async Task RequestAccessImportRollbackAsync_RequiresReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.RequestAccessImportRollbackAsync(
            dryRun.Value!.Id,
            new AccessImportRollbackRequest { Reason = " " },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_rollback_reason_required", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "import.rollback_requested");
        Assert.DoesNotContain(database.Context.AccessImportRunLogEntries, item => item.StepCode == "rollback_requested");
    }

    [Fact]
    public async Task RequestAccessImportRollbackAsync_ReturnsNotFoundForMissingRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.RequestAccessImportRollbackAsync(
            Guid.NewGuid(),
            new AccessImportRollbackRequest { Reason = "Ошибочный запуск" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_not_found", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RequestAccessImportRollbackAsync_RejectsRunAfterApplyRequest()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);
        await service.RequestAccessImportApplyAsync(
            dryRun.Value!.Id,
            new AccessImportApplyRequest { Reason = "Dry-run проверен", BackupConfirmed = true },
            Guid.NewGuid(),
            CancellationToken.None);

        var result = await service.RequestAccessImportRollbackAsync(
            dryRun.Value.Id,
            new AccessImportRollbackRequest { Reason = "Остановить заявку" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_import_requested", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AccessImportRunLogEntries, item => item.StepCode == "rollback_requested");
    }

    [Fact]
    public async Task RequestAccessImportApplyAsync_MarksRunAndWritesAuditWithBackupConfirmation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.RequestAccessImportApplyAsync(
            dryRun.Value!.Id,
            new AccessImportApplyRequest { Reason = "Dry-run проверен, backup создан", BackupConfirmed = true },
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("import_requested", result.Value!.Status);
        Assert.Contains("Фактический импорт запрошен", result.Value.Summary, StringComparison.Ordinal);
        Assert.Contains(database.Context.AccessImportRunLogEntries, item => item.AccessImportRunId == dryRun.Value.Id && item.StepCode == "import_requested");
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.apply_requested");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("import", auditEvent.ActionKind);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("GSK archive.accdb", auditEvent.EntityDisplayName);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("GSK archive.accdb", auditEvent.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("Dry-run проверен, backup создан", metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal("dry_run", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("import_requested", metadata.RootElement.GetProperty("status").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("backupConfirmed").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("importExecuted").GetString());
        Assert.Equal("pending_access_reader", metadata.RootElement.GetProperty("importState").GetString());
    }

    [Fact]
    public async Task RequestAccessImportApplyAsync_RequiresReasonAndBackupConfirmation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var missingReason = await service.RequestAccessImportApplyAsync(
            dryRun.Value!.Id,
            new AccessImportApplyRequest { Reason = " ", BackupConfirmed = true },
            Guid.NewGuid(),
            CancellationToken.None);
        var missingBackup = await service.RequestAccessImportApplyAsync(
            dryRun.Value.Id,
            new AccessImportApplyRequest { Reason = "Проверено", BackupConfirmed = false },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(missingReason.Succeeded);
        Assert.Equal("import_apply_reason_required", missingReason.ErrorCode);
        Assert.False(missingBackup.Succeeded);
        Assert.Equal("import_apply_backup_confirmation_required", missingBackup.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "import.apply_requested");
        Assert.DoesNotContain(database.Context.AccessImportRunLogEntries, item => item.StepCode == "import_requested");
    }

    [Fact]
    public async Task RequestAccessImportApplyAsync_RejectsBlockedOrRolledBackRuns()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var blocked = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("blocked.accdb", CreateAccessLikeStream("")), null, CancellationToken.None);
        var rolledBack = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("rollback.accdb", CreateAccessLikeStream("garage")), null, CancellationToken.None);
        database.Context.AccessImportRuns.Single(run => run.Id == blocked.Value!.Id).Status = "blocked";
        await database.Context.SaveChangesAsync();
        await service.RequestAccessImportRollbackAsync(rolledBack.Value!.Id, new AccessImportRollbackRequest { Reason = "Ошибочный файл" }, null, CancellationToken.None);

        var blockedResult = await service.RequestAccessImportApplyAsync(
            blocked.Value!.Id,
            new AccessImportApplyRequest { Reason = "Проверено", BackupConfirmed = true },
            Guid.NewGuid(),
            CancellationToken.None);
        var rolledBackResult = await service.RequestAccessImportApplyAsync(
            rolledBack.Value.Id,
            new AccessImportApplyRequest { Reason = "Проверено", BackupConfirmed = true },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(blockedResult.Succeeded);
        Assert.Equal("import_run_blocked", blockedResult.ErrorCode);
        Assert.False(rolledBackResult.Succeeded);
        Assert.Equal("import_run_rollback_requested", rolledBackResult.ErrorCode);
    }

    [Fact]
    public async Task CancelAccessImportApplyRequestAsync_MarksRunAndWritesAuditWithReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);
        await service.RequestAccessImportApplyAsync(
            dryRun.Value!.Id,
            new AccessImportApplyRequest { Reason = "Dry-run проверен", BackupConfirmed = true },
            null,
            CancellationToken.None);

        var result = await service.CancelAccessImportApplyRequestAsync(
            dryRun.Value.Id,
            new AccessImportApplyCancelRequest { Reason = "Нужно перепроверить backup" },
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("import_request_cancelled", result.Value!.Status);
        Assert.Contains("Заявка на фактический импорт отменена", result.Value.Summary, StringComparison.Ordinal);
        Assert.Contains(database.Context.AccessImportRunLogEntries, item => item.AccessImportRunId == dryRun.Value.Id && item.StepCode == "import_request_cancelled");
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.apply_request_cancelled");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("cancel", auditEvent.ActionKind);
        Assert.Equal(dryRun.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("GSK archive.accdb", auditEvent.EntityDisplayName);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("Нужно перепроверить backup", metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal("import_request_cancelled", metadata.RootElement.GetProperty("status").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("importExecuted").GetString());
        Assert.Equal("apply_request_cancelled", metadata.RootElement.GetProperty("importState").GetString());
    }

    [Fact]
    public async Task CancelAccessImportApplyRequestAsync_RequiresActiveRequestAndReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var missingReason = await service.CancelAccessImportApplyRequestAsync(
            dryRun.Value!.Id,
            new AccessImportApplyCancelRequest { Reason = " " },
            Guid.NewGuid(),
            CancellationToken.None);
        var inactiveRequest = await service.CancelAccessImportApplyRequestAsync(
            dryRun.Value.Id,
            new AccessImportApplyCancelRequest { Reason = "Ошибочная заявка" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(missingReason.Succeeded);
        Assert.Equal("import_apply_cancel_reason_required", missingReason.ErrorCode);
        Assert.False(inactiveRequest.Succeeded);
        Assert.Equal("import_apply_request_not_active", inactiveRequest.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "import.apply_request_cancelled");
    }

    [Fact]
    public async Task GetAccessImportRunLogEntriesAsync_ReturnsRunLogInChronologicalOrderWithoutDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.GetAccessImportRunLogEntriesAsync(dryRun.Value!.Id, new AccessImportRunLogListRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Count >= 4);
        Assert.Equal("file_received", result.Value[0].StepCode);
        Assert.Contains(result.Value, entry => entry.StepCode == "hash_calculated");
        Assert.Contains(result.Value, entry => entry.StepCode == "dry_run_finished");
        Assert.All(result.Value, entry =>
        {
            Assert.Equal(dryRun.Value.Id, entry.AccessImportRunId);
            Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        });
    }

    [Fact]
    public async Task GetAccessImportRunLogEntriesAsync_AppliesLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.GetAccessImportRunLogEntriesAsync(dryRun.Value!.Id, new AccessImportRunLogListRequest { Limit = 2 }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task ExportAccessImportRunReportAsync_ReturnsNotFoundForMissingRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.ExportAccessImportRunReportAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_not_found", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task GetAccessImportRunLogEntriesAsync_ReturnsNotFoundForMissingRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.GetAccessImportRunLogEntriesAsync(Guid.NewGuid(), new AccessImportRunLogListRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_not_found", result.ErrorCode);
    }

    private static MemoryStream CreateAccessLikeStream(string text)
    {
        var oleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        return new MemoryStream([.. oleSignature, .. Encoding.UTF8.GetBytes(text)]);
    }

    private static ImportService CreateService(GarageBalanceDbContext context)
    {
        return new ImportService(context, new AuditEventWriter(context));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
