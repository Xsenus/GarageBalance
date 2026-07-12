using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Import;

public sealed class ImportQuarantineServiceTests
{
    private const string RowHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string AnotherRowHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task RegisterAsync_CreatesQuarantineItemAndAuditWithoutRawSnapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var result = await service.RegisterAsync(
            new RegisterImportQuarantineItemRequest(
                " Access ",
                " Garage ",
                " 42 ",
                RowHash.ToUpperInvariant(),
                "missing-owner",
                "Не найден владелец гаража.",
                "WARNING",
                "{\"owner\":\"Петров\",\"garage\":\"42\"}",
                runId),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(runId, result.Value!.AccessImportRunId);
        Assert.Equal("Access", result.Value.SourceSystem);
        Assert.Equal("Garage", result.Value.EntityType);
        Assert.Equal("42", result.Value.ExternalId);
        Assert.Equal(RowHash, result.Value.RowHash);
        Assert.Equal("warning", result.Value.Severity);
        Assert.Equal("open", result.Value.Status);
        Assert.Single(database.Context.AccessImportQuarantineItems);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.quarantine_registered");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("import", auditEvent.ActionKind);
        Assert.Equal("Access/Garage/missing-owner", auditEvent.EntityDisplayName);
        Assert.Equal(runId.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("42", auditEvent.RelatedDocumentNumber);
        Assert.Contains("Комментарий: Не найден владелец гаража.", auditEvent.Summary, StringComparison.Ordinal);
        Assert.Contains("\"sourceSystem\":\"Access\"", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"importEntityType\":\"Garage\"", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"externalId\":\"42\"", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"reasonCode\":\"missing-owner\"", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"severity\":\"warning\"", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Петров", auditEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Петров", auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Garage", RowHash, "missing-owner", "error", "{}", "import_source_required")]
    [InlineData("Access", "", RowHash, "missing-owner", "error", "{}", "import_entity_type_required")]
    [InlineData("Access", "Garage", "", "missing-owner", "error", "{}", "import_row_hash_required")]
    [InlineData("Access", "Garage", "not-sha-256", "missing-owner", "error", "{}", "import_row_hash_invalid")]
    [InlineData("Access", "Garage", RowHash, "", "error", "{}", "import_quarantine_reason_code_required")]
    [InlineData("Access", "Garage", RowHash, "missing-owner", "info", "{}", "import_quarantine_severity_invalid")]
    [InlineData("Access", "Garage", RowHash, "missing-owner", "error", "{broken", "import_quarantine_snapshot_invalid")]
    public async Task RegisterAsync_ValidatesRequiredFieldsHashSeverityAndSnapshot(
        string source,
        string entityType,
        string rowHash,
        string reasonCode,
        string severity,
        string rowSnapshotJson,
        string expectedCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterAsync(
            new RegisterImportQuarantineItemRequest(source, entityType, "1", rowHash, reasonCode, "Причина.", severity, rowSnapshotJson, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(database.Context.AccessImportQuarantineItems);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task GetOpenItemsAsync_ReturnsOpenItemsNewestFirstAndFiltersByRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();

        var first = await service.RegisterAsync(CreateRequest(RowHash, firstRunId, "garage-1"), null, CancellationToken.None);
        await Task.Delay(5);
        var second = await service.RegisterAsync(CreateRequest(AnotherRowHash, secondRunId, "garage-2"), null, CancellationToken.None);
        await service.ResolveAsync(first.Value!.Id, new ResolveImportQuarantineItemRequest("Разобрано."), null, CancellationToken.None);

        var openItems = await service.GetOpenItemsAsync(null, CancellationToken.None);
        var filteredItems = await service.GetOpenItemsAsync(secondRunId, CancellationToken.None);

        var openItem = Assert.Single(openItems);
        Assert.Equal(second.Value!.Id, openItem.Id);
        Assert.Equal(second.Value.Id, Assert.Single(filteredItems).Id);
    }

    [Fact]
    public async Task GetOpenItemsAsync_AppliesExplicitLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        await service.RegisterAsync(CreateRequest(RowHash, null, "garage-1"), null, CancellationToken.None);
        await Task.Delay(5);
        await service.RegisterAsync(CreateRequest(AnotherRowHash, null, "garage-2"), null, CancellationToken.None);
        await Task.Delay(5);
        await service.RegisterAsync(CreateRequest("1111111111111111111111111111111111111111111111111111111111111111", null, "garage-3"), null, CancellationToken.None);

        var openItems = await service.GetOpenItemsAsync(null, CancellationToken.None, 2);

        Assert.Equal(2, openItems.Count);
        Assert.Equal("garage-3", openItems[0].ExternalId);
        Assert.Equal("garage-2", openItems[1].ExternalId);
    }

    [Fact]
    public async Task ResolveAsync_MarksItemResolvedAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var registered = await service.RegisterAsync(CreateRequest(RowHash, null, "garage-1"), null, CancellationToken.None);

        var resolved = await service.ResolveAsync(registered.Value!.Id, new ResolveImportQuarantineItemRequest("Владелец создан вручную."), actorUserId, CancellationToken.None);

        Assert.True(resolved.Succeeded);
        Assert.Equal("resolved", resolved.Value!.Status);
        Assert.Equal("Владелец создан вручную.", resolved.Value.ResolutionComment);
        Assert.Equal(actorUserId, resolved.Value.ResolvedByUserId);
        Assert.NotNull(resolved.Value.ResolvedAtUtc);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.quarantine_resolved");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal(registered.Value.Id.ToString(), auditEvent.EntityId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal("Access/Garage/missing-owner", auditEvent.EntityDisplayName);
        Assert.Equal("garage-1", auditEvent.RelatedDocumentNumber);
        Assert.Contains("Комментарий: Владелец создан вручную.", auditEvent.Summary, StringComparison.Ordinal);
        using var metadataJson = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("Владелец создан вручную.", metadataJson.RootElement.GetProperty("resolutionComment").GetString());
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFoundForMissingItem()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.ResolveAsync(Guid.NewGuid(), new ResolveImportQuarantineItemRequest("Нет строки."), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_quarantine_item_not_found", result.ErrorCode);
    }

    private static RegisterImportQuarantineItemRequest CreateRequest(string rowHash, Guid? runId, string externalId)
    {
        return new RegisterImportQuarantineItemRequest(
            "Access",
            "Garage",
            externalId,
            rowHash,
            "missing-owner",
            "Не найден владелец гаража.",
            "error",
            "{}",
            runId);
    }

    private static ImportQuarantineService CreateService(GarageBalanceDbContext context)
    {
        return new ImportQuarantineService(new EfImportQuarantineRepository(context), new AuditEventWriter(context));
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
