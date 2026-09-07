namespace GarageBalance.Api.Application.Finance;

public sealed class ExpenseBatchPreparationException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IExpenseBatchStaffDebtQuery
{
    Task<IReadOnlyList<ExpenseBatchDebt>> GetAsync(
        IReadOnlyList<Guid> staffMemberIds,
        DateOnly monthTo,
        DateOnly? salaryAccrualMonthTo,
        CancellationToken cancellationToken);
}
