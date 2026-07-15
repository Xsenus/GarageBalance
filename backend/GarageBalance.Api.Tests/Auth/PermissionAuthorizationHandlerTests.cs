using System.Security.Claims;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PermissionAuthorizationHandlerTests
{
    public static TheoryData<string, string[], string> RolePermissionCases
    {
        get
        {
            var data = new TheoryData<string, string[], string>();
            var roles = new[]
            {
                (SystemRoles.Administrator, SystemPermissions.Administrator),
                (SystemRoles.Accountant, SystemPermissions.Accountant),
                (SystemRoles.Operator, SystemPermissions.Operator),
                (SystemRoles.ReportsViewer, SystemPermissions.ReportsViewer)
            };

            foreach (var (role, permissions) in roles)
            {
                foreach (var permission in SystemPermissions.All)
                {
                    data.Add(role, permissions, permission);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RolePermissionCases))]
    public async Task HandleAsync_EnforcesEveryBuiltInRolePermissionCombination(string role, string[] grantedPermissions, string requiredPermission)
    {
        var requirement = new PermissionRequirement(requiredPermission);
        var claims = grantedPermissions.Select(permission => new Claim("permission", permission));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, role));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.Equal(grantedPermissions.Contains(requiredPermission, StringComparer.Ordinal), context.HasSucceeded);
    }

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
