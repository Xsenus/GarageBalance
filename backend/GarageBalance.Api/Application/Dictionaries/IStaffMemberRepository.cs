using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IStaffMemberRepository
{
    Task<IReadOnlyList<StaffMember>> GetListAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<StaffMemberPageData> GetPageAsync(Guid? departmentId, string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<StaffMember?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffMember?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffMember?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffSalaryRatePeriod?> FindSalaryRatePeriodAsync(Guid staffMemberId, DateOnly effectiveFrom, CancellationToken cancellationToken);
    Task<StaffEmploymentPeriod?> FindOpenEmploymentPeriodAsync(Guid staffMemberId, CancellationToken cancellationToken);
    Task<StaffEmploymentPeriod?> FindLatestEmploymentPeriodAsync(Guid staffMemberId, CancellationToken cancellationToken);
    Task<StaffSalaryStateData?> GetSalaryStateAsync(Guid staffMemberId, DateOnly accountingMonth, CancellationToken cancellationToken);
    void Add(StaffMember member);
    void AddSalaryRatePeriod(StaffSalaryRatePeriod period);
    void AddEmploymentPeriod(StaffEmploymentPeriod period);
}

public sealed record StaffMemberPageData(IReadOnlyList<StaffMember> Items, int TotalCount);

public sealed record StaffSalaryStateData(decimal Rate, bool HasEmploymentHistory, bool IsEmployed);
