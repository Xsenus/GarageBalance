using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
