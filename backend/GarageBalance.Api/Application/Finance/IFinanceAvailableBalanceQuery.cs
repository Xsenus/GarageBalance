namespace GarageBalance.Api.Application.Finance;

public interface IFinanceAvailableBalanceQuery
{
    Task<IAsyncDisposable> AcquireUpdateLockAsync(
        FinanceBalanceAccounts accounts,
        CancellationToken cancellationToken);

    Task<FinanceAvailableBalanceData> GetAsync(
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken);
}

[Flags]
public enum FinanceBalanceAccounts
{
    None = 0,
    Cash = 1,
    Bank = 2
}

public sealed record FinanceAvailableBalanceData(
    decimal IncomeTotal,
    decimal BankDepositTotal,
    decimal CashExpenseTotal,
    decimal BankExpenseTotal)
{
    public decimal CashAdjustmentTotal { get; init; }
    public decimal BankAdjustmentTotal { get; init; }
}
