namespace GarageBalance.Api.Application.Integrations;

public interface IReceiptPrintingService
{
    Task<ReceiptPrintingResult<ReceiptPrintingActionDto>> RegisterActionAsync(
        Guid financialOperationId,
        ReceiptPrintingActionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
