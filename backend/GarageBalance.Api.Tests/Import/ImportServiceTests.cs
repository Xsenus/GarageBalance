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
        var dryRun = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("GSK archive.accdb", CreateAccessLikeStream("garage owner")), null, CancellationToken.None);

        var result = await service.ExportAccessImportRunReportAsync(dryRun.Value!.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("application/json; charset=utf-8", result.Value!.ContentType);
        Assert.StartsWith("garagebalance-access-dry-run-gsk-archive-", result.Value.FileName);
        var text = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains("\"originalFileName\": \"GSK archive.accdb\"", text);
        Assert.Contains("\"checks\":", text);
        Assert.Contains("\"schema_hints\"", text);
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

        var result = await service.ExportAccessImportRunReportAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_not_found", result.ErrorCode);
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
