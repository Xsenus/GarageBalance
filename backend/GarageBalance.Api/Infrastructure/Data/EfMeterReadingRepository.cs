using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new MeterReadingPageData(items, totalCount);
    }

    private async Task<MeterReadingPageData> GetPostgresPageAsync(
        IQueryable<MeterReading> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(reading => new
            {
                Category = PageCategory,
                Id = (Guid?)reading.Id,
                GarageId = (Guid?)reading.GarageId,
                GarageNumber = (string?)reading.Garage.Number,
                OwnerId = reading.Garage.OwnerId,
                OwnerLastName = reading.Garage.Owner == null ? null : reading.Garage.Owner.LastName,
                OwnerFirstName = reading.Garage.Owner == null ? null : reading.Garage.Owner.FirstName,
                OwnerMiddleName = reading.Garage.Owner == null ? null : reading.Garage.Owner.MiddleName,
                MeterKind = (string?)reading.MeterKind,
                AccountingMonth = (DateOnly?)reading.AccountingMonth,
                ReadingDate = (DateOnly?)reading.ReadingDate,
                CurrentValue = (decimal?)reading.CurrentValue,
                PreviousValue = (decimal?)reading.PreviousValue,
                Consumption = (decimal?)reading.Consumption,
                HasGapWarning = (bool?)reading.HasGapWarning,
                Comment = reading.Comment,
                IsCanceled = (bool?)reading.IsCanceled,
                Version = (Guid?)reading.Version,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                OwnerId = (Guid?)null,
                OwnerLastName = (string?)null,
                OwnerFirstName = (string?)null,
                OwnerMiddleName = (string?)null,
                MeterKind = (string?)null,
                AccountingMonth = (DateOnly?)null,
                ReadingDate = (DateOnly?)null,
                CurrentValue = (decimal?)null,
                PreviousValue = (decimal?)null,
                Consumption = (decimal?)null,
                HasGapWarning = (bool?)null,
                Comment = (string?)null,
                IsCanceled = (bool?)null,
                Version = (Guid?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.AccountingMonth)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.MeterKind)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new MeterReading
            {
                Id = row.Id!.Value,
                GarageId = row.GarageId!.Value,
                Garage = new Garage
                {
                    Id = row.GarageId.Value,
                    Number = row.GarageNumber!,
                    OwnerId = row.OwnerId,
                    Owner = row.OwnerId is null
                        ? null
                        : new Owner
                        {
                            Id = row.OwnerId.Value,
                            LastName = row.OwnerLastName!,
                            FirstName = row.OwnerFirstName!,
                            MiddleName = row.OwnerMiddleName
                        }
                },
                MeterKind = row.MeterKind!,
                AccountingMonth = row.AccountingMonth!.Value,
                ReadingDate = row.ReadingDate!.Value,
                CurrentValue = row.CurrentValue!.Value,
                PreviousValue = row.PreviousValue!.Value,
                Consumption = row.Consumption!.Value,
                HasGapWarning = row.HasGapWarning!.Value,
                Comment = row.Comment,
                IsCanceled = row.IsCanceled!.Value,
                Version = row.Version!.Value
            })
            .ToList();
        return new MeterReadingPageData(items, totalCount);
    }

    public async Task<MeterReadingYearPageData> GetYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresYearPageAsync(year, meterKind, offset, limit, cancellationToken);
        }

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
                reading.CurrentValue,
                reading.Version))
            .ToListAsync(cancellationToken);

        return new MeterReadingYearPageData(garages, readings, totalCount);
    }

    private async Task<MeterReadingYearPageData> GetPostgresYearPageAsync(
        int year,
        string meterKind,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var monthFrom = new DateOnly(year, 1, 1);
        var monthTo = new DateOnly(year, 12, 1);
        var rows = await dbContext.Database.SqlQuery<MeterReadingYearPageRow>($$"""
            WITH paged_garages AS (
                SELECT
                    garage."Id" AS "GarageId",
                    garage."Number" AS "GarageNumber",
                    ROW_NUMBER() OVER (
                        ORDER BY LENGTH(garage."Number"), garage."Number", garage."Id") AS "GarageOrdinal",
                    COUNT(*) OVER () AS "TotalCount"
                FROM garages AS garage
                WHERE garage."IsArchived" = FALSE
                ORDER BY LENGTH(garage."Number"), garage."Number", garage."Id"
                OFFSET {{offset}}
                LIMIT {{limit}}
            )
            SELECT
                1 AS "Category",
                paged_garage."GarageId",
                paged_garage."GarageNumber",
                paged_garage."GarageOrdinal",
                reading."Id" AS "ReadingId",
                reading."AccountingMonth",
                reading."CurrentValue",
                reading."Version",
                paged_garage."TotalCount"
            FROM paged_garages AS paged_garage
            LEFT JOIN meter_readings AS reading
                ON reading."GarageId" = paged_garage."GarageId"
               AND reading."IsCanceled" = FALSE
               AND reading."MeterKind" = {{meterKind}}
               AND reading."AccountingMonth" >= {{monthFrom}}
               AND reading."AccountingMonth" <= {{monthTo}}

            UNION ALL

            SELECT
                2 AS "Category",
                NULL::uuid AS "GarageId",
                NULL::text AS "GarageNumber",
                NULL::bigint AS "GarageOrdinal",
                NULL::uuid AS "ReadingId",
                NULL::date AS "AccountingMonth",
                NULL::numeric AS "CurrentValue",
                NULL::uuid AS "Version",
                (SELECT COUNT(*) FROM garages AS garage WHERE garage."IsArchived" = FALSE) AS "TotalCount"
            WHERE NOT EXISTS (SELECT 1 FROM paged_garages)
            ORDER BY "Category", "GarageOrdinal", "AccountingMonth", "ReadingId"
            """).ToListAsync(cancellationToken);

        var totalCount = checked((int)(rows.FirstOrDefault()?.TotalCount ?? 0));
        var garages = rows
            .Where(row => row.Category == 1)
            .GroupBy(row => new { row.GarageId, row.GarageNumber, row.GarageOrdinal })
            .OrderBy(group => group.Key.GarageOrdinal)
            .Select(group => new MeterReadingYearGarageData(group.Key.GarageId!.Value, group.Key.GarageNumber!))
            .ToList();
        var readings = rows
            .Where(row => row.Category == 1 && row.ReadingId is not null)
            .OrderBy(row => row.GarageId)
            .ThenBy(row => row.AccountingMonth)
            .Select(row => new MeterReadingYearValueData(
                row.ReadingId!.Value,
                row.GarageId!.Value,
                row.AccountingMonth!.Value,
                row.CurrentValue!.Value,
                row.Version!.Value))
            .ToList();
        return new MeterReadingYearPageData(garages, readings, totalCount);
    }

    private sealed class MeterReadingYearPageRow
    {
        public int Category { get; set; }
        public Guid? GarageId { get; set; }
        public string? GarageNumber { get; set; }
        public long? GarageOrdinal { get; set; }
        public Guid? ReadingId { get; set; }
        public DateOnly? AccountingMonth { get; set; }
        public decimal? CurrentValue { get; set; }
        public Guid? Version { get; set; }
        public long TotalCount { get; set; }
    }

    public async Task<IReadOnlyDictionary<Guid, MeterReading>> GetActiveByGarageIdsAsync(
        IReadOnlyCollection<Guid> garageIds,
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        if (garageIds.Count == 0)
        {
            return new Dictionary<Guid, MeterReading>();
        }

        return await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.MeterKind == meterKind &&
                reading.AccountingMonth == accountingMonth)
            .ToDictionaryAsync(reading => reading.GarageId, cancellationToken);
    }

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
