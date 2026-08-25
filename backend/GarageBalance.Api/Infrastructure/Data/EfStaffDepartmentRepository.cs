using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffDepartmentRepository(GarageBalanceDbContext dbContext) : IStaffDepartmentRepository
{
    public async Task<IReadOnlyList<StaffDepartment>> GetListAsync(bool includeArchived, int limit, CancellationToken cancellationToken) =>
        await dbContext.StaffDepartments.AsNoTracking()
            .Where(department => includeArchived || !department.IsArchived)
            .OrderBy(department => department.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StaffDepartmentSalaryFundDto>> GetSalaryFundAsync(CancellationToken cancellationToken) =>
        await dbContext.StaffDepartments.AsNoTracking()
            .Where(department => !department.IsArchived)
            .OrderBy(department => department.Name)
            .Select(department => new StaffDepartmentSalaryFundDto(
                department.Id,
                department.Name,
                department.StaffMembers.Count(member => !member.IsArchived),
                department.StaffMembers.Where(member => !member.IsArchived).Sum(member => (decimal?)member.Rate) ?? 0m))
            .ToListAsync(cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.StaffDepartments.AnyAsync(
            department => !department.IsArchived && department.Name == name && (!ignoredId.HasValue || department.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> HasActiveMembersAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffMembers.AnyAsync(member => member.DepartmentId == id && !member.IsArchived, cancellationToken);

    public Task<StaffDepartment?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffDepartments.SingleOrDefaultAsync(department => department.Id == id && !department.IsArchived, cancellationToken);

    public Task<StaffDepartment?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.StaffDepartments.SingleOrDefaultAsync(department => department.Id == id && department.IsArchived, cancellationToken);

    public void Add(StaffDepartment department) => dbContext.StaffDepartments.Add(department);
}
