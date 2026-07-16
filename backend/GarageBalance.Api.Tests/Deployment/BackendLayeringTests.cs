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
    public void FinanceFundDepositQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Funds", "IFundRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfFundRepository.cs"));

        Assert.Contains("IFundRepository fundRepository", service, StringComparison.Ordinal);
        Assert.Equal(2, service.Split("fundRepository.GetActiveDepositTotalAsync(cancellationToken)", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("dbContext.FundOperations", service, StringComparison.Ordinal);
        Assert.Contains("GetActiveDepositTotalAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("GetActiveDepositTotalAsync", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void CashMovementReportMethods_DelegatePersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("ICashMovementReportQuery cashMovementReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("cashMovementReportQuery.GetCashPaymentsAsync", service, StringComparison.Ordinal);
        Assert.Contains("cashMovementReportQuery.GetBankDepositsAsync", service, StringComparison.Ordinal);
        Assert.Contains("NormalizeReportLimit(request.Limit.Value)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CashMovementReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ICashMovementReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfCashMovementReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface ICashMovementReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfCashMovementReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": ICashMovementReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ICashMovementReportQuery, EfCashMovementReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FundChangeReportMethod_DelegatesPersistenceQueryToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IFundChangeReportQuery fundChangeReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("fundChangeReportQuery.GetFundChangesAsync", service, StringComparison.Ordinal);
        Assert.Contains("data.UsersById.TryGetValue", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FundChangeReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IFundChangeReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfFundChangeReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IFundChangeReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfFundChangeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IFundChangeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFundChangeReportQuery, EfFundChangeReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedMonthlyReport_DelegatesPersistenceQueryToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IConsolidatedMonthlyReportQuery consolidatedMonthlyReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("consolidatedMonthlyReportQuery.GetMonthlyDataAsync", service, StringComparison.Ordinal);
        Assert.Contains("MonthPeriod.Enumerate(periodFrom, periodTo)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedMonthlyReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IConsolidatedMonthlyReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfConsolidatedMonthlyReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IConsolidatedMonthlyReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfConsolidatedMonthlyReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IConsolidatedMonthlyReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IConsolidatedMonthlyReportQuery, EfConsolidatedMonthlyReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedGarageReport_DelegatesPersistenceQueryToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IConsolidatedGarageReportQuery consolidatedGarageReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("consolidatedGarageReportQuery.GetGarageRowsAsync", service, StringComparison.Ordinal);
        Assert.Contains("FormatOwnerName(row.OwnerLastName", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsolidatedGarageReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IConsolidatedGarageReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfConsolidatedGarageReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IConsolidatedGarageReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfConsolidatedGarageReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IConsolidatedGarageReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IConsolidatedGarageReportQuery, EfConsolidatedGarageReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeReport_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IFeeReportQuery feeReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("feeReportQuery.GetActiveCampaignsAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeReportQuery.GetActiveIncomeTypesAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeReportQuery.GetFeeDataAsync", service, StringComparison.Ordinal);
        Assert.Contains("BuildFeeGoal", service, StringComparison.Ordinal);
        Assert.Contains("accrued - paid", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IFeeReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfFeeReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IFeeReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfFeeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IFeeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFeeReportQuery, EfFeeReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseReport_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IExpenseReportQuery expenseReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("expenseReportQuery.GetRowsAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Suppliers", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SupplierAccruals", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IExpenseReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfExpenseReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IExpenseReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfExpenseReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IExpenseReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IExpenseReportQuery, EfExpenseReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeReport_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IIncomeReportQuery incomeReportQuery", service, StringComparison.Ordinal);
        Assert.Contains("incomeReportQuery.GetRowsAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure.Data", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeReportQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "IIncomeReportQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Data",
            "EfIncomeReportQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IIncomeReportQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfIncomeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains(": IIncomeReportQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("CalculateDebtAfterPaymentsAsync", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IIncomeReportQuery, EfIncomeReportQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportAuditPersistence_UsesApplicationUnitOfWorkInsteadOfSavingDbContextDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Reports",
            "ReportService.cs"));

        Assert.Contains("IApplicationUnitOfWork unitOfWork", service, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.SaveChangesAsync(cancellationToken)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SaveChangesAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryPersistence_UsesApplicationUnitOfWorkInsteadOfSavingDbContextDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Dictionaries",
            "DictionaryService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Common",
            "DictionaryServiceTestFactory.cs"));

        Assert.Contains("IApplicationUnitOfWork unitOfWork", service, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.SaveChangesAsync(cancellationToken)", service, StringComparison.Ordinal);
        Assert.Contains("new EfApplicationUnitOfWork(dbContext)", testFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SaveChangesAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancePersistence_UsesApplicationUnitOfWorkInsteadOfSavingDbContextDirectly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Application",
            "Finance",
            "FinanceService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "backend",
            "GarageBalance.Api.Tests",
            "Common",
            "FinanceServiceTestFactory.cs"));

        Assert.Contains("IApplicationUnitOfWork unitOfWork", service, StringComparison.Ordinal);
        Assert.Contains("await unitOfWork.SaveChangesAsync(cancellationToken)", service, StringComparison.Ordinal);
        Assert.Contains("new EfApplicationUnitOfWork(dbContext)", testFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SaveChangesAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));

        Assert.Contains("IOwnerRepository ownerRepository", service, StringComparison.Ordinal);
        Assert.Contains("ownerRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("ownerRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("ownerRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("ownerRepository.FindArchivedWithGaragesAsync", service, StringComparison.Ordinal);
        Assert.Contains("ownerRepository.Add(owner)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IOwnerRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfOwnerRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface IOwnerRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfOwnerRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": IOwnerRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IOwnerRepository, EfOwnerRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IGarageRepository garageRepository", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.GetBalanceTotalsAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.ActiveNumberExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.FindActiveWithOwnerAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.FindArchivedWithOwnerAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.GetActiveByIdsAsync", service, StringComparison.Ordinal);
        Assert.Contains("garageRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Garages", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Owners", service, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IGarageRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfGarageRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IGarageRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IGarageRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IGarageRepository, EfGarageRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceGarageLookupsAndBatches_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IGarageRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfGarageRepository.cs"));

        Assert.Contains("IGarageRepository garageRepository", service, StringComparison.Ordinal);
        Assert.True(
            service.Split("garageRepository.FindActiveWithOwnerAsync", StringSplitOptions.None).Length - 1 >= 9,
            "Finance workflows must share the active garage lookup instead of querying the DbSet directly.");
        Assert.Equal(2, service.Split("garageRepository.GetAllActiveWithOwnerAsync(cancellationToken)", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, service.Split("garageRepository.GetStartingBalanceAsync(garageId, cancellationToken)", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("dbContext.Garages", service, StringComparison.Ordinal);
        Assert.Contains("IMissingMeterReadingQuery missingMeterReadingQuery", service, StringComparison.Ordinal);
        Assert.Contains("GetAllActiveWithOwnerAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("GetStartingBalanceAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains(".Where(garage => !garage.IsArchived)", implementation, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(garage => garage.Number)", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingMeterReadingQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IMissingMeterReadingQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfMissingMeterReadingQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));

        Assert.Contains("interface IMissingMeterReadingQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IMissingMeterReadingQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("missingMeterReadingQuery.GetMissingAsync", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IMissingMeterReadingQuery, EfMissingMeterReadingQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageIncomeWorksheetQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IGarageIncomeWorksheetQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfGarageIncomeWorksheetQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Common", "FinanceServiceTestFactory.cs"));

        Assert.Contains("interface IGarageIncomeWorksheetQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IGarageIncomeWorksheetQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("IGarageIncomeWorksheetQuery garageIncomeWorksheetQuery", service, StringComparison.Ordinal);
        Assert.Contains("garageIncomeWorksheetQuery.GetAsync", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IGarageIncomeWorksheetQuery, EfGarageIncomeWorksheetQuery>()", program, StringComparison.Ordinal);
        Assert.Contains("new EfGarageIncomeWorksheetQuery(dbContext)", testFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void GarageBalanceHistoryQuery_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IGarageBalanceHistoryQuery.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfGarageBalanceHistoryQuery.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Common", "FinanceServiceTestFactory.cs"));

        Assert.Contains("interface IGarageBalanceHistoryQuery", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IGarageBalanceHistoryQuery", implementation, StringComparison.Ordinal);
        Assert.Contains("IGarageBalanceHistoryQuery garageBalanceHistoryQuery", service, StringComparison.Ordinal);
        Assert.Contains("garageBalanceHistoryQuery.GetAsync", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IGarageBalanceHistoryQuery, EfGarageBalanceHistoryQuery>()", program, StringComparison.Ordinal);
        Assert.Contains("new EfGarageBalanceHistoryQuery(dbContext)", testFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void MeterReadingLists_DelegateToInfrastructureRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IMeterReadingRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfMeterReadingRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));

        Assert.Contains("interface IMeterReadingRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IMeterReadingRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("IMeterReadingRepository meterReadingRepository", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("meterReadingRepository.GetForGaragePeriodAsync", service, StringComparison.Ordinal);
        Assert.Contains("IFinanceSectionCountQuery financeSectionCountQuery", service, StringComparison.Ordinal);
        Assert.Contains("financeSectionCountQuery.GetAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetPreviousActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetNextActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.FindForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.GetActiveByGarageIdsAsync", service, StringComparison.Ordinal);
        Assert.Contains("meterReadingRepository.Add(reading)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.MeterReadings", service, StringComparison.Ordinal);
        Assert.DoesNotContain("private IQueryable<MeterReading> QueryMeterReadings", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyMeterReadingFilters", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IMeterReadingRepository, EfMeterReadingRepository>()", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFinanceSectionCountQuery, EfFinanceSectionCountQuery>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialOperationLists_DelegateToInfrastructureRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IFinancialOperationRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfFinancialOperationRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Common", "FinanceServiceTestFactory.cs"));

        Assert.Contains("interface IFinancialOperationRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IFinancialOperationRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("IFinancialOperationRepository financialOperationRepository", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.FindForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.ActiveDocumentDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetIncomeTotalBeforeMonthAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("financialOperationRepository.GetIncomeMonthlyBucketsAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("financialOperationRepository.GetIncomeTypeBucketsAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetWorksheetDataAsync", service, StringComparison.Ordinal);
        Assert.Contains("IFinanceTotalsQuery financeTotalsQuery", service, StringComparison.Ordinal);
        Assert.Contains("financeTotalsQuery.GetAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetOpeningDebtPaymentTotalAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetBankExpenseTotalAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetCashBalanceDataAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetStaffExpenseTotalAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetPreviousGarageIncomeTotalAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.GetPreviousSupplierExpenseTotalAsync", service, StringComparison.Ordinal);
        Assert.Contains("financialOperationRepository.Add(operation)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.FinancialOperations", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure.Data", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
        Assert.DoesNotContain("private IQueryable<FinancialOperation> QueryOperations", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFilters", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFinancialOperationRepository, EfFinancialOperationRepository>()", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFinanceTotalsQuery, EfFinanceTotalsQuery>()", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFinanceService>(services => new FinanceService(", program, StringComparison.Ordinal);
        Assert.Contains("class FinanceServiceTestFactory", testFactory, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", testFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void AccrualLists_DelegateToInfrastructureRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "IAccrualRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfAccrualRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));

        Assert.Contains("interface IAccrualRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IAccrualRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("IAccrualRepository accrualRepository", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.GetTotalBeforeMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.GetMonthlyBucketsAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("accrualRepository.GetIncomeTypeBucketsAsync", service, StringComparison.Ordinal);
        Assert.Contains("financeTotalsQuery.GetAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.FindForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.FindActiveForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.GetTotalThroughMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("accrualRepository.Add(accrual)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Accruals", service, StringComparison.Ordinal);
        Assert.DoesNotContain("private IQueryable<Accrual> QueryAccruals", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAccrualFilters", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IAccrualRepository, EfAccrualRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierAccrualLists_DelegateToInfrastructureRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "ISupplierAccrualRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfSupplierAccrualRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));

        Assert.Contains("interface ISupplierAccrualRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": ISupplierAccrualRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("ISupplierAccrualRepository supplierAccrualRepository", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetActiveForMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.FindForUpdateAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetTotalThroughMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.GetMonthlyBucketsThroughMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierAccrualRepository.Add(accrual)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SupplierAccruals", service, StringComparison.Ordinal);
        Assert.DoesNotContain("private IQueryable<SupplierAccrual> QuerySupplierAccruals", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySupplierAccrualFilters", service, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ISupplierAccrualRepository, EfSupplierAccrualRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierGroupDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));

        Assert.Contains("ISupplierGroupRepository supplierGroupRepository", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.Add(group)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierGroupRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "ISupplierGroupRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfSupplierGroupRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface ISupplierGroupRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfSupplierGroupRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": ISupplierGroupRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ISupplierGroupRepository, EfSupplierGroupRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));

        Assert.Contains("ISupplierRepository supplierRepository", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.FindActiveWithGroupAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.FindArchivedWithGroupAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.Add(supplier)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Suppliers", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SupplierGroups", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "ISupplierRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfSupplierRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface ISupplierRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfSupplierRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": ISupplierRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ISupplierRepository, EfSupplierRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceSupplierQueries_DelegateToExistingApplicationPorts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        Assert.Contains("ISupplierGroupRepository supplierGroupRepository", service, StringComparison.Ordinal);
        Assert.Contains("ISupplierRepository supplierRepository", service, StringComparison.Ordinal);
        Assert.Contains("supplierGroupRepository.FindActiveAsync(request.SupplierGroupId", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.FindActiveWithGroupAsync(request.SupplierId", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.GetActiveByGroupAsync(group.Id", service, StringComparison.Ordinal);
        Assert.Contains("supplierRepository.GetStartingBalanceAsync(supplierId", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SupplierGroups", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Suppliers", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierContactDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));

        Assert.Contains("ISupplierContactRepository supplierContactRepository", service, StringComparison.Ordinal);
        Assert.Contains("supplierContactRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierContactRepository.FindActiveWithSupplierAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierContactRepository.FindArchivedWithSupplierGroupAsync", service, StringComparison.Ordinal);
        Assert.Contains("supplierContactRepository.Add(contact)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.SupplierContacts", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierContactRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "ISupplierContactRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfSupplierContactRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        Assert.Contains("interface ISupplierContactRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains("class EfSupplierContactRepository", implementation, StringComparison.Ordinal);
        Assert.Contains(": ISupplierContactRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ISupplierContactRepository, EfSupplierContactRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffDepartmentDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IStaffDepartmentRepository staffDepartmentRepository", service, StringComparison.Ordinal);
        Assert.Contains("staffDepartmentRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffDepartmentRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffDepartmentRepository.HasActiveMembersAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffDepartmentRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffDepartmentRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.StaffDepartments", service, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffDepartmentRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IStaffDepartmentRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfStaffDepartmentRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IStaffDepartmentRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IStaffDepartmentRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStaffDepartmentRepository, EfStaffDepartmentRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffMemberDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IStaffMemberRepository staffMemberRepository", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.StaffMembers", service, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffMemberRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IStaffMemberRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfStaffMemberRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IStaffMemberRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IStaffMemberRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IStaffMemberRepository, EfStaffMemberRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceStaffMemberQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        Assert.Contains("IStaffMemberRepository staffMemberRepository", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.GetActiveForExpenseWorksheetAsync", service, StringComparison.Ordinal);
        Assert.Contains("staffMemberRepository.FindActiveAsync(request.StaffMemberId", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.StaffMembers", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeTypeDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IIncomeTypeRepository incomeTypeRepository", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.IncomeTypes", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IncomeTypeRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IIncomeTypeRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfIncomeTypeRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IIncomeTypeRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IIncomeTypeRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("FindFirstActiveByCodeAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("FindFirstActiveByNameAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("FindFirstArchivedByCodeOrNameAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IIncomeTypeRepository, EfIncomeTypeRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceIncomeTypeQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        Assert.Contains("IIncomeTypeRepository incomeTypeRepository", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindActiveAsync(request.IncomeTypeId", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindFirstActiveByCodeAsync(DebtTransferIncomeTypeCode", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindFirstActiveByNameAsync(DebtTransferIncomeTypeName", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.FindFirstArchivedByCodeOrNameAsync", service, StringComparison.Ordinal);
        Assert.Contains("incomeTypeRepository.Add(incomeType)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.IncomeTypes", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseTypeDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IExpenseTypeRepository expenseTypeRepository", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.ExpenseTypes", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseTypeRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IExpenseTypeRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfExpenseTypeRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IExpenseTypeRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IExpenseTypeRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("FindActiveByCodeAsync", abstraction, StringComparison.Ordinal);
        Assert.Contains("FindActiveByCodeAsync", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IExpenseTypeRepository, EfExpenseTypeRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceExpenseTypeQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        Assert.Contains("IExpenseTypeRepository expenseTypeRepository", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId", service, StringComparison.Ordinal);
        Assert.Contains("expenseTypeRepository.FindActiveByCodeAsync(\"salary\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.ExpenseTypes", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TariffDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("ITariffRepository tariffRepository", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.GetEarliestRegularAccrualMonthAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Tariffs", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TariffRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "ITariffRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfTariffRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface ITariffRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": ITariffRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ITariffRepository, EfTariffRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceTariffQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        Assert.Contains("ITariffRepository tariffRepository", service, StringComparison.Ordinal);
        Assert.Contains("tariffRepository.FindActiveAsync(request.TariffId", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Tariffs", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IrregularPaymentDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IIrregularPaymentRepository irregularPaymentRepository", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.IsUsedAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.GetUsedNamesAsync", service, StringComparison.Ordinal);
        Assert.Contains("irregularPaymentRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.IrregularPayments", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.Accruals", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.FinancialOperations", service, StringComparison.Ordinal);
    }

    [Fact]
    public void IrregularPaymentRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IIrregularPaymentRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfIrregularPaymentRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IIrregularPaymentRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IIrregularPaymentRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IIrregularPaymentRepository, EfIrregularPaymentRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryService_HasNoInfrastructureDependencyAndUsesTestFactoryForSqliteWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        var testFactory = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api.Tests", "Common", "DictionaryServiceTestFactory.cs"));
        Assert.DoesNotContain("dbContext.", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalanceDbContext", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure.Data", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", service, StringComparison.Ordinal);
        Assert.Contains("class DictionaryServiceTestFactory", testFactory, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", testFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void ChargeServiceSettingDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IChargeServiceSettingRepository chargeServiceSettingRepository", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.FindActiveAsync", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.FindArchivedAsync", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.ChargeServiceSettings", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ChargeServiceSettingRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IChargeServiceSettingRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfChargeServiceSettingRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IChargeServiceSettingRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IChargeServiceSettingRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IChargeServiceSettingRepository, EfChargeServiceSettingRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceChargeServiceCatalogQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfChargeServiceSettingRepository.cs"));
        Assert.Contains("IChargeServiceSettingRepository chargeServiceSettingRepository", service, StringComparison.Ordinal);
        Assert.Contains("chargeServiceSettingRepository.GetActiveRegularAsync(cancellationToken)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.ChargeServiceSettings", service, StringComparison.Ordinal);
        Assert.Contains("!setting.IsArchived && setting.IsRegular", implementation, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(setting => setting.Name)", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeCampaignDictionary_DelegatesPersistenceQueriesToApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "DictionaryService.cs"));
        Assert.Contains("IFeeCampaignRepository feeCampaignRepository", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.GetListAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.ActiveDuplicateExistsAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.FindActiveWithDetailsAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.FindArchivedWithDetailsAsync", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.Add", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.FeeCampaigns", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FeeCampaignRepository_IsImplementedInInfrastructureAndRegisteredInCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var abstraction = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Dictionaries", "IFeeCampaignRepository.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfFeeCampaignRepository.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));
        Assert.Contains("interface IFeeCampaignRepository", abstraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", abstraction, StringComparison.Ordinal);
        Assert.Contains(": IFeeCampaignRepository", implementation, StringComparison.Ordinal);
        Assert.Contains("GarageBalanceDbContext dbContext", implementation, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFeeCampaignRepository, EfFeeCampaignRepository>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FinanceFeeCampaignQueries_DelegateToExistingApplicationPort()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application", "Finance", "FinanceService.cs"));
        var implementation = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "EfFeeCampaignRepository.cs"));
        Assert.Contains("IFeeCampaignRepository feeCampaignRepository", service, StringComparison.Ordinal);
        Assert.Contains("feeCampaignRepository.FindActiveForAccrualGenerationAsync(request.FeeCampaignId", service, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext.FeeCampaigns", service, StringComparison.Ordinal);
        Assert.Contains("FindActiveForAccrualGenerationAsync", implementation, StringComparison.Ordinal);
        Assert.Contains("ThenInclude(garage => garage.Owner)", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendLayering_IsCompleteWhenApplicationHasNoInfrastructureOrEfDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roadmapLines = File.ReadAllLines(Path.Combine(repositoryRoot, "docs", "archive", "project-roadmap.md"));
        var activeRoadmapLines = roadmapLines
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .ToArray();
        var history = string.Join('\n', roadmapLines.SkipWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal)));
        var layeringLine = activeRoadmapLines.Single(line =>
            line.Contains("Backend разделен на слои", StringComparison.Ordinal));

        Assert.StartsWith("- `[x]`", layeringLine, StringComparison.Ordinal);
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
        Assert.Contains("DictionaryService", layeringLine, StringComparison.Ordinal);
        Assert.Contains("FinanceService", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IIntegrationSecretSettingsRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfIntegrationSecretSettingsRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IUserManagementRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfUserManagementRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IOwnerRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfOwnerRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IGarageRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfGarageRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ISupplierGroupRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfSupplierGroupRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ISupplierRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfSupplierRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ISupplierContactRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfSupplierContactRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IStaffDepartmentRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfStaffDepartmentRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IStaffMemberRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfStaffMemberRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IIncomeTypeRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfIncomeTypeRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IExpenseTypeRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfExpenseTypeRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ITariffRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfTariffRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IIrregularPaymentRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfIrregularPaymentRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IChargeServiceSettingRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfChargeServiceSettingRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFeeCampaignRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFeeCampaignRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFundRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFundRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFinancialOperationRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFinancialOperationRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IAccrualRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfAccrualRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ISupplierAccrualRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfSupplierAccrualRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IAuditEventRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfAuditEventRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IImportRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfImportRepository", layeringLine, StringComparison.Ordinal);
        Assert.Contains("ICashMovementReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfCashMovementReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFundChangeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFundChangeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IConsolidatedMonthlyReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfConsolidatedMonthlyReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IConsolidatedGarageReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfConsolidatedGarageReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IFeeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfFeeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IExpenseReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfExpenseReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("IIncomeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains("EfIncomeReportQuery", layeringLine, StringComparison.Ordinal);
        Assert.Contains(nameof(BackendLayeringTests), layeringLine, StringComparison.Ordinal);
        Assert.Contains("выполнен сорок третий срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен сорок второй срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен сорок первый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен сороковой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать девятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать восьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать седьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать шестой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать пятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать четвертый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать третий срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать второй срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцать первый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тридцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать девятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать восьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать седьмой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать шестой срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать пятый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать четвертый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать третий срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать второй срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцать первый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен двадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен девятнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен восемнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен семнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен шестнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен пятнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен четырнадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
        Assert.Contains("выполнен тринадцатый срез разделения backend-слоев", history, StringComparison.Ordinal);
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
        var applicationRoot = Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Application");
        foreach (var applicationFile in Directory.GetFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
        {
            var applicationSource = File.ReadAllText(applicationFile);
            Assert.DoesNotContain("GarageBalance.Api.Infrastructure", applicationSource, StringComparison.Ordinal);
            Assert.DoesNotContain("GarageBalanceDbContext", applicationSource, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore", applicationSource, StringComparison.Ordinal);
        }
        Assert.Contains("общий backend-layering пункт закрыт", history, StringComparison.Ordinal);
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
