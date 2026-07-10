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
    public async Task PreviewSyncAsync_RequiresRefreshToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context, new FakeSecretSettingsService(null), new FakeSyncAdapter());

        var result = await service.PreviewSyncAsync(new OneCFreshSyncRequest(null), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("one_c_fresh_not_configured", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task PreviewSyncAsync_CreatesAuditEventWithoutCallingAdapterOrPlaintextToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var adapter = new FakeSyncAdapter();
        var service = CreateService(database.Context, new FakeSecretSettingsService("super-secret-token"), adapter);

        var result = await service.PreviewSyncAsync(new OneCFreshSyncRequest("Проверить период перед обменом"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("OneCFresh", result.Value!.Provider);
        Assert.Equal("preview", result.Value.Mode);
        Assert.Equal("pending_decision", result.Value.Direction);
        Assert.Equal("draft_preview", result.Value.Status);
        Assert.False(result.Value.CanApply);
        Assert.NotEmpty(result.Value.SnapshotHash);
        Assert.All(result.Value.Counts, count => Assert.Equal(0, count.Count));
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "one_c_fresh_exchange_decisions_required");
        Assert.Empty(result.Value.Conflicts);
        Assert.Null(adapter.LastRequest);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal(result.Value.AuditEventId, audit.Id);
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("one_c_fresh.sync_preview_requested", audit.Action);
        Assert.Equal("sync", audit.ActionKind);
        Assert.Equal("integrations", audit.Section);
        Assert.Equal("integration_sync", audit.EntityType);
        Assert.Equal("OneCFresh", audit.EntityId);
        Assert.Equal("1C Fresh", audit.EntityDisplayName);
        Assert.DoesNotContain("super-secret-token", audit.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", audit.MetadataJson, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("preview", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("pending_decision", metadata.RootElement.GetProperty("direction").GetString());
        Assert.Equal("draft_preview", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal(result.Value.SnapshotHash, metadata.RootElement.GetProperty("snapshotHash").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("canApply").GetString());
        Assert.Equal("0", metadata.RootElement.GetProperty("conflictCount").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("protectedCredentialConfigured").GetString());
        Assert.Equal("Проверить период перед обменом", metadata.RootElement.GetProperty("reason").GetString());
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
        Assert.False(result.Value.IsRetry);
        Assert.True(result.Value.CanRetry);
        Assert.False(result.Value.HasConflict);
        Assert.Null(result.Value.ErrorCode);
        Assert.Equal("retry", result.Value.RecoveryAction);
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
        Assert.Equal("True", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("hasConflict").GetString());
        Assert.Equal("retry", metadata.RootElement.GetProperty("recoveryAction").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("protectedCredentialConfigured").GetString());
    }

    [Fact]
    public async Task StartSyncAsync_ForwardsTrimmedCommentRetryFlagAndCancellationTokenToAdapter()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakeSyncAdapter(OneCFreshSyncAdapterResult.Started("Запуск принят.", "fresh-run-42"));
        var service = CreateService(database.Context, new FakeSecretSettingsService("runtime-token"), adapter);

        var result = await service.RetrySyncAsync(
            new OneCFreshSyncRequest("  Повторить после таймаута  "),
            Guid.NewGuid(),
            cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Equal("fresh-run-42", result.Value!.ExternalRunId);
        Assert.Equal("runtime-token", adapter.LastRequest!.RefreshToken);
        Assert.Equal("Повторить после таймаута", adapter.LastRequest.Comment);
        Assert.True(adapter.LastRequest.IsRetry);
        Assert.Equal(cancellation.Token, adapter.LastCancellationToken);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Повторить после таймаута", metadata.RootElement.GetProperty("reason").GetString());
        Assert.Equal("fresh-run-42", metadata.RootElement.GetProperty("externalRunId").GetString());
    }

    [Fact]
    public async Task StartSyncAsync_MapsStartedStatusToWatchStatusWithoutRetry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var adapter = new FakeSyncAdapter(OneCFreshSyncAdapterResult.Started("1C Fresh приняла запуск.", "fresh-run-100"));
        var service = CreateService(database.Context, new FakeSecretSettingsService("token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest(null), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("started", result.Value!.Status);
        Assert.False(result.Value.CanRetry);
        Assert.False(result.Value.HasConflict);
        Assert.Null(result.Value.ErrorCode);
        Assert.Equal("fresh-run-100", result.Value.ExternalRunId);
        Assert.Equal("watch_status", result.Value.RecoveryAction);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("started", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("fresh-run-100", metadata.RootElement.GetProperty("externalRunId").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("hasConflict").GetString());
        Assert.Equal("watch_status", metadata.RootElement.GetProperty("recoveryAction").GetString());
    }

    [Fact]
    public async Task StartSyncAsync_WritesAdapterErrorStatusAsRetryable()
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
        Assert.True(result.Value.CanRetry);
        Assert.False(result.Value.HasConflict);
        Assert.Equal("TIMEOUT", result.Value.ErrorCode);
        Assert.Equal("retry", result.Value.RecoveryAction);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("adapter_error", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("TIMEOUT", metadata.RootElement.GetProperty("adapterErrorCode").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("hasConflict").GetString());
        Assert.Equal("retry", metadata.RootElement.GetProperty("recoveryAction").GetString());
    }

    [Theory]
    [InlineData("failed", null, "one_c_fresh_adapter_error")]
    [InlineData("export_failed", null, "one_c_fresh_adapter_error")]
    [InlineData("timeout", null, "one_c_fresh_adapter_error")]
    [InlineData("rate_limited", "too_many_requests", "too_many_requests")]
    [InlineData("adapter_error", "fresh_unavailable", "fresh_unavailable")]
    public async Task StartSyncAsync_MapsRetryableAdapterStatusesAndErrorCodes(
        string adapterStatus,
        string? adapterErrorCode,
        string expectedErrorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var adapter = new FakeSyncAdapter(OneCFreshSyncAdapterResult.Failed(
            adapterStatus,
            "1C Fresh не завершила обмен.",
            adapterErrorCode));
        var service = CreateService(database.Context, new FakeSecretSettingsService("token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest(null), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(adapterStatus, result.Value!.Status);
        Assert.True(result.Value.CanRetry);
        Assert.False(result.Value.HasConflict);
        Assert.Equal(expectedErrorCode, result.Value.ErrorCode);
        Assert.Equal("retry", result.Value.RecoveryAction);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(adapterStatus, metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal(expectedErrorCode, metadata.RootElement.GetProperty("adapterErrorCode").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("hasConflict").GetString());
    }

    [Fact]
    public async Task StartSyncAsync_WritesConflictStatusWithoutRetry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var adapter = new FakeSyncAdapter(OneCFreshSyncAdapterResult.Conflict(
            "Найдены конфликтующие документы 1C Fresh.",
            "duplicate_external"));
        var service = CreateService(database.Context, new FakeSecretSettingsService("token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest("Проверка конфликтов"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("conflict", result.Value!.Status);
        Assert.False(result.Value.CanRetry);
        Assert.True(result.Value.HasConflict);
        Assert.Equal("duplicate_external", result.Value.ErrorCode);
        Assert.Equal("resolve_conflict", result.Value.RecoveryAction);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("conflict", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("duplicate_external", metadata.RootElement.GetProperty("adapterErrorCode").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("hasConflict").GetString());
        Assert.Equal("resolve_conflict", metadata.RootElement.GetProperty("recoveryAction").GetString());
    }

    [Theory]
    [InlineData("conflict", "one_c_fresh_conflict")]
    [InlineData("conflict_payment", "one_c_fresh_conflict")]
    [InlineData("document_conflict", "one_c_fresh_conflict")]
    [InlineData("counterparty_conflict", "counterparty_duplicate")]
    public async Task StartSyncAsync_MapsConflictStatusFamiliesToResolutionRequired(
        string adapterStatus,
        string expectedErrorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var adapterErrorCode = expectedErrorCode == "one_c_fresh_conflict" ? null : expectedErrorCode;
        var adapter = new FakeSyncAdapter(new OneCFreshSyncAdapterResult(
            adapterStatus,
            "Нужен разбор конфликта.",
            ErrorCode: adapterErrorCode));
        var service = CreateService(database.Context, new FakeSecretSettingsService("token"), adapter);

        var result = await service.StartSyncAsync(new OneCFreshSyncRequest(null), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(adapterStatus, result.Value!.Status);
        Assert.False(result.Value.CanRetry);
        Assert.True(result.Value.HasConflict);
        Assert.Equal(expectedErrorCode, result.Value.ErrorCode);
        Assert.Equal("resolve_conflict", result.Value.RecoveryAction);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(adapterStatus, metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal(expectedErrorCode, metadata.RootElement.GetProperty("adapterErrorCode").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("hasConflict").GetString());
    }

    [Fact]
    public async Task RetrySyncAsync_CreatesSeparateAuditEventWithoutPlaintextToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var adapter = new FakeSyncAdapter(new OneCFreshSyncAdapterResult(
            "pending_adapter",
            "Повтор ожидает адаптер."));
        var service = CreateService(database.Context, new FakeSecretSettingsService("super-secret-token"), adapter);

        var result = await service.RetrySyncAsync(new OneCFreshSyncRequest("Повтор после ошибки адаптера"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("pending_adapter", result.Value!.Status);
        Assert.True(result.Value.IsRetry);
        Assert.True(result.Value.CanRetry);
        Assert.True(adapter.LastRequest!.IsRetry);
        Assert.Equal("Повтор после ошибки адаптера", adapter.LastRequest.Comment);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal(result.Value.AuditEventId, audit.Id);
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("one_c_fresh.sync_retry_requested", audit.Action);
        Assert.Equal("sync", audit.ActionKind);
        Assert.Equal("integrations", audit.Section);
        Assert.DoesNotContain("super-secret-token", audit.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", audit.MetadataJson, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("pending_adapter", metadata.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("isRetry").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("canRetry").GetString());
        Assert.Equal("Повтор после ошибки адаптера", metadata.RootElement.GetProperty("reason").GetString());
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

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<OneCFreshSyncAdapterResult> StartAsync(OneCFreshSyncAdapterRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
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
