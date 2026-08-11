using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlServiceMeterKindMigrationIntegrationTests
{
    private const string PreviousMigration = "20260811142436_LinkIrregularPayments";

    [PostgreSqlFact]
    public async Task Migration_PreservesLegacyKindsAndAssignsIndependentKindToArbitraryService()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var customIncomeTypeId = Guid.NewGuid();
        var customIncomeTypeCode = $"custom_security_{Guid.NewGuid():N}";
        var customIncomeTypeName = $"Охрана {Guid.NewGuid():N}";
        var waterServiceId = Guid.NewGuid();
        var customServiceId = Guid.NewGuid();

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var waterIncomeTypeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "water")
                .Select(item => item.Id)
                .SingleAsync();
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO income_types (
                    "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({customIncomeTypeId}, {customIncomeTypeName}, {customIncomeTypeCode}, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                INSERT INTO charge_service_settings (
                    "Id", "Name", "IsRegular", "OverdueGraceDays", "IncomeTypeId", "IsMetered",
                    "HasTieredTariff", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                VALUES
                    ({waterServiceId}, 'Вода', TRUE, 30, {waterIncomeTypeId}, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid()),
                    ({customServiceId}, 'Охрана', TRUE, 30, {customIncomeTypeId}, TRUE, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid());
                """);

            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var kinds = await verificationContext.ChargeServiceSettings
            .Where(setting => setting.Id == waterServiceId || setting.Id == customServiceId)
            .ToDictionaryAsync(setting => setting.Id, setting => setting.MeterKind);
        Assert.Equal(MeterKinds.Water, kinds[waterServiceId]);
        Assert.Equal(MeterKinds.ForService(customServiceId), kinds[customServiceId]);
    }
}
