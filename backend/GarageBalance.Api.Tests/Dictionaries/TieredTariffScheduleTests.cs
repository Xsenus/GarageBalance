using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class TieredTariffScheduleTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public async Task Schedule_SavesDifferentThresholdsForEachPeriodWithChangeDate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = await CreateTieredServiceAsync(database, tiered: true);
        var dictionary = DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateChargeServiceTariffScheduleAsync(
            service.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [
                    new(null, Start, new DateOnly(2026, 9, 30), 7.47m, ElectricityTiers: Tiers(100m, 7.47m, 10.17m)),
                    new(null, new DateOnly(2026, 10, 1), null, 8.31m, ElectricityTiers: Tiers(120m, 8.31m, 11.70m))
                ],
                true,
                "Новая сетка с октября",
                service.Version),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var periods = result.Value!.Periods;
        Assert.Equal(2, periods.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), periods[1].EffectiveFrom);
        Assert.Equal((100m, 7.47m, 10.17m), (periods[0].ElectricityTiers![0].UpperBound!.Value, periods[0].ElectricityTiers![0].Rate, periods[0].ElectricityTiers![1].Rate));
        Assert.Equal((120m, 8.31m, 11.70m), (periods[1].ElectricityTiers![0].UpperBound!.Value, periods[1].ElectricityTiers![0].Rate, periods[1].ElectricityTiers![1].Rate));
        Assert.Equal(8.31m, result.Value.Tariff.Rate);
        Assert.True(result.Value.Service.HasTieredTariff);
        var read = await dictionary.GetChargeServiceTariffScheduleAsync(service.Id, CancellationToken.None);
        Assert.Equal(120m, read.Value![1].ElectricityTiers![0].UpperBound);
        Assert.Equal(2, await database.Context.ChargeServiceTariffVersions.CountAsync(item => item.ChargeServiceSettingId == service.Id && !item.IsArchived));
    }

    [Fact]
    public async Task Schedule_KeepsThresholdsWhenOnlyTheChangeDateMoves()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = await CreateTieredServiceAsync(database, tiered: true);
        var dictionary = DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 10, 10));
        var current = (await dictionary.GetChargeServiceTariffScheduleAsync(service.Id, CancellationToken.None)).Value!.Single();

        var result = await dictionary.UpdateChargeServiceTariffScheduleAsync(
            service.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [new(current.TariffId, new DateOnly(2026, 3, 1), null, current.Rate, current.TariffVersion)],
                true,
                null,
                service.Version),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var period = Assert.Single(result.Value!.Periods);
        Assert.Equal(new DateOnly(2026, 3, 1), period.EffectiveFrom);
        Assert.Equal(2, period.ElectricityTiers!.Count);
        Assert.Equal(125m, period.ElectricityTiers[0].UpperBound);
    }

    [Fact]
    public async Task Schedule_RejectsInvalidThresholdsWithoutChangingTheSchedule()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = await CreateTieredServiceAsync(database, tiered: true);
        var dictionary = DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 10, 10));
        var before = (await dictionary.GetChargeServiceTariffScheduleAsync(service.Id, CancellationToken.None)).Value!;

        var result = await dictionary.UpdateChargeServiceTariffScheduleAsync(
            service.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [new(null, Start, null, 5m, ElectricityTiers: [new(null, "A", 100m, 5m), new(null, "B", 200m, 6m)])],
                true,
                null,
                service.Version),
            null,
            CancellationToken.None);

        Assert.Equal("tariff_electricity_last_tier_unbounded_required", result.ErrorCode);
        context_ClearAndAssert(database, service.Id, before.Count);
    }

    [Fact]
    public async Task Schedule_RejectsThresholdsForServiceWithoutTieredTariff()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = await CreateTieredServiceAsync(database, tiered: false);
        var dictionary = DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateChargeServiceTariffScheduleAsync(
            service.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [new(null, Start, null, 5m, ElectricityTiers: Tiers(100m, 5m, 6m))],
                true,
                null,
                service.Version),
            null,
            CancellationToken.None);

        Assert.Equal("tariff_schedule_tiers_not_supported", result.ErrorCode);
    }

    private static void context_ClearAndAssert(SqliteTestDatabase database, Guid serviceId, int expectedPeriods)
    {
        database.Context.ChangeTracker.Clear();
        Assert.Equal(expectedPeriods, database.Context.ChargeServiceTariffVersions.Count(item => item.ChargeServiceSettingId == serviceId && !item.IsArchived));
    }

    private static List<UpsertElectricityTariffTierRequest> Tiers(decimal threshold, decimal firstRate, decimal secondRate) =>
    [
        new(null, "Ступень 1", threshold, firstRate),
        new(null, "Ступень 2", null, secondRate)
    ];

    private static async Task<ChargeServiceSetting> CreateTieredServiceAsync(SqliteTestDatabase database, bool tiered)
    {
        var fund = new Fund { Name = "Фонд электроэнергии", NormalizedName = "ФОНД ЭЛЕКТРОЭНЕРГИИ", SortOrder = 10, IsSystem = false };
        var incomeType = new IncomeType { Name = "Электроэнергия по порогам", Code = "tiered_schedule", DestinationFundId = fund.Id };
        var template = new Tariff { Name = "Шаблон", CalculationBase = "fixed", Rate = 7m, EffectiveFrom = Start };
        database.Context.AddRange(fund, incomeType, template);
        await database.Context.SaveChangesAsync();
        var created = await DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 10, 10)).CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest("Электроэнергия по порогам", true, 1, 1, 25, null, 20, true, tiered, "кВт·ч", incomeType.Id, template.Id),
                7.47m,
                Start,
                fund.Id,
                tiered ? "metered_tiered" : "metered",
                tiered ? [new(null, "До 125", 125m, 7.47m), new(null, "Свыше 125", null, 10.17m)] : null,
                "meter_electricity"),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        database.Context.ChangeTracker.Clear();
        return await database.Context.ChargeServiceSettings.AsNoTracking().SingleAsync(item => item.Id == created.Value!.Service.Id);
    }
}
