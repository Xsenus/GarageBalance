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
    private const string InvalidDateRangeTitle = "Проверьте период истории";
    private const string InvalidDateRangeDetail = "Начало периода истории изменений не может быть позже конца.";
    private const string InvalidPaginationTitle = "Проверьте пагинацию истории";
    private const int MaxPageLimit = 500;

    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<AuditEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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
        [FromQuery] string? relatedGarage,
        [FromQuery] string? relatedAccountingMonth,
        [FromQuery] string? relatedCounterparty,
        [FromQuery] string? relatedDocument,
        CancellationToken cancellationToken)
    {
        var invalidDateRange = ValidateDateRange(dateFrom, dateTo);
        if (invalidDateRange is not null)
        {
            return invalidDateRange;
        }

        return Ok(await auditService.GetEventsAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, limit, section, actionKind, entityType, actorUserId, quickFilter, null, relatedGarage, relatedAccountingMonth, relatedCounterparty, relatedDocument), cancellationToken));
    }

    [HttpGet("events/page")]
    [ProducesResponseType<AuditEventPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditEventPageDto>> GetEventsPage(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? action,
        [FromQuery] string? search,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] string? section,
        [FromQuery] string? actionKind,
        [FromQuery] string? entityType,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? quickFilter,
        [FromQuery] string? relatedGarage,
        [FromQuery] string? relatedAccountingMonth,
        [FromQuery] string? relatedCounterparty,
        [FromQuery] string? relatedDocument,
        CancellationToken cancellationToken)
    {
        var invalidDateRange = ValidateDateRange(dateFrom, dateTo);
        if (invalidDateRange is not null)
        {
            return invalidDateRange;
        }

        var invalidPaging = ValidatePaging(offset, limit);
        if (invalidPaging is not null)
        {
            return invalidPaging;
        }

        return Ok(await auditService.GetEventsPageAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, limit, section, actionKind, entityType, actorUserId, quickFilter, offset, relatedGarage, relatedAccountingMonth, relatedCounterparty, relatedDocument), cancellationToken));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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
        [FromQuery] string? relatedGarage,
        [FromQuery] string? relatedAccountingMonth,
        [FromQuery] string? relatedCounterparty,
        [FromQuery] string? relatedDocument,
        CancellationToken cancellationToken)
    {
        var invalidDateRange = ValidateDateRange(dateFrom, dateTo);
        if (invalidDateRange is not null)
        {
            return invalidDateRange;
        }

        var export = await auditService.ExportEventsCsvAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, null, section, actionKind, entityType, actorUserId, quickFilter, null, relatedGarage, relatedAccountingMonth, relatedCounterparty, relatedDocument), cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }

    [HttpGet("events/export/xlsx")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportEventsXlsx(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? action,
        [FromQuery] string? search,
        [FromQuery] string? section,
        [FromQuery] string? actionKind,
        [FromQuery] string? entityType,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? quickFilter,
        [FromQuery] string? relatedGarage,
        [FromQuery] string? relatedAccountingMonth,
        [FromQuery] string? relatedCounterparty,
        [FromQuery] string? relatedDocument,
        CancellationToken cancellationToken)
    {
        var invalidDateRange = ValidateDateRange(dateFrom, dateTo);
        if (invalidDateRange is not null)
        {
            return invalidDateRange;
        }

        var export = await auditService.ExportEventsXlsxAsync(new AuditEventListRequest(dateFrom, dateTo, action, search, null, section, actionKind, entityType, actorUserId, quickFilter, null, relatedGarage, relatedAccountingMonth, relatedCounterparty, relatedDocument), cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
    }

    private BadRequestObjectResult? ValidateDateRange(DateTimeOffset? dateFrom, DateTimeOffset? dateTo)
    {
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value > dateTo.Value)
        {
            return CreateBadRequestProblem(InvalidDateRangeTitle, InvalidDateRangeDetail);
        }

        return null;
    }

    private BadRequestObjectResult? ValidatePaging(int? offset, int? limit)
    {
        if (offset is < 0)
        {
            return CreateBadRequestProblem(InvalidPaginationTitle, "Смещение страницы истории не может быть отрицательным.");
        }

        if (limit is <= 0 or > MaxPageLimit)
        {
            return CreateBadRequestProblem(InvalidPaginationTitle, $"Количество строк истории должно быть от 1 до {MaxPageLimit}.");
        }

        return null;
    }

    private BadRequestObjectResult CreateBadRequestProblem(string title, string detail)
    {
        return BadRequest(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status400BadRequest,
        });
    }
}
