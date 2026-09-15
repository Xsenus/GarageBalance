using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
[Route("api/dictionaries/garages")]
public sealed class GarageCreationController(IGarageCreationService garageCreationService) : ControllerBase
{
    [HttpPost("with-annual-payments")]
    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [ProducesResponseType<GarageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageDto>> CreateWithAnnualPayments(
        CreateGarageWithAnnualPaymentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await garageCreationService.CreateWithAnnualPaymentsAsync(
            request,
            GetActorUserId(),
            cancellationToken);
        if (!result.Succeeded)
        {
            var status = result.ErrorCode is "garage_number_duplicate" or "expense_batch_concurrency"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return Problem(statusCode: status, title: result.ErrorCode, detail: result.ErrorMessage);
        }

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    private Guid? GetActorUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var actorUserId) ? actorUserId : null;
    }
}
