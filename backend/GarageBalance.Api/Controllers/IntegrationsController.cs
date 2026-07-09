using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController(IIntegrationStatusService integrationStatusService) : ControllerBase
{
    [HttpGet("one-c-fresh/status")]
    [Authorize(Policy = SystemPermissions.ImportRun)]
    [ProducesResponseType<OneCFreshIntegrationStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OneCFreshIntegrationStatusDto>> GetOneCFreshStatus(CancellationToken cancellationToken)
    {
        return Ok(await integrationStatusService.GetOneCFreshStatusAsync(cancellationToken));
    }

    [HttpGet("receipt-printing/status")]
    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [ProducesResponseType<ReceiptPrintingIntegrationStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReceiptPrintingIntegrationStatusDto>> GetReceiptPrintingStatus(CancellationToken cancellationToken)
    {
        return Ok(await integrationStatusService.GetReceiptPrintingStatusAsync(cancellationToken));
    }
}
