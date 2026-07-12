using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfMissingMeterReadingQuery(GarageBalanceDbContext dbContext) : IMissingMeterReadingQuery
{
    public async Task<IReadOnlyList<MissingMeterReadingData>> GetMissingAsync(
        DateOnly accountingMonth,
        IReadOnlyList<string> meterKinds,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        if (meterKinds.Count == 0)
        {
            return [];
        }

        var missingGarageIdsByKind = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var meterKind in meterKinds)
        {
            var query = dbContext.Garages.AsNoTracking()
                .Where(garage => !garage.IsArchived)
                .Where(garage => !dbContext.MeterReadings.Any(reading =>
                    !reading.IsCanceled &&
                    reading.GarageId == garage.Id &&
                    reading.AccountingMonth == accountingMonth &&
                    reading.MeterKind == meterKind));
            IReadOnlyList<Guid> garageIds;
            if (normalizedSearch is not null && IsSqliteProvider())
            {
                garageIds = (await query
                        .Include(garage => garage.Owner)
                        .OrderBy(garage => garage.Number)
                        .ToListAsync(cancellationToken))
                    .Where(garage => GarageMatchesSearch(garage, normalizedSearch))
                    .Take(limit)
                    .Select(garage => garage.Id)
                    .ToList();
            }
            else
            {
                garageIds = await ApplySearch(query, normalizedSearch)
                    .OrderBy(garage => garage.Number)
                    .Take(limit)
                    .Select(garage => garage.Id)
                    .ToListAsync(cancellationToken);
            }

            missingGarageIdsByKind[meterKind] = garageIds.ToHashSet();
        }

        var candidateGarageIds = missingGarageIdsByKind.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToArray();
        if (candidateGarageIds.Length == 0)
        {
            return [];
        }

        var garages = await dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => candidateGarageIds.Contains(garage.Id))
            .OrderBy(garage => garage.Number)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return garages
            .SelectMany(garage => meterKinds
                .Where(meterKind => missingGarageIdsByKind[meterKind].Contains(garage.Id))
                .Select(meterKind => new MissingMeterReadingData(
                    garage.Id,
                    garage.Number,
                    garage.Owner?.FullName,
                    meterKind)))
            .Take(limit)
            .ToList();
    }

    private static IQueryable<Garage> ApplySearch(IQueryable<Garage> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(garage =>
            garage.Number.ToLower().Contains(normalizedSearch) ||
            (garage.Owner != null && (
                garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch))));
    }

    private static bool GarageMatchesSearch(Garage garage, string normalizedSearch)
    {
        if (garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (garage.Owner is null)
        {
            return false;
        }

        return garage.Owner.LastName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            garage.Owner.FirstName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            (garage.Owner.MiddleName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
            garage.Owner.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
