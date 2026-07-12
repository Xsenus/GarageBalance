using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IIrregularPaymentRepository
{
    Task<IReadOnlyList<IrregularPayment>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<IrregularPayment?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<IrregularPayment?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<bool> IsUsedAsync(string name, CancellationToken cancellationToken);
    void Add(IrregularPayment payment);
}
