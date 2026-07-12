using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IStaffDepartmentRepository
{
    Task<IReadOnlyList<StaffDepartment>> GetListAsync(bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<bool> HasActiveMembersAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffDepartment?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffDepartment?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    void Add(StaffDepartment department);
}
