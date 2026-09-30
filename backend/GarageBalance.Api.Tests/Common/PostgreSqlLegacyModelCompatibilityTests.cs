using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

public sealed class PostgreSqlLegacyModelCompatibilityTests
{
    [PostgreSqlFact]
    public async Task CompatibilityRestoresHistoricalColumnsWithoutRemovingExistingVersions()
    {
        foreach (var migration in new[] { "20260723114059_LinkChargeServicesToExpenseTypes", "20260805051709_ChargeServiceTariffHistory" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync(migration);
            bool versionExisted;
            await using (var context = database.CreateContext())
            {
                versionExisted = await HasColumn(context, "application_settings", "Version");
                Assert.False(await HasColumn(context, "garages", "RegisteredOn"));
                await PostgreSqlLegacyModelCompatibility.AddCurrentVersionColumnsAsync(context);
                await PostgreSqlLegacyModelCompatibility.AddCurrentVersionColumnsAsync(context);
                Assert.True(await HasColumn(context, "application_settings", "Version"));
                Assert.True(await HasColumn(context, "garages", "RegisteredOn"));
            }

            await using var restoredContext = database.CreateContext();
            await PostgreSqlLegacyModelCompatibility.RemoveCurrentVersionColumnsAsync(restoredContext);
            await PostgreSqlLegacyModelCompatibility.RemoveCurrentVersionColumnsAsync(restoredContext);
            Assert.Equal(versionExisted, await HasColumn(restoredContext, "application_settings", "Version"));
            Assert.False(await HasColumn(restoredContext, "garages", "RegisteredOn"));
            await restoredContext.Database.MigrateAsync();
            Assert.True(await HasColumn(restoredContext, "application_settings", "JsonValue"));
        }
    }

    private static Task<bool> HasColumn(GarageBalance.Api.Infrastructure.Data.GarageBalanceDbContext context, string table, string column) =>
        context.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name={table} AND column_name={column}) AS \"Value\"").SingleAsync();
}
