using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.AuditRead)]
[Route("api/audit")]
public sealed class AuditController(IAuditService auditService) : ControllerBase
{
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<AuditEventDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> GetEvents(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? action,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await auditService.GetEventsAsync(new AuditEventListRequest(dateFrom, dateTo, action, search), cancellationToken));
    }

    [HttpGet("events/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportEvents(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? action,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var export = await auditService.ExportEventsCsvAsync(new AuditEventListRequest(dateFrom, dateTo, action, search), cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }
}
