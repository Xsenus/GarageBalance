namespace GarageBalance.Api.Domain.Finance;

public static class AccrualPaymentAllocator
{
    public static IReadOnlyList<AccrualPaymentAllocationPlanItem> Allocate(
        IEnumerable<AccrualPaymentAllocationAccrual> accruals,
        IEnumerable<AccrualPaymentAllocationPayment> payments)
    {
        ArgumentNullException.ThrowIfNull(accruals);
        ArgumentNullException.ThrowIfNull(payments);

        var orderedAccruals = accruals
            .Where(item => item.Amount > 0m)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.AccountingMonth)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .ToList();
        var remainingByAccrual = orderedAccruals.ToDictionary(item => item.Id, item => item.Amount);
        var result = new List<AccrualPaymentAllocationPlanItem>();

        foreach (var payment in payments
                     .Where(item => item.Amount > 0m)
                     .OrderBy(item => item.OperationDate)
                     .ThenBy(item => item.CreatedAtUtc)
                     .ThenBy(item => item.Id))
        {
            var paymentRemainder = payment.Amount;
            var matchingAccruals = orderedAccruals
                .Where(accrual =>
                    payment.FeeCampaignId.HasValue
                        ? accrual.FeeCampaignId == payment.FeeCampaignId
                        : payment.IrregularPaymentId.HasValue
                            ? accrual.IrregularPaymentId == payment.IrregularPaymentId
                            : true)
                .OrderBy(accrual =>
                    payment.FeeCampaignId.HasValue || payment.IrregularPaymentId.HasValue
                        ? 0
                        : accrual.FeeCampaignId.HasValue || accrual.IrregularPaymentId.HasValue
                            ? 2
                            : accrual.AccountingMonth == payment.AccountingMonth ? 0 : 1)
                .ThenBy(accrual => accrual.DueDate)
                .ThenBy(accrual => accrual.AccountingMonth)
                .ThenBy(accrual => accrual.CreatedAtUtc)
                .ThenBy(accrual => accrual.Id);

            foreach (var accrual in matchingAccruals)
            {
                if (paymentRemainder <= 0m)
                {
                    break;
                }

                var accrualRemainder = remainingByAccrual[accrual.Id];
                if (accrualRemainder <= 0m)
                {
                    continue;
                }

                var allocated = decimal.Min(paymentRemainder, accrualRemainder);
                result.Add(new AccrualPaymentAllocationPlanItem(payment.Id, accrual.Id, allocated));
                paymentRemainder -= allocated;
                remainingByAccrual[accrual.Id] -= allocated;
            }
        }

        return result;
    }
}

public sealed record AccrualPaymentAllocationAccrual(
    Guid Id,
    DateOnly DueDate,
    DateOnly AccountingMonth,
    decimal Amount,
    DateTimeOffset CreatedAtUtc,
    Guid? FeeCampaignId = null,
    Guid? IrregularPaymentId = null);

public sealed record AccrualPaymentAllocationPayment(
    Guid Id,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    decimal Amount,
    DateTimeOffset CreatedAtUtc,
    Guid? FeeCampaignId = null,
    Guid? IrregularPaymentId = null);

public sealed record AccrualPaymentAllocationPlanItem(
    Guid FinancialOperationId,
    Guid AccrualId,
    decimal Amount);
