using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfConsolidatedMonthlyReportQuery(GarageBalanceDbContext dbContext) : IConsolidatedMonthlyReportQuery
{
    public async Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken)
    {
        var operations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.AccountingMonth >= periodFrom && operation.AccountingMonth <= periodTo);
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= periodFrom && accrual.AccountingMonth <= periodTo);
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.AccountingMonth >= periodFrom && reading.AccountingMonth <= periodTo);

        var operationsByMonth = await operations
            .Where(operation =>
                operation.OperationKind == FinancialOperationKinds.Income ||
                operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => new { operation.AccountingMonth, operation.OperationKind })
            .Select(group => new
            {
                Month = group.Key.AccountingMonth,
                Kind = group.Key.OperationKind,
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);
        var incomeByMonth = operationsByMonth
            .Where(row => row.Kind == FinancialOperationKinds.Income)
            .Select(row => new AmountCountByMonth(row.Month, row.Amount, row.Count))
            .ToList();
        var expenseByMonth = operationsByMonth
            .Where(row => row.Kind == FinancialOperationKinds.Expense)
            .Select(row => new AmountCountByMonth(row.Month, row.Amount, row.Count))
            .ToList();
        var incomeBreakdownRows = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => new
            {
                operation.IncomeTypeId,
                Name = operation.IncomeType == null ? "Без вида поступления" : operation.IncomeType.Name
            })
            .Select(group => new
            {
                TypeId = group.Key.IncomeTypeId,
                group.Key.Name,
                Amount = group.Sum(item => item.Amount)
            })
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);
        var incomeBreakdown = incomeBreakdownRows
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name, row.Amount))
            .ToList();
        var expenseBreakdownRows = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => new
            {
                operation.ExpenseTypeId,
                Name = operation.ExpenseType == null ? "Без вида выплаты" : operation.ExpenseType.Name
            })
            .Select(group => new
            {
                TypeId = group.Key.ExpenseTypeId,
                group.Key.Name,
                Amount = group.Sum(item => item.Amount)
            })
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);
        var expenseBreakdown = expenseBreakdownRows
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name, row.Amount))
            .ToList();
        var accrualByMonth = await accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new AmountCountByMonth(group.Key, group.Sum(item => item.Amount), group.Count()))
            .ToListAsync(cancellationToken);
        var readingsByMonth = await meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new CountByMonth(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        var garageStartingBalanceTotal = await dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived && garage.StartingBalance != 0)
            .SumAsync(garage => garage.StartingBalance, cancellationToken);

        return new ConsolidatedMonthlyReportData(
            incomeByMonth,
            expenseByMonth,
            accrualByMonth,
            readingsByMonth,
            garageStartingBalanceTotal,
            incomeBreakdown,
            expenseBreakdown);
    }
}
