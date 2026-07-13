using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IStaffMemberRepository
{
    Task<IReadOnlyList<StaffMember>> GetListAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<StaffMemberPageData> GetPageAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffMember>> GetActiveForExpenseWorksheetAsync(CancellationToken cancellationToken);
    Task<StaffMember?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffMember?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    void Add(StaffMember member);
}

public sealed record StaffMemberPageData(IReadOnlyList<StaffMember> Items, int TotalCount);
