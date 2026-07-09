using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Import;

public sealed class ImportFingerprintServiceTests
{
    private const string RowHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string AnotherRowHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task RegisterAsync_CreatesFingerprintAndAuditEvent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var importRunId = Guid.NewGuid();

        var result = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest("Access", "Garage", " 42 ", RowHash.ToUpperInvariant(), importRunId, "garage", "target-42"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Created);
        Assert.Equal("Access", result.Value.Fingerprint.SourceSystem);
        Assert.Equal("42", result.Value.Fingerprint.ExternalId);
        Assert.Equal(RowHash, result.Value.Fingerprint.RowHash);
        Assert.Equal(importRunId, result.Value.Fingerprint.AccessImportRunId);
        Assert.True(await service.ExistsAsync("access", "garage", "42", AnotherRowHash, CancellationToken.None));
        Assert.Single(database.Context.AccessImportRowFingerprints);
        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "import.row_fingerprint_registered");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("ACCESS|GARAGE|external:42", auditEvent.EntityId);
        Assert.Equal("import", auditEvent.Section);
        Assert.Equal("import", auditEvent.ActionKind);
        Assert.Equal("Access/Garage", auditEvent.EntityDisplayName);
        Assert.Equal(importRunId.ToString(), auditEvent.RelatedDocumentId);
        Assert.Equal("42", auditEvent.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(auditEvent.MetadataJson!);
        Assert.Equal("Access", metadata.RootElement.GetProperty("sourceSystem").GetString());
        Assert.Equal("Garage", metadata.RootElement.GetProperty("importEntityType").GetString());
        Assert.Equal("42", metadata.RootElement.GetProperty("externalId").GetString());
        Assert.Equal(AuditTextMasker.Mask(importRunId.ToString()), metadata.RootElement.GetProperty("accessImportRunId").GetString());
        Assert.Equal("garage", metadata.RootElement.GetProperty("targetEntityType").GetString());
        Assert.Equal("target-42", metadata.RootElement.GetProperty("targetEntityId").GetString());
    }

    [Fact]
    public async Task RegisterAsync_ReturnsExistingForDuplicateExternalIdWithoutSecondAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var first = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest("Access", "Payment", "pay-7", RowHash, null, "financial_operation", "operation-7"),
            null,
            CancellationToken.None);

        var duplicate = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest(" access ", " payment ", " PAY-7 ", AnotherRowHash, null, "financial_operation", "operation-duplicate"),
            null,
            CancellationToken.None);

        Assert.True(first.Value!.Created);
        Assert.False(duplicate.Value!.Created);
        Assert.Equal(first.Value.Fingerprint.Id, duplicate.Value.Fingerprint.Id);
        Assert.Equal(1, await database.Context.AccessImportRowFingerprints.CountAsync());
        Assert.Equal(1, await database.Context.AuditEvents.CountAsync(item => item.Action == "import.row_fingerprint_registered"));
    }

    [Fact]
    public async Task RegisterAsync_UsesRowHashWhenExternalIdIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var first = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest("Access", "MeterReading", null, RowHash, null, "meter_reading", "reading-1"),
            null,
            CancellationToken.None);
        var duplicate = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest("Access", "MeterReading", " ", RowHash, null, "meter_reading", "reading-2"),
            null,
            CancellationToken.None);
        var distinct = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest("Access", "MeterReading", null, AnotherRowHash, null, "meter_reading", "reading-3"),
            null,
            CancellationToken.None);

        Assert.True(first.Value!.Created);
        Assert.False(duplicate.Value!.Created);
        Assert.True(distinct.Value!.Created);
        Assert.Equal(2, await database.Context.AccessImportRowFingerprints.CountAsync());
    }

    [Theory]
    [InlineData("", "Garage", RowHash, "import_source_required")]
    [InlineData("Access", "", RowHash, "import_entity_type_required")]
    [InlineData("Access", "Garage", "", "import_row_hash_required")]
    [InlineData("Access", "Garage", "not-sha-256", "import_row_hash_invalid")]
    public async Task RegisterAsync_ValidatesRequiredFieldsAndRowHash(string source, string entityType, string rowHash, string expectedCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterAsync(
            new RegisterImportRowFingerprintRequest(source, entityType, "1", rowHash, null, null, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(database.Context.AccessImportRowFingerprints);
        Assert.Empty(database.Context.AuditEvents);
    }

    private static ImportFingerprintService CreateService(GarageBalanceDbContext context)
    {
        return new ImportFingerprintService(context, new AuditEventWriter(context));
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
