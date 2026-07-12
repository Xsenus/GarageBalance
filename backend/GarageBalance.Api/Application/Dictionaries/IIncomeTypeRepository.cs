using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IIncomeTypeRepository
{
    Task<IReadOnlyList<IncomeType>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<IncomeTypePageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<IncomeType?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<IncomeType?> FindFirstActiveByCodeAsync(string code, CancellationToken cancellationToken);
    Task<IncomeType?> FindFirstActiveByNameAsync(string name, CancellationToken cancellationToken);
    Task<IncomeType?> FindFirstArchivedByCodeOrNameAsync(string code, string name, CancellationToken cancellationToken);
    Task<IncomeType?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    void Add(IncomeType incomeType);
}

public sealed record IncomeTypePageData(IReadOnlyList<IncomeType> Items, int TotalCount);
