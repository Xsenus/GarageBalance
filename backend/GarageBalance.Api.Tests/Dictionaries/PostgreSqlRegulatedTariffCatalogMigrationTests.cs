using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlRegulatedTariffCatalogMigrationTests
{
    [PostgreSqlFact]
    public async Task LatestMigration_LoadsVerifiedPeriodsAndKeepsLegacyTariffs()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var services = await context.ChargeServiceSettings
            .AsNoTracking()
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .Where(item => item.IncomeType != null
                && new[] { "water", "electricity", "trash" }.Contains(item.IncomeType.Code))
            .ToDictionaryAsync(item => item.IncomeType!.Code!);

        Assert.Equal(100.60m, services["water"].Tariff!.Rate);
        Assert.Equal(7.47m, services["electricity"].Tariff!.Rate);
        Assert.Equal(128.69m, services["trash"].Tariff!.Rate);
        Assert.Equal(100m, services["electricity"].Tariff!.ElectricityFirstThreshold);
        Assert.Equal(155m, services["electricity"].Tariff!.ElectricitySecondThreshold);
        Assert.Equal(10.17m, services["electricity"].Tariff!.ElectricitySecondRate);
        Assert.Equal(14.88m, services["electricity"].Tariff!.ElectricityThirdRate);

        await AssertHistoryAsync(context, services["water"].Id, 17, new DateOnly(2020, 1, 1), new DateOnly(2028, 12, 31));
        await AssertHistoryAsync(context, services["electricity"].Id, 13, new DateOnly(2020, 1, 1), new DateOnly(2026, 12, 31));
        await AssertHistoryAsync(context, services["trash"].Id, 8, new DateOnly(2020, 1, 1), new DateOnly(2026, 9, 30));

        Assert.True(await context.Tariffs.AnyAsync(item => item.Id == Guid.Parse("8a92bf70-9339-4bbc-8e5d-a05cda185101")));
        Assert.True(await context.AuditEvents.AnyAsync(item =>
            item.Action == "dictionary.regulated_tariff_history_reconciled"));
    }

    [PostgreSqlFact]
    public async Task Migration_ReplacesIncorrectServicePeriodsWithoutDeletingTheirTariffs()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260907132111_AllowGeneratedAccrualCommentExpansion");
        await using (var context = database.CreateContext())
        {
            var waterService = await context.ChargeServiceSettings
                .Include(item => item.IncomeType)
                .SingleAsync(item => item.IncomeType!.Code == "water");
            var oldTariff = new Tariff
            {
                Id = Guid.Parse("b0010000-0000-4000-8000-000000000001"),
                Name = "Проверочная ошибочная ставка воды",
                CalculationBase = TariffCalculationBases.MeterWater,
                Rate = 103m,
                EffectiveFrom = new DateOnly(2026, 9, 4),
                Comment = "Synthetic migration test tariff"
            };
            context.Tariffs.Add(oldTariff);
            context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = waterService.Id,
                Tariff = oldTariff,
                EffectiveFrom = new DateOnly(2026, 9, 4)
            });
            await context.SaveChangesAsync();
            await context.Database.MigrateAsync();
        }

        await using var verification = database.CreateContext();
        var oldTariffId = Guid.Parse("b0010000-0000-4000-8000-000000000001");
        Assert.True(await verification.Tariffs.AnyAsync(item => item.Id == oldTariffId));
        Assert.False(await verification.ChargeServiceTariffVersions.AnyAsync(item => item.TariffId == oldTariffId));

        var waterServiceId = await verification.ChargeServiceSettings
            .Where(item => item.IncomeType != null && item.IncomeType.Code == "water")
            .Select(item => item.Id)
            .SingleAsync();
        Assert.Equal(17, await verification.ChargeServiceTariffVersions
            .CountAsync(item => item.ChargeServiceSettingId == waterServiceId));
    }

    private static async Task AssertHistoryAsync(
        GarageBalance.Api.Infrastructure.Data.GarageBalanceDbContext context,
        Guid settingId,
        int expectedCount,
        DateOnly expectedStart,
        DateOnly expectedEnd)
    {
        var periods = await context.ChargeServiceTariffVersions
            .AsNoTracking()
            .Where(item => item.ChargeServiceSettingId == settingId)
            .OrderBy(item => item.EffectiveFrom)
            .Select(item => new { item.EffectiveFrom, item.EffectiveTo })
            .ToListAsync();

        Assert.Equal(expectedCount, periods.Count);
        Assert.Equal(expectedStart, periods[0].EffectiveFrom);
        Assert.Equal(expectedEnd, periods[^1].EffectiveTo);
        for (var index = 1; index < periods.Count; index++)
        {
            Assert.Equal(periods[index - 1].EffectiveTo!.Value.AddDays(1), periods[index].EffectiveFrom);
        }
    }
}
