using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public interface IExpenseFundDisbursementService
{
    Task<IAsyncDisposable> AcquireUpdateLockAsync(CancellationToken cancellationToken);

    Task<ExpenseFundDisbursementResult> CreateAsync(
        FinancialOperation sourceOperation,
        string supplierName,
        Guid? actorUserId,
        bool allowNegativeBalance,
        CancellationToken cancellationToken);

    Task<ExpenseFundDisbursementResult> UpdateAsync(
        FinancialOperation sourceOperation,
        Guid expenseFundId,
        string supplierName,
        decimal amount,
        Guid? actorUserId,
        bool allowNegativeBalance,
        CancellationToken cancellationToken);

    Task<ExpenseFundDisbursementResult> CancelAsync(
        FinancialOperation sourceOperation,
        string reason,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<ExpenseFundDisbursementResult> RestoreAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}

public sealed record ExpenseFundDisbursementResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ExpenseFundDisbursementResult Success() => new(true);

    public static ExpenseFundDisbursementResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
