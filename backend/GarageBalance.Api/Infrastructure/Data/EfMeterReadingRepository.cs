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
