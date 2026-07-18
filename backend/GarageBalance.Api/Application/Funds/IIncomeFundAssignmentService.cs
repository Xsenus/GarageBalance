using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public interface IIncomeFundAssignmentService
{
    Task<IAsyncDisposable> AcquireUpdateLockAsync(CancellationToken cancellationToken);

    Task<IncomeFundAssignmentResult> CreateAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<IncomeFundAssignmentResult> UpdateAsync(
        FinancialOperation sourceOperation,
        Guid? destinationFundId,
        string incomeTypeName,
        decimal amount,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<IncomeFundAssignmentResult> CancelAsync(
        FinancialOperation sourceOperation,
        string reason,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<IncomeFundAssignmentResult> RestoreAsync(
        FinancialOperation sourceOperation,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}

public sealed record IncomeFundAssignmentResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static IncomeFundAssignmentResult Success() => new(true);

    public static IncomeFundAssignmentResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
