using System.Reflection;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GarageBalance.Api.Tests.Auth;

public sealed class ControllerAuthorizationCoverageTests
{
    private static readonly HashSet<string> AnonymousActions = new(StringComparer.Ordinal)
    {
        $"{nameof(AuthController)}.{nameof(AuthController.BootstrapAdmin)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
        $"{nameof(HealthController)}.{nameof(HealthController.Get)}"
    };

    [Fact]
    public void EveryHttpActionExplicitlyDeclaresAuthorizationOrAnonymousAccess()
    {
        var missingActions = GetControllerActionMethods()
            .Where(method => !HasAuthorizationMetadata(method.DeclaringType!) && !HasAuthorizationMetadata(method))
            .Where(method => !HasAnonymousMetadata(method.DeclaringType!) && !HasAnonymousMetadata(method))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missingActions);
    }

    [Fact]
    public void AnonymousAccessIsAllowedOnlyForLoginBootstrapAndHealth()
    {
        var anonymousActions = GetControllerActionMethods()
            .Where(method => HasAnonymousMetadata(method.DeclaringType!) || HasAnonymousMetadata(method))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(AnonymousActions.Order(StringComparer.Ordinal), anonymousActions);
    }

    [Fact]
    public void ProtectedHttpActionsRequireAuthorization()
    {
        var unprotectedActions = GetControllerActionMethods()
            .Where(method => !AnonymousActions.Contains($"{method.DeclaringType!.Name}.{method.Name}"))
            .Where(method => !HasAuthorizationMetadata(method.DeclaringType!) && !HasAuthorizationMetadata(method))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unprotectedActions);
    }

    [Theory]
    [InlineData(nameof(DictionariesController.CreateTariff))]
    [InlineData(nameof(DictionariesController.UpdateTariff))]
    [InlineData(nameof(DictionariesController.ArchiveTariff))]
    public void TariffWriteActionsRequireTariffManagementPermission(string actionName)
    {
        var action = typeof(DictionariesController).GetMethod(actionName);
        Assert.NotNull(action);

        var policy = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SingleOrDefault(attribute => attribute.Policy == SystemPermissions.TariffsManage);

        Assert.NotNull(policy);
    }

    [Fact]
    public void ReportActionsRequireReportsReadPermission()
    {
        var controllerPolicy = typeof(ReportsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SingleOrDefault(attribute => attribute.Policy == SystemPermissions.ReportsRead);
        Assert.NotNull(controllerPolicy);

        var anonymousReportActions = typeof(ReportsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Where(method => HasAnonymousMetadata(method))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(anonymousReportActions);
    }

    [Fact]
    public void FinanceActionsRequireExpectedPaymentPermissions()
    {
        var controllerPolicy = typeof(FinanceController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SingleOrDefault(attribute => attribute.Policy == SystemPermissions.PaymentsRead);
        Assert.NotNull(controllerPolicy);

        var financeActions = typeof(FinanceController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToList();

        var anonymousFinanceActions = financeActions
            .Where(method => HasAnonymousMetadata(method))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(anonymousFinanceActions);

        var mutatingActionsWithoutWritePolicy = financeActions
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(attribute => attribute.HttpMethods).Any(httpMethod => !string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)))
            .Where(method => !method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(attribute => attribute.Policy == SystemPermissions.PaymentsWrite))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(mutatingActionsWithoutWritePolicy);
    }

    [Fact]
    public void DictionaryActionsRequireExpectedDictionaryPermissions()
    {
        var controllerPolicy = typeof(DictionariesController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SingleOrDefault(attribute => attribute.Policy == SystemPermissions.DictionariesRead);
        Assert.NotNull(controllerPolicy);

        var dictionaryActions = typeof(DictionariesController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToList();

        var anonymousDictionaryActions = dictionaryActions
            .Where(method => HasAnonymousMetadata(method))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(anonymousDictionaryActions);

        var mutatingActions = dictionaryActions
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                .SelectMany(attribute => attribute.HttpMethods)
                .Any(httpMethod => !string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var mutatingDictionaryActionsWithoutWritePolicy = mutatingActions
            .Where(method => !method.Name.Contains("Tariff", StringComparison.Ordinal))
            .Where(method => !method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(attribute => attribute.Policy == SystemPermissions.DictionariesWrite))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(mutatingDictionaryActionsWithoutWritePolicy);

        var mutatingTariffActionsWithoutTariffPolicy = mutatingActions
            .Where(method => method.Name.Contains("Tariff", StringComparison.Ordinal))
            .Where(method => !method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(attribute => attribute.Policy == SystemPermissions.TariffsManage))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(mutatingTariffActionsWithoutTariffPolicy);
    }

    private static IEnumerable<MethodInfo> GetControllerActionMethods()
    {
        return typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
    }

    private static bool HasAuthorizationMetadata(MemberInfo member)
    {
        return member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
    }

    private static bool HasAnonymousMetadata(MemberInfo member)
    {
        return member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
    }
}
