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
        CancellationToken cancellationToken)
    {
        var result = await reportService.GetConsolidatedReportAsync(new ConsolidatedReportRequest(monthFrom, monthTo, search), cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(new ProblemDetails { Title = result.ErrorCode, Detail = result.ErrorMessage });
    }
}
