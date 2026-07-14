using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfMeterReadingRepository(GarageBalanceDbContext dbContext) : IMeterReadingRepository
{
    public async Task<IReadOnlyList<MeterReading>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, meterKind);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await Order(query).ToListAsync(cancellationToken))
                .Where(reading => ReadingMatchesSearch(reading, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await Order(ApplySearch(query, normalizedSearch))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<MeterReadingPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, meterKind);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(reading => ReadingMatchesSearch(reading, normalizedSearch))
                .ToList();
            return new MeterReadingPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new MeterReadingPageData(items, totalCount);
    }

    public async Task<MeterReadingYearPageData> GetYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var garageQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived);
        var totalCount = await garageQuery.CountAsync(cancellationToken);
        var garages = await garageQuery
            .OrderBy(garage => garage.Number.Length)
            .ThenBy(garage => garage.Number)
            .ThenBy(garage => garage.Id)
            .Skip(offset)
            .Take(limit)
            .Select(garage => new MeterReadingYearGarageData(garage.Id, garage.Number))
            .ToListAsync(cancellationToken);

        if (garages.Count == 0)
        {
            return new MeterReadingYearPageData(garages, [], totalCount);
        }

        var monthFrom = new DateOnly(year, 1, 1);
        var monthTo = new DateOnly(year, 12, 1);
        var garageIds = garages.Select(garage => garage.Id).ToArray();
        var readings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo &&
                garageIds.Contains(reading.GarageId))
            .OrderBy(reading => reading.GarageId)
            .ThenBy(reading => reading.AccountingMonth)
            .Select(reading => new MeterReadingYearValueData(
                reading.Id,
                reading.GarageId,
                reading.AccountingMonth,
                reading.CurrentValue))
            .ToListAsync(cancellationToken);

        return new MeterReadingYearPageData(garages, readings, totalCount);
    }

    public async Task<IReadOnlyList<MeterReading>> GetForGaragePeriodAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken) =>
        await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo)
            .ToListAsync(cancellationToken);

    public async Task<int> CountActiveAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, null);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .Count(reading => ReadingMatchesSearch(reading, normalizedSearch));
        }

        return await ApplySearch(query, normalizedSearch).CountAsync(cancellationToken);
    }

    public Task<bool> ActiveDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking().AnyAsync(reading =>
            !reading.IsCanceled &&
            (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
            reading.GarageId == garageId &&
            reading.MeterKind == meterKind &&
            reading.AccountingMonth == accountingMonth,
            cancellationToken);

    public Task<MeterReading?> GetPreviousActiveAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth < accountingMonth)
            .OrderByDescending(reading => reading.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterReading?> GetNextActiveAsync(
        Guid? ignoredId,
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                (!ignoredId.HasValue || reading.Id != ignoredId.Value) &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth > accountingMonth)
            .OrderBy(reading => reading.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterReading?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MeterReadings
            .Include(reading => reading.Garage)
            .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(reading => reading.Id == id, cancellationToken);

    public Task<MeterReading?> GetActiveAsync(
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken) =>
        dbContext.MeterReadings.AsNoTracking().SingleOrDefaultAsync(
            reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth == accountingMonth,
            cancellationToken);

    public void Add(MeterReading reading) => dbContext.MeterReadings.Add(reading);

    private IQueryable<MeterReading> QueryActive() =>
        dbContext.MeterReadings.AsNoTracking()
            .Include(reading => reading.Garage)
            .ThenInclude(garage => garage.Owner)
            .Where(reading => !reading.IsCanceled);

    private static IQueryable<MeterReading> ApplyFilters(
        IQueryable<MeterReading> query,
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? meterKind)
    {
        if (monthFrom.HasValue)
        {
            query = query.Where(reading => reading.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            query = query.Where(reading => reading.AccountingMonth <= monthTo.Value);
        }

        if (meterKind is not null)
        {
            query = query.Where(reading => reading.MeterKind == meterKind);
        }

        return query;
    }

    private static IQueryable<MeterReading> ApplySearch(IQueryable<MeterReading> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(reading =>
            reading.Garage.Number.ToLower().Contains(normalizedSearch) ||
            (reading.Comment != null && reading.Comment.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<MeterReading> Order(IQueryable<MeterReading> query) =>
        query.OrderByDescending(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Garage.Number)
            .ThenBy(reading => reading.MeterKind);

    private static bool ReadingMatchesSearch(MeterReading reading, string normalizedSearch) =>
        reading.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (reading.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
