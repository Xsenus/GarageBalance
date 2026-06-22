using System.Security.Claims;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_SucceedsWhenPermissionClaimExists()
    {
        var requirement = new PermissionRequirement("users.manage");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "users.manage")], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_DoesNotSucceedWhenPermissionClaimIsMissing()
    {
        var requirement = new PermissionRequirement("users.manage");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "reports.read")], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
