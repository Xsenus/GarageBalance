using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ReportsRead)]
[Route("api/reports")]
public sealed class ReportsController(IReportService reportService) : ControllerBase
{
    [HttpGet("consolidated")]
    [ProducesResponseType<ConsolidatedReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConsolidatedReportDto>> GetConsolidatedReport(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetConsolidatedReportAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search, limit, GetActorUserId()), cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("consolidated/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportConsolidatedReportXlsx(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportConsolidatedReportXlsxAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search, ActorUserId: GetActorUserId()), cancellationToken);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("consolidated/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportConsolidatedReportPdf(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportConsolidatedReportPdfAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search, ActorUserId: GetActorUserId()), cancellationToken);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("income")]
    [ProducesResponseType<IncomeReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IncomeReportDto>> GetIncomeReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? garageIds,
        [FromQuery] Guid[]? ownerIds,
        [FromQuery] Guid[]? incomeTypeIds,
        [FromQuery] string? rowMode,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetIncomeReportAsync(
            new IncomeReportRequest(
                dateFrom,
                dateTo,
                search,
                garageIds ?? [],
                ownerIds ?? [],
                incomeTypeIds ?? [],
                rowMode,
                limit,
                GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("income/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportIncomeReportXlsx(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? garageIds,
        [FromQuery] Guid[]? ownerIds,
        [FromQuery] Guid[]? incomeTypeIds,
        [FromQuery] string? rowMode,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportIncomeReportXlsxAsync(
            new IncomeReportRequest(
                dateFrom,
                dateTo,
                search,
                garageIds ?? [],
                ownerIds ?? [],
                incomeTypeIds ?? [],
                rowMode,
                ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("income/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportIncomeReportPdf(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? garageIds,
        [FromQuery] Guid[]? ownerIds,
        [FromQuery] Guid[]? incomeTypeIds,
        [FromQuery] string? rowMode,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportIncomeReportPdfAsync(
            new IncomeReportRequest(
                dateFrom,
                dateTo,
                search,
                garageIds ?? [],
                ownerIds ?? [],
                incomeTypeIds ?? [],
                rowMode,
                ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("expense")]
    [ProducesResponseType<ExpenseReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExpenseReportDto>> GetExpenseReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? supplierIds,
        [FromQuery] Guid[]? expenseTypeIds,
        [FromQuery] string? rowMode,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetExpenseReportAsync(
            new ExpenseReportRequest(
                dateFrom,
                dateTo,
                search,
                supplierIds ?? [],
                expenseTypeIds ?? [],
                rowMode,
                limit,
                GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("fund-changes")]
    [ProducesResponseType<FundChangeReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FundChangeReportDto>> GetFundChangeReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetFundChangeReportAsync(
            new FundChangeReportRequest(dateFrom, dateTo, search, limit, offset, GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("fund-changes/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportFundChangeReportXlsx(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportFundChangeReportXlsxAsync(
            new FundChangeReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("fund-changes/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportFundChangeReportPdf(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportFundChangeReportPdfAsync(
            new FundChangeReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("cash-payments")]
    [ProducesResponseType<CashPaymentReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CashPaymentReportDto>> GetCashPaymentReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetCashPaymentReportAsync(
            new CashPaymentReportRequest(dateFrom, dateTo, search, limit, offset, GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("cash-payments/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportCashPaymentReportXlsx(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportCashPaymentReportXlsxAsync(
            new CashPaymentReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("cash-payments/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportCashPaymentReportPdf(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportCashPaymentReportPdfAsync(
            new CashPaymentReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("bank-deposits")]
    [ProducesResponseType<BankDepositReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BankDepositReportDto>> GetBankDepositReport(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetBankDepositReportAsync(
            new BankDepositReportRequest(dateFrom, dateTo, search, limit, offset, GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("bank-deposits/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportBankDepositReportXlsx(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportBankDepositReportXlsxAsync(
            new BankDepositReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("bank-deposits/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportBankDepositReportPdf(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportBankDepositReportPdfAsync(
            new BankDepositReportRequest(dateFrom, dateTo, search, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("fees")]
    [ProducesResponseType<FeeReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FeeReportDto>> GetFeeReport(
        [FromQuery] string? variation,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetFeeReportAsync(
            new FeeReportRequest(variation, limit, GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("fees/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportFeeReportXlsx(
        [FromQuery] string? variation,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportFeeReportXlsxAsync(
            new FeeReportRequest(variation, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("fees/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportFeeReportPdf(
        [FromQuery] string? variation,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportFeeReportPdfAsync(
            new FeeReportRequest(variation, ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("expense/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExpenseReportXlsx(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? supplierIds,
        [FromQuery] Guid[]? expenseTypeIds,
        [FromQuery] string? rowMode,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportExpenseReportXlsxAsync(
            new ExpenseReportRequest(
                dateFrom,
                dateTo,
                search,
                supplierIds ?? [],
                expenseTypeIds ?? [],
                rowMode,
                ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("expense/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExpenseReportPdf(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? search,
        [FromQuery] Guid[]? supplierIds,
        [FromQuery] Guid[]? expenseTypeIds,
        [FromQuery] string? rowMode,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportExpenseReportPdfAsync(
            new ExpenseReportRequest(
                dateFrom,
                dateTo,
                search,
                supplierIds ?? [],
                expenseTypeIds ?? [],
                rowMode,
                ActorUserId: GetActorUserId()),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    private Guid? GetActorUserId()
    {
        var principal = HttpContext?.User;
        return principal is not null && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
