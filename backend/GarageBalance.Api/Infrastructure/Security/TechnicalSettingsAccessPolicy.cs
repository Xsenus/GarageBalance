using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;

namespace GarageBalance.Api.Infrastructure.Security;

public static class TechnicalSettingsAccessPolicy
{
    public static void Configure(AuthorizationOptions options) =>
        options.AddPolicy(SystemPolicies.TechnicalSettingsAccess, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.IsInRole(SystemRoles.Administrator)
                || !context.User.IsInRole(SystemRoles.Accountant)));
}
