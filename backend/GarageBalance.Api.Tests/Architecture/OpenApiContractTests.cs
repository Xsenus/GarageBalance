using System.Reflection;
using GarageBalance.Api.Controllers;

namespace GarageBalance.Api.Tests.Architecture;

public sealed class OpenApiContractTests
{
    [Fact]
    public void ApiContracts_DoNotExposeDateTimeOffsetWithInvalidOptionalDefault()
    {
        var apiAssembly = typeof(HealthController).Assembly;

        var invalidParameters = apiAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("GarageBalance.Api.Application", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(DateTimeOffset)
                && parameter.HasDefaultValue
                && parameter.DefaultValue is not DateTimeOffset)
            .Select(parameter => $"{parameter.Member.DeclaringType?.FullName}.{parameter.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(invalidParameters);
    }
}
