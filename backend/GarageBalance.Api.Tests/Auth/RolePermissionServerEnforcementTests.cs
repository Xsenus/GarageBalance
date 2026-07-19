using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GarageBalance.Api.Tests.Auth;

public sealed class RolePermissionServerEnforcementTests
{
    public static TheoryData<string, string[], Type, string, string> ForbiddenEndpointCases => new()
    {
        {
            SystemRoles.Operator,
            SystemPermissions.Operator,
            typeof(ReportsController),
            nameof(ReportsController.GetConsolidatedReport),
            SystemPermissions.ReportsRead
        },
        {
            SystemRoles.ReportsViewer,
            SystemPermissions.ReportsViewer,
            typeof(FinanceController),
            nameof(FinanceController.GetOperations),
            SystemPermissions.PaymentsRead
        },
        {
            "finance_reader",
            [SystemPermissions.PaymentsRead],
            typeof(FinanceController),
            nameof(FinanceController.CreateIncome),
            SystemPermissions.PaymentsWrite
        },
        {
            SystemRoles.Operator,
            SystemPermissions.Operator,
            typeof(ReportsController),
            nameof(ReportsController.ExportGarageReportXlsx),
            SystemPermissions.ReportsRead
        }
    };

    public static TheoryData<string, string[], Type, string> AllowedEndpointCases => new()
    {
        {
            SystemRoles.Operator,
            SystemPermissions.Operator,
            typeof(FinanceController),
            nameof(FinanceController.GetOperations)
        },
        {
            SystemRoles.ReportsViewer,
            SystemPermissions.ReportsViewer,
            typeof(ReportsController),
            nameof(ReportsController.GetConsolidatedReport)
        },
        {
            SystemRoles.ReportsViewer,
            SystemPermissions.ReportsViewer,
            typeof(ReportsController),
            nameof(ReportsController.ExportGarageReportXlsx)
        }
    };

    [Theory]
    [MemberData(nameof(ForbiddenEndpointCases))]
    public async Task ServerPipeline_ReturnsForbiddenBeforeEndpointForRoleWithoutRequiredPermission(
        string role,
        string[] grantedPermissions,
        Type controllerType,
        string actionName,
        string missingPermission)
    {
        await using var provider = CreateServices();
        var policy = await GetEndpointPolicyAsync(provider, controllerType, actionName);
        Assert.Contains(policy.Requirements.OfType<PermissionRequirement>(), requirement => requirement.Permission == missingPermission);

        var authorizationResult = await AuthorizeAsync(provider, role, grantedPermissions, policy);
        Assert.False(authorizationResult.Succeeded);

        var context = CreateHttpContext(provider);
        var nextCalled = false;
        var resultHandler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        await resultHandler.HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            policy,
            PolicyAuthorizationResult.Forbid());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var problem = await ReadProblemAsync(context);
        Assert.Equal(ApiProblemDetails.ForbiddenCode, problem.GetProperty("title").GetString());
        Assert.Equal(ApiProblemDetails.ForbiddenCode, problem.GetProperty(ApiProblemDetails.CodeExtensionKey).GetString());
    }

    [Theory]
    [MemberData(nameof(AllowedEndpointCases))]
    public async Task ServerPipeline_AllowsRoleWithRequiredPermission(
        string role,
        string[] grantedPermissions,
        Type controllerType,
        string actionName)
    {
        await using var provider = CreateServices();
        var policy = await GetEndpointPolicyAsync(provider, controllerType, actionName);

        var authorizationResult = await AuthorizeAsync(provider, role, grantedPermissions, policy);

        Assert.True(authorizationResult.Succeeded);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            foreach (var permission in SystemPermissions.All)
            {
                options.AddPolicy(permission, policy => policy.Requirements.Add(new PermissionRequirement(permission)));
            }
        });
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationMiddlewareResultHandler>();
        return services.BuildServiceProvider();
    }

    private static async Task<AuthorizationPolicy> GetEndpointPolicyAsync(
        IServiceProvider provider,
        Type controllerType,
        string actionName)
    {
        var action = controllerType.GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(action);

        var authorizeData = controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Cast<IAuthorizeData>()
            .ToArray();
        Assert.NotEmpty(authorizeData);

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
        Assert.NotNull(policy);
        return policy;
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        IServiceProvider provider,
        string role,
        IEnumerable<string> permissions,
        AuthorizationPolicy policy)
    {
        var claims = permissions.Select(permission => new Claim("permission", permission));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, role));
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        return await authorizationService.AuthorizeAsync(principal, resource: null, policy);
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider provider)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
