using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Integrations;

public interface IReceiptPrintingRepository
{
    Task<IReadOnlyList<FinancialOperation>> FindReceiptOperationsAsync(Guid financialOperationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReceiptPrintingAllocationData>> GetActiveAllocationsAsync(
        IReadOnlyCollection<Guid> financialOperationIds,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ReceiptPrintingAllocationData(
    Guid FinancialOperationId,
    Guid AccrualId,
    DateOnly AccountingMonth,
    string IncomeTypeName,
    decimal Amount);

public static class ReceiptPrintingLimits
{
    public const int MaximumLineCount = 100;
}
