using System.Reflection;
using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ControllerThinnessTests
{
    [Fact]
    public void Controllers_DoNotUseEfCoreOrInfrastructureDataDirectly()
    {
        var controllerRoot = Path.Combine(FindApiProjectRoot(), "Controllers");
        var forbiddenPatterns = new[]
        {
            "using Microsoft.EntityFrameworkCore",
            "GarageBalanceDbContext",
            "Infrastructure.Data",
            "DbSet<",
            ".SaveChanges",
            ".FromSql",
            ".ExecuteSql"
        };

        var offenders = Directory.EnumerateFiles(controllerRoot, "*Controller.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);

                return forbiddenPatterns
                    .Where(pattern => text.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetFileName(path)} contains {pattern}");
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ControllerConstructors_DoNotDependOnInfrastructureServices()
    {
        var offenders = GetControllerTypes()
            .SelectMany(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(ctor => ctor.GetParameters()
                .Where(parameter => IsForbiddenInfrastructureType(parameter.ParameterType))
                .Select(parameter => $"{ctor.DeclaringType!.Name} depends on {parameter.ParameterType.FullName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ControllerConstructors_DependOnServiceAbstractionsOnly()
    {
        var offenders = GetControllerTypes()
            .SelectMany(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(ctor => ctor.GetParameters()
                .Where(parameter => IsGarageBalanceServiceType(parameter.ParameterType) && !parameter.ParameterType.IsInterface)
                .Select(parameter => $"{ctor.DeclaringType!.Name} depends on concrete service {parameter.ParameterType.FullName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ControllerActions_DoNotExposeDomainEntities()
    {
        var offenders = GetControllerActionMethods()
            .SelectMany(method => EnumerateReturnedTypes(method.ReturnType)
                .Where(IsDomainType)
                .Select(type => $"{method.DeclaringType!.Name}.{method.Name} returns {type.FullName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DangerousControllerActions_RequireConstrainedReasonRequest()
    {
        var offenders = GetControllerActionMethods()
            .Where(IsDangerousActionName)
            .Where(method => !method.GetParameters().Any(parameter => RequestTypeHasConstrainedRequiredReason(parameter.ParameterType)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name} must require a request body with required Reason limited to 1000 characters.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> GetControllerTypes()
    {
        return typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));
    }

    private static IEnumerable<MethodInfo> GetControllerActionMethods()
    {
        return GetControllerTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
    }

    private static IEnumerable<Type> EnumerateReturnedTypes(Type type)
    {
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(EnumerateReturnedTypes))
            {
                yield return argument;
            }
        }

        if (type.IsArray)
        {
            foreach (var elementType in EnumerateReturnedTypes(type.GetElementType()!))
            {
                yield return elementType;
            }
        }

        yield return type;
    }

    private static bool IsForbiddenInfrastructureType(Type type)
    {
        var typeName = type.FullName ?? type.Name;

        return typeName.Contains(".Infrastructure.", StringComparison.Ordinal) ||
            typeName.EndsWith("DbContext", StringComparison.Ordinal) ||
            typeName.EndsWith("Repository", StringComparison.Ordinal);
    }

    private static bool IsGarageBalanceServiceType(Type type)
    {
        var typeName = type.FullName ?? type.Name;

        return type.Assembly == typeof(AuthController).Assembly &&
            (typeName.Contains(".Application.", StringComparison.Ordinal) ||
                typeName.Contains(".Domain.", StringComparison.Ordinal) ||
                typeName.Contains(".Infrastructure.", StringComparison.Ordinal));
    }

    private static bool IsDomainType(Type type)
    {
        return (type.Namespace?.StartsWith("GarageBalance.Api.Domain.", StringComparison.Ordinal) ?? false) &&
            type.Assembly == typeof(AuthController).Assembly;
    }

    private static bool IsDangerousActionName(MethodInfo method)
    {
        return method.Name.StartsWith("Archive", StringComparison.Ordinal) ||
            method.Name.StartsWith("Cancel", StringComparison.Ordinal) ||
            method.Name.StartsWith("Delete", StringComparison.Ordinal);
    }

    private static bool RequestTypeHasConstrainedRequiredReason(Type type)
    {
        var requestType = Nullable.GetUnderlyingType(type) ?? type;
        var reasonProperty = requestType.GetProperty("Reason", BindingFlags.Instance | BindingFlags.Public);
        var reasonConstructorParameter = requestType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(ctor => ctor.GetParameters())
            .FirstOrDefault(parameter => parameter.Name?.Equals("Reason", StringComparison.OrdinalIgnoreCase) == true);

        var maxLength = reasonProperty?.GetCustomAttribute<MaxLengthAttribute>() ??
            reasonConstructorParameter?.GetCustomAttribute<MaxLengthAttribute>();

        return reasonProperty?.PropertyType == typeof(string) &&
            (reasonProperty.GetCustomAttribute<RequiredAttribute>() is not null ||
                reasonConstructorParameter?.GetCustomAttribute<RequiredAttribute>() is not null) &&
            maxLength is { Length: > 0 and <= 1000 };
    }

    private static string FindApiProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "GarageBalance.Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти проект GarageBalance.Api.");
    }
}
