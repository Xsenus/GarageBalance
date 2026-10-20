using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Tests.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

// Recheck of the customer comments 20.10.2026: when the tariff schedule of a service had a gap
// at the working date, the service list returned no tariff, so the service card, the schedule
// dialog and the rate typed into the tariff grid were all rejected and the rate was lost.
public sealed class TariffScheduleGapRateEntryTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public async Task Sqlite_RateCanBeEnteredWhileTheScheduleHasAGapAtTheWorkingDate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRateEntryInTariffGapAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RateCanBeEnteredWhileTheScheduleHasAGapAtTheWorkingDate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRateEntryInTariffGapAsync(context);
    }

    private static async Task AssertRateEntryInTariffGapAsync(GarageBalanceDbContext context)
    {
        foreach (var existing in context.ChargeServiceSettings) existing.IsArchived = true;
        var fund = new Fund { Name = "Synthetic gap tariff", NormalizedName = "SYNTHETIC GAP TARIFF", IsSystem = false };
        var income = new IncomeType { Name = "Synthetic gap waste", Code = "synthetic_gap_waste", DestinationFund = fund };
        var tariff = new Tariff { Name = "Synthetic gap waste", CalculationBase = TariffCalculationBases.People, Rate = 128.69m, EffectiveFrom = new(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Synthetic gap waste",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            UnitName = "чел.",
            IncomeType = income,
            Tariff = tariff
        };
        var garage = new Garage { Number = "synthetic-gap-1", RegisteredOn = October, AccountingStartMonth = September, PeopleCount = 2, FloorCount = 1 };
        context.AddRange(garage, setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October.AddDays(19));
        var recalculation = new TariffAccrualRecalculationService(new EfAccrualRepository(context), new EfChargeServiceSettingRepository(context), finance);
        var dictionaries = DictionaryServiceTestFactory.Create(context, October.AddDays(19), recalculation);
        var ended = await dictionaries.UpdateChargeServiceTariffScheduleAsync(setting.Id,
            new([new(tariff.Id, null, September.AddMonths(1).AddDays(-1), 128.69m)], true, "Synthetic tariff ends in September", setting.Version),
            null, CancellationToken.None);
        Assert.True(ended.Succeeded, ended.ErrorMessage);

        // The list shows the service without a tariff for the working date.
        var listed = Assert.Single(await dictionaries.GetChargeServiceSettingsAsync(setting.Name, CancellationToken.None));
        Assert.Null(listed.TariffId);
        UpsertChargeServiceSettingRequest FromList(ChargeServiceSettingDto dto, int overdueGraceDays = 30) => new(
            dto.Name, dto.IsRegular, dto.PeriodicityMonths, dto.AccrualStartMonth, dto.PaymentDueDay, dto.PaymentDueMonth,
            overdueGraceDays, dto.IsMetered, dto.HasTieredTariff, dto.UnitName, dto.IncomeTypeId, dto.TariffId, dto.Version);

        // The service card keeps working and keeps the stored tariff link.
        var storedLink = (await context.ChargeServiceSettings.AsNoTracking().SingleAsync(item => item.Id == setting.Id)).TariffId;
        Assert.NotNull(storedLink);
        var card = await dictionaries.UpdateChargeServiceSettingAsync(setting.Id, FromList(listed, overdueGraceDays: 45), null, CancellationToken.None);
        Assert.True(card.Succeeded, card.ErrorMessage);
        Assert.Equal(45, card.Value!.OverdueGraceDays);
        Assert.Equal(storedLink, (await context.ChargeServiceSettings.AsNoTracking().SingleAsync(item => item.Id == setting.Id)).TariffId);

        // A rate typed into the grid starts a new period and recalculates the month at once.
        listed = Assert.Single(await dictionaries.GetChargeServiceSettingsAsync(setting.Name, CancellationToken.None));
        var rate = await dictionaries.UpdateChargeServiceWithTariffAsync(setting.Id,
            new(FromList(listed, overdueGraceDays: 45), 150m, EffectiveFrom: October), null, CancellationToken.None);
        Assert.True(rate.Succeeded, rate.ErrorMessage);
        var worksheet = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, October), null, CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(257.38m, Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == income.Id && row.AccountingMonth == September).AccrualAmount);
        Assert.Equal(300m, Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == income.Id && row.AccountingMonth == October).AccrualAmount);

        // The schedule dialog sends the service as listed and saves a further period.
        var periods = (await dictionaries.GetChargeServiceTariffScheduleAsync(setting.Id, CancellationToken.None)).Value!;
        listed = Assert.Single(await dictionaries.GetChargeServiceSettingsAsync(setting.Name, CancellationToken.None));
        var schedule = await dictionaries.UpdateChargeServiceTariffScheduleAsync(setting.Id,
            new([
                .. periods.Select(period => new UpsertChargeServiceTariffPeriodRequest(
                    period.TariffId, period.EffectiveFrom, period.EffectiveFrom == October ? November.AddDays(-1) : period.EffectiveTo, period.Rate, period.TariffVersion)),
                new(null, November, null, 160m)
            ], true, "Synthetic rate from November", listed.Version, FromList(listed, overdueGraceDays: 45)),
            null, CancellationToken.None);
        Assert.True(schedule.Succeeded, schedule.ErrorMessage);
        Assert.Equal(
            [((DateOnly?)null, (DateOnly?)September.AddMonths(1).AddDays(-1), 128.69m), (October, November.AddDays(-1), 150m), (November, null, 160m)],
            (await dictionaries.GetChargeServiceTariffScheduleAsync(setting.Id, CancellationToken.None)).Value!
                .Select(period => (period.EffectiveFrom, period.EffectiveTo, period.Rate)).ToArray());
    }
}
