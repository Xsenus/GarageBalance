using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffMemberRepository(GarageBalanceDbContext dbContext) : IStaffMemberRepository
{
    public async Task<IReadOnlyList<StaffMember>> GetListAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken)
    {
        var rows = await ProjectListRows(ApplyFilters(departmentId, normalizedSearch, includeArchived))
            .OrderBy(row => row.DepartmentName)
            .ThenBy(row => row.FullName)
            .ThenBy(row => row.MemberId)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(ToStaffMember).ToList();
    }

    public async Task<StaffMemberPageData> GetPageAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken)
    {
        var query = ApplyFilters(departmentId, normalizedSearch, includeArchived);
        if (IsNpgsqlProvider())
        {
            return await GetPostgresPageAsync(query, offset, limit, sortBy, sortDescending, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var queryWithDepartment = IncludeDepartment(query);
        if (sortBy == "rate" && IsSqliteProvider())
        {
            var filteredItems = await queryWithDepartment.ToListAsync(cancellationToken);
            var sortedItems = sortDescending
                ? filteredItems.OrderByDescending(member => member.Rate).ThenBy(member => member.Id)
                : filteredItems.OrderBy(member => member.Rate).ThenBy(member => member.Id);
            return new StaffMemberPageData(sortedItems.Skip(offset).Take(limit).ToList(), totalCount);
        }

        var orderedQuery = ApplyPageSorting(queryWithDepartment, sortBy, sortDescending);
        var items = await orderedQuery
            .ThenBy(member => member.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new StaffMemberPageData(items, totalCount);
    }

    private async Task<StaffMemberPageData> GetPostgresPageAsync(
        IQueryable<StaffMember> query,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var projectedRows = ProjectListRows(query);
        var pageRows = ApplyPostgresSorting(projectedRows, sortBy, sortDescending)
            .Skip(offset)
            .Take(limit);
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new StaffMemberListRow
            {
                Category = TotalsCategory,
                MemberId = null,
                FullName = null,
                Rate = null,
                IsArchived = null,
                DepartmentId = null,
                DepartmentName = null,
                TotalCount = query.Count()
            });
        var rows = await ApplyPostgresSortingByCategory(
                pageRows.Concat(totalsRow),
                sortBy,
                sortDescending)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(ToStaffMember)
            .ToList();
        return new StaffMemberPageData(items, totalCount);
    }

    private static IQueryable<StaffMemberListRow> ProjectListRows(IQueryable<StaffMember> query) =>
        query.Select(member => new StaffMemberListRow
        {
            Category = 1,
            MemberId = member.Id,
            FullName = member.FullName,
            Rate = member.Rate,
            IsArchived = member.IsArchived,
            DepartmentId = member.DepartmentId,
            DepartmentName = member.Department.Name,
            TotalCount = 0
        });

    private static StaffMember ToStaffMember(StaffMemberListRow row) => new()
    {
        Id = row.MemberId!.Value,
        FullName = row.FullName!,
        Rate = row.Rate!.Value,
        IsArchived = row.IsArchived!.Value,
        DepartmentId = row.DepartmentId!.Value,
        Department = new StaffDepartment
        {
            Id = row.DepartmentId.Value,
            Name = row.DepartmentName!
        }
    };

    private static IOrderedQueryable<StaffMemberListRow> ApplyPostgresSorting(
        IQueryable<StaffMemberListRow> query,
        string sortBy,
        bool descending)
    {
        IOrderedQueryable<StaffMemberListRow> ordered = (sortBy, descending) switch
        {
            ("department", true) => query.OrderByDescending(row => row.DepartmentName),
            ("department", false) => query.OrderBy(row => row.DepartmentName),
            ("rate", true) => query.OrderByDescending(row => row.Rate),
            ("rate", false) => query.OrderBy(row => row.Rate),
            (_, true) => query.OrderByDescending(row => row.FullName),
            _ => query.OrderBy(row => row.FullName)
        };
        return ordered.ThenBy(row => row.MemberId);
    }

    private static IOrderedQueryable<StaffMemberListRow> ApplyPostgresSortingByCategory(
        IQueryable<StaffMemberListRow> query,
        string sortBy,
        bool descending)
    {
        IOrderedQueryable<StaffMemberListRow> ordered = (sortBy, descending) switch
        {
            ("department", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.DepartmentName),
            ("department", false) => query.OrderBy(row => row.Category).ThenBy(row => row.DepartmentName),
            ("rate", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.Rate),
            ("rate", false) => query.OrderBy(row => row.Category).ThenBy(row => row.Rate),
            (_, true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.FullName),
            _ => query.OrderBy(row => row.Category).ThenBy(row => row.FullName)
        };
        return ordered.ThenBy(row => row.MemberId);
    }

    public Task<StaffMember?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffMembers.Include(member => member.Department)
            .SingleOrDefaultAsync(member => member.Id == id && !member.IsArchived, cancellationToken);

    public Task<StaffMember?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffMembers.Include(member => member.Department)
            .SingleOrDefaultAsync(member => member.Id == id && member.IsArchived, cancellationToken);

    public void Add(StaffMember member) => dbContext.StaffMembers.Add(member);

    private IQueryable<StaffMember> ApplyFilters(Guid? departmentId, string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.StaffMembers.AsNoTracking()
            .Where(member => includeArchived || !member.IsArchived);
        if (departmentId is not null)
        {
            query = query.Where(member => member.DepartmentId == departmentId);
        }

        if (normalizedSearch is not null)
        {
            if (IsNpgsqlProvider())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(member =>
                    EF.Functions.ILike(member.FullName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                    EF.Functions.ILike(member.Department.Name, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\"));
            }
            else
            {
                query = query.Where(member =>
                    member.FullName.ToLower().Contains(normalizedSearch) ||
                    member.Department.Name.ToLower().Contains(normalizedSearch));
            }
        }

        return query;
    }

    private static IQueryable<StaffMember> IncludeDepartment(IQueryable<StaffMember> query) =>
        query.Include(member => member.Department);

    private static IOrderedQueryable<StaffMember> ApplyPageSorting(IQueryable<StaffMember> query, string sortBy, bool descending)
    {
        return (sortBy, descending) switch
        {
            ("department", true) => query.OrderByDescending(member => member.Department.Name),
            ("department", false) => query.OrderBy(member => member.Department.Name),
            ("rate", true) => query.OrderByDescending(member => member.Rate),
            ("rate", false) => query.OrderBy(member => member.Rate),
            (_, true) => query.OrderByDescending(member => member.FullName),
            _ => query.OrderBy(member => member.FullName)
        };
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class StaffMemberListRow
    {
        public int Category { get; init; }
        public Guid? MemberId { get; init; }
        public string? FullName { get; init; }
        public decimal? Rate { get; init; }
        public bool? IsArchived { get; init; }
        public Guid? DepartmentId { get; init; }
        public string? DepartmentName { get; init; }
        public int TotalCount { get; init; }
    }
}
