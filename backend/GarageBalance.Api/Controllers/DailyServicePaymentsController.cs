using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.ReportsRead)]
[Route("api/reports/daily-service-payments")]
public sealed class DailyServicePaymentsController(IDailyServicePaymentReportService reportService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DailyServicePaymentReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DailyServicePaymentReportDto>> Get([FromQuery] DateOnly? throughDate, [FromQuery] Guid? garageId,
        [FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await reportService.GetAsync(new(throughDate, garageId, offset, limit), cancellationToken);
        return result.Succeeded ? Ok(result.Value)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpPost("export/xlsx")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportXlsx([FromQuery] DateOnly? throughDate, [FromQuery] Guid? garageId, CancellationToken cancellationToken) =>
        Export(throughDate, garageId, false, cancellationToken);

    [HttpPost("export/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportPdf([FromQuery] DateOnly? throughDate, [FromQuery] Guid? garageId, CancellationToken cancellationToken) =>
        Export(throughDate, garageId, true, cancellationToken);

    private async Task<IActionResult> Export(DateOnly? date, Guid? garageId, bool pdf, CancellationToken cancellationToken)
    {
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : (Guid?)null;
        var result = await reportService.ExportAsync(new(date, garageId, ActorUserId: actor), pdf, cancellationToken);
        return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName)
            : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }
}
