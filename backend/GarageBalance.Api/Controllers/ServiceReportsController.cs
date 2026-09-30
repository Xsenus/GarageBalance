using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/reports/services")]
public sealed class ServiceReportsController(IServiceReportService service) : ControllerBase
{
    [HttpGet("columns")]
    [Authorize(Policy = SystemPermissions.TariffsManage)]
    public async Task<ActionResult<ServiceReportColumnsDto>> GetColumns(CancellationToken cancellationToken) => Ok(await service.GetColumnsAsync(cancellationToken));

    [HttpPut("columns")]
    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [RequireConcurrencyVersion("request.Version")]
    public async Task<ActionResult<ServiceReportColumnsDto>> SaveColumns(UpdateServiceReportColumnsRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SaveColumnsAsync(request, ActorId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }

    [HttpGet("payments")]
    [Authorize(Policy = SystemPermissions.ReportsRead)]
    public Task<ActionResult<ServiceReportDto>> Payments([FromQuery] ServiceReportRequest request, CancellationToken cancellationToken) => Get(request, false, cancellationToken);

    [HttpGet("debt")]
    [Authorize(Policy = SystemPermissions.ReportsRead)]
    public Task<ActionResult<ServiceReportDto>> Debt([FromQuery] ServiceReportRequest request, CancellationToken cancellationToken) => Get(request, true, cancellationToken);

    [HttpPost("payments/export/{format}")]
    [Authorize(Policy = SystemPermissions.ReportsRead)]
    public Task<IActionResult> ExportPayments([FromQuery] ServiceReportRequest request, string format, CancellationToken cancellationToken) => Export(request, false, format, cancellationToken);

    [HttpPost("debt/export/{format}")]
    [Authorize(Policy = SystemPermissions.ReportsRead)]
    public Task<IActionResult> ExportDebt([FromQuery] ServiceReportRequest request, string format, CancellationToken cancellationToken) => Export(request, true, format, cancellationToken);

    private async Task<ActionResult<ServiceReportDto>> Get(ServiceReportRequest request, bool debt, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(request, debt, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }
    private async Task<IActionResult> Export(ServiceReportRequest request, bool debt, string format, CancellationToken cancellationToken)
    {
        if (format is not ("pdf" or "xlsx")) return BadRequest(ApiProblemDetails.Create("format_invalid", "Выберите XLSX или PDF.", StatusCodes.Status400BadRequest));
        var result = await service.ExportAsync(request, debt, format == "pdf", ActorId(), cancellationToken);
        return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest));
    }
    private Guid? ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
