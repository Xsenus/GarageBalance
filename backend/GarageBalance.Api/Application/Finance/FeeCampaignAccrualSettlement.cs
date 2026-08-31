using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public static class FeeCampaignAccrualSettlement
{
    public static IReadOnlyList<AccrualPaymentAllocationKey> BuildCampaignWideAllocationKeys(
        IEnumerable<Accrual> accruals,
        IEnumerable<Guid> destinationIncomeTypeIds,
        IEnumerable<Guid>? additionalGarageIds = null)
    {
        var accrualList = accruals.ToArray();
        var destinationIds = destinationIncomeTypeIds.Distinct().ToArray();
        var keys = new HashSet<AccrualPaymentAllocationKey>();
        foreach (var accrual in accrualList)
        {
            keys.Add(new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId));
            foreach (var destinationId in destinationIds)
            {
                keys.Add(new AccrualPaymentAllocationKey(accrual.GarageId, destinationId));
            }
        }

        if (additionalGarageIds is not null)
        {
            foreach (var garageId in additionalGarageIds.Distinct())
            {
                foreach (var destinationId in destinationIds)
                {
                    keys.Add(new AccrualPaymentAllocationKey(garageId, destinationId));
                }
            }
        }

        return keys.ToArray();
    }

    public static Accrual? CollapseOpenGarageAccruals(
        IEnumerable<Accrual> accruals,
        DateTimeOffset changedAtUtc)
    {
        var ordered = accruals
            .Where(accrual => !accrual.IsCanceled)
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.CreatedAtUtc)
            .ThenBy(accrual => accrual.Id)
            .ToList();
        var principal = ordered.FirstOrDefault();
        foreach (var duplicate in ordered.Skip(1))
        {
            duplicate.IsCanceled = true;
            duplicate.UpdatedAtUtc = changedAtUtc;
        }

        return principal;
    }

    public static IReadOnlyList<AccrualPaymentAllocationKey> SettleClosedCampaign(
        IEnumerable<Accrual> accruals,
        IReadOnlyDictionary<Guid, decimal> paidAmountsByGarage,
        DateTimeOffset changedAtUtc)
    {
        var keys = new HashSet<AccrualPaymentAllocationKey>();
        foreach (var group in accruals
                     .Where(accrual => !accrual.IsCanceled)
                     .GroupBy(accrual => accrual.GarageId))
        {
            var principal = CollapseOpenGarageAccruals(group, changedAtUtc);
            if (principal is null)
            {
                continue;
            }

            keys.Add(new AccrualPaymentAllocationKey(principal.GarageId, principal.IncomeTypeId));
            var paidAmount = decimal.Round(
                Math.Max(paidAmountsByGarage.GetValueOrDefault(principal.GarageId), 0m),
                2,
                MidpointRounding.AwayFromZero);
            principal.UpdatedAtUtc = changedAtUtc;
            if (paidAmount <= 0m)
            {
                principal.IsCanceled = true;
                continue;
            }

            principal.Amount = paidAmount;
        }

        return keys.ToArray();
    }
}
