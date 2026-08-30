using System.Data.Common;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class PostgreSqlIntegrationSecretBatchQueryTests
{
    [PostgreSqlFact]
    public async Task GetSecretsAsync_LoadsAllRequestedProtectedSettingsWithOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commandCounter = new SelectCommandCounter();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCounter)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var protector = new DataProtectionSensitiveDataProtector(new EphemeralDataProtectionProvider());
        var service = new IntegrationSecretSettingsService(
            new EfIntegrationSecretSettingsRepository(context),
            protector,
            new AuditEventWriter(context));
        await service.UpsertSecretAsync(
            new UpsertIntegrationSecretRequest("ReceiptPrinting", "DeviceConnection", "synthetic-device-secret"),
            null,
            CancellationToken.None);
        await service.UpsertSecretAsync(
            new UpsertIntegrationSecretRequest("ReceiptPrinting", "ReceiptTemplate", "synthetic-template-secret"),
            null,
            CancellationToken.None);
        context.ChangeTracker.Clear();
        commandCounter.Reset();

        var result = await service.GetSecretsAsync(
            "ReceiptPrinting",
            ["DeviceConnection", "ReceiptTemplate"],
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("synthetic-device-secret", result.Value!["DeviceConnection"]);
        Assert.Equal("synthetic-template-secret", result.Value["ReceiptTemplate"]);
        var command = Assert.Single(commandCounter.Commands);
        Assert.Contains("NormalizedProvider", command, StringComparison.Ordinal);
        Assert.Contains("NormalizedSettingKey", command, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-device-secret", command, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-template-secret", command, StringComparison.Ordinal);
    }

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Reset() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
