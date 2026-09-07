using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/finance/expense-batches")]
[Authorize(Policy = SystemPermissions.PaymentsRead)]
[Authorize(Policy = SystemPermissions.PaymentsWrite)]
public sealed class ExpenseBatchesController(
    IExpenseBatchPreviewService previewService,
    IExpenseBatchPaymentService paymentService) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<ExpenseBatchPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseBatchPreviewDto>> Preview(ExpenseBatchPreviewRequest request, CancellationToken cancellationToken)
    {
        var result = await previewService.PreviewAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    [HttpPost]
    [ProducesResponseType<ExpenseBatchPaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseBatchPaymentResult>> Pay(ExpenseBatchPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
        {
            return Unauthorized(ApiProblemDetails.CreateUnauthorized());
        }
        var result = await paymentService.PayAsync(request, actor, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    private ObjectResult ToProblem<T>(FinanceResult<T> result)
    {
        var status = result.ErrorCode is "expense_batch_request_conflict" or "expense_batch_preview_changed"
            or "expense_batch_concurrency" or "expense_batch_data_changed"
            ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        return StatusCode(status, ApiProblemDetails.Create(result.ErrorCode!, result.ErrorMessage!, status));
    }
}
