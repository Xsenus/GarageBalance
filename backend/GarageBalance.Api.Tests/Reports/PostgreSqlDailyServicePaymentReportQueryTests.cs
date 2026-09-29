using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlDailyServicePaymentReportQueryTests
{
    [PostgreSqlFact]
    public async Task GeneralReceiptsUseActiveAllocationsAndRemainderWithoutDoubleCountingTypedReceipts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var types = await context.IncomeTypes.Where(type => type.Code == "electricity" || type.Code == "water" || type.Code == "membership").ToDictionaryAsync(type => type.Code!);
        var garage = new Garage { Number = "85" };
        var day = new DateOnly(2046, 9, 17);
        var general = Payment(garage, types["electricity"], day, 100m);
        general.IncomeType = null;
        var typed = Payment(garage, types["water"], day, 45m);
        var fee = Payment(garage, types["electricity"], day, 8m);
        fee.FeeCampaign = new FeeCampaign { Name = "Проверка распределения 2046", IncomeType = types["electricity"], ContributionAmount = 8m, StartsOn = day };
        var irregular = Allocation(general, garage, types["water"], 10m, day);
        irregular.Accrual.IrregularPayment = new IrregularPayment { Name = "Нерегулярное распределение 2046", Amount = 10m };
        var inactive = Allocation(general, garage, types["water"], 1000m, day);
        inactive.IsActive = false;
        var canceled = Allocation(general, garage, types["water"], 1000m, day);
        canceled.Accrual.IsCanceled = true;
        context.AddRange(Allocation(general, garage, types["electricity"], 60m, day),
            Allocation(general, garage, types["membership"], 20m, day), irregular, inactive, canceled,
            Allocation(typed, garage, types["electricity"], 45m, day), Allocation(fee, garage, types["electricity"], 8m, day));
        await context.SaveChangesAsync();
        var report = await new EfDailyServicePaymentReportQuery(context).GetRowsAsync(day, null, 0, 25, CancellationToken.None);
        var amounts = Assert.Single(report.Rows).Amounts;
        Assert.Equal((60m, 45m, 20m, 28m, 153m), (amounts.Electricity, amounts.Water, amounts.Membership, amounts.Other, amounts.Total));
        Assert.Equal(153m, report.MonthTotal.Total);
        Assert.Equal(153m, Assert.Single(report.Days).Amounts.Total);
        Assert.True(report.HasOther);
    }

    [PostgreSqlFact]
    public async Task FullyAllocatedGeneralPaymentDoesNotProduceOtherColumn()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var income = await context.IncomeTypes.SingleAsync(type => type.Code == "electricity");
        var garage = new Garage { Number = "85" };
        var day = new DateOnly(2046, 9, 17);
        var payment = Payment(garage, income, day, 100m);
        payment.IncomeType = null;
        context.Add(Allocation(payment, garage, income, 100m, day));
        await context.SaveChangesAsync();
        var report = await new EfDailyServicePaymentReportQuery(context).GetRowsAsync(day, null, 0, 25, CancellationToken.None);
        Assert.Equal(100m, Assert.Single(report.Rows).Amounts.Electricity);
        Assert.Equal(100m, report.MonthTotal.Total);
        Assert.False(report.HasOther);
        Assert.Equal(0m, report.MonthTotal.Other);
    }

    [PostgreSqlFact]
    public async Task CorruptOverAllocationFailsInsteadOfProducingNegativeOtherOrInflatedTotal()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var income = await context.IncomeTypes.SingleAsync(type => type.Code == "electricity");
        var garage = new Garage { Number = "85" };
        var day = new DateOnly(2046, 9, 17);
        var payment = Payment(garage, income, day, 10m);
        payment.IncomeType = null;
        context.Add(Allocation(payment, garage, income, 11m, day));
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EfDailyServicePaymentReportQuery(context).GetRowsAsync(day, null, 0, 25, CancellationToken.None));
        Assert.Contains("превышает платёж", error.Message, StringComparison.Ordinal);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task ReportGroupsActualPaymentDatesAndServicesWithUnpaginatedDayAndMonthTotals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var codes = new[] { "electricity", "water", "trash", "outdoor_lighting", "membership", "target", "penalty" };
        var types = await context.IncomeTypes.Where(type => codes.Contains(type.Code!)).ToDictionaryAsync(type => type.Code!);
        var garage = new Garage { Number = "85", PeopleCount = 1, FloorCount = 1, IsArchived = true };
        var second = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 };
        var tenth = new Garage { Number = "10", PeopleCount = 1, FloorCount = 1 };
        var day = new DateOnly(2046, 9, 17);
        var receipt = Guid.NewGuid();
        for (var index = 0; index < codes.Length; index++)
        {
            var payment = Payment(garage, types[codes[index]], day, (index + 1) * 10.1m);
            payment.ReceiptBatchId = receipt;
            context.Add(payment);
        }
        var irregular = Payment(garage, types["electricity"], day, 5m);
        irregular.IrregularPayment = new IrregularPayment { Name = "Разовая проверка отчёта 2046", Amount = 5m };
        var fee = Payment(garage, types["water"], day, 6m);
        fee.FeeCampaign = new FeeCampaign { Name = "Сбор проверки отчёта 2046", IncomeType = types["water"], ContributionAmount = 6m, StartsOn = day };
        context.AddRange(irregular, fee,
            Payment(garage, new IncomeType { Name = "Дополнительная услуга 2046", Code = "daily_report_custom" }, day, 7m),
            Payment(garage, new IncomeType { Name = "Услуга без кода 2046" }, day, 8m),
            Payment(second, types["electricity"], day.AddDays(1), 3m),
            Payment(tenth, types["electricity"], day.AddDays(1), 4m),
            Payment(garage, types["electricity"], day.AddDays(2), 1000m),
            Payment(garage, types["electricity"], day.AddMonths(-1), 1000m));
        var canceled = Payment(garage, types["electricity"], day, 1000m);
        canceled.IsCanceled = true;
        var expense = Payment(garage, types["electricity"], day, 1000m);
        expense.OperationKind = FinancialOperationKinds.Expense;
        var noGarage = Payment(null, types["electricity"], day, 1000m);
        context.AddRange(canceled, expense, noGarage);
        await context.SaveChangesAsync();
        var query = new EfDailyServicePaymentReportQuery(context);
        var report = await query.GetRowsAsync(day.AddDays(1), null, 0, 1, CancellationToken.None);
        var row = Assert.Single(report.Rows);
        Assert.Equal(garage.Id, row.GarageId);
        Assert.Equal(day, row.Date);
        Assert.Equal((10.1m, 20.2m, 30.3m, 40.4m, 50.5m, 60.6m, 96.7m),
            (row.Amounts.Electricity, row.Amounts.Water, row.Amounts.Trash, row.Amounts.OutdoorLighting,
                row.Amounts.Membership, row.Amounts.Target, row.Amounts.Other));
        Assert.Equal(308.8m, row.Amounts.Total);
        Assert.Equal(3, report.RowCount);
        Assert.True(report.HasOther);
        Assert.Equal([308.8m, 7m], report.Days.Select(total => total.Amounts.Total).ToArray());
        Assert.Equal(315.8m, report.MonthTotal.Total);
        var page = await query.GetRowsAsync(day.AddDays(1), null, 1, 2, CancellationToken.None);
        Assert.Equal(["2", "10"], page.Rows.Select(item => item.GarageNumber).ToArray());
        Assert.Equal(report.MonthTotal, page.MonthTotal);
        var filtered = await query.GetRowsAsync(day.AddDays(1), second.Id, 0, 10, CancellationToken.None);
        Assert.Single(filtered.Rows);
        Assert.Equal(3m, filtered.MonthTotal.Total);
        Assert.False(filtered.HasOther);
        var emptyPage = await query.GetRowsAsync(day.AddDays(1), null, 100, 10, CancellationToken.None);
        Assert.Empty(emptyPage.Rows);
        Assert.Equal(report.MonthTotal, emptyPage.MonthTotal);
        Assert.Equal(3, emptyPage.RowCount);
    }

    [PostgreSqlFact]
    public async Task ReportHandlesEmptyMonthUnknownGaragePageBoundsAndCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var query = new EfDailyServicePaymentReportQuery(context);
        var report = await query.GetRowsAsync(new DateOnly(2046, 2, 1), Guid.NewGuid(), -1, int.MaxValue, CancellationToken.None);
        Assert.Empty(report.Rows);
        Assert.Empty(report.Days);
        Assert.Equal(0m, report.MonthTotal.Total);
        Assert.Equal(0, report.RowCount);
        Assert.False(report.HasOther);
        Assert.Equal(0, report.Offset);
        Assert.Equal(500, report.Limit);
        var export = await query.GetRowsAsync(new DateOnly(2046, 2, 1), null, 0, int.MaxValue, CancellationToken.None, forExport: true);
        Assert.Equal(5000, export.Limit);
        var defaultPage = await query.GetRowsAsync(new DateOnly(2046, 2, 1), null, 0, 0, CancellationToken.None);
        Assert.Equal(25, defaultPage.Limit);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetRowsAsync(new DateOnly(2046, 2, 1), null, 0, 10, cancellation.Token));
        Assert.Null(context.Database.CurrentTransaction);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await query.GetRowsAsync(new DateOnly(2046, 2, 1), null, 0, 10, CancellationToken.None);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync();
    }

    private static FinancialOperation Payment(Garage? garage, IncomeType income, DateOnly date, decimal amount) => new()
    {
        Garage = garage,
        IncomeType = income,
        OperationKind = FinancialOperationKinds.Income,
        OperationDate = date,
        AccountingMonth = new DateOnly(2045, 1, 1),
        Amount = amount,
    };

    private static AccrualPaymentAllocation Allocation(FinancialOperation payment, Garage garage, IncomeType income, decimal amount, DateOnly day) => new()
    {
        FinancialOperation = payment,
        Amount = amount,
        IsActive = true,
        Accrual = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            Amount = amount,
            AccountingMonth = day.AddMonths(-1),
            DueDate = day,
            OverdueFromDate = day.AddDays(1),
            Source = AccrualSources.Manual,
        },
    };
}
