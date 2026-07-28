using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlElectricityEnergyUnitMigrationIntegrationTests
{
    private const string MigrationId = "20260728054806_NormalizeElectricityEnergyUnits";

    [PostgreSqlFact]
    public async Task Migration_NormalizesLegacyElectricityUnitsAndPreservesCorrectValues()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var electricityTariff = new Tariff
        {
            Name = $"Электроэнергия {Guid.NewGuid():N}",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 3m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstTierName = "До 50 кВт",
            ElectricitySecondTierName = "От 50 кВт до 100 кВт·ч",
            ElectricityThirdTierName = "Свыше 100 кВт·ч",
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            ElectricityTiersJson =
                """[{"id":"first","name":"До 50 кВт","upperBound":50,"rate":2,"isCustom":false},{"id":"last","name":"Свыше 50 кВт·ч","upperBound":null,"rate":5,"isCustom":false}]"""
        };
        var electricityService = new ChargeServiceSetting
        {
            Name = $"Электроэнергия {Guid.NewGuid():N}",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            Tariff = electricityTariff,
            IsMetered = true,
            HasTieredTariff = true,
            UnitName = " кВт "
        };

        context.Tariffs.Add(electricityTariff);
        context.ChargeServiceSettings.Add(electricityService);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = {MigrationId};
            """);
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();

        var savedTariff = await context.Tariffs.SingleAsync(item => item.Id == electricityTariff.Id);
        var savedService = await context.ChargeServiceSettings.SingleAsync(item => item.Id == electricityService.Id);

        Assert.Equal("До 50 кВт·ч", savedTariff.ElectricityFirstTierName);
        Assert.Equal("От 50 кВт·ч до 100 кВт·ч", savedTariff.ElectricitySecondTierName);
        Assert.Equal("Свыше 100 кВт·ч", savedTariff.ElectricityThirdTierName);
        Assert.Contains("\"name\": \"До 50 кВт·ч\"", savedTariff.ElectricityTiersJson, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Свыше 50 кВт·ч\"", savedTariff.ElectricityTiersJson, StringComparison.Ordinal);
        Assert.DoesNotContain("кВт·ч·ч", savedTariff.ElectricityTiersJson, StringComparison.Ordinal);
        Assert.Equal("кВт·ч", savedService.UnitName);
    }
}
