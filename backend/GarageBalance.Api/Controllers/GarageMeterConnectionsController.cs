using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/finance/meter-connections")]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
[Authorize(Policy = SystemPermissions.DictionariesWrite)]
[Authorize(Policy = SystemPermissions.OpeningDataAdjust)]
public sealed class GarageMeterConnectionsController(IGarageMeterConnectionService service) : ControllerBase
{
    [HttpPost("batch")]
    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [ProducesResponseType<GarageMeterConnectionBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageMeterConnectionBatchDto>> ConnectBatch(ConnectGarageMetersBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ConnectBatchAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpPost("preview")]
    [Authorize(Policy = SystemPermissions.PaymentsRead)]
    [ProducesResponseType<GarageMeterConnectionPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageMeterConnectionPreviewDto>> Preview(PreviewGarageMeterConnectionsRequest request, CancellationToken cancellationToken)
    {
        var result = await service.PreviewAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    private Guid? GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null;

    private ActionResult<T> ToError<T>(FinanceResult<T> result) => result.ErrorCode switch
    {
        "garage_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
        "meter_connection_already_exists" or "meter_connection_changed" or "meter_connection_version_required" or "meter_reading_accrual_paid" or "meter_start_accrual_paid" =>
            Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
        _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
    };
}
