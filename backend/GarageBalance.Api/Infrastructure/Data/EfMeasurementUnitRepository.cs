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
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.Name).Skip(offset).Take(limit).ToListAsync(cancellationToken);
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

    private static IQueryable<MeasurementUnit> ApplySearch(IQueryable<MeasurementUnit> query, string? normalizedSearch) =>
        normalizedSearch is null ? query : query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
}
