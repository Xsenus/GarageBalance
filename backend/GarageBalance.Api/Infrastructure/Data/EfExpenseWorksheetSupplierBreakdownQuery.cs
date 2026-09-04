using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

internal static class EfExpenseWorksheetSupplierBreakdownQuery
{
    public static async Task<ExpenseWorksheetSupplierBreakdownData> GetAsync(
        GarageBalanceDbContext dbContext,
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly monthFrom,
        DateOnly monthTo,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var accruals = dbContext.SupplierAccruals
            .AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.SupplierId == supplierId &&
                accrual.ExpenseTypeId == expenseTypeId &&
                accrual.AccountingMonth >= monthFrom &&
                accrual.AccountingMonth <= monthTo);
        var expenses = dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.SupplierId == supplierId &&
                operation.ExpenseTypeId == expenseTypeId &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo);

        var accrualSummary = await accruals
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Amount = group.Sum(item => item.Amount) })
            .SingleOrDefaultAsync(cancellationToken);
        var expenseSummary = await expenses
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Amount = group.Sum(item => item.Amount) })
            .SingleOrDefaultAsync(cancellationToken);

        var accrualEntries = accruals.Select(accrual => new
        {
            accrual.Id,
            EntryKind = "accrual",
            accrual.AccountingMonth,
            OperationDate = (DateOnly?)null,
            accrual.Amount,
            accrual.DocumentNumber,
            accrual.Comment,
            Source = (string?)accrual.Source,
            accrual.CreatedAtUtc,
            ExpensePaymentType = (string?)null,
            ExpensePaymentSource = (string?)null,
            ExpenseFundId = (Guid?)null,
            CounterpartyName = (string?)null,
            Version = (Guid?)null
        });
        var expenseEntries = expenses.Select(operation => new
        {
            operation.Id,
            EntryKind = "payment",
            operation.AccountingMonth,
            OperationDate = (DateOnly?)operation.OperationDate,
            operation.Amount,
            operation.DocumentNumber,
            operation.Comment,
            Source = operation.ExpensePaymentSource,
            operation.CreatedAtUtc,
            operation.ExpensePaymentType,
            operation.ExpensePaymentSource,
            operation.ExpenseFundId,
            operation.CounterpartyName,
            Version = (Guid?)operation.Version
        });
        var rawItems = await accrualEntries
            .Concat(expenseEntries)
            .OrderByDescending(item => item.AccountingMonth)
            .ThenByDescending(item => item.OperationDate)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var items = rawItems.Select(item => new ExpenseWorksheetSupplierBreakdownEntryData(
            item.Id,
            item.EntryKind,
            item.AccountingMonth,
            item.OperationDate,
            item.Amount,
            item.DocumentNumber,
            item.Comment,
            item.Source,
            item.CreatedAtUtc,
            ExpensePaymentType: item.ExpensePaymentType,
            ExpensePaymentSource: item.ExpensePaymentSource,
            ExpenseFundId: item.ExpenseFundId,
            CounterpartyName: item.CounterpartyName,
            Version: item.Version)).ToList();

        return new ExpenseWorksheetSupplierBreakdownData(
            items,
            (accrualSummary?.Count ?? 0) + (expenseSummary?.Count ?? 0),
            accrualSummary?.Amount ?? 0m,
            expenseSummary?.Amount ?? 0m);
    }
}
