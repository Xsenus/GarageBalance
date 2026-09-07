using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlWaterDefaultTariffTests
{
    [PostgreSqlFact]
    public async Task FreshInstallation_CreatesWaterWithSingleRateAndNoThresholds()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("0");
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var water = await context.ChargeServiceSettings
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .SingleAsync(item => item.IncomeType!.Code == "water" && !item.IsArchived);

        Assert.True(water.IsMetered);
        Assert.False(water.HasTieredTariff);
        var tariff = Assert.IsType<Tariff>(water.Tariff);
        Assert.Equal(TariffCalculationBases.MeterWater, tariff.CalculationBase);
        Assert.True(tariff.Rate > 0);
        Assert.Null(tariff.ElectricityFirstThreshold);
        Assert.Null(tariff.ElectricitySecondThreshold);
        Assert.Null(tariff.ElectricityFirstRate);
        Assert.Null(tariff.ElectricitySecondRate);
        Assert.Null(tariff.ElectricityThirdRate);
        Assert.True(string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson));
    }

    [PostgreSqlFact]
    public async Task RepeatedMigration_PreservesUserConfiguredWaterTiers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var water = await context.ChargeServiceSettings
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .SingleAsync(item => item.IncomeType!.Code == "water" && !item.IsArchived);
        water.HasTieredTariff = true;
        var tariff = water.Tariff!;
        tariff.Rate = 123m;
        tariff.ElectricityFirstThreshold = 10m;
        tariff.ElectricitySecondThreshold = 20m;
        tariff.ElectricityFirstRate = 123m;
        tariff.ElectricitySecondRate = 150m;
        tariff.ElectricityThirdRate = 200m;
        tariff.ElectricityTiersJson = """[{"id":"first","name":"До 10 м³","upperBound":10,"rate":123,"isCustom":true},{"id":"last","name":"Свыше 10 м³","upperBound":null,"rate":150,"isCustom":true}]""";
        await context.SaveChangesAsync();
        var storedTariff = await context.Tariffs.AsNoTracking().SingleAsync(item => item.Id == tariff.Id);
        var expectedTariff = System.Text.Json.JsonSerializer.Serialize(storedTariff);
        var serviceVersion = water.Version;

        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();

        var saved = await context.ChargeServiceSettings.Include(item => item.Tariff)
            .SingleAsync(item => item.Id == water.Id);
        Assert.True(saved.HasTieredTariff);
        Assert.Equal(serviceVersion, saved.Version);
        Assert.Equal(expectedTariff, System.Text.Json.JsonSerializer.Serialize(saved.Tariff));
    }
}
