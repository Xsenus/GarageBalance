using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ImportRun)]
[Route("api/integrations")]
public sealed class IntegrationsController(IIntegrationStatusService integrationStatusService) : ControllerBase
{
    [HttpGet("one-c-fresh/status")]
    [ProducesResponseType<OneCFreshIntegrationStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OneCFreshIntegrationStatusDto>> GetOneCFreshStatus(CancellationToken cancellationToken)
    {
        return Ok(await integrationStatusService.GetOneCFreshStatusAsync(cancellationToken));
    }
}
