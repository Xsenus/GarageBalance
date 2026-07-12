namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendLayeringTests
{
    [Fact]
    public void FormStateApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Workflows",
            "FormStateService.cs"));

        Assert.Contains("IFormStateRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FormStateRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Workflows",
            "IFormStateRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfFormStateRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IFormStateRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfFormStateRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IFormStateRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFormStateRepository, EfFormStateRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FormStateRepositoryLayeringProgress_IsRecordedWithoutClosingRemainingApplicationServices()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var history = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var layeringLine = activeRoadmapLines.Single(line =>
            line.Contains("Backend разделить на слои", StringComparison.Ordinal));

        Assert.StartsWith("- `[~]`", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFormStateRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFormStateRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains(nameof(BackendLayeringTests), layeringLine, StringComparison.Ordinal);
        Assert.Contains("выполнен следующий срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("Общий layering-пункт остается `[~]`", history, StringComparison.Ordinal);
        Assert.Contains("Новая запись \"Что нового\" не добавлялась", history, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти корень репозитория GarageBalance.");
    }
}
