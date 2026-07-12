using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Integrations;

public interface IReceiptPrintingRepository
{
    Task<FinancialOperation?> FindOperationAsync(Guid financialOperationId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
