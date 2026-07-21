using System.Security.Claims;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Contracts.Settings;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(
    IApplicationSettingsService applicationSettingsService,
    IDatabaseBackupService databaseBackupService) : ControllerBase
{
    [HttpGet("payments/display")]
    [Authorize(Policy = SystemPermissions.PaymentsRead)]
    [ProducesResponseType<PaymentDisplaySettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDisplaySettingsDto>> GetPaymentDisplaySettings(CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.GetPaymentDisplaySettingsAsync(cancellationToken));
    }

    [HttpPut("payments/display")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<PaymentDisplaySettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDisplaySettingsDto>> UpdatePaymentDisplaySettings(
        UpdatePaymentDisplaySettingsRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.UpdatePaymentDisplaySettingsAsync(request, GetActorUserId(), cancellationToken));
    }

    [HttpGet("business-date")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<BusinessDateSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDateSettingsDto>> GetBusinessDateSettings(CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.GetBusinessDateSettingsAsync(cancellationToken));
    }

    [HttpPut("business-date")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<BusinessDateSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BusinessDateSettingsDto>> UpdateBusinessDateSettings(
        UpdateBusinessDateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await applicationSettingsService.UpdateBusinessDateSettingsAsync(request, GetActorUserId(), cancellationToken));
        }
        catch (BusinessDateValidationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "business_date_out_of_range",
                detail: exception.Message);
        }
    }

    [HttpGet("backups")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<DatabaseBackupStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DatabaseBackupStatusDto>> GetDatabaseBackups(CancellationToken cancellationToken)
    {
        return Ok(await databaseBackupService.GetStatusAsync(cancellationToken));
    }

    [HttpPost("backups")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<DatabaseBackupFileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DatabaseBackupFileDto>> CreateDatabaseBackup(
        CreateDatabaseBackupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await databaseBackupService.CreateAsync(
            DatabaseBackupKind.Manual,
            request.Reason,
            GetActorUserId(),
            cancellationToken);
        if (result.Succeeded)
        {
            return StatusCode(StatusCodes.Status201Created, result.Value);
        }

        var statusCode = result.ErrorCode switch
        {
            "database_backup_in_progress" => StatusCodes.Status409Conflict,
            "database_backup_disabled" or "database_backup_tools_unavailable" or "database_backup_dump_failed" or "database_backup_verification_failed" or "database_backup_failed" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(statusCode: statusCode, title: result.ErrorCode, detail: result.ErrorMessage);
    }

    private Guid? GetActorUserId()
    {
        var principal = ControllerContext.HttpContext?.User;
        return principal is not null && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
