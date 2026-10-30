using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageDiscountTests
{
    [PostgreSqlFact]
    public async Task DiscountSettlesOpeningDebtBeforeFutureChargeAndDoesNotOfferItForPaymentAgain()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "DISCOUNT-OPENING", PeopleCount = 1, FloorCount = 1, StartingBalance = 100, StartingOverdueDebt = 100 };
        var income = new IncomeType { Name = "DISCOUNT-OPENING-SERVICE" };
        var penalty = await context.IncomeTypes.SingleAsync(item => item.Code == "penalty" && item.IsSystem);
        context.AddRange(garage, income);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: new(2026, 8, 1));
        Assert.True((await service.CreateAccrualAsync(new(garage.Id, income.Id, new(2026, 8, 1), 100, "manual", "Будущее"), null, default)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(new(garage.Id, penalty.Id, new(2026, 7, 1), -150, "manual", "Скидка"), null, default)).Succeeded);
        Assert.Equal(50m, (await context.AccrualPaymentAllocations.Where(item => item.IsActive).SingleAsync()).Amount);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(new(2026, 7, 1), new(2026, 8, 1)), default);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(50m, worksheet.Value!.ClosingBalance);
        Assert.Equal(0m, worksheet.Value.UnrepresentedOpeningDebt);
        Assert.Equal(50m, worksheet.Value.Rows.Sum(row => row.Debt));
        Assert.Empty(await context.FinancialOperations.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task DiscountPersistsCrossServiceCreditWithoutMoneyAndWorksheetKeepsPaymentsZero()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "DISCOUNT", PeopleCount = 1, FloorCount = 1 };
        var serviceType = new IncomeType { Name = "DISCOUNT-SERVICE" };
        var penalty = await context.IncomeTypes.SingleAsync(item => item.Code == "penalty" && item.IsSystem);
        context.AddRange(garage, serviceType);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: new(2026, 8, 1));
        Assert.True((await service.CreateAccrualAsync(new(garage.Id, serviceType.Id, new(2026, 6, 1), 100, "manual", "Долг"), null, default)).Succeeded);
        var credit = await service.CreateAccrualAsync(new(garage.Id, penalty.Id, new(2026, 7, 1), -150, "manual", "Скидка"), null, default);
        Assert.True(credit.Succeeded, credit.ErrorMessage);
        Assert.True((await service.CreateAccrualAsync(new(garage.Id, serviceType.Id, new(2026, 8, 1), 80, "manual", "Будущее начисление"), null, default)).Succeeded);
        context.ChangeTracker.Clear();
        var allocations = await context.AccrualPaymentAllocations.Where(item => item.IsActive).ToArrayAsync();
        Assert.Equal(150m, allocations.Sum(item => item.Amount));
        Assert.All(allocations, item => { Assert.Null(item.FinancialOperationId); Assert.Equal(credit.Value!.Id, item.CreditAccrualId); });
        Assert.Empty(await context.FinancialOperations.ToArrayAsync());

        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(new(2026, 6, 1), new(2026, 8, 1)), default);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(30m, worksheet.Value!.ClosingBalance);
        Assert.Equal(0m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
        Assert.Equal(150m, worksheet.Value.Rows.Sum(row => row.DiscountAmount));
        Assert.All(worksheet.Value.Rows, row => Assert.Equal(0m, row.IncomeAmount));
        Assert.Equal(30m, worksheet.Value.Rows.Sum(row => row.Debt));
        var fees = await new EfFeeReportQuery(context).GetFeeReportPageAsync([serviceType.Id], false, new ReportSort("garageNumber", false), 0, 25, default);
        Assert.Equal(30m, Assert.Single(fees.GarageRows).Debt);
        Assert.Equal(0m, fees.GarageRows[0].Paid);
        var fallbackData = await new EfFeeReportQuery(context).GetFeeDataAsync([serviceType.Id, penalty.Id], default);
        Assert.Equal(30m, fallbackData.AccrualTotals.Values.Sum());
        Assert.Empty(fallbackData.PaymentsByGarage);
        foreach (var (today, expectedOverdue) in new[] { (new DateOnly(2026, 8, 1), 0m), (new DateOnly(2026, 11, 1), 30m) })
        {
            var repository = new EfGarageRepository(context, new TestBusinessDateProvider(today));
            var totals = await repository.GetBalanceTotalsAsync([garage.Id], default);
            Assert.Equal(30m, totals.AccrualTotals[garage.Id]);
            Assert.Equal(0m, totals.IncomeTotals.GetValueOrDefault(garage.Id));
            var overdue = Math.Max(totals.OverdueAccrualTotals.GetValueOrDefault(garage.Id) -
                (totals.IncomeTotals.GetValueOrDefault(garage.Id) - totals.AllocatedIncomeTotals.GetValueOrDefault(garage.Id)), 0m);
            Assert.Equal(expectedOverdue, overdue);
            var debtors = await repository.GetPageAsync(null, new(null, null, null, null, null), false, true, 0, 25, "overdueDebt", false, default);
            Assert.Equal(expectedOverdue > 0m ? 1 : 0, debtors.TotalCount);
        }
    }

    [PostgreSqlFact]
    public async Task GeneratedOpeningGridUsesJulyAndAugustWithoutChangingPhysicalHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, AccountingStartMonth = new(2026, 7, 1), InitialMeterReadingMonth = new(2026, 9, 1), InitialElectricityMeterValue = 100 };
        var second = new Garage { Number = "20", PeopleCount = 1, FloorCount = 1, AccountingStartMonth = new(2026, 8, 1), InitialMeterReadingMonth = new(2026, 7, 1), InitialElectricityMeterValue = 100 };
        context.AddRange(first, second);
        context.MeterDevices.AddRange(
            new MeterDevice { GarageId = first.Id, MeterKind = MeterKinds.Electricity, SerialNumber = "Начало учёта", InstalledOn = new(2026, 10, 1), InitialValue = 100 },
            new MeterDevice { GarageId = second.Id, MeterKind = MeterKinds.Electricity, SerialNumber = "Начало учёта", InstalledOn = new(2026, 8, 1), InitialValue = 100 });
        await context.SaveChangesAsync();
        var page = await new EfMeterReadingRepository(context).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, default);
        Assert.Equal(new DateOnly(2026, 7, 1), page.Garages.Single(item => item.Number == "12").StartValueDate);
        Assert.Equal(new DateOnly(2026, 8, 1), page.Garages.Single(item => item.Number == "20").StartValueDate);
        Assert.Equal(new DateOnly(2026, 10, 1), (await context.MeterDevices.SingleAsync(item => item.GarageId == first.Id)).InstalledOn);
    }
}
