using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfMeasurementUnitRepository(GarageBalanceDbContext dbContext) : IMeasurementUnitRepository
{
    public async Task<IReadOnlyList<MeasurementUnit>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken) =>
        await ApplySearch(ApplyArchiveFilter(includeArchived), normalizedSearch)
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<MeasurementUnitPageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = ApplySearch(ApplyArchiveFilter(includeArchived), normalizedSearch);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.Name).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new MeasurementUnitPageData(items, totalCount);
    }

    private async Task<MeasurementUnitPageData> GetPostgresPageAsync(
        IQueryable<MeasurementUnit> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .Select(item => new MeasurementUnitPageRow
            {
                Category = PageCategory,
                Id = item.Id,
                Name = item.Name,
                IsArchived = item.IsArchived,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new MeasurementUnitPageRow
            {
                Category = TotalsCategory,
                Id = null,
                Name = null,
                IsArchived = null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new MeasurementUnit
            {
                Id = row.Id!.Value,
                Name = row.Name!,
                IsArchived = row.IsArchived!.Value
            })
            .ToList();
        return new MeasurementUnitPageData(items, totalCount);
    }

    public Task<MeasurementUnit?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MeasurementUnits.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<MeasurementUnit?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MeasurementUnits.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<MeasurementUnit?> FindActiveByNameAsync(string name, CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        if (dbContext.Database.IsNpgsql())
        {
            return dbContext.MeasurementUnits.FirstOrDefaultAsync(
                item => !item.IsArchived && EF.Functions.ILike(
                    item.Name,
                    EF.Functions.Collate(trimmedName, PostgresLikeSearch.UnicodeCollation)),
                cancellationToken);
        }

        var normalizedName = trimmedName.ToLower();
        return dbContext.MeasurementUnits.FirstOrDefaultAsync(
            item => !item.IsArchived && item.Name.ToLower() == normalizedName,
            cancellationToken);
    }

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        if (dbContext.Database.IsNpgsql())
        {
            return dbContext.MeasurementUnits.AsNoTracking().AnyAsync(
                item => !item.IsArchived &&
                    EF.Functions.ILike(item.Name, EF.Functions.Collate(trimmedName, PostgresLikeSearch.UnicodeCollation)) &&
                    (!ignoredId.HasValue || item.Id != ignoredId.Value),
                cancellationToken);
        }

        var normalizedName = trimmedName.ToLower();
        return dbContext.MeasurementUnits.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name.ToLower() == normalizedName && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);
    }

    public Task<bool> HasActiveServiceAssignmentsAsync(string name, CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        if (dbContext.Database.IsNpgsql())
        {
            return dbContext.ChargeServiceSettings.AsNoTracking().AnyAsync(
                item => !item.IsArchived && item.UnitName != null &&
                    EF.Functions.ILike(item.UnitName, EF.Functions.Collate(trimmedName, PostgresLikeSearch.UnicodeCollation)),
                cancellationToken);
        }

        var normalizedName = trimmedName.ToLower();
        return dbContext.ChargeServiceSettings.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.UnitName != null && item.UnitName.ToLower() == normalizedName,
            cancellationToken);
    }

    public async Task RenameServiceAssignmentsAsync(string previousName, string newName, CancellationToken cancellationToken)
    {
        var trimmedPreviousName = previousName.Trim();
        var query = dbContext.ChargeServiceSettings.Where(item => item.UnitName != null);
        var settings = dbContext.Database.IsNpgsql()
            ? await query.Where(item => EF.Functions.ILike(
                    item.UnitName!,
                    EF.Functions.Collate(trimmedPreviousName, PostgresLikeSearch.UnicodeCollation)))
                .ToListAsync(cancellationToken)
            : await query.Where(item => item.UnitName!.ToLower() == trimmedPreviousName.ToLower())
                .ToListAsync(cancellationToken);
        foreach (var setting in settings)
        {
            setting.UnitName = newName;
            setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Add(MeasurementUnit unit) => dbContext.MeasurementUnits.Add(unit);

    private IQueryable<MeasurementUnit> ApplyArchiveFilter(bool includeArchived) =>
        dbContext.MeasurementUnits.AsNoTracking().Where(item => includeArchived || !item.IsArchived);

    private IQueryable<MeasurementUnit> ApplySearch(IQueryable<MeasurementUnit> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        if (dbContext.Database.IsNpgsql())
        {
            var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
            return query.Where(item => EF.Functions.ILike(item.Name, pattern, @"\"));
        }

        return query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
    }

    private sealed class MeasurementUnitPageRow
    {
        public int Category { get; init; }
        public Guid? Id { get; init; }
        public string? Name { get; init; }
        public bool? IsArchived { get; init; }
        public int TotalCount { get; init; }
    }
}
