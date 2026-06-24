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
        var result = await reportService.GetConsolidatedReportAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search, limit), cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("consolidated/export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportConsolidatedReportXlsx(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportConsolidatedReportXlsxAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search), cancellationToken);
        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("consolidated/export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportConsolidatedReportPdf(
        [FromQuery] DateOnly? monthFrom,
        [FromQuery] DateOnly? monthTo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await reportService.ExportConsolidatedReportPdfAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search), cancellationToken);
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
                limit),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("income/export/xlsx")]
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
                rowMode),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("income/export/pdf")]
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
                rowMode),
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
                limit),
            cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("expense/export/xlsx")]
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
                rowMode),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("expense/export/pdf")]
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
                rowMode),
            cancellationToken);

        return result.Succeeded
            ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }
}
