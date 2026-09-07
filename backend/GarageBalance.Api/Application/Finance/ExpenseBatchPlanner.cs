using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Finance;

public sealed record ExpenseBatchDebt(
    string RecipientKind,
    Guid RecipientId,
    Guid ExpenseTypeId,
    Guid? FundId,
    DateOnly AccountingMonth,
    decimal RemainingAmount);

public sealed record ExpenseBatchLine(
    string RecipientKind,
    Guid RecipientId,
    Guid ExpenseTypeId,
    Guid? FundId,
    DateOnly AccountingMonth,
    decimal Amount,
    string PaymentSource);

public sealed record ExpenseBatchPlan(
    IReadOnlyList<ExpenseBatchLine> Lines,
    decimal BankAmount,
    decimal CashAmount,
    IReadOnlyDictionary<Guid, decimal> FundAmounts);

public static class ExpenseBatchPlanner
{
    // Supplier debt is the closing balance at the selected month. Staff debt is
    // supplied separately for each employment month so existing salary limits apply.
    public static ExpenseBatchPlan Build(DateOnly accountingMonth, IReadOnlyList<ExpenseBatchDebt> debts,
        IReadOnlyDictionary<Guid, decimal>? staffClosingDebts = null)
    {
        ArgumentNullException.ThrowIfNull(debts);
        if (accountingMonth.Day != 1)
        {
            throw new ArgumentException("Расчётный месяц должен начинаться с первого числа.", nameof(accountingMonth));
        }
        if (staffClosingDebts?.Any(pair => pair.Key == Guid.Empty || pair.Value < 0) == true)
        {
            throw new ArgumentException("Некорректный остаток ведомости.", nameof(staffClosingDebts));
        }

        var keys = new HashSet<(string, Guid, Guid, DateOnly)>();
        foreach (var debt in debts)
        {
            if (debt.RecipientKind is not ("supplier" or "staff")
                || debt.RecipientId == Guid.Empty || debt.ExpenseTypeId == Guid.Empty
                || debt.FundId == Guid.Empty || debt.AccountingMonth.Day != 1
                || debt.AccountingMonth > accountingMonth
                || (debt.RecipientKind == "supplier" && debt.AccountingMonth != accountingMonth)
                || (debt.RecipientKind == "staff" && debt.FundId.HasValue)
                || !keys.Add((debt.RecipientKind, debt.RecipientId, debt.ExpenseTypeId, debt.AccountingMonth)))
            {
                throw new ArgumentException("Некорректная или повторная строка задолженности.", nameof(debts));
            }
        }

        var lines = new List<ExpenseBatchLine>();
        foreach (var supplier in debts.Where(item => item.RecipientKind == "supplier")
            .OrderBy(item => item.RecipientId).ThenBy(item => item.ExpenseTypeId))
        {
            var amount = MoneyMath.RoundMoney(supplier.RemainingAmount);
            if (amount > 0)
            {
                lines.Add(ToLine(supplier, amount, "bank"));
            }
        }

        foreach (var staff in debts.Where(item => item.RecipientKind == "staff")
            .GroupBy(item => item.RecipientId).OrderBy(group => group.Key))
        {
            // A credit in another month reduces the total debt. Do not create an
            // advance by paying every positive month without accounting for it.
            var remaining = Math.Max(0, staff.Sum(item => MoneyMath.RoundMoney(item.RemainingAmount)));
            if (staffClosingDebts is not null)
            {
                if (!staffClosingDebts.TryGetValue(staff.Key, out var closingDebt) || MoneyMath.RoundMoney(closingDebt) > remaining)
                    throw new ArgumentException("Помесячные долги не покрывают остаток ведомости.", nameof(staffClosingDebts));
                remaining = MoneyMath.RoundMoney(closingDebt);
            }
            foreach (var month in staff.OrderBy(item => item.AccountingMonth).ThenBy(item => item.ExpenseTypeId))
            {
                var amount = Math.Min(remaining, Math.Max(0, MoneyMath.RoundMoney(month.RemainingAmount)));
                if (amount > 0)
                {
                    lines.Add(ToLine(month, amount, "cash"));
                    remaining -= amount;
                }
            }
        }

        var paidByStaff = lines.Where(line => line.RecipientKind == "staff")
            .GroupBy(line => line.RecipientId).ToDictionary(group => group.Key, group => group.Sum(line => line.Amount));
        if (staffClosingDebts is not null && staffClosingDebts.Any(pair =>
            MoneyMath.RoundMoney(pair.Value) != paidByStaff.GetValueOrDefault(pair.Key)))
        {
            throw new ArgumentException("Не удалось распределить остаток ведомости по месяцам.", nameof(staffClosingDebts));
        }

        return new ExpenseBatchPlan(
            lines,
            lines.Where(item => item.PaymentSource == "bank").Sum(item => item.Amount),
            lines.Where(item => item.PaymentSource == "cash").Sum(item => item.Amount),
            lines.Where(item => item.FundId.HasValue).GroupBy(item => item.FundId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount)));
    }

    private static ExpenseBatchLine ToLine(ExpenseBatchDebt debt, decimal amount, string source) =>
        new(debt.RecipientKind, debt.RecipientId, debt.ExpenseTypeId, debt.FundId, debt.AccountingMonth, amount, source);
}
