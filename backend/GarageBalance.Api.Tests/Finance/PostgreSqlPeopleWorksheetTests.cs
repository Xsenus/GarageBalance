using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlPeopleWorksheetTests
{
    [PostgreSqlFact]
    public async Task PeopleTariffUsesCardCountWithoutMeterEvenWhenLegacySettingStillMarksItMetered()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2026, 8, 1);
        var incomeType = new IncomeType { Name = "Вывоз мусора — контроль", Code = "people_worksheet_test" };
        var tariff = new Tariff
        {
            Name = "Месячная ставка на человека",
            CalculationBase = TariffCalculationBases.People,
            Rate = 125m,
            EffectiveFrom = month
        };
        var setting = new ChargeServiceSetting
        {
            Name = incomeType.Name,
            IncomeType = incomeType,
            Tariff = tariff,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            UnitName = "чел."
        };
        context.Add(setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)));
        var dictionaries = DictionaryServiceTestFactory.Create(context, month);

        foreach (var people in new[] { 1, 2, 5, 0 })
        {
            var created = await dictionaries.CreateGarageAsync(
                new UpsertGarageRequest($"PEOPLE-{people}", people, 1, null, 0, null, null, null),
                null, CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            var garageId = created.Value!.Id;
            var request = new GarageIncomeWorksheetRequest(month, month);
            for (var opening = 0; opening < 2; opening++)
            {
                var result = await finance.CalculateGarageIncomeWorksheetAsync(garageId, request, null, CancellationToken.None);
                Assert.True(result.Succeeded, result.ErrorMessage);
                var rows = result.Value!.Rows.Where(row => row.IncomeTypeId == incomeType.Id).ToArray();
                if (people == 0)
                {
                    Assert.Empty(rows);
                    continue;
                }

                var row = Assert.Single(rows);
                Assert.Equal(125m * people, row.AccrualAmount);
                Assert.Equal(row.AccrualAmount, row.PayableAmount);
                Assert.Null(row.MeterKind);
                Assert.Null(row.MeterReadingId);
                Assert.Null(row.MeterValue);
                Assert.Null(row.MeterConsumption);
                Assert.False(row.CalculationDetails!.RequiresMeter);
                Assert.Equal(people, Assert.Single(row.CalculationDetails.Lines).Quantity);
            }
            Assert.Equal(people == 0 ? 0 : 1,
                await context.Accruals.CountAsync(item => item.GarageId == garageId && item.IncomeTypeId == incomeType.Id && !item.IsCanceled));
        }
        Assert.False(await context.MeterReadings.AnyAsync());
    }

    [PostgreSqlFact]
    public async Task CardChangeRecalculatesUnpaidPeopleAmountButPreservesPartiallyPaidSnapshot()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2026, 8, 1);
        var incomeType = new IncomeType { Name = "Мусор на человека", Code = "people_update_test" };
        var tariff = new Tariff { Name = "На человека", CalculationBase = TariffCalculationBases.People, Rate = 125, EffectiveFrom = month };
        context.Add(new ChargeServiceSetting
        {
            Name = incomeType.Name,
            IncomeType = incomeType,
            Tariff = tariff,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            UnitName = "чел."
        });
        await context.SaveChangesAsync();
        var dictionaries = DictionaryServiceTestFactory.Create(context, month);
        var request = new UpsertGarageRequest("PEOPLE-UPDATE", 1, 1, null, 0, null, null, null);
        var created = await dictionaries.CreateGarageAsync(request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garageId = created.Value!.Id;
        var finance = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)));
        var period = new GarageIncomeWorksheetRequest(month, month);
        var first = await finance.CalculateGarageIncomeWorksheetAsync(garageId, period, null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(125m, Assert.Single(first.Value!.Rows, row => row.IncomeTypeId == incomeType.Id).AccrualAmount);
        var updated = await dictionaries.UpdateGarageAsync(garageId, request with { PeopleCount = 2 }, null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var second = await finance.CalculateGarageIncomeWorksheetAsync(garageId, period, null, CancellationToken.None);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(250m, Assert.Single(second.Value!.Rows, row => row.IncomeTypeId == incomeType.Id).AccrualAmount);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garageId && item.IncomeTypeId == incomeType.Id && !item.IsCanceled);
        var snapshot = accrual.CalculationDetailsJson;
        context.Add(new AccrualPaymentAllocation
        {
            Accrual = accrual,
            Amount = 100m,
            FinancialOperation = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = garageId,
                IncomeType = incomeType,
                AccountingMonth = month,
                OperationDate = month,
                Amount = 100m
            }
        });
        await context.SaveChangesAsync();
        updated = await dictionaries.UpdateGarageAsync(garageId, request with { PeopleCount = 5 }, null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var paid = await finance.CalculateGarageIncomeWorksheetAsync(garageId, period, null, CancellationToken.None);
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        var paidRow = Assert.Single(paid.Value!.Rows, row => row.IncomeTypeId == incomeType.Id);
        Assert.Equal(250m, paidRow.AccrualAmount);
        Assert.Equal(100m, paidRow.IncomeAmount);
        Assert.Equal(150m, paidRow.Debt);
        Assert.Null(paidRow.MeterKind);
        Assert.Equal(snapshot, accrual.CalculationDetailsJson);
        Assert.True(await context.AuditEvents.AnyAsync(audit => audit.Action == "finance.regular_accrual_recalculated_for_garage_worksheet"));

        var currentSetting = await context.ChargeServiceSettings.SingleAsync(item => item.IncomeTypeId == incomeType.Id);
        currentSetting.IsMetered = true;
        currentSetting.MeterKind = MeterKinds.Water;
        tariff.CalculationBase = TariffCalculationBases.MeterWater;
        await context.SaveChangesAsync();
        var reopened = await finance.GetGarageIncomeWorksheetAsync(garageId, period, CancellationToken.None);
        Assert.True(reopened.Succeeded, reopened.ErrorMessage);
        var historicalPeopleRow = Assert.Single(reopened.Value!.Rows, row => row.IncomeTypeId == incomeType.Id);
        Assert.Null(historicalPeopleRow.MeterKind);
        Assert.Null(historicalPeopleRow.MeterValue);
        Assert.Equal(250m, historicalPeopleRow.AccrualAmount);
    }

    [PostgreSqlFact]
    public async Task HistoricalMeterSnapshotKeepsReadingAfterCurrentServiceBecomesNonMetered()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2026, 8, 1);
        var garage = new Garage { Number = "HISTORICAL-METER", PeopleCount = 2 };
        var incomeType = new IncomeType { Name = "Историческая услуга", Code = "historical_meter_test" };
        var reading = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = month,
            ReadingDate = month.AddDays(20),
            PreviousValue = 100,
            CurrentValue = 105,
            Consumption = 5
        };
        var calculation = RegularAccrualCalculator.Calculate(garage, month, reading,
            [new RegularAccrualSegmentDefinition(month, month.AddMonths(1).AddDays(-1), TariffCalculationBases.MeterElectricity, 10m, "кВт·ч", [])]);
        Assert.True(calculation.Succeeded, calculation.ErrorMessage);
        context.AddRange(reading, new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = month,
            Amount = 50,
            Source = AccrualSources.Regular,
            CalculationDetailsJson = RegularAccrualCalculator.Serialize(calculation.Details!)
        }, new ChargeServiceSetting
        {
            Name = incomeType.Name,
            IncomeType = incomeType,
            IsRegular = true,
            IsMetered = false,
            PeriodicityMonths = 1,
            Tariff = new Tariff { Name = "Текущая ставка", CalculationBase = TariffCalculationBases.People, Rate = 125, EffectiveFrom = month.AddMonths(1) }
        });
        await context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(context).GetGarageIncomeWorksheetAsync(
            garage.Id, new GarageIncomeWorksheetRequest(month, month), CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == incomeType.Id);
        Assert.Equal(MeterKinds.Electricity, row.MeterKind);
        Assert.Equal(reading.Id, row.MeterReadingId);
        Assert.Equal(105m, row.MeterValue);
        Assert.Equal(5m, row.MeterConsumption);
        Assert.Equal(50m, row.AccrualAmount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
