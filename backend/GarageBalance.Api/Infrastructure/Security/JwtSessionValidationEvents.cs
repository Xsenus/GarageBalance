using System.Globalization;
using System.Security.Claims;
using GarageBalance.Api.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GarageBalance.Api.Infrastructure.Security;

public sealed class JwtSessionValidationEvents(IUserRepository users) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionVersionValue = context.Principal?.FindFirstValue(JwtClaimNames.SessionVersion);
        if (!Guid.TryParse(userIdValue, out var userId) ||
            !long.TryParse(sessionVersionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionVersion) ||
            sessionVersion < 1)
        {
            context.Fail("Токен не содержит действующую версию сессии.");
            return;
        }

        if (!await users.IsSessionValidAsync(userId, sessionVersion, context.HttpContext.RequestAborted))
        {
            context.Fail("Сессия пользователя отозвана.");
        }
    }
}
