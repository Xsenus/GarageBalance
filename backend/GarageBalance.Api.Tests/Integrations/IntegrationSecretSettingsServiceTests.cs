using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class IntegrationSecretSettingsServiceTests
{
    [Fact]
    public async Task UpsertSecretAsync_StoresProtectedValueAndWritesAuditWithoutPlaintext()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        const string secret = "one-c-refresh-token-very-secret";

        var result = await service.UpsertSecretAsync(
            new UpsertIntegrationSecretRequest("OneCFresh", "RefreshToken", secret),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.HasProtectedValue);
        Assert.Equal("OneCFresh.RefreshToken", result.Value.Purpose);

        var stored = Assert.Single(database.Context.IntegrationSecretSettings);
        Assert.StartsWith(DataProtectionSensitiveDataProtector.ProtectedValuePrefix, stored.ProtectedValue, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, stored.ProtectedValue, StringComparison.Ordinal);

        var auditEvent = Assert.Single(database.Context.AuditEvents, item => item.Action == "integration.secret_upserted");
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Equal("integration", auditEvent.Section);
        Assert.Equal("update", auditEvent.ActionKind);
        Assert.Equal("OneCFresh:RefreshToken", auditEvent.EntityDisplayName);
        Assert.Contains("OneCFresh", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("RefreshToken", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("OneCFresh.RefreshToken", auditEvent.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, auditEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, auditEvent.EntityId, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, auditEvent.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsPlaintextThroughProtector()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        await service.UpsertSecretAsync(
            new UpsertIntegrationSecretRequest("FiscalPrinter", "ApiToken", "printer-api-token"),
            null,
            CancellationToken.None);

        var result = await service.GetSecretAsync("FiscalPrinter", "ApiToken", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("printer-api-token", result.Value);
    }

    [Fact]
    public async Task UpsertSecretAsync_UpdatesExistingSecretCaseInsensitively()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        await service.UpsertSecretAsync(new UpsertIntegrationSecretRequest("OneCFresh", "RefreshToken", "first-token"), null, CancellationToken.None);
        await service.UpsertSecretAsync(new UpsertIntegrationSecretRequest("onecfresh", "refreshtoken", "second-token"), null, CancellationToken.None);

        Assert.Equal(1, await database.Context.IntegrationSecretSettings.CountAsync());
        var result = await service.GetSecretAsync("ONECFRESH", "REFRESHTOKEN", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("second-token", result.Value);
        Assert.Equal(2, await database.Context.AuditEvents.CountAsync(item => item.Action == "integration.secret_upserted"));
    }

    [Theory]
    [InlineData("", "RefreshToken", "secret", "integration_provider_required")]
    [InlineData("OneCFresh", "", "secret", "integration_secret_key_required")]
    [InlineData("OneCFresh", "RefreshToken", "", "integration_secret_value_required")]
    public async Task UpsertSecretAsync_ValidatesRequiredFields(string provider, string key, string secret, string expectedCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.UpsertSecretAsync(new UpsertIntegrationSecretRequest(provider, key, secret), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(database.Context.IntegrationSecretSettings);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsMetadataOnlyWithoutSecretValue()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        await service.UpsertSecretAsync(new UpsertIntegrationSecretRequest("OneCFresh", "RefreshToken", "one-c-secret"), null, CancellationToken.None);

        var settings = await service.GetSettingsAsync("onecfresh", CancellationToken.None);

        var setting = Assert.Single(settings);
        Assert.Equal("OneCFresh", setting.Provider);
        Assert.Equal("RefreshToken", setting.SettingKey);
        Assert.True(setting.HasProtectedValue);
        Assert.DoesNotContain("one-c-secret", setting.ToString(), StringComparison.Ordinal);
    }

    private static IntegrationSecretSettingsService CreateService(GarageBalanceDbContext context)
    {
        var provider = DataProtectionProvider.Create("GarageBalance.IntegrationSecretSettings.Tests");
        var protector = new DataProtectionSensitiveDataProtector(provider);
        return new IntegrationSecretSettingsService(context, protector, new AuditEventWriter(context));
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
