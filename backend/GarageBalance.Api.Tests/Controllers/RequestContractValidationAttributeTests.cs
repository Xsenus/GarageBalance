using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class RequestContractValidationAttributeTests
{
    [Fact]
    public void ApplicationContractsDoNotUsePropertyTargetedValidationAttributes()
    {
        var apiRoot = FindApiProjectRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(apiRoot, "Application"), "*Contracts.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("[property:", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(apiRoot, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GarageDebtPaymentValidationIsCultureIndependent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var request = new CreateGarageDebtPaymentRequest(
                Guid.NewGuid(),
                new DateOnly(2026, 7, 15),
                new DateOnly(2026, 7, 1),
                1_417.38m,
                "Полное погашение");
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

            Assert.True(isValid);
            Assert.Empty(results);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void MeterReadingContractsRequireAnExplicitManualValue()
    {
        var requestTypes = new[]
        {
            typeof(CreateMeterReadingRequest),
            typeof(SavePaymentFormMeterReadingRequest),
            typeof(CorrectHistoricalMeterReadingRequest)
        };

        foreach (var requestType in requestTypes)
        {
            var constructor = Assert.Single(requestType.GetConstructors());
            var currentValue = Assert.Single(
                constructor.GetParameters(),
                parameter => string.Equals(parameter.Name, "CurrentValue", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(typeof(decimal?), currentValue.ParameterType);
            Assert.Contains(currentValue.GetCustomAttributes(inherit: true), attribute => attribute is RequiredAttribute);
        }
    }

    [Fact]
    public void MissingMeterReadingValueDeserializesAsNullInsteadOfZero()
    {
        var request = JsonSerializer.Deserialize<CreateMeterReadingRequest>(
            """
            {
              "garageId": "3ec3e358-00a8-40a2-a29b-9cceea22951f",
              "meterKind": "water",
              "accountingMonth": "2026-07-01",
              "readingDate": "2026-07-17"
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Null(request.CurrentValue);
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
