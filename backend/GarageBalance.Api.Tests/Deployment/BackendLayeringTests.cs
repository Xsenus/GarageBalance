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
    public void AppReleaseApplicationService_UsesUnitOfWorkAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Releases",
            "AppReleaseService.cs"));

        Assert.Contains("IApplicationUnitOfWork? unitOfWork", service, StringComparison.Ordinal);
        Assert.Contains("unitOfWork.SaveChangesAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationUnitOfWork_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Common",
            "IApplicationUnitOfWork.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfApplicationUnitOfWork.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IApplicationUnitOfWork", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfApplicationUnitOfWork", implementation, StringComparison.Ordinal);
        Assert.Contains(": IApplicationUnitOfWork", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IApplicationUnitOfWork, EfApplicationUnitOfWork>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEventWriter_DependsOnStoreAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var writer = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Audit",
            "AuditEventWriter.cs"));

        Assert.Contains("IAuditEventStore store", writer, StringComparison.Ordinal);
        Assert.Contains("store.Add(auditEvent)", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEventStore_IsImplementedByInfrastructureContextAndRegisteredAsScopedAlias()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Audit",
            "IAuditEventStore.cs"));
        var dbContext = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "GarageBalanceDbContext.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IAuditEventStore", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("DbContext(options), IAuditEventStore", dbContext, StringComparison.Ordinal);
        Assert.Contains("void IAuditEventStore.Add", dbContext, StringComparison.Ordinal);
        Assert.Contains("Set<AuditEvent>().Add(auditEvent)", dbContext, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IAuditEventStore>", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<GarageBalanceDbContext>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportFingerprintApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "ImportFingerprintService.cs"));

        Assert.Contains("IImportFingerprintRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportFingerprintRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "IImportFingerprintRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfImportFingerprintRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IImportFingerprintRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfImportFingerprintRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IImportFingerprintRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IImportFingerprintRepository, EfImportFingerprintRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendLayeringProgress_IsRecordedWithoutClosingRemainingApplicationServices()
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
        Assert.Contains("IApplicationUnitOfWork", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfApplicationUnitOfWork", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IAuditEventStore", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IImportFingerprintRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfImportFingerprintRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains(nameof(BackendLayeringTests), layeringLine, StringComparison.Ordinal);
        Assert.Contains("выполнен четвертый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен третий срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен второй срез разделения backend-слоев", history, StringComparison.Ordinal);
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
