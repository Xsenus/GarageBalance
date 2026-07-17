namespace GarageBalance.Api.Application.Finance;

public interface IFinanceAvailableBalanceQuery
{
    Task<IAsyncDisposable> AcquireUpdateLockAsync(
        bool cashExpense,
        CancellationToken cancellationToken);

    Task<FinanceAvailableBalanceData> GetAsync(
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken);
}

public sealed record FinanceAvailableBalanceData(
    decimal IncomeTotal,
    decimal BankDepositTotal,
    decimal CashExpenseTotal,
    decimal BankExpenseTotal);
