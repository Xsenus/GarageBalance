using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfOwnerRepository(GarageBalanceDbContext dbContext) : IOwnerRepository
{
    public async Task<IReadOnlyList<Owner>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyFilters(normalizedSearch, includeArchived)
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<OwnerPageData> GetPageAsync(
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new OwnerPageData(items, totalCount);
    }

    public Task<Owner?> FindActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Owners.SingleOrDefaultAsync(owner => owner.Id == id && !owner.IsArchived, cancellationToken);
    }

    public Task<Owner?> FindArchivedWithGaragesAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Owners.Include(owner => owner.Garages)
            .SingleOrDefaultAsync(owner => owner.Id == id && owner.IsArchived, cancellationToken);
    }

    public void Add(Owner owner)
    {
        dbContext.Owners.Add(owner);
    }

    private IQueryable<Owner> ApplyFilters(string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Owners.AsNoTracking()
            .Include(owner => owner.Garages)
            .Where(owner => includeArchived || !owner.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(owner =>
                owner.LastName.ToLower().Contains(normalizedSearch) ||
                owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (owner.MiddleName != null && owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (owner.Phone != null && owner.Phone.ToLower().Contains(normalizedSearch)));
        }

        return query;
    }
}
