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
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetOperationsAsync(new FinancialOperationListRequest(dateFrom, dateTo, operationKind, search, limit), cancellationToken));
    }

    [HttpGet("accruals")]
    [ProducesResponseType<IReadOnlyList<AccrualDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccrualDto>>> GetAccruals(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetAccrualsAsync(new AccrualListRequest(monthFrom, monthTo, search, limit), cancellationToken));
    }

    [HttpGet("supplier-accruals")]
    [ProducesResponseType<IReadOnlyList<SupplierAccrualDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierAccrualDto>>> GetSupplierAccruals(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(monthFrom, monthTo, search, limit), cancellationToken));
    }

    [HttpGet("meter-readings")]
    [ProducesResponseType<IReadOnlyList<MeterReadingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MeterReadingDto>>> GetMeterReadings(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? meterKind,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        return Ok(await financeService.GetMeterReadingsAsync(new MeterReadingListRequest(monthFrom, monthTo, meterKind, search, limit), cancellationToken));
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
    [HttpPost("operations/{operationId:guid}/cancel")]
    [ProducesResponseType<FinancialOperationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FinancialOperationDto>> CancelOperation(Guid operationId, CancelFinanceEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CancelOperationAsync(operationId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
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

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("accruals/{accrualId:guid}/cancel")]
    [ProducesResponseType<AccrualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccrualDto>> CancelAccrual(Guid accrualId, CancelFinanceEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CancelAccrualAsync(accrualId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("supplier-accruals")]
    [ProducesResponseType<SupplierAccrualDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierAccrualDto>> CreateSupplierAccrual(CreateSupplierAccrualRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateSupplierAccrualAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetSupplierAccruals), new { search = result.Value!.SupplierName }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("supplier-accruals/{supplierAccrualId:guid}/cancel")]
    [ProducesResponseType<SupplierAccrualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierAccrualDto>> CancelSupplierAccrual(Guid supplierAccrualId, CancelFinanceEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CancelSupplierAccrualAsync(supplierAccrualId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("accruals/generate-regular")]
    [ProducesResponseType<RegularAccrualGenerationResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegularAccrualGenerationResultDto>> GenerateRegularAccruals(GenerateRegularAccrualsRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.GenerateRegularAccrualsAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetAccruals), new { monthFrom = result.Value!.AccountingMonth, monthTo = result.Value.AccountingMonth }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("meter-readings")]
    [ProducesResponseType<MeterReadingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeterReadingDto>> CreateMeterReading(CreateMeterReadingRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CreateMeterReadingAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetMeterReadings), new { meterKind = result.Value!.MeterKind, search = result.Value.GarageNumber }, result.Value)
            : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.PaymentsWrite)]
    [HttpPost("meter-readings/{meterReadingId:guid}/cancel")]
    [ProducesResponseType<MeterReadingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeterReadingDto>> CancelMeterReading(Guid meterReadingId, CancelFinanceEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await financeService.CancelMeterReadingAsync(meterReadingId, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(FinanceResult<T> result)
    {
        return result.ErrorCode switch
        {
            "garage_not_found" or "income_type_not_found" or "supplier_not_found" or "expense_type_not_found" or "tariff_not_found" or "operation_not_found" or "accrual_not_found" or "supplier_accrual_not_found" or "meter_reading_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "operation_duplicate" or "operation_already_canceled" or "accrual_duplicate" or "accrual_already_canceled" or "supplier_accrual_duplicate" or "supplier_accrual_already_canceled" or "meter_reading_duplicate" or "meter_reading_already_canceled" or "regular_accruals_empty" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
