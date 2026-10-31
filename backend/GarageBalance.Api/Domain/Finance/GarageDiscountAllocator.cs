namespace GarageBalance.Api.Domain.Finance;

// A discount settles debt using an accrual credit. It never creates a cash operation.
public static class GarageDiscountAllocator
{
    public static IReadOnlyList<GarageDiscountAllocation> Allocate(
        IEnumerable<AccrualPaymentAllocationAccrual> accruals,
        IReadOnlyDictionary<Guid, decimal> paidAmounts,
        decimal openingDebt = 0m)
    {
        var rows = accruals.ToArray();
        var debt = rows.Where(row => row.Amount > 0m)
            .OrderBy(row => row.DueDate).ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToArray();
        var remaining = debt.ToDictionary(row => row.Id, row => Math.Max(0m, row.Amount - paidAmounts.GetValueOrDefault(row.Id)));
        var result = new List<GarageDiscountAllocation>();
        foreach (var credit in rows.Where(row => row.Amount < 0m)
                     .OrderBy(row => row.AccountingMonth).ThenBy(row => row.CreatedAtUtc).ThenBy(row => row.Id))
        {
            var openingSettlement = Math.Min(Math.Max(openingDebt, 0m), -credit.Amount);
            openingDebt -= openingSettlement;
            var available = -credit.Amount - openingSettlement;
            foreach (var target in debt)
            {
                var amount = Math.Min(available, remaining[target.Id]);
                if (amount <= 0m) continue;
                result.Add(new(credit.Id, target.Id, amount));
                remaining[target.Id] -= amount;
                available -= amount;
                if (available == 0m) break;
            }
        }
        return result;
    }
}

public sealed record GarageDiscountAllocation(Guid CreditAccrualId, Guid AccrualId, decimal Amount);
