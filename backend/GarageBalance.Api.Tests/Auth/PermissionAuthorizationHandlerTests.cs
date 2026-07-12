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

    [Fact]
    public async Task HandleAsync_SucceedsForReportsReadPermission()
    {
        var requirement = new PermissionRequirement("reports.read");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "reports.read")], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_DoesNotSucceedForReportsReadWhenPermissionClaimIsMissing()
    {
        var requirement = new PermissionRequirement("reports.read");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "dictionaries.read")], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData("dictionaries.read", "reports.read")]
    [InlineData("dictionaries.write", "dictionaries.read")]
    [InlineData("tariffs.manage", "dictionaries.write")]
    public async Task HandleAsync_DoesNotSucceedForDictionaryPolicyWhenRequiredClaimIsMissing(string requiredPermission, string grantedPermission)
    {
        var requirement = new PermissionRequirement(requiredPermission);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", grantedPermission)], "Test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
