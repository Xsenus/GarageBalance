using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IExpenseTypeRepository
{
    Task<IReadOnlyList<ExpenseType>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<ExpenseTypePageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<ExpenseType?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<ExpenseType?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken);
    Task<ExpenseType?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    void Add(ExpenseType expenseType);
}

public sealed record ExpenseTypePageData(IReadOnlyList<ExpenseType> Items, int TotalCount);
