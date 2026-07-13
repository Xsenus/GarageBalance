using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffMemberRepository(GarageBalanceDbContext dbContext) : IStaffMemberRepository
{
    public async Task<IReadOnlyList<StaffMember>> GetListAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken)
    {
        return await ApplyFilters(departmentId, normalizedSearch, includeArchived)
            .OrderBy(member => member.Department.Name)
            .ThenBy(member => member.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffMemberPageData> GetPageAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = ApplyFilters(departmentId, normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(member => member.Department.Name)
            .ThenBy(member => member.FullName)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new StaffMemberPageData(items, totalCount);
    }

    public async Task<IReadOnlyList<StaffMember>> GetActiveForExpenseWorksheetAsync(CancellationToken cancellationToken) =>
        await dbContext.StaffMembers.AsNoTracking()
            .Include(member => member.Department)
            .Where(member => !member.IsArchived)
            .OrderBy(member => member.FullName)
            .ToListAsync(cancellationToken);

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
            .Include(member => member.Department)
            .Where(member => includeArchived || !member.IsArchived);
        if (departmentId is not null)
        {
            query = query.Where(member => member.DepartmentId == departmentId);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(member =>
                member.FullName.ToLower().Contains(normalizedSearch) ||
                member.Department.Name.ToLower().Contains(normalizedSearch));
        }

        return query;
    }
}
