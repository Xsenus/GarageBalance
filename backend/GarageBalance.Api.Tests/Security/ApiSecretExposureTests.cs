using System.Collections;
using System.Reflection;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GarageBalance.Api.Tests.Security;

public sealed class ApiSecretExposureTests
{
    private static readonly string[] SecretNameMarkers =
    [
        "password",
        "passwordhash",
        "signingkey",
        "secret",
        "apikey",
        "api_key",
        "token"
    ];

    private static readonly HashSet<string> AllowedSecretResponseProperties = new(StringComparer.Ordinal)
    {
        $"{nameof(AuthController)}.{nameof(AuthController.BootstrapAdmin)}:{nameof(AuthResponse)}.{nameof(AuthResponse.AccessToken)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Login)}:{nameof(AuthResponse)}.{nameof(AuthResponse.AccessToken)}"
    };

    [Fact]
    public void HttpResponseContractsDoNotExposeSecretLikeProperties()
    {
        var leaks = GetControllerActionMethods()
            .SelectMany(action => GetActionResponseTypes(action)
                .SelectMany(type => GetSecretLikePropertyPaths(type)
                    .Select(path => $"{action.DeclaringType!.Name}.{action.Name}:{path}")))
            .Where(path => !AllowedSecretResponseProperties.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(leaks);
    }

    [Fact]
    public void ConfigurationAndDomainSecretTypesAreNotReturnedFromControllers()
    {
        var forbiddenResponseTypes = GetControllerActionMethods()
            .SelectMany(GetActionResponseTypes)
            .Where(type =>
                type == typeof(JwtOptions) ||
                type.Namespace?.Contains(".Domain.", StringComparison.Ordinal) == true ||
                type.Name.EndsWith("Options", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(forbiddenResponseTypes);
    }

    private static IEnumerable<MethodInfo> GetControllerActionMethods()
    {
        return typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
    }

    private static IEnumerable<Type> GetActionResponseTypes(MethodInfo action)
    {
        var type = UnwrapTask(action.ReturnType);
        type = UnwrapActionResult(type);

        if (type is null || type == typeof(IActionResult) || type == typeof(ActionResult))
        {
            yield break;
        }

        yield return UnwrapCollection(type);
    }

    private static Type UnwrapTask(Type type)
    {
        if (type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static Type? UnwrapActionResult(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
        {
            return type.GetGenericArguments()[0];
        }

        if (typeof(IActionResult).IsAssignableFrom(type))
        {
            return null;
        }

        return type;
    }

    private static Type UnwrapCollection(Type type)
    {
        if (type == typeof(string))
        {
            return type;
        }

        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        var enumerableType = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableType?.GetGenericArguments()[0] ?? type;
    }

    private static IEnumerable<string> GetSecretLikePropertyPaths(Type type)
    {
        var visited = new HashSet<Type>();
        return GetSecretLikePropertyPaths(type, type.Name, visited);
    }

    private static IEnumerable<string> GetSecretLikePropertyPaths(Type type, string path, ISet<Type> visited)
    {
        type = UnwrapCollection(type);
        if (!visited.Add(type) || IsSimpleType(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var propertyPath = $"{path}.{property.Name}";
            if (IsSecretLikeName(property.Name))
            {
                yield return propertyPath;
            }

            foreach (var nestedPath in GetSecretLikePropertyPaths(property.PropertyType, propertyPath, visited))
            {
                yield return nestedPath;
            }
        }
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateOnly) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(Guid) ||
            typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string) && !type.IsGenericType;
    }

    private static bool IsSecretLikeName(string name)
    {
        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SecretNameMarkers.Any(marker => normalized.Contains(marker.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }
}
