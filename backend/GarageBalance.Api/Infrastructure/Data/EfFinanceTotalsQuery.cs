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
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled);
        var supplierAccruals = dbContext.SupplierAccruals.AsNoTracking()
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
            meterReadings = meterReadings.Where(reading => reading.AccountingMonth >= monthFrom.Value);
            supplierAccruals = supplierAccruals.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            accruals = accruals.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
            meterReadings = meterReadings.Where(reading => reading.AccountingMonth <= monthTo.Value);
            supplierAccruals = supplierAccruals.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
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
            var meterRows = await meterReadings
                .Include(reading => reading.Garage)
                .ToListAsync(cancellationToken);
            var supplierRows = await supplierAccruals
                .Include(accrual => accrual.Supplier)
                .Include(accrual => accrual.ExpenseType)
                .ToListAsync(cancellationToken);
            return CreateTotals(
                operationRows.Where(operation => OperationMatchesSearch(operation, normalizedSearch)),
                accrualRows.Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch)),
                meterRows.Where(reading => MeterReadingMatchesSearch(reading, normalizedSearch)),
                supplierRows.Where(accrual => SupplierAccrualMatchesSearch(accrual, normalizedSearch)));
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
            meterReadings = meterReadings.Where(reading =>
                reading.Garage.Number.ToLower().Contains(normalizedSearch) ||
                (reading.Comment != null && reading.Comment.ToLower().Contains(normalizedSearch)));
            supplierAccruals = supplierAccruals.Where(accrual =>
                accrual.Supplier.Name.ToLower().Contains(normalizedSearch) ||
                accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch) ||
                (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
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
                AccrualCount = 0,
                MeterReadingCount = 0,
                SupplierAccrualCount = 0
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
                AccrualCount = group.Count(),
                MeterReadingCount = 0,
                SupplierAccrualCount = 0
            });
        var meterReadingTotalsQuery = meterReadings
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = 3,
                IncomeTotal = 0m,
                ExpenseTotal = 0m,
                AccrualTotal = 0m,
                OperationCount = 0,
                IncomeCount = 0,
                ExpenseCount = 0,
                AccrualCount = 0,
                MeterReadingCount = group.Count(),
                SupplierAccrualCount = 0
            });
        var supplierAccrualTotalsQuery = supplierAccruals
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = 4,
                IncomeTotal = 0m,
                ExpenseTotal = 0m,
                AccrualTotal = 0m,
                OperationCount = 0,
                IncomeCount = 0,
                ExpenseCount = 0,
                AccrualCount = 0,
                MeterReadingCount = 0,
                SupplierAccrualCount = group.Count()
            });
        var rows = await operationTotalsQuery
            .Concat(accrualTotalsQuery)
            .Concat(meterReadingTotalsQuery)
            .Concat(supplierAccrualTotalsQuery)
            .ToListAsync(cancellationToken);
        var operationTotals = rows.FirstOrDefault(row => row.Category == 1);
        var accrualTotals = rows.FirstOrDefault(row => row.Category == 2);
        var meterReadingTotals = rows.FirstOrDefault(row => row.Category == 3);
        var supplierAccrualTotals = rows.FirstOrDefault(row => row.Category == 4);

        return new FinanceTotalsData(
            operationTotals?.IncomeTotal ?? 0m,
            operationTotals?.ExpenseTotal ?? 0m,
            accrualTotals?.AccrualTotal ?? 0m,
            operationTotals?.OperationCount ?? 0,
            operationTotals?.IncomeCount ?? 0,
            operationTotals?.ExpenseCount ?? 0,
            accrualTotals?.AccrualCount ?? 0,
            meterReadingTotals?.MeterReadingCount ?? 0,
            supplierAccrualTotals?.SupplierAccrualCount ?? 0);
    }

    private static FinanceTotalsData CreateTotals(
        IEnumerable<FinancialOperation> operations,
        IEnumerable<Accrual> accruals,
        IEnumerable<MeterReading> meterReadings,
        IEnumerable<SupplierAccrual> supplierAccruals)
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
            accrualRows.Count,
            meterReadings.Count(),
            supplierAccruals.Count());
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

    private static bool MeterReadingMatchesSearch(MeterReading reading, string normalizedSearch) =>
        reading.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (reading.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool SupplierAccrualMatchesSearch(SupplierAccrual accrual, string normalizedSearch) =>
        accrual.Supplier.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.ExpenseType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
