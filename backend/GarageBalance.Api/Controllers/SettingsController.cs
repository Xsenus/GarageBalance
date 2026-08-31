using System.Security.Claims;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Finance;
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
    ICashBankBalanceSettingsService cashBankBalanceSettingsService,
    IDatabaseBackupService databaseBackupService) : ControllerBase
{
    [HttpGet("payments/display")]
    [Authorize]
    [ProducesResponseType<PaymentDisplaySettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDisplaySettingsDto>> GetPaymentDisplaySettings(CancellationToken cancellationToken)
    {
        var payments = await applicationSettingsService.GetPaymentDisplaySettingsAsync(cancellationToken);
        var tariffs = await applicationSettingsService.GetTariffTableDisplaySettingsAsync(cancellationToken);
        return Ok(new PaymentDisplaySettingsDto(
            payments.ShowAllGarageOperationsByDefault,
            payments.Version,
            tariffs.ShowPeriodicityColumn,
            tariffs.ShowAccrualMonthColumn,
            tariffs.Version));
    }

    [HttpPut("payments/display")]
    [RequireConcurrencyVersion("request.Version")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<PaymentDisplaySettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentDisplaySettingsDto>> UpdatePaymentDisplaySettings(
        UpdatePaymentDisplaySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        var payments = await applicationSettingsService.UpdatePaymentDisplaySettingsAsync(request, actorUserId, cancellationToken);
        var tariffs = await applicationSettingsService.UpdateTariffTableDisplaySettingsAsync(
            new UpdateTariffTableDisplaySettingsRequest(
                request.ShowPeriodicityColumn,
                request.ShowAccrualMonthColumn,
                request.TariffTableVersion),
            actorUserId,
            cancellationToken);
        return Ok(new PaymentDisplaySettingsDto(
            payments.ShowAllGarageOperationsByDefault,
            payments.Version,
            tariffs.ShowPeriodicityColumn,
            tariffs.ShowAccrualMonthColumn,
            tariffs.Version));
    }

    [HttpGet("tariffs/layout")]
    [Authorize]
    [ProducesResponseType<TariffPanelsLayoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TariffPanelsLayoutDto>> GetTariffPanelsLayout(CancellationToken cancellationToken)
    {
        var userId = GetActorUserId();
        return userId.HasValue
            ? Ok(await applicationSettingsService.GetTariffPanelsLayoutAsync(userId.Value, cancellationToken))
            : Unauthorized();
    }

    [HttpPut("tariffs/layout")]
    [Authorize]
    [ProducesResponseType<TariffPanelsLayoutDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TariffPanelsLayoutDto>> UpdateTariffPanelsLayout(
        UpdateTariffPanelsLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetActorUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await applicationSettingsService.UpdateTariffPanelsLayoutAsync(request, userId.Value, cancellationToken));
        }
        catch (TariffPanelsLayoutValidationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "tariff_panels_layout_invalid",
                detail: exception.Message);
        }
    }

    [HttpGet("salary-accrual")]
    [Authorize(Policy = SystemPermissions.PaymentsRead)]
    [ProducesResponseType<SalaryAccrualSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SalaryAccrualSettingsDto>> GetSalaryAccrualSettings(CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.GetSalaryAccrualSettingsAsync(cancellationToken));
    }

    [HttpPut("salary-accrual")]
    [RequireConcurrencyVersion("request.Version")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<SalaryAccrualSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SalaryAccrualSettingsDto>> UpdateSalaryAccrualSettings(
        UpdateSalaryAccrualSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await applicationSettingsService.UpdateSalaryAccrualSettingsAsync(
                request,
                GetActorUserId(),
                cancellationToken));
        }
        catch (SalaryAccrualSettingsValidationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "salary_accrual_day_invalid",
                detail: exception.Message);
        }
    }

    [HttpGet("action-comments")]
    [Authorize]
    [ProducesResponseType<ActionCommentSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ActionCommentSettingsDto>> GetActionCommentSettings(CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.GetActionCommentSettingsAsync(cancellationToken));
    }

    [HttpPut("action-comments")]
    [RequireConcurrencyVersion("request.Version")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<ActionCommentSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ActionCommentSettingsDto>> UpdateActionCommentSettings(
        UpdateActionCommentSettingsRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.UpdateActionCommentSettingsAsync(
            request,
            GetActorUserId(),
            cancellationToken));
    }

    [HttpGet("business-date")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<BusinessDateSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDateSettingsDto>> GetBusinessDateSettings(CancellationToken cancellationToken)
    {
        return Ok(await applicationSettingsService.GetBusinessDateSettingsAsync(cancellationToken));
    }

    [HttpPost("business-date/preview")]
    [RequireConcurrencyVersion("request.Version")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<BusinessDateChangePreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BusinessDateChangePreviewDto>> PreviewBusinessDateChange(
        PreviewBusinessDateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await applicationSettingsService.PreviewBusinessDateChangeAsync(request, cancellationToken));
        }
        catch (BusinessDateValidationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "business_date_out_of_range",
                detail: exception.Message);
        }
    }

    [HttpPut("business-date")]
    [RequireConcurrencyVersion("request.Version")]
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

    [HttpGet("cash-bank-balances")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<CashBankBalanceSettingsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CashBankBalanceSettingsDto>> GetCashBankBalances(
        CancellationToken cancellationToken)
    {
        return Ok(await cashBankBalanceSettingsService.GetAsync(cancellationToken));
    }

    [HttpPut("cash-bank-balances/opening")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<CashBankBalanceSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashBankBalanceSettingsDto>> UpdateCashBankOpeningBalances(
        UpdateCashBankOpeningBalancesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await cashBankBalanceSettingsService.UpdateOpeningBalancesAsync(
            request,
            GetActorUserId(),
            cancellationToken);
        return ToCashBankBalanceActionResult(result);
    }

    [HttpPost("cash-bank-balances/adjustments")]
    [Authorize(Roles = SystemRoles.Administrator)]
    [ProducesResponseType<CashBankBalanceSettingsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashBankBalanceSettingsDto>> CreateCashBankBalanceAdjustment(
        CreateCashBankBalanceAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await cashBankBalanceSettingsService.CreateAdjustmentAsync(
            request,
            GetActorUserId(),
            cancellationToken);
        var response = ToCashBankBalanceActionResult(result);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : response;
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

    [HttpGet("backups/{fileName}/download")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadDatabaseBackup(
        string fileName,
        CancellationToken cancellationToken)
    {
        var result = await databaseBackupService.OpenDownloadAsync(fileName, GetActorUserId(), cancellationToken);
        if (result.Succeeded && result.Value is not null)
        {
            return File(
                result.Value.Content,
                "application/octet-stream",
                result.Value.FileName,
                enableRangeProcessing: true);
        }

        return ToDatabaseBackupProblem(result.ErrorCode, result.ErrorMessage);
    }

    [HttpDelete("backups/{fileName}")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<DatabaseBackupFileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DatabaseBackupFileDto>> DeleteDatabaseBackup(
        string fileName,
        [FromBody] DeleteDatabaseBackupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await databaseBackupService.DeleteAsync(
            fileName,
            request.Reason,
            GetActorUserId(),
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : ToDatabaseBackupProblem(result.ErrorCode, result.ErrorMessage);
    }

    private Guid? GetActorUserId()
    {
        var principal = ControllerContext.HttpContext?.User;
        return principal is not null && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ObjectResult ToDatabaseBackupProblem(string? errorCode, string? errorMessage)
    {
        var statusCode = errorCode switch
        {
            "database_backup_not_found" => StatusCodes.Status404NotFound,
            "database_backup_in_progress" => StatusCodes.Status409Conflict,
            "database_backup_download_failed" or "database_backup_delete_failed" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(statusCode: statusCode, title: errorCode, detail: errorMessage);
    }

    private ActionResult<CashBankBalanceSettingsDto> ToCashBankBalanceActionResult(
        FinanceResult<CashBankBalanceSettingsDto> result)
    {
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        var statusCode = result.ErrorCode is
            "insufficient_balance" or
            "opening_balance_below_committed_amount"
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return Problem(
            statusCode: statusCode,
            title: result.ErrorCode,
            detail: result.ErrorMessage);
    }
}
