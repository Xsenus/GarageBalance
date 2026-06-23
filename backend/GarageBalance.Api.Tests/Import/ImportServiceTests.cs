using System.Text;
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
        var service = new ImportService(database.Context);
        var actorUserId = Guid.NewGuid();
        await using var stream = CreateAccessLikeStream("гараж владелец платеж счетчик");

        var result = await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("ГСК.accdb", stream), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("completed", result.Value!.Status);
        Assert.Equal(".accdb", result.Value.FileExtension);
        Assert.Equal(64, result.Value.ContentSha256.Length);
        Assert.Contains(result.Value.Checks, check => check.Code == "schema_hints" && check.Status == "passed");
        Assert.Single(database.Context.AccessImportRuns);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "import.access_dry_run" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task DryRunAccessImportAsync_RejectsUnsupportedExtension()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ImportService(database.Context);
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
        var service = new ImportService(database.Context);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("first.accdb", CreateAccessLikeStream("garage")), null, CancellationToken.None);
        await service.DryRunAccessImportAsync(new AccessImportDryRunRequest("second.accdb", CreateAccessLikeStream("owner")), null, CancellationToken.None);

        var result = await service.GetAccessImportRunsAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("second.accdb", result[0].OriginalFileName);
    }

    [Fact]
    public async Task ExportAccessImportRunReportAsync_ReturnsJsonReportFile()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ImportService(database.Context);
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
    public async Task ExportAccessImportRunReportAsync_ReturnsNotFoundForMissingRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ImportService(database.Context);

        var result = await service.ExportAccessImportRunReportAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("import_run_not_found", result.ErrorCode);
    }

    private static MemoryStream CreateAccessLikeStream(string text)
    {
        var oleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        return new MemoryStream([.. oleSignature, .. Encoding.UTF8.GetBytes(text)]);
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
