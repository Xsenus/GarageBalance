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
            .Include(owner => owner.Garages)
            .AsSplitQuery()
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .ThenBy(owner => owner.Id)
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
        if (IsNpgsqlProvider())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(owner => owner.Garages)
            .AsSplitQuery()
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .ThenBy(owner => owner.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new OwnerPageData(items, totalCount);
    }

    private async Task<OwnerPageData> GetPostgresPageAsync(
        IQueryable<Owner> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageOwners = query
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .ThenBy(owner => owner.Id)
            .Skip(offset)
            .Take(limit);
        var pageRows =
            from owner in pageOwners
            join garage in dbContext.Garages.AsNoTracking().Where(garage => !garage.IsArchived)
                on owner.Id equals garage.OwnerId into garages
            from garage in garages.DefaultIfEmpty()
            select new
            {
                Category = PageCategory,
                OwnerId = (Guid?)owner.Id,
                LastName = (string?)owner.LastName,
                FirstName = (string?)owner.FirstName,
                owner.MiddleName,
                owner.Phone,
                owner.Address,
                owner.MeterNotes,
                OwnerIsArchived = (bool?)owner.IsArchived,
                GarageId = garage == null ? null : (Guid?)garage.Id,
                GarageNumber = garage == null ? null : garage.Number,
                TotalCount = 0
            };
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                OwnerId = (Guid?)null,
                LastName = (string?)null,
                FirstName = (string?)null,
                MiddleName = (string?)null,
                Phone = (string?)null,
                Address = (string?)null,
                MeterNotes = (string?)null,
                OwnerIsArchived = (bool?)null,
                GarageId = (Guid?)null,
                GarageNumber = (string?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenBy(row => row.LastName)
            .ThenBy(row => row.FirstName)
            .ThenBy(row => row.OwnerId)
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.GarageId)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var owners = rows
            .Where(row => row.Category == PageCategory)
            .GroupBy(row => row.OwnerId!.Value)
            .Select(group =>
            {
                var first = group.First();
                var owner = new Owner
                {
                    Id = first.OwnerId!.Value,
                    LastName = first.LastName!,
                    FirstName = first.FirstName!,
                    MiddleName = first.MiddleName,
                    Phone = first.Phone,
                    Address = first.Address,
                    MeterNotes = first.MeterNotes,
                    IsArchived = first.OwnerIsArchived!.Value
                };
                owner.Garages = group
                    .Where(row => row.GarageId.HasValue)
                    .Select(row => new Garage
                    {
                        Id = row.GarageId!.Value,
                        Number = row.GarageNumber!,
                        OwnerId = owner.Id,
                        Owner = owner
                    })
                    .ToList();
                return owner;
            })
            .ToList();
        return new OwnerPageData(owners, totalCount);
    }

    public Task<Owner?> FindActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Owners.SingleOrDefaultAsync(owner => owner.Id == id && !owner.IsArchived, cancellationToken);
    }

    public Task<Owner?> FindArchivedWithGaragesAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Owners
            .Include(owner => owner.Garages)
            .AsSplitQuery()
            .SingleOrDefaultAsync(owner => owner.Id == id && owner.IsArchived, cancellationToken);
    }

    public Task<bool> HasActiveGaragesAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Garages.AsNoTracking().AnyAsync(garage => garage.OwnerId == id && !garage.IsArchived, cancellationToken);

    public void Add(Owner owner)
    {
        dbContext.Owners.Add(owner);
    }

    private IQueryable<Owner> ApplyFilters(string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Owners.AsNoTracking()
            .Where(owner => includeArchived || !owner.IsArchived);
        if (normalizedSearch is not null)
        {
            if (IsNpgsqlProvider())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(owner =>
                    EF.Functions.ILike(owner.LastName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    EF.Functions.ILike(owner.FirstName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    (owner.MiddleName != null && EF.Functions.ILike(owner.MiddleName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")) ||
                    (owner.Phone != null && EF.Functions.ILike(owner.Phone, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")));
            }
            else
            {
                query = query.Where(owner =>
                    owner.LastName.ToLower().Contains(normalizedSearch) ||
                    owner.FirstName.ToLower().Contains(normalizedSearch) ||
                    (owner.MiddleName != null && owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                    (owner.Phone != null && owner.Phone.ToLower().Contains(normalizedSearch)));
            }
        }

        return query;
    }

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}
