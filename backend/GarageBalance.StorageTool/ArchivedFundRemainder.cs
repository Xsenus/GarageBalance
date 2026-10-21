using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.StorageTool;

// A deleted (archived) fund always ends at zero: deleting it returned the remainder to cash/bank
// with a final withdrawal that has no source payment. When removed history changes that remainder,
// the same withdrawal shrinks or grows, so cash/bank and the funds stay reconciled.
internal static class ArchivedFundRemainder
{
    // history: the fund's remaining operations in chain order with a consistent BalanceBefore/After chain.
    public static bool TryFind(Fund fund, IReadOnlyList<FundOperation> history, out FundOperation remainder, out decimal repairedAmount)
    {
        remainder = null!;
        repairedAmount = 0m;
        var last = history.LastOrDefault(operation => !operation.IsCanceled);
        if (last is null || last.SourceFinancialOperationId is not null || last.OperationKind != FundOperationKinds.Withdraw ||
            history[^1].BalanceAfter != fund.Balance)
            return false;
        repairedAmount = MoneyMath.RoundMoney(last.Amount + fund.Balance);
        if (repairedAmount < 0m) return false;
        remainder = last;
        return true;
    }

    // A remainder that drops to zero is cancelled rather than deleted, so no history row disappears.
    public static void Apply(Fund fund, IReadOnlyList<FundOperation> history, FundOperation remainder, decimal repairedAmount, DateTimeOffset now)
    {
        if (repairedAmount == 0m) remainder.IsCanceled = true;
        else remainder.Amount = repairedAmount;
        remainder.BalanceAfter = remainder.IsCanceled ? remainder.BalanceBefore : MoneyMath.RoundMoney(remainder.BalanceBefore - repairedAmount);
        remainder.UpdatedAtUtc = now;
        var balance = remainder.BalanceAfter;
        foreach (var operation in history.SkipWhile(operation => operation != remainder).Skip(1))
        {
            operation.BalanceBefore = balance;
            operation.BalanceAfter = balance;
        }
        fund.Balance = balance;
        fund.UpdatedAtUtc = now;
        fund.Version = Guid.NewGuid();
    }
}
