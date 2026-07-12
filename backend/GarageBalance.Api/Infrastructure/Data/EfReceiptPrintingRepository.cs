using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfReceiptPrintingRepository(GarageBalanceDbContext dbContext) : IReceiptPrintingRepository
{
    public Task<FinancialOperation?> FindOperationAsync(Guid financialOperationId, CancellationToken cancellationToken)
    {
        return dbContext.FinancialOperations
            .Include(item => item.Garage)
                .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == financialOperationId, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
