namespace GarageBalance.Api.Application.Integrations;

public interface IIntegrationStatusService
{
    Task<OneCFreshIntegrationStatusDto> GetOneCFreshStatusAsync(CancellationToken cancellationToken);

    Task<ReceiptPrintingIntegrationStatusDto> GetReceiptPrintingStatusAsync(CancellationToken cancellationToken);
}
