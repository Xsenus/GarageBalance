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

        var operationMonthlyQuery = operations
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
            });
        var accrualMonthlyQuery = accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new
            {
                Month = group.Key,
                Kind = "accrual",
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            });
        var readingMonthlyQuery = meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new
            {
                Month = group.Key,
                Kind = "meter-reading",
                Amount = 0m,
                Count = group.Count()
            });
        var garageStartingBalanceQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived && garage.StartingBalance != 0)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Month = periodFrom,
                Kind = "starting-balance",
                Amount = group.Sum(garage => garage.StartingBalance),
                Count = group.Count()
            });
        var monthlyRows = await operationMonthlyQuery
            .Concat(accrualMonthlyQuery)
            .Concat(readingMonthlyQuery)
            .Concat(garageStartingBalanceQuery)
            .ToListAsync(cancellationToken);
        var incomeByMonth = monthlyRows
            .Where(row => row.Kind == FinancialOperationKinds.Income)
            .Select(row => new AmountCountByMonth(row.Month, row.Amount, row.Count))
            .ToList();
        var expenseByMonth = monthlyRows
            .Where(row => row.Kind == FinancialOperationKinds.Expense)
            .Select(row => new AmountCountByMonth(row.Month, row.Amount, row.Count))
            .ToList();
        var incomeBreakdownQuery = operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => new
            {
                operation.IncomeTypeId,
                Name = operation.IncomeType == null ? "Без вида поступления" : operation.IncomeType.Name
            })
            .Select(group => new
            {
                Kind = FinancialOperationKinds.Income,
                TypeId = group.Key.IncomeTypeId,
                group.Key.Name,
                Amount = group.Sum(item => item.Amount)
            });
        var expenseBreakdownQuery = operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => new
            {
                operation.ExpenseTypeId,
                Name = operation.ExpenseType == null ? "Без вида выплаты" : operation.ExpenseType.Name
            })
            .Select(group => new
            {
                Kind = FinancialOperationKinds.Expense,
                TypeId = group.Key.ExpenseTypeId,
                group.Key.Name,
                Amount = group.Sum(item => item.Amount)
            });
        var operationBreakdownRows = incomeByMonth.Count == 0 && expenseByMonth.Count == 0
            ? []
            : await incomeBreakdownQuery
                .Concat(expenseBreakdownQuery)
                .OrderBy(row => row.Kind)
                .ThenBy(row => row.Name)
                .ToListAsync(cancellationToken);
        var incomeBreakdown = operationBreakdownRows
            .Where(row => row.Kind == FinancialOperationKinds.Income)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name, row.Amount))
            .ToList();
        var expenseBreakdown = operationBreakdownRows
            .Where(row => row.Kind == FinancialOperationKinds.Expense)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name, row.Amount))
            .ToList();
        var accrualByMonth = monthlyRows
            .Where(row => row.Kind == "accrual")
            .Select(row => new AmountCountByMonth(row.Month, row.Amount, row.Count))
            .ToList();
        var readingsByMonth = monthlyRows
            .Where(row => row.Kind == "meter-reading")
            .Select(row => new CountByMonth(row.Month, row.Count))
            .ToList();
        var garageStartingBalanceTotal = monthlyRows
            .Where(row => row.Kind == "starting-balance")
            .Sum(row => row.Amount);

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
