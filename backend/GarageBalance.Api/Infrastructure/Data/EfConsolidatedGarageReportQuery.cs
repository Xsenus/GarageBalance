using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfConsolidatedGarageReportQuery(GarageBalanceDbContext dbContext) : IConsolidatedGarageReportQuery
{
    public Task<ConsolidatedGarageRowsData> GetGarageRowsAsync(
        string? search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(search)
            ? GetRowsWithoutSearchAsync(periodFrom, periodTo, limit, cancellationToken)
            : GetRowsWithSearchAsync(search, periodFrom, periodTo, limit, cancellationToken);
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithoutSearchAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived)
            .Select(garage => new
            {
                GarageId = garage.Id,
                GarageNumber = garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                IncomeTotal = dbContext.FinancialOperations.AsNoTracking()
                    .Where(operation =>
                        !operation.IsCanceled &&
                        operation.OperationKind == FinancialOperationKinds.Income &&
                        operation.GarageId == garage.Id &&
                        operation.AccountingMonth >= periodFrom &&
                        operation.AccountingMonth <= periodTo)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m,
                AccrualTotal = garage.StartingBalance + (dbContext.Accruals.AsNoTracking()
                    .Where(accrual =>
                        !accrual.IsCanceled &&
                        accrual.GarageId == garage.Id &&
                        accrual.AccountingMonth >= periodFrom &&
                        accrual.AccountingMonth <= periodTo)
                    .Sum(accrual => (decimal?)accrual.Amount) ?? 0m),
                MeterReadingCount = dbContext.MeterReadings.AsNoTracking()
                    .Count(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth >= periodFrom &&
                        reading.AccountingMonth <= periodTo)
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .OrderBy(row => row.GarageNumber);

        var rowCount = await query.CountAsync(cancellationToken);
        var rows = await ApplyLimit(query, limit).ToListAsync(cancellationToken);
        return new ConsolidatedGarageRowsData(
            rowCount,
            rows.Select(row => new ConsolidatedGarageRowData(
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName,
                    row.IncomeTotal,
                    row.AccrualTotal,
                    row.MeterReadingCount))
                .ToList());
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithSearchAsync(
        string search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var garages = await dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        var normalized = search.Trim();
        garages = garages.Where(garage =>
                garage.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (garage.Owner?.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        var garageIds = garages.Select(garage => garage.Id).ToList();

        var incomeByGarage = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.AccountingMonth >= periodFrom &&
                operation.AccountingMonth <= periodTo)
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var accrualByGarage = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new AmountByGarage(group.Key, group.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var readingsByGarage = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.AccountingMonth >= periodFrom &&
                reading.AccountingMonth <= periodTo)
            .GroupBy(reading => reading.GarageId)
            .Select(group => new CountByGarage(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var rows = garages.Select(garage =>
            {
                var income = incomeByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var accrual = accrualByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Amount;
                var readings = readingsByGarage.SingleOrDefault(row => row.GarageId == garage.Id).Count;
                return new ConsolidatedGarageRowData(
                    garage.Id,
                    garage.Number,
                    garage.Owner?.LastName,
                    garage.Owner?.FirstName,
                    garage.Owner?.MiddleName,
                    income,
                    accrual + garage.StartingBalance,
                    readings);
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .ToList();
        return new ConsolidatedGarageRowsData(rows.Count, ApplyLimit(rows, limit).ToList());
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> rows, int? limit) =>
        limit is > 0 ? rows.Take(limit.Value) : rows;
}
