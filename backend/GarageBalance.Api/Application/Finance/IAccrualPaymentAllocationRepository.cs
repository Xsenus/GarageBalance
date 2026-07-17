using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IAccrualPaymentAllocationRepository
{
    Task RebuildAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken);
}

public sealed record AccrualPaymentAllocationKey(Guid GarageId, Guid IncomeTypeId);
