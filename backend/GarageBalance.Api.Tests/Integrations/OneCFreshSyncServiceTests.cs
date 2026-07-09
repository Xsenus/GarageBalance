using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class OneCFreshSyncServiceTests
{
    [Fact]
    public async Task StartSyncAsync_RequiresRefreshToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context, new FakeSecretSettingsService(null), new FakeSyncAdapter());

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest(null), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("one_c_fresh_not_configured", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task StartSyncAsync_CreatesAuditEventWithoutPlaintextToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var adapter = new FakeSyncAdapter(new OneCFreshSyncAdapterResult(
            "pending_adapter",
            "Запуск ожидает адаптер."));
        var service = CreateService(database.Context, new FakeSecretSettingsService("super-secret-token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest("Тестовая синхронизация"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("OneCFresh", result.Value!.Provider);
        Assert.Equal("pending_adapter", result.Value.Status);
        Assert.Equal("super-secret-token", adapter.LastRequest!.RefreshToken);
        Assert.Equal("Тестовая синхронизация", adapter.LastRequest.Comment);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal(result.Value.AuditEventId, audit.Id);
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("one_c_fresh.sync_requested", audit.Action);
        Assert.Equal("sync", audit.ActionKind);
        Assert.Equal("integrations", audit.Section);
        Assert.Equal("integration_sync", audit.EntityType);
        Assert.Equal("OneCFresh", audit.EntityId);
        Assert.Equal("1C Fresh", audit.EntityDisplayName);
        Assert.DoesNotContain("super-secret-token", audit.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", audit.MetadataJson, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("pending_adapter", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("protectedCredentialConfigured").GetString());
    }

    [Fact]
    public async Task StartSyncAsync_WritesAdapterErrorStatus()
    {
        await using var database = await TestDatabase.CreateAsync();
        var adapter = new FakeSyncAdapter(new OneCFreshSyncAdapterResult(
            "adapter_error",
            "1C Fresh временно недоступен.",
            ErrorCode: "TIMEOUT"));
        var service = CreateService(database.Context, new FakeSecretSettingsService("token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest(null), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("adapter_error", result.Value!.Status);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("adapter_error", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("TIMEOUT", metadata.RootElement.GetProperty("adapterErrorCode").GetString());
    }

    private static OneCFreshSyncService CreateService(
        GarageBalanceDbContext context,
        IIntegrationSecretSettingsService secretSettingsService,
        IOneCFreshSyncAdapter adapter)
    {
        return new OneCFreshSyncService(context, secretSettingsService, adapter, new AuditEventWriter(context));
    }

    private sealed class FakeSecretSettingsService(string? refreshToken) : IIntegrationSecretSettingsService
    {
        public Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
            UpsertIntegrationSecretRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IntegrationSecretSettingResult<string>> GetSecretAsync(
            string provider,
            string settingKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(refreshToken)
                ? IntegrationSecretSettingResult<string>.Failure("integration_secret_not_found", "Not found.")
                : IntegrationSecretSettingResult<string>.Success(refreshToken));
        }

        public Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(string? provider, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<IntegrationSecretSettingDto>>([]);
        }
    }

    private sealed class FakeSyncAdapter(OneCFreshSyncAdapterResult? result = null) : IOneCFreshSyncAdapter
    {
        public OneCFreshSyncAdapterRequest? LastRequest { get; private set; }

        public Task<OneCFreshSyncAdapterResult> StartAsync(OneCFreshSyncAdapterRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result ?? OneCFreshSyncAdapterResult.Pending("Ожидает адаптер."));
        }
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
