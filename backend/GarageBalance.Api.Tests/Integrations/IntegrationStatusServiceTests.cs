using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class IntegrationStatusServiceTests
{
    [Fact]
    public async Task GetOneCFreshStatusAsync_ReturnsNotConfiguredWithoutSecrets()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateStatusService(database.Context);

        var status = await service.GetOneCFreshStatusAsync(CancellationToken.None);

        Assert.Equal("OneCFresh", status.Provider);
        Assert.Equal("1C Fresh", status.DisplayName);
        Assert.False(status.IsConfigured);
        Assert.False(status.CanSynchronize);
        Assert.Equal("not_configured", status.Status);
        Assert.Contains("OneCFresh:RefreshToken", status.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("RefreshToken", status.RequiredSettings);
        Assert.Empty(status.ConfiguredSettings);
        Assert.Null(status.LastProtectedSettingUpdatedAtUtc);
    }

    [Fact]
    public async Task GetOneCFreshStatusAsync_ReturnsPreparedStateWithoutPlaintext()
    {
        await using var database = await TestDatabase.CreateAsync();
        var secretService = CreateSecretService(database.Context);
        const string plaintextSecret = "one-c-refresh-token";
        await secretService.UpsertSecretAsync(
            new UpsertIntegrationSecretRequest("OneCFresh", "RefreshToken", plaintextSecret),
            Guid.NewGuid(),
            CancellationToken.None);
        var service = new IntegrationStatusService(secretService);

        var status = await service.GetOneCFreshStatusAsync(CancellationToken.None);

        Assert.True(status.IsConfigured);
        Assert.False(status.CanSynchronize);
        Assert.Equal("prepared", status.Status);
        Assert.Contains("RefreshToken", status.ConfiguredSettings);
        Assert.NotNull(status.LastProtectedSettingUpdatedAtUtc);
        Assert.DoesNotContain(plaintextSecret, status.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(plaintextSecret, status.StatusMessage, StringComparison.Ordinal);
    }

    private static IntegrationStatusService CreateStatusService(GarageBalanceDbContext context)
    {
        return new IntegrationStatusService(CreateSecretService(context));
    }

    private static IntegrationSecretSettingsService CreateSecretService(GarageBalanceDbContext context)
    {
        var provider = DataProtectionProvider.Create("GarageBalance.IntegrationStatus.Tests");
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
