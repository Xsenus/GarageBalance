namespace GarageBalance.Api.Application.Finance;

public interface IFinancialOperationDisplayQuery
{
    Task<FinancialOperationDisplayData> GetAsync(IReadOnlyCollection<Guid> operationIds, CancellationToken cancellationToken);
}

public sealed record FinancialOperationDisplayData(
    IReadOnlyList<FinancialOperationCalculationData> Calculations,
    IReadOnlyList<FinancialOperationAccrualBucketData> AccrualBuckets);

public sealed record FinancialOperationCalculationData(
    Guid OperationId,
    string CounterpartyKind,
    Guid CounterpartyId,
    DateOnly AccountingMonth,
    decimal PreviousPaymentTotal);

public sealed record FinancialOperationAccrualBucketData(
    string CounterpartyKind,
    Guid CounterpartyId,
    DateOnly AccountingMonth,
    decimal Amount);
