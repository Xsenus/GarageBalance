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

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(FundResult<T> result)
    {
        return result.ErrorCode switch
        {
            "fund_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "fund_operation_not_allowed" or "fund_balance_insufficient" or "fund_distribution_amount_exceeded" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
