using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfConsolidatedMonthlyReportQuery(GarageBalanceDbContext dbContext) : IConsolidatedMonthlyReportQuery
{
    private const int OperationMonthlyCategory = 1;
    private const int AccrualMonthlyCategory = 2;
    private const int MeterReadingMonthlyCategory = 3;
    private const int StartingBalanceCategory = 4;
    private const int IncomeBreakdownCategory = 5;
    private const int ExpenseBreakdownCategory = 6;

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
                Category = OperationMonthlyCategory,
                Month = (DateOnly?)group.Key.AccountingMonth,
                Kind = (string?)group.Key.OperationKind,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            });
        var accrualMonthlyQuery = accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new
            {
                Category = AccrualMonthlyCategory,
                Month = (DateOnly?)group.Key,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            });
        var readingMonthlyQuery = meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new
            {
                Category = MeterReadingMonthlyCategory,
                Month = (DateOnly?)group.Key,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = 0m,
                Count = group.Count()
            });
        var garageStartingBalanceQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived && garage.StartingBalance != 0)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = StartingBalanceCategory,
                Month = (DateOnly?)periodFrom,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(garage => garage.StartingBalance),
                Count = group.Count()
            });
        var incomeBreakdownQuery = operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => new
            {
                operation.IncomeTypeId,
                Name = operation.IncomeType == null ? "Без вида поступления" : operation.IncomeType.Name
            })
            .Select(group => new
            {
                Category = IncomeBreakdownCategory,
                Month = (DateOnly?)null,
                Kind = (string?)null,
                TypeId = group.Key.IncomeTypeId,
                Name = (string?)group.Key.Name,
                Amount = group.Sum(item => item.Amount),
                Count = 0
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
                Category = ExpenseBreakdownCategory,
                Month = (DateOnly?)null,
                Kind = (string?)null,
                TypeId = group.Key.ExpenseTypeId,
                Name = (string?)group.Key.Name,
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });

        var rows = await operationMonthlyQuery
            .Concat(accrualMonthlyQuery)
            .Concat(readingMonthlyQuery)
            .Concat(garageStartingBalanceQuery)
            .Concat(incomeBreakdownQuery)
            .Concat(expenseBreakdownQuery)
            .ToListAsync(cancellationToken);
        var incomeByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Income)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var expenseByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Expense)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var incomeBreakdown = rows
            .Where(row => row.Category == IncomeBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var expenseBreakdown = rows
            .Where(row => row.Category == ExpenseBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var accrualByMonth = rows
            .Where(row => row.Category == AccrualMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var readingsByMonth = rows
            .Where(row => row.Category == MeterReadingMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new CountByMonth(row.Month!.Value, row.Count))
            .ToList();
        var garageStartingBalanceTotal = rows
            .Where(row => row.Category == StartingBalanceCategory)
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
