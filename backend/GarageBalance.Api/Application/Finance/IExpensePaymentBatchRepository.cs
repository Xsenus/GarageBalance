using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IExpensePaymentBatchRepository
{
    Task<ExpensePaymentBatch?> FindAsync(Guid batchId, Guid actorUserId, CancellationToken cancellationToken);
    void Add(ExpensePaymentBatch batch);
}
