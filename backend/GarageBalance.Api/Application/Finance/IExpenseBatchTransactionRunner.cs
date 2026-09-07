namespace GarageBalance.Api.Application.Finance;

public interface IExpenseBatchTransactionRunner
{
    Task<FinanceResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<FinanceResult<T>>> action,
        CancellationToken cancellationToken);
}
