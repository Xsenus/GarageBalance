using System.Security.Claims;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(IApplicationSettingsService applicationSettingsService) : ControllerBase
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

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }
}
