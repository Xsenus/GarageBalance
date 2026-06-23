using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task GetEventsAsync_ReturnsLatestEventsFirstAndFiltersBySearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                Action = "finance.income_created",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Поступление по гаражу 12."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
                Action = "import.access_dry_run",
                EntityType = "access_import_run",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Проверка файла Access."
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, "access"), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("import.access_dry_run", auditEvent.Action);
        Assert.Equal("Проверка файла Access.", auditEvent.Summary);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByDateAndAction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero),
                Action = "auth.login_success",
                EntityType = "user",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Вход пользователя."
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero),
                Action = "users.user_updated",
                EntityType = "app_user",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Пользователь обновлен."
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(
            new AuditEventListRequest(
                new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
                "users.user_updated",
                null),
            CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("users.user_updated", auditEvent.Action);
    }

    [Fact]
    public async Task GetEventsAsync_MasksSensitiveValuesInSummaryAndEntityId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
            Action = "auth.login_failed",
            EntityType = "login_email",
            EntityId = "owner@example.com",
            Summary = "Login owner@example.com failed: password=Secret123, token: abc.def.ghi, card 40817810507220051060, Bearer eyJhbGciOi."
        });
        await database.Context.SaveChangesAsync();

        var result = await service.GetEventsAsync(new AuditEventListRequest(null, null, null, "owner@example.com"), CancellationToken.None);

        var auditEvent = Assert.Single(result);
        Assert.Equal("[email скрыт]", auditEvent.EntityId);
        Assert.Contains("[email скрыт]", auditEvent.Summary);
        Assert.Contains("password=[секрет скрыт]", auditEvent.Summary);
        Assert.Contains("token: [секрет скрыт]", auditEvent.Summary);
        Assert.Contains("[номер скрыт]", auditEvent.Summary);
        Assert.Contains("Bearer [token скрыт]", auditEvent.Summary);
        Assert.DoesNotContain("owner@example.com", auditEvent.Summary);
        Assert.DoesNotContain("40817810507220051060", auditEvent.Summary);
        Assert.DoesNotContain("Secret123", auditEvent.Summary);
    }

    [Fact]
    public async Task ExportEventsCsvAsync_UsesFiltersAndMaskedCsvValues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AuditService(database.Context);
        database.Context.AuditEvents.AddRange(
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
                Action = "auth.login_failed",
                EntityType = "login_email",
                EntityId = "owner@example.com",
                Summary = "Login owner@example.com failed, password=Secret123 \"quoted\""
            },
            new AuditEvent
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 23, 9, 0, 0, TimeSpan.Zero),
                Action = "finance.income_created",
                EntityType = "financial_operation",
                EntityId = Guid.NewGuid().ToString(),
                Summary = "Поступление по гаражу 12."
            });
        await database.Context.SaveChangesAsync();

        var export = await service.ExportEventsCsvAsync(new AuditEventListRequest(null, null, "auth.login_failed", null), CancellationToken.None);
        var csv = Encoding.UTF8.GetString(export.Content);

        Assert.StartsWith("audit-events-", export.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".csv", export.FileName, StringComparison.Ordinal);
        Assert.Equal("text/csv; charset=utf-8", export.ContentType);
        Assert.Contains("createdAtUtc,actorUserId,action,entityType,entityId,summary", csv);
        Assert.Contains("auth.login_failed", csv);
        Assert.Contains("\"Login [email скрыт] failed, password=[секрет скрыт] \"\"quoted\"\"\"", csv);
        Assert.DoesNotContain("finance.income_created", csv);
        Assert.DoesNotContain("owner@example.com", csv);
        Assert.DoesNotContain("Secret123", csv);
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
