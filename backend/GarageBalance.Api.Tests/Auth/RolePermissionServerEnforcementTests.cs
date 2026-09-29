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
    [Theory]
    [InlineData(nameof(GarageTariffAssignmentsController.Get), false)]
    [InlineData(nameof(GarageTariffAssignmentsController.Get), true)]
    [InlineData(nameof(GarageTariffAssignmentsController.Create), false)]
    [InlineData(nameof(GarageTariffAssignmentsController.Create), true)]
    [InlineData(nameof(GarageTariffAssignmentsController.Update), false)]
    [InlineData(nameof(GarageTariffAssignmentsController.Update), true)]
    [InlineData(nameof(GarageTariffAssignmentsController.Archive), false)]
    [InlineData(nameof(GarageTariffAssignmentsController.Archive), true)]
    public async Task IndividualTariffsRequireReadAndSeparateWritePermission(string action, bool write)
    {
        await using var provider = CreateServices();
        var policy = await GetEndpointPolicyAsync(provider, typeof(GarageTariffAssignmentsController), action);
        var granted = write ? new[] { SystemPermissions.DictionariesRead, SystemPermissions.TariffsManage }
            : new[] { SystemPermissions.DictionariesRead };
        Assert.Equal(action == nameof(GarageTariffAssignmentsController.Get) || write,
            (await AuthorizeAsync(provider, SystemRoles.Accountant, granted, policy)).Succeeded);
        Assert.False((await AuthorizeAsync(provider, SystemRoles.Accountant, [], policy)).Succeeded);
    }

    public static IEnumerable<object[]> TechnicalSettingsCases()
    {
        (Type Controller, string Action)[] endpoints =
        [
            (typeof(DiagnosticsController), nameof(DiagnosticsController.GetStatus)),
            (typeof(DiagnosticsController), nameof(DiagnosticsController.CreatePackage)),
            (typeof(IntegrationsController), nameof(IntegrationsController.UpdateProtectedSetting)),
            (typeof(IntegrationsController), nameof(IntegrationsController.GetOneCFreshStatus)),
            (typeof(IntegrationsController), nameof(IntegrationsController.StartOneCFreshSync)),
            (typeof(IntegrationsController), nameof(IntegrationsController.PreviewOneCFreshSync)),
            (typeof(IntegrationsController), nameof(IntegrationsController.RetryOneCFreshSync)),
            (typeof(IntegrationsController), nameof(IntegrationsController.GetReceiptPrintingStatus))
        ];
        foreach (var (controller, action) in endpoints)
        {
            yield return [controller, action, SystemRoles.Accountant, false];
            yield return [controller, action, SystemRoles.Administrator, true];
            yield return [controller, action, SystemRoles.Operator, true];
        }
    }

    [Theory]
    [MemberData(nameof(TechnicalSettingsCases))]
    public async Task TechnicalSettings_ExcludeAccountantEvenWithAllPermissions(
        Type controller, string action, string role, bool allowed)
    {
        await using var provider = CreateServices();
        var policy = await GetEndpointPolicyAsync(provider, controller, action);
        var result = await AuthorizeAsync(provider, role, SystemPermissions.All, policy);
        Assert.Equal(allowed, result.Succeeded);
    }

    [Theory]
    [InlineData(SystemRoles.Accountant, true, true, true, true)]
    [InlineData(SystemRoles.Accountant, true, false, true, false)]
    [InlineData(SystemRoles.Accountant, false, false, false, false)]
    [InlineData(SystemRoles.Operator, true, true, false, false)]
    [InlineData(SystemRoles.Administrator, true, true, true, true)]
    public async Task CashBank_EnforcesRolesAndReadWritePermissions(
        string role, bool read, bool write, bool expectedRead, bool expectedWrite)
    {
        await using var provider = CreateServices();
        var granted = new List<string>();
        if (read) granted.Add(SystemPermissions.PaymentsRead);
        if (write) granted.Add(SystemPermissions.PaymentsWrite);
        var readPolicy = await GetEndpointPolicyAsync(provider, typeof(SettingsController), nameof(SettingsController.GetCashBankBalances));
        var writePolicy = await GetEndpointPolicyAsync(provider, typeof(SettingsController), nameof(SettingsController.CreateCashBankBalanceAdjustment));
        Assert.Equal(expectedRead, (await AuthorizeAsync(provider, role, granted, readPolicy)).Succeeded);
        Assert.Equal(expectedWrite, (await AuthorizeAsync(provider, role, granted, writePolicy)).Succeeded);
        var openingPolicy = await GetEndpointPolicyAsync(provider, typeof(SettingsController), nameof(SettingsController.UpdateCashBankOpeningBalances));
        Assert.Equal(role == SystemRoles.Administrator, (await AuthorizeAsync(provider, role, granted, openingPolicy)).Succeeded);
    }

    [Fact]
    public async Task TechnicalSettings_RejectAnonymousAndRetainAdministratorInCombinedRoles()
    {
        await using var provider = CreateServices();
        var service = provider.GetRequiredService<IAuthorizationService>();
        Assert.False((await service.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, SystemPolicies.TechnicalSettingsAccess)).Succeeded);
        var combined = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, SystemRoles.Accountant),
            new Claim(ClaimTypes.Role, SystemRoles.Administrator)
        ], "Test"));
        Assert.True((await service.AuthorizeAsync(combined, null, SystemPolicies.TechnicalSettingsAccess)).Succeeded);
        var receiptPolicy = await GetEndpointPolicyAsync(provider, typeof(IntegrationsController), nameof(IntegrationsController.RegisterReceiptPrintingAction));
        Assert.True((await AuthorizeAsync(provider, SystemRoles.Accountant, SystemPermissions.Accountant, receiptPolicy)).Succeeded);
    }

    public static TheoryData<string, string[], Type, string, string> ForbiddenEndpointCases => new()
    {
        { SystemRoles.Operator, SystemPermissions.Operator, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.Get), SystemPermissions.ReportsRead },
        { SystemRoles.Operator, SystemPermissions.Operator, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.ExportXlsx), SystemPermissions.ReportsRead },
        { SystemRoles.Operator, SystemPermissions.Operator, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.ExportPdf), SystemPermissions.ReportsRead },
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
        },
        {
            "dictionary_editor",
            [SystemPermissions.DictionariesRead],
            typeof(FundsController),
            nameof(FundsController.GetFunds),
            SystemPermissions.ReportsRead
        }
    };

    public static TheoryData<string, string[], Type, string> AllowedEndpointCases => new()
    {
        { SystemRoles.ReportsViewer, SystemPermissions.ReportsViewer, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.Get) },
        { SystemRoles.ReportsViewer, SystemPermissions.ReportsViewer, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.ExportXlsx) },
        { SystemRoles.ReportsViewer, SystemPermissions.ReportsViewer, typeof(DailyServicePaymentsController), nameof(DailyServicePaymentsController.ExportPdf) },
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
        },
        {
            "dictionary_editor",
            [SystemPermissions.DictionariesRead],
            typeof(FundsController),
            nameof(FundsController.GetFundOptions)
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
            TechnicalSettingsAccessPolicy.Configure(options);
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
        var claims = permissions.Select(permission => new Claim("permission", permission))
            .Append(new Claim(ClaimTypes.Role, role));
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
