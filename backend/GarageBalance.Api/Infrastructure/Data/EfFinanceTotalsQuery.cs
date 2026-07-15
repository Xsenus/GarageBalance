using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinanceTotalsQuery(GarageBalanceDbContext dbContext) : IFinanceTotalsQuery
{
    public async Task<FinanceTotalsData> GetAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        CancellationToken cancellationToken)
    {
        var operations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled);
        DateOnly? monthFrom = dateFrom.HasValue ? MonthPeriod.Normalize(dateFrom.Value) : null;
        DateOnly? monthTo = dateTo.HasValue ? MonthPeriod.Normalize(dateTo.Value) : null;
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled);

        if (dateFrom.HasValue)
        {
            operations = operations.Where(operation => operation.OperationDate >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            operations = operations.Where(operation => operation.OperationDate <= dateTo.Value);
        }

        if (operationKind is not null)
        {
            operations = operations.Where(operation => operation.OperationKind == operationKind);
        }

        if (garageId.HasValue)
        {
            operations = operations.Where(operation => operation.GarageId == garageId.Value);
        }

        if (supplierId.HasValue)
        {
            operations = operations.Where(operation => operation.SupplierId == supplierId.Value);
        }

        if (staffMemberId.HasValue)
        {
            operations = operations.Where(operation => operation.StaffMemberId == staffMemberId.Value);
        }

        if (monthFrom.HasValue)
        {
            accruals = accruals.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            accruals = accruals.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
        }

        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var operationRows = await operations
                .Include(operation => operation.Garage)
                .Include(operation => operation.Supplier)
                .Include(operation => operation.StaffMember)
                .ToListAsync(cancellationToken);
            var accrualRows = await accruals
                .Include(accrual => accrual.Garage)
                .Include(accrual => accrual.IncomeType)
                .ToListAsync(cancellationToken);
            return CreateTotals(
                operationRows.Where(operation => OperationMatchesSearch(operation, normalizedSearch)),
                accrualRows.Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch)));
        }

        if (normalizedSearch is not null)
        {
            operations = operations.Where(operation =>
                (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
                (operation.Comment != null && operation.Comment.ToLower().Contains(normalizedSearch)) ||
                (operation.Garage != null && operation.Garage.Number.ToLower().Contains(normalizedSearch)) ||
                (operation.Supplier != null && operation.Supplier.Name.ToLower().Contains(normalizedSearch)) ||
                (operation.StaffMember != null && operation.StaffMember.FullName.ToLower().Contains(normalizedSearch)));
            accruals = accruals.Where(accrual =>
                accrual.Garage.Number.ToLower().Contains(normalizedSearch) ||
                accrual.IncomeType.Name.ToLower().Contains(normalizedSearch) ||
                (accrual.Comment != null && accrual.Comment.ToLower().Contains(normalizedSearch)));
        }

        var operationTotalsQuery = operations
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = 1,
                IncomeTotal = group.Where(operation => operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m,
                ExpenseTotal = group.Where(operation => operation.OperationKind == FinancialOperationKinds.Expense).Sum(operation => (decimal?)operation.Amount) ?? 0m,
                AccrualTotal = 0m,
                OperationCount = group.Count(),
                IncomeCount = group.Count(operation => operation.OperationKind == FinancialOperationKinds.Income),
                ExpenseCount = group.Count(operation => operation.OperationKind == FinancialOperationKinds.Expense),
                AccrualCount = 0
            });
        var accrualTotalsQuery = accruals
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = 2,
                IncomeTotal = 0m,
                ExpenseTotal = 0m,
                AccrualTotal = group.Sum(accrual => (decimal?)accrual.Amount) ?? 0m,
                OperationCount = 0,
                IncomeCount = 0,
                ExpenseCount = 0,
                AccrualCount = group.Count()
            });
        var rows = await operationTotalsQuery
            .Concat(accrualTotalsQuery)
            .ToListAsync(cancellationToken);
        var operationTotals = rows.FirstOrDefault(row => row.Category == 1);
        var accrualTotals = rows.FirstOrDefault(row => row.Category == 2);

        return new FinanceTotalsData(
            operationTotals?.IncomeTotal ?? 0m,
            operationTotals?.ExpenseTotal ?? 0m,
            accrualTotals?.AccrualTotal ?? 0m,
            operationTotals?.OperationCount ?? 0,
            operationTotals?.IncomeCount ?? 0,
            operationTotals?.ExpenseCount ?? 0,
            accrualTotals?.AccrualCount ?? 0);
    }

    private static FinanceTotalsData CreateTotals(
        IEnumerable<FinancialOperation> operations,
        IEnumerable<Accrual> accruals)
    {
        var operationRows = operations.ToList();
        var accrualRows = accruals.ToList();
        var incomeTotal = operationRows
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .Sum(operation => operation.Amount);
        var expenseTotal = operationRows
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .Sum(operation => operation.Amount);
        return new FinanceTotalsData(
            incomeTotal,
            expenseTotal,
            accrualRows.Sum(accrual => accrual.Amount),
            operationRows.Count,
            operationRows.Count(operation => operation.OperationKind == FinancialOperationKinds.Income),
            operationRows.Count(operation => operation.OperationKind == FinancialOperationKinds.Expense),
            accrualRows.Count);
    }

    private static bool OperationMatchesSearch(FinancialOperation operation, string normalizedSearch) =>
        (operation.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Garage?.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.Supplier?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (operation.StaffMember?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool AccrualMatchesSearch(Accrual accrual, string normalizedSearch) =>
        accrual.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.IncomeType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
