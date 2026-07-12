using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditEventWriterTests
{
    [Fact]
    public async Task Add_CreatesStructuredAuditEventWithDiffMetadataAndRelatedFields()
    {
        await using var database = await TestDatabase.CreateAsync();
        var writer = new AuditEventWriter(database.Context);
        var actorUserId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var auditEvent = writer.Add(new AuditEventWriteRequest(
            actorUserId,
            "finance.income_updated",
            "financial_operation",
            entityId.ToString(),
            OldValues: new Dictionary<string, object?>
            {
                ["amount"] = 100m,
                ["comment"] = "old token=secret"
            },
            NewValues: new Dictionary<string, object?>
            {
                ["amount"] = 150.50m,
                ["comment"] = "new token=secret"
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["amount"] = "Сумма",
                ["comment"] = "Комментарий"
            },
            Metadata: new Dictionary<string, object?>
            {
                ["documentNumber"] = "PKO-1",
                ["apiToken"] = "raw-token",
                ["ownerPhone"] = "+7 999 111-22-33"
            },
            EntityDisplayName: "Поступление PKO-1",
            RelatedGarageNumber: "12",
            RelatedAccountingMonth: "2026-06",
            RelatedDocumentNumber: "PKO-1"));

        Assert.NotNull(auditEvent);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("finance", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal("Поступление PKO-1", auditEvent.EntityDisplayName);
        Assert.Equal("12", auditEvent.RelatedGarageNumber);
        Assert.Equal("2026-06", auditEvent.RelatedAccountingMonth);
        Assert.Equal("PKO-1", auditEvent.RelatedDocumentNumber);
        Assert.Contains("Сумма: было 100, стало 150.5", auditEvent.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: было old token=[секрет скрыт], стало new token=[секрет скрыт]", auditEvent.Summary, StringComparison.Ordinal);
        using var metadataJson = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("[секрет скрыт]", metadataJson.RootElement.GetProperty("apiToken").GetString());
        Assert.Equal("[секрет скрыт]", metadataJson.RootElement.GetProperty("ownerPhone").GetString());
        Assert.DoesNotContain("raw-token", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("+7 999 111-22-33", auditEvent.MetadataJson, StringComparison.Ordinal);

        await database.Context.SaveChangesAsync();

        var service = new AuditService(new EfAuditEventRepository(database.Context));
        var dto = Assert.Single(await service.GetEventsAsync(new AuditEventListRequest(null, null, null, null), CancellationToken.None));
        Assert.Equal("finance", dto.Section);
        Assert.Equal("update", dto.ActionKind);
        Assert.Equal("Поступление PKO-1", dto.EntityDisplayName);
        Assert.Equal("12", dto.RelatedGarageNumber);
        Assert.Equal("2026-06", dto.RelatedAccountingMonth);
        Assert.Equal("PKO-1", dto.RelatedDocumentNumber);
        Assert.DoesNotContain("raw-token", dto.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-token", dto.Metadata?.Values ?? []);
    }

    [Fact]
    public void Add_ReturnsNullAndDoesNotAddEventWhenExplicitDiffHasNoChanges()
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        var auditEvent = writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            "dictionary.tariff_updated",
            "tariff",
            Guid.NewGuid().ToString(),
            OldValues: new Dictionary<string, object?> { ["name"] = "Тариф" },
            NewValues: new Dictionary<string, object?> { ["name"] = "  Тариф  " }));

        Assert.Null(auditEvent);
        Assert.Empty(database.Context.AuditEvents.Local);
    }

    [Theory]
    [InlineData("dictionary.owner_archived")]
    [InlineData("finance.operation_canceled")]
    [InlineData("dictionary.supplier_deleted")]
    public void Add_RequiresReasonForDangerousActions(string action)
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        var error = Assert.Throws<InvalidOperationException>(() => writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            action,
            "owner",
            Guid.NewGuid().ToString(),
            Summary: "Опасное действие.")));

        Assert.Equal("Причина обязательна для удаления, архивирования и отмены.", error.Message);
    }

    [Fact]
    public void Add_AllowsDangerousActionWithReasonAndUsesReasonLabel()
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        var auditEvent = writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            "dictionary.owner_archived",
            "owner",
            Guid.NewGuid().ToString(),
            Summary: "Владелец архивирован",
            Reason: "Дубликат"));

        Assert.NotNull(auditEvent);
        Assert.Equal("archive", auditEvent.ActionKind);
        Assert.Contains("Причина: Дубликат.", auditEvent.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dictionary.owner_created", "create")]
    [InlineData("dictionary.owner_updated", "update")]
    [InlineData("users.password_changed", "update")]
    [InlineData("dictionary.owner_archived", "archive")]
    [InlineData("dictionary.owner_restored", "restore")]
    [InlineData("finance.operation_canceled", "cancel")]
    [InlineData("finance.operation_cancelled", "cancel")]
    [InlineData("dictionary.owner_deleted", "delete")]
    [InlineData("finance.regular_accruals_generated", "generate")]
    [InlineData("auth.login_success", "login")]
    [InlineData("import.dry_run_completed", "import")]
    [InlineData("reports.consolidated_exported", "export")]
    [InlineData("reports.cash.export.completed", "export")]
    [InlineData("system.health_checked", "other")]
    public void Add_InfersStableActionKindFromActionName(string action, string expectedActionKind)
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        var auditEvent = writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            action,
            "audit_target",
            Guid.NewGuid().ToString(),
            Summary: "Audit action kind regression.",
            Reason: expectedActionKind is "archive" or "cancel" or "delete" ? "Required reason." : null));

        Assert.NotNull(auditEvent);
        Assert.Equal(expectedActionKind, auditEvent.ActionKind);
    }

    [Fact]
    public void Add_UsesExplicitActionKindInsteadOfInferringFromAction()
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        var auditEvent = writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            "reports.custom_action",
            "report",
            Guid.NewGuid().ToString(),
            Summary: "Audit action kind override.",
            ActionKind: "export"));

        Assert.NotNull(auditEvent);
        Assert.Equal("export", auditEvent.ActionKind);
    }

    [Fact]
    public void Add_ValidatesRequiredActionAndEntityType()
    {
        using var database = TestDatabase.Create();
        var writer = new AuditEventWriter(database.Context);

        Assert.Throws<ArgumentException>(() => writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            " ",
            "owner",
            Guid.NewGuid().ToString())));
        Assert.Throws<ArgumentException>(() => writer.Add(new AuditEventWriteRequest(
            Guid.NewGuid(),
            "dictionary.owner_created",
            " ",
            Guid.NewGuid().ToString())));
    }

    private sealed class TestDatabase : IAsyncDisposable, IDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static TestDatabase Create()
        {
            var database = CreateAsync().GetAwaiter().GetResult();
            return database;
        }

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

        public void Dispose()
        {
            Context.Dispose();
            connection.Dispose();
        }
    }
}
