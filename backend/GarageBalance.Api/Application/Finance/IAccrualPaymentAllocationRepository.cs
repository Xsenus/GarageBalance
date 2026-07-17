using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IAccrualPaymentAllocationRepository
{
    Task<AccrualPaymentAllocationRebuildResult> RebuildAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken);
}

public sealed record AccrualPaymentAllocationKey(Guid GarageId, Guid IncomeTypeId);

public sealed record AccrualPaymentAllocationRebuildResult(
    int KeyCount,
    int PreviousActiveAllocationCount,
    int ActiveAllocationCount);
