using System.Security.Claims;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ReportsRead)]
[Route("api/funds")]
public sealed class FundsController(IFundService fundService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FundDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FundDto>>> GetFunds(CancellationToken cancellationToken)
    {
        return Ok(await fundService.GetFundsAsync(cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost]
    [ProducesResponseType<FundDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundDto>> CreateFund(UpsertFundRequest request, CancellationToken cancellationToken)
    {
        var result = await fundService.CreateFundAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetFunds), new { fundId = result.Value!.Id }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPut("{fundId:guid}")]
    [ProducesResponseType<FundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundDto>> UpdateFund(
        Guid fundId,
        UpsertFundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await fundService.UpdateFundAsync(fundId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpDelete("{fundId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<bool>> DeleteFund(
        Guid fundId,
        [FromBody] DeleteFundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await fundService.DeleteFundAsync(fundId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result);
    }

    [HttpGet("operations")]
    [ProducesResponseType<IReadOnlyList<FundOperationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FundOperationDto>>> GetOperations([FromQuery] int limit = 25, [FromQuery] bool includeCanceled = false, CancellationToken cancellationToken = default)
    {
        return Ok(await fundService.GetOperationsAsync(limit, includeCanceled, cancellationToken));
    }

    [HttpGet("operations/page")]
    [ProducesResponseType<FundOperationPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FundOperationPageDto>> GetOperationsPage([FromQuery] int offset = 0, [FromQuery] int limit = 25, [FromQuery] bool includeCanceled = false, CancellationToken cancellationToken = default)
    {
        return Ok(await fundService.GetOperationsPageAsync(offset, limit, includeCanceled, cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("{fundId:guid}/operations")]
    [ProducesResponseType<FundOperationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundOperationDto>> CreateOperation(Guid fundId, CreateFundOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await fundService.CreateOperationAsync(fundId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetFunds), new { fundId = result.Value!.FundId }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPut("operations/{operationId:guid}")]
    [ProducesResponseType<FundOperationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundOperationDto>> UpdateOperation(Guid operationId, UpdateFundOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await fundService.UpdateOperationAsync(operationId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("operations/{operationId:guid}/cancel")]
    [ProducesResponseType<FundOperationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundOperationDto>> CancelOperation(Guid operationId, [FromBody] CancelFundOperationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(ApiProblemDetails.Create("fund_operation_cancel_reason_required", "Для отмены операции фонда нужна причина.", StatusCodes.Status400BadRequest));
        }

        var result = await fundService.CancelOperationAsync(operationId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("operations/{operationId:guid}/restore")]
    [ProducesResponseType<FundOperationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FundOperationDto>> RestoreOperation(Guid operationId, CancellationToken cancellationToken)
    {
        var result = await fundService.RestoreOperationAsync(operationId, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(FundResult<T> result)
    {
        return result.ErrorCode switch
        {
            "fund_not_found" or "fund_operation_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "fund_duplicate" or "fund_has_linked_services" or "fund_balance_not_zero" or "fund_operation_not_allowed" or "fund_balance_insufficient" or "fund_distribution_amount_exceeded" or "fund_operation_already_canceled" or "fund_operation_not_canceled" or "fund_operation_canceled" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
