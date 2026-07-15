using GarageBalance.Api.Application.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinanceSectionCountQuery(GarageBalanceDbContext dbContext) : IFinanceSectionCountQuery
{
    public async Task<FinanceSectionCountData> GetAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        CancellationToken cancellationToken)
    {
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled);
        var supplierAccruals = dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled);

        if (monthFrom.HasValue)
        {
            meterReadings = meterReadings.Where(reading => reading.AccountingMonth >= monthFrom.Value);
            supplierAccruals = supplierAccruals.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            meterReadings = meterReadings.Where(reading => reading.AccountingMonth <= monthTo.Value);
            supplierAccruals = supplierAccruals.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
        }

        if (normalizedSearch is not null)
        {
            if (IsSqliteProvider())
            {
                var meterRows = await meterReadings
                    .Include(reading => reading.Garage)
                    .ToListAsync(cancellationToken);
                var supplierRows = await supplierAccruals
                    .Include(accrual => accrual.Supplier)
                    .Include(accrual => accrual.ExpenseType)
                    .ToListAsync(cancellationToken);
                return new FinanceSectionCountData(
                    meterRows.Count(reading =>
                        reading.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (reading.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false)),
                    supplierRows.Count(accrual =>
                        accrual.Supplier.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        accrual.ExpenseType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (accrual.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false)));
            }

            meterReadings = meterReadings.Where(reading =>
                reading.Garage.Number.ToLower().Contains(normalizedSearch) ||
                (reading.Comment != null && reading.Comment.ToLower().Contains(normalizedSearch)));
            supplierAccruals = supplierAccruals.Where(accrual =>
                accrual.Supplier.Name.ToLower().Contains(normalizedSearch) ||
                accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch) ||
                (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
                (accrual.Comment != null && accrual.Comment.ToLower().Contains(normalizedSearch)));
        }

        var encodedCounts = await meterReadings
            .GroupBy(_ => 1)
            .Select(group => group.Count())
            .Concat(supplierAccruals
                .GroupBy(_ => 1)
                .Select(group => -group.Count()))
            .ToListAsync(cancellationToken);

        return new FinanceSectionCountData(
            encodedCounts.FirstOrDefault(count => count > 0),
            -encodedCounts.FirstOrDefault(count => count < 0));
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
