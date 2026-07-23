using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfReceiptPrintingRepository(GarageBalanceDbContext dbContext) : IReceiptPrintingRepository
{
    public async Task<IReadOnlyList<FinancialOperation>> FindReceiptOperationsAsync(Guid financialOperationId, CancellationToken cancellationToken)
    {
        var anchor = await ReceiptOperationQuery()
            .SingleOrDefaultAsync(item => item.Id == financialOperationId, cancellationToken);
        if (anchor is null)
        {
            return [];
        }

        if (anchor.ReceiptBatchId is null)
        {
            return [anchor];
        }

        return await ReceiptOperationQuery()
            .Where(item => item.ReceiptBatchId == anchor.ReceiptBatchId)
            .OrderBy(item => item.AccountingMonth)
            .ThenBy(item => item.IncomeType!.Name)
            .ThenBy(item => item.Id)
            .Take(ReceiptPrintingLimits.MaximumLineCount + 1)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReceiptPrintingAllocationData>> GetActiveAllocationsAsync(
        IReadOnlyCollection<Guid> financialOperationIds,
        CancellationToken cancellationToken)
    {
        if (financialOperationIds.Count == 0)
        {
            return [];
        }

        return await dbContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item =>
                financialOperationIds.Contains(item.FinancialOperationId) &&
                item.IsActive &&
                !item.Accrual.IsCanceled)
            .OrderBy(item => item.Accrual.AccountingMonth)
            .ThenBy(item => item.Accrual.IncomeType.Name)
            .Select(item => new ReceiptPrintingAllocationData(
                item.FinancialOperationId,
                item.AccrualId,
                item.Accrual.AccountingMonth,
                item.Accrual.IncomeType.Name,
                item.Amount))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<FinancialOperation> ReceiptOperationQuery()
    {
        return dbContext.FinancialOperations
            .Include(item => item.Garage)
                .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .AsTracking();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
