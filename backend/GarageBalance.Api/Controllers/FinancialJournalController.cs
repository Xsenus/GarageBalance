using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.PaymentsRead)]
[Route("api/finance/journal")]
public sealed class FinancialJournalController(IFinancialJournalQuery query) : ControllerBase
{
    private static readonly HashSet<string> SupportedEntityTypes =
    [
        "financial_operation",
        "accrual",
        "supplier_accrual",
        "staff_salary_adjustment",
        "fund_operation",
        "cash_bank_transfer",
        "cash_bank_balance_operation"
    ];

    [HttpGet("page")]
    [ProducesResponseType<FinancePagedResult<FinancialJournalEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FinancePagedResult<FinancialJournalEntryDto>>> GetPage(
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] string? entityType,
        [FromQuery] string? counterparty,
        [FromQuery] string? status,
        [FromQuery] string? document,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Начальная дата журнала не может быть позже конечной.");
        }

        var normalizedEntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim();
        if (normalizedEntityType is not null && !SupportedEntityTypes.Contains(normalizedEntityType))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Неизвестный вид финансовой операции.");
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (normalizedStatus is not null && normalizedStatus is not "active" and not "canceled")
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Статус журнала должен быть active или canceled.");
        }

        var result = await query.GetPageAsync(
            new FinancialJournalRequest(dateFrom, dateTo, normalizedEntityType, counterparty, normalizedStatus, document, offset, limit),
            cancellationToken);
        return Ok(result);
    }
}
