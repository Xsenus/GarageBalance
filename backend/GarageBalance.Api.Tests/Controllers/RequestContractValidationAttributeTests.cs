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
