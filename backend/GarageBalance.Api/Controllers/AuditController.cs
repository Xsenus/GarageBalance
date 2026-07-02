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
        [FromQuery] int? limit,
        [FromQuery] string? section,
        [FromQuery] string? actionKind,
        [FromQuery] string? entityType,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? quickFilter,
        CancellationToken cancellationToken)
    {
        return Ok(await auditService.GetEventsAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, limit, section, actionKind, entityType, actorUserId, quickFilter), cancellationToken));
    }

    [HttpGet("events/{id:guid}")]
    [ProducesResponseType<AuditEventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditEventDto>> GetEvent(Guid id, CancellationToken cancellationToken)
    {
        var auditEvent = await auditService.GetEventAsync(id, cancellationToken);
        return auditEvent is null ? NotFound() : Ok(auditEvent);
    }

    [HttpGet("events/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportEvents(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? action,
        [FromQuery] string? search,
        [FromQuery] string? section,
        [FromQuery] string? actionKind,
        [FromQuery] string? entityType,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? quickFilter,
        CancellationToken cancellationToken)
    {
        var export = await auditService.ExportEventsCsvAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, null, section, actionKind, entityType, actorUserId, quickFilter), cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }
}
