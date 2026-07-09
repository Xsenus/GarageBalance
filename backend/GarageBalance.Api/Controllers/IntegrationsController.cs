using System.Security.Claims;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController(
    IIntegrationStatusService integrationStatusService,
    IReceiptPrintingService receiptPrintingService) : ControllerBase
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

    [HttpPost("receipt-printing/operations/{operationId:guid}/actions")]
    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [ProducesResponseType<ReceiptPrintingActionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptPrintingActionDto>> RegisterReceiptPrintingAction(
        Guid operationId,
        [FromBody] ReceiptPrintingActionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(ApiProblemDetails.Create("receipt_print_request_required", "Передайте действие печати.", StatusCodes.Status400BadRequest));
        }

        var result = await receiptPrintingService.RegisterActionAsync(operationId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToReceiptPrintingError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToReceiptPrintingError<T>(ReceiptPrintingResult<T> result)
    {
        return result.ErrorCode switch
        {
            "financial_operation_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "receipt_print_income_required" or "receipt_print_operation_canceled" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
