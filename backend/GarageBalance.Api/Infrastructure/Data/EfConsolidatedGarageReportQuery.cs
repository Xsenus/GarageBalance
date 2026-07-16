using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
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
        if (string.IsNullOrWhiteSpace(search))
        {
            return GetRowsWithoutSearchAsync(periodFrom, periodTo, limit, cancellationToken);
        }

        return IsNpgsql()
            ? GetRowsWithServerSearchAsync(search, periodFrom, periodTo, limit, cancellationToken)
            : GetRowsWithClientSearchAsync(search, periodFrom, periodTo, limit, cancellationToken);
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithoutSearchAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var garages = dbContext.Garages.AsNoTracking().Where(garage => !garage.IsArchived);
        return await ExecuteBoundedRowsAsync(
            BuildRowsQuery(garages, periodFrom, periodTo),
            limit,
            cancellationToken);
    }

    private Task<ConsolidatedGarageRowsData> GetRowsWithServerSearchAsync(
        string search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedServerSearch = search.Trim().ToLowerInvariant();
        var garages = dbContext.Garages.AsNoTracking()
            .Where(garage =>
                !garage.IsArchived &&
                (garage.Number.ToLower().Contains(normalizedServerSearch) ||
                 (garage.Owner != null && (
                     garage.Owner.LastName.ToLower().Contains(normalizedServerSearch) ||
                     garage.Owner.FirstName.ToLower().Contains(normalizedServerSearch) ||
                     (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedServerSearch)) ||
                     (garage.Owner.LastName + " " + garage.Owner.FirstName).ToLower().Contains(normalizedServerSearch) ||
                     (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedServerSearch)))));

        return ExecuteBoundedRowsAsync(
            BuildRowsQuery(garages, periodFrom, periodTo),
            limit,
            cancellationToken);
    }

    private IQueryable<ConsolidatedGarageProjectionRow> BuildRowsQuery(
        IQueryable<Garage> garages,
        DateOnly periodFrom,
        DateOnly periodTo) =>
        garages
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
            .OrderBy(row => row.GarageNumber)
            .Select(row => new ConsolidatedGarageProjectionRow(
                row.GarageId,
                row.GarageNumber,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTotal,
                row.AccrualTotal,
                row.MeterReadingCount));

    private static async Task<ConsolidatedGarageRowsData> ExecuteBoundedRowsAsync(
        IQueryable<ConsolidatedGarageProjectionRow> query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var rowCount = await query.CountAsync(cancellationToken);
        var rows = await ApplyLimit(query, limit).ToListAsync(cancellationToken);
        return new ConsolidatedGarageRowsData(
            rowCount,
            rows.Select(row => row.ToData()).ToList());
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithClientSearchAsync(
        string search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalized = search.Trim();
        var garageQuery = dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived);
        var garages = (await garageQuery.OrderBy(garage => garage.Number).ToListAsync(cancellationToken))
            .Where(garage =>
                garage.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (garage.Owner?.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        var garageIds = garages.Select(garage => garage.Id).ToList();

        var incomeByGarageQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.AccountingMonth >= periodFrom &&
                operation.AccountingMonth <= periodTo)
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "income",
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });
        var accrualByGarageQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "accrual",
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });
        var readingsByGarageQuery = dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.AccountingMonth >= periodFrom &&
                reading.AccountingMonth <= periodTo)
            .GroupBy(reading => reading.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "reading",
                Amount = 0m,
                Count = group.Count()
            });
        var aggregateRows = await incomeByGarageQuery
            .Concat(accrualByGarageQuery)
            .Concat(readingsByGarageQuery)
            .ToListAsync(cancellationToken);
        var incomeLookup = aggregateRows
            .Where(row => row.Kind == "income")
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var accrualLookup = aggregateRows
            .Where(row => row.Kind == "accrual")
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var readingsLookup = aggregateRows
            .Where(row => row.Kind == "reading")
            .ToDictionary(row => row.GarageId, row => row.Count);

        var rows = garages.Select(garage =>
            {
                incomeLookup.TryGetValue(garage.Id, out var income);
                accrualLookup.TryGetValue(garage.Id, out var accrual);
                readingsLookup.TryGetValue(garage.Id, out var readings);
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

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

    private sealed record ConsolidatedGarageProjectionRow(
        Guid GarageId,
        string GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        decimal IncomeTotal,
        decimal AccrualTotal,
        int MeterReadingCount)
    {
        public ConsolidatedGarageRowData ToData() =>
            new(GarageId, GarageNumber, OwnerLastName, OwnerFirstName, OwnerMiddleName, IncomeTotal, AccrualTotal, MeterReadingCount);
    }
}
