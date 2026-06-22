using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.PaymentsRead)]
[Route("api/finance")]
public sealed class FinanceController(IFinanceService financeService) : ControllerBase
{
    [HttpGet("operations")]
    [ProducesResponseType<IReadOnlyList<FinancialOperationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FinancialOperationDto>>> GetOperations(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? operationKind,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetOperationsAsync(new FinancialOperationListRequest(dateFrom, dateTo, operationKind, search), cancellationToken));
    }

    [HttpGet("accruals")]
    [ProducesResponseType<IReadOnlyList<AccrualDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccrualDto>>> GetAccruals(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetAccrualsAsync(new AccrualListRequest(monthFrom, monthTo, search), cancellationToken));
    }

    [HttpGet("summary")]
    [ProducesResponseType<FinanceSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FinanceSummaryDto>> GetSummary(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? operationKind,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetSummaryAsync(new FinancialOperationListRequest(dateFrom, dateTo, operationKind, search), cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("income")]
    [ProducesResponseType<FinancialOperationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FinancialOperationDto>> CreateIncome(CreateIncomeOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateIncomeAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetOperations), new { operationKind = result.Value!.OperationKind }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("expense")]
    [ProducesResponseType<FinancialOperationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FinancialOperationDto>> CreateExpense(CreateExpenseOperationRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateExpenseAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetOperations), new { operationKind = result.Value!.OperationKind }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("accruals")]
    [ProducesResponseType<AccrualDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccrualDto>> CreateAccrual(CreateAccrualRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateAccrualAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAccruals), new { search = result.Value!.GarageNumber }, result.Value)
            : ToError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(FinanceResult<T> result)
    {
        var problem = new ProblemDetails
        {
            Title = result.ErrorCode,
            Detail = result.ErrorMessage
        };

        return result.ErrorCode switch
        {
            "garage_not_found" or "income_type_not_found" or "supplier_not_found" or "expense_type_not_found" => NotFound(problem),
            "operation_duplicate" or "accrual_duplicate" => Conflict(problem),
            _ => BadRequest(problem)
        };
    }
}
