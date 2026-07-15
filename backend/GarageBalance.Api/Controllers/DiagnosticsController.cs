using System.Security.Claims;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Contracts.Diagnostics;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(
    IDiagnosticPackageService packageService,
    ILogger<DiagnosticsController> logger) : ControllerBase
{
    [HttpPost("client-errors")]
    [Authorize]
    [EnableRateLimiting("client-diagnostics")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult ReportClientError(ClientErrorReportRequest request)
    {
        logger.LogError(
            "Client runtime error {ClientErrorId}; Name={ErrorName}; Route={Route}; ComponentStack={ComponentStack}",
            DiagnosticLogSanitizer.Sanitize(request.ClientErrorId),
            DiagnosticLogSanitizer.Sanitize(request.ErrorName),
            DiagnosticLogSanitizer.Sanitize(request.Route),
            DiagnosticLogSanitizer.Sanitize(request.ComponentStack));
        return Accepted();
    }

    [HttpGet("status")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [ProducesResponseType<DiagnosticLogStatusDto>(StatusCodes.Status200OK)]
    public ActionResult<DiagnosticLogStatusDto> GetStatus() => Ok(packageService.GetStatus());

    [HttpPost("package")]
    [Authorize(Policy = SystemPermissions.UsersManage)]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreatePackage(CancellationToken cancellationToken)
    {
        var package = await packageService.CreatePackageAsync(GetActorUserId(), cancellationToken);
        if (package is not null)
        {
            return File(package.Content, "application/zip", package.FileName);
        }

        var status = packageService.GetStatus();
        var code = status.Enabled ? "diagnostic_package_unavailable" : "diagnostic_logging_disabled";
        var detail = status.Enabled
            ? "Не удалось сформировать диагностический пакет. Проверьте состояние папки журналов и повторите попытку."
            : "Сбор диагностических журналов отключен на сервере.";
        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            ApiProblemDetails.Create(code, detail, StatusCodes.Status503ServiceUnavailable));
    }

    private Guid? GetActorUserId() =>
        Guid.TryParse(HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
