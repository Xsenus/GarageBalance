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
    public void AuditApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Audit",
            "AuditService.cs"));

        Assert.Contains("IAuditEventRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEventRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Audit",
            "IAuditEventRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfAuditEventRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IAuditEventRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfAuditEventRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IAuditEventRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IAuditEventRepository, EfAuditEventRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "ImportService.cs"));

        Assert.Contains("IImportRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "IImportRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfImportRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IImportRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfImportRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IImportRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IImportRepository, EfImportRepository>()", program, StringComparison.Ordinal);
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
    public void ImportQuarantineApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "ImportQuarantineService.cs"));

        Assert.Contains("IImportQuarantineRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportQuarantineRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Import",
            "IImportQuarantineRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfImportQuarantineRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IImportQuarantineRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfImportQuarantineRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IImportQuarantineRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IImportQuarantineRepository, EfImportQuarantineRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "ReceiptPrintingService.cs"));

        Assert.Contains("IReceiptPrintingRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPrintingRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IReceiptPrintingRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfReceiptPrintingRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IReceiptPrintingRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfReceiptPrintingRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IReceiptPrintingRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IReceiptPrintingRepository, EfReceiptPrintingRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void OneCFreshSyncApplicationService_UsesUnitOfWorkAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "OneCFreshSyncService.cs"));

        Assert.Contains("IApplicationUnitOfWork unitOfWork", service, StringComparison.Ordinal);
        Assert.Contains("unitOfWork.SaveChangesAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationSecretSettingsApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IntegrationSecretSettingsService.cs"));

        Assert.Contains("IIntegrationSecretSettingsRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationSecretSettingsRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Integrations",
            "IIntegrationSecretSettingsRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfIntegrationSecretSettingsRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IIntegrationSecretSettingsRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfIntegrationSecretSettingsRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IIntegrationSecretSettingsRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IIntegrationSecretSettingsRepository, EfIntegrationSecretSettingsRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Users",
            "UserManagementService.cs"));

        Assert.Contains("IUserManagementRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Users",
            "IUserManagementRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfUserManagementRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IUserManagementRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfUserManagementRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IUserManagementRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IUserManagementRepository, EfUserManagementRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FundApplicationService_DependsOnRepositoryAbstractionInsteadOfEfInfrastructure()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Funds", "FundService.cs"));

        Assert.Contains("IFundRepository repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.Api.Infrastructure", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FundRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Funds", "IFundRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfFundRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IFundRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfFundRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IFundRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFundRepository, EfFundRepository>()", program, StringComparison.Ordinal);
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
        Assert.Contains("IImportQuarantineRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfImportQuarantineRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IReceiptPrintingRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfReceiptPrintingRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("OneCFreshSyncService", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfIntegrationSecretSettingsRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IUserManagementRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfUserManagementRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFundRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFundRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IAuditEventRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfAuditEventRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IImportRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfImportRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains(nameof(BackendLayeringTests), layeringLine, StringComparison.Ordinal);
        Assert.Contains("выполнен двенадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен одиннадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен десятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен девятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен восьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен седьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен шестой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен пятый срез разделения backend-слоев", history, StringComparison.Ordinal);
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
