using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Tests.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

// Customer report 14.10.2026: opening "Поступления" for September–October recalculated
// September with October tariff versions, so unpaid September charges disappeared.
public sealed class MultiMonthTariffWorksheetTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly December = new(2026, 12, 1);

    [Fact]
    public async Task Sqlite_MultiMonthWorksheetUsesEachMonthsOwnTariffVersion()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertEachMonthUsesOwnTariffAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_MultiMonthWorksheetUsesEachMonthsOwnTariffVersion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertEachMonthUsesOwnTariffAsync(context);
    }

    private static async Task AssertEachMonthUsesOwnTariffAsync(GarageBalanceDbContext context)
    {
        foreach (var existing in context.ChargeServiceSettings) existing.IsArchived = true;
        var fund = new Fund { Name = "Synthetic period tariff", NormalizedName = "SYNTHETIC PERIOD TARIFF", IsSystem = false };
        var changed = CreateService(fund, "changed", TariffCalculationBases.Fixed, 100m);
        var ended = CreateService(fund, "ended", TariffCalculationBases.People, 128.69m);
        var garage = new Garage
        {
            Number = "synthetic-period-1",
            RegisteredOn = October,
            AccountingStartMonth = September,
            PeopleCount = 2,
            FloorCount = 1
        };
        context.AddRange(garage, changed.Setting, ended.Setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October.AddDays(17));
        var recalculation = new TariffAccrualRecalculationService(new EfAccrualRepository(context), new EfChargeServiceSettingRepository(context), finance);
        var dictionaries = DictionaryServiceTestFactory.Create(context, October.AddDays(17), recalculation);

        var initial = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        Assert.True(initial.Succeeded, initial.ErrorMessage);

        // A new rate from October 1 for one service and the end of the other service's tariff.
        var rateChanged = await dictionaries.UpdateChargeServiceTariffScheduleAsync(changed.Setting.Id,
            new([new(changed.Tariff.Id, null, September.AddMonths(1).AddDays(-1), 100m), new(null, October, null, 125m)], false,
                "Synthetic rate from October", changed.Setting.Version), null, CancellationToken.None);
        Assert.True(rateChanged.Succeeded, rateChanged.ErrorMessage);
        var tariffEnded = await dictionaries.UpdateChargeServiceTariffScheduleAsync(ended.Setting.Id,
            new([new(ended.Tariff.Id, null, September.AddMonths(1).AddDays(-1), 128.69m)], true,
                "Synthetic tariff ends in September", ended.Setting.Version), null, CancellationToken.None);
        Assert.True(tariffEnded.Succeeded, tariffEnded.ErrorMessage);

        foreach (var monthTo in new[] { October, December })
        {
            var worksheet = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, monthTo), null, CancellationToken.None);
            Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
            var rows = worksheet.Value!.Rows;
            Assert.Equal(100m, Assert.Single(rows, row => row.IncomeTypeId == changed.Income.Id && row.AccountingMonth == September).AccrualAmount);
            Assert.Equal(125m, Assert.Single(rows, row => row.IncomeTypeId == changed.Income.Id && row.AccountingMonth == October).AccrualAmount);
            var septemberEnded = Assert.Single(rows, row => row.IncomeTypeId == ended.Income.Id && row.AccountingMonth == September);
            Assert.Equal(257.38m, septemberEnded.AccrualAmount);
            Assert.Null(septemberEnded.CalculationIssue);
            Assert.NotNull(Assert.Single(rows, row => row.IncomeTypeId == ended.Income.Id && row.AccountingMonth == October).CalculationIssue);
        }

        var active = await context.Accruals.AsNoTracking()
            .Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && accrual.AccountingMonth <= October)
            .OrderBy(accrual => accrual.AccountingMonth)
            .ToListAsync();
        Assert.Equal(
            [(September, 100m), (September, 257.38m), (October, 125m)],
            active.Select(accrual => (accrual.AccountingMonth, accrual.Amount)).OrderBy(item => item.AccountingMonth).ThenBy(item => item.Amount).ToArray());
        Assert.False(await context.Accruals.AnyAsync(accrual => accrual.GarageId == garage.Id && accrual.IsCanceled));
    }

    private static (ChargeServiceSetting Setting, IncomeType Income, Tariff Tariff) CreateService(
        Fund fund, string code, string calculationBase, decimal rate)
    {
        var income = new IncomeType { Name = $"Synthetic period {code}", Code = $"synthetic_period_{code}", DestinationFund = fund };
        var tariff = new Tariff { Name = $"Synthetic period {code}", CalculationBase = calculationBase, Rate = rate, EffectiveFrom = new(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = $"Synthetic period {code}",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            UnitName = calculationBase == TariffCalculationBases.People ? "чел." : "гараж",
            IncomeType = income,
            Tariff = tariff
        };
        return (setting, income, tariff);
    }
}
