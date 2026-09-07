using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpensePaymentBatchRepository(GarageBalanceDbContext dbContext) : IExpensePaymentBatchRepository
{
    public Task<ExpensePaymentBatch?> FindAsync(Guid batchId, Guid actorUserId, CancellationToken cancellationToken) =>
        dbContext.ExpensePaymentBatches.AsNoTracking()
            .Where(batch => batch.Id == batchId && batch.ActorUserId == actorUserId)
            .Include(batch => batch.Operations)
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(ExpensePaymentBatch batch) => dbContext.ExpensePaymentBatches.Add(batch);
}
