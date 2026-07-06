using System.Security.Claims;
using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/form-states")]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
public sealed class FormStatesController(IFormStateService formStateService) : ControllerBase
{
    [HttpGet("{scope}")]
    [ProducesResponseType<FormStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<FormStateDto>> GetState(string scope, CancellationToken cancellationToken)
    {
        var state = await formStateService.GetStateAsync(scope, cancellationToken);
        return state is null ? NoContent() : Ok(state);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPut("{scope}")]
    [ProducesResponseType<FormStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FormStateDto>> UpsertState(string scope, UpsertFormStateRequest request, CancellationToken cancellationToken)
    {
        var result = await formStateService.UpsertStateAsync(scope, request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode!, result.ErrorMessage!, StatusCodes.Status400BadRequest));
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
