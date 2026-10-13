using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class OctoberTariffWorksheetTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OctoberRate_RefreshesUnpaidWorksheetPreservesPaidAndCreatesMissingCharge(bool schedule)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertOctoberAsync(database.Context, schedule);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_OctoberRateRecalculatesOnlyEligibleChargesAndReadOnlyQueryDoesNotWrite()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertOctoberAsync(context, false);
    }

    private static async Task AssertOctoberAsync(GarageBalanceDbContext context, bool schedule)
    {
        foreach (var existing in context.ChargeServiceSettings) existing.IsArchived = true;
        var fund = new Fund { Name = "Synthetic October tariff", NormalizedName = "SYNTHETIC OCTOBER TARIFF", IsSystem = false };
        var income = new IncomeType { Name = "Synthetic October tariff", Code = "synthetic_october_tariff", DestinationFund = fund };
        var tariff = new Tariff { Name = "Synthetic October tariff", CalculationBase = TariffCalculationBases.Fixed, Rate = 100, EffectiveFrom = new(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Synthetic October tariff",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            UnitName = "гараж",
            IncomeType = income,
            Tariff = tariff
        };
        var garages = Enumerable.Range(0, 4).Select(index => new Garage
        {
            Number = $"synthetic-october-{index}",
            RegisteredOn = September,
            AccountingStartMonth = September,
            PeopleCount = 1,
            FloorCount = 1
        }).ToArray();
        context.AddRange(garages);
        context.Add(setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October);
        foreach (var garage in garages.Take(3))
        {
            var result = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, October), null, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        var original = await context.Accruals.Where(row => row.IncomeTypeId == income.Id).ToListAsync();
        Assert.Equal(6, original.Count);
        var snapshots = original.ToDictionary(row => row.Id, row => row.CalculationDetailsJson);
        for (var index = 1; index <= 2; index++)
        {
            var accrual = original.Single(row => row.GarageId == garages[index].Id && row.AccountingMonth == October);
            var payment = new FinancialOperation
            {
                Garage = garages[index],
                IncomeType = income,
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = October,
                AccountingMonth = October,
                Amount = index == 1 ? 1 : 100
            };
            context.AddRange(payment, new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = payment.Amount, IsActive = true });
        }
        await context.SaveChangesAsync();
        var recalculation = new TariffAccrualRecalculationService(new EfAccrualRepository(context), new EfChargeServiceSettingRepository(context), finance);
        var dictionaries = DictionaryServiceTestFactory.Create(context, October, recalculation);
        if (schedule)
        {
            var updated = await dictionaries.UpdateChargeServiceTariffScheduleAsync(setting.Id,
                new([new(tariff.Id, null, October.AddDays(-1), 100), new(null, October, null, 125)], false,
                    "Synthetic October rate", setting.Version), null, CancellationToken.None);
            Assert.True(updated.Succeeded, updated.ErrorMessage);
        }
        else
        {
            var updated = await dictionaries.UpdateChargeServiceWithTariffAsync(setting.Id,
                new(new(setting.Name, true, 1, 1, 30, null, 30, false, false, "гараж", income.Id, tariff.Id, setting.Version),
                    125, EffectiveFrom: October, ChangeReason: "Synthetic October rate"), null, CancellationToken.None);
            Assert.True(updated.Succeeded, updated.ErrorMessage);
        }
        var updatedAccruals = await context.Accruals.Where(row => row.IncomeTypeId == income.Id).ToListAsync();
        Assert.Equal(6, updatedAccruals.Count);
        foreach (var accrual in updatedAccruals)
        {
            var changed = accrual.GarageId == garages[0].Id && accrual.AccountingMonth == October;
            Assert.Equal(changed ? 125m : 100m, accrual.Amount);
            if (!changed) Assert.Equal(snapshots[accrual.Id], accrual.CalculationDetailsJson);
        }
        var read = await finance.GetGarageIncomeWorksheetAsync(garages[0].Id, new(October, October), CancellationToken.None);
        Assert.True(read.Succeeded, read.ErrorMessage);
        Assert.Equal(125m, Assert.Single(read.Value!.Rows, row => row.IncomeTypeId == income.Id).AccrualAmount);
        var missing = await finance.GetGarageIncomeWorksheetAsync(garages[3].Id, new(October, October), CancellationToken.None);
        Assert.True(missing.Succeeded, missing.ErrorMessage);
        Assert.Equal(0m, missing.Value!.AccrualTotal);
        Assert.Equal(6, await context.Accruals.CountAsync(row => row.IncomeTypeId == income.Id));
        var calculated = await finance.CalculateGarageIncomeWorksheetAsync(garages[3].Id, new(October, October), null, CancellationToken.None);
        Assert.True(calculated.Succeeded, calculated.ErrorMessage);
        Assert.Equal(125m, Assert.Single(calculated.Value!.Rows, row => row.IncomeTypeId == income.Id).AccrualAmount);
        var repeated = await finance.CalculateGarageIncomeWorksheetAsync(garages[3].Id, new(October, October), null, CancellationToken.None);
        Assert.True(repeated.Succeeded, repeated.ErrorMessage);
        Assert.Equal(7, await context.Accruals.CountAsync(row => row.IncomeTypeId == income.Id));
    }
}
