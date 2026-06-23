using System.Reflection;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
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
