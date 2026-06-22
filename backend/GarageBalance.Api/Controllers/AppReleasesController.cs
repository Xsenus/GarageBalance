using GarageBalance.Api.Application.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/app-releases")]
public sealed class AppReleasesController(IAppReleaseService appReleaseService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AppReleaseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<AppReleaseDto>>> GetReleases([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await appReleaseService.GetReleasesAsync(limit, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails { Title = result.ErrorCode, Detail = result.ErrorMessage });
    }
}
