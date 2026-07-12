using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffMemberRepository(GarageBalanceDbContext dbContext) : IStaffMemberRepository
{
    public async Task<IReadOnlyList<StaffMember>> GetListAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken)
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

        return await query
            .OrderBy(member => member.Department.Name)
            .ThenBy(member => member.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<StaffMember?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffMembers.Include(member => member.Department)
            .SingleOrDefaultAsync(member => member.Id == id && !member.IsArchived, cancellationToken);

    public Task<StaffMember?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffMembers.Include(member => member.Department)
            .SingleOrDefaultAsync(member => member.Id == id && member.IsArchived, cancellationToken);

    public void Add(StaffMember member) => dbContext.StaffMembers.Add(member);
}
