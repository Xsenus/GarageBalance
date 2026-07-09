using System.Security.Claims;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Domain.Security;
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
            : StatusCode(StatusCodes.Status500InternalServerError, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status500InternalServerError));
    }

    [Authorize(Policy = SystemPermissions.AppReleasesManage)]
    [HttpGet("manage")]
    [ProducesResponseType<IReadOnlyList<AppReleaseDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<AppReleaseDto>>> GetManageableReleases([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await appReleaseService.GetManageableReleasesAsync(limit, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : StatusCode(StatusCodes.Status500InternalServerError, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status500InternalServerError));
    }

    [Authorize(Policy = SystemPermissions.AppReleasesManage)]
    [HttpPost]
    [ProducesResponseType<AppReleaseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AppReleaseDto>> CreateRelease(UpsertAppReleaseRequest request, CancellationToken cancellationToken)
    {
        var result = await appReleaseService.CreateReleaseAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToWriteError(result);
        }

        return CreatedAtAction(nameof(GetReleases), new { limit = 1 }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.AppReleasesManage)]
    [HttpPut("{releaseId}")]
    [ProducesResponseType<AppReleaseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AppReleaseDto>> UpdateRelease(string releaseId, UpsertAppReleaseRequest request, CancellationToken cancellationToken)
    {
        var result = await appReleaseService.UpdateReleaseAsync(releaseId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToWriteError(result);
    }

    [Authorize(Policy = SystemPermissions.AppReleasesManage)]
    [HttpPost("{releaseId}/publish")]
    [ProducesResponseType<AppReleaseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AppReleaseDto>> PublishRelease(string releaseId, CancellationToken cancellationToken)
    {
        var result = await appReleaseService.PublishReleaseAsync(releaseId, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToWriteError(result);
    }

    private ActionResult<AppReleaseDto> ToWriteError<T>(AppReleaseResult<T> result)
    {
        var statusCode = result.ErrorCode switch
        {
            "release_not_found" => StatusCodes.Status404NotFound,
            "releases_file_missing" or "releases_file_invalid" or "releases_file_unavailable" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(statusCode, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode));
    }

    private Guid? GetActorUserId()
    {
        var principal = HttpContext?.User;
        return principal is not null && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
