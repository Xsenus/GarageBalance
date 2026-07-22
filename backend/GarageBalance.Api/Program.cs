using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Security;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Diagnostics;
using GarageBalance.Api.Infrastructure.Import;
using GarageBalance.Api.Infrastructure.Integrations;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>>(_ => new JwtOptionsValidator(builder.Environment.EnvironmentName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
JwtOptionsValidator.ThrowIfInvalid(jwtOptions, builder.Environment.EnvironmentName);

builder.Services.AddDbContext<GarageBalanceDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddScoped<IApplicationUnitOfWork, EfApplicationUnitOfWork>();
builder.Services.AddScoped<IApplicationSettingRepository, EfApplicationSettingRepository>();
builder.Services.AddScoped<IApplicationSettingsService, ApplicationSettingsService>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDictionaryService, DictionaryService>();
builder.Services.AddScoped<IOwnerRepository, EfOwnerRepository>();
builder.Services.AddScoped<IGarageRepository, EfGarageRepository>();
builder.Services.AddScoped<ISupplierGroupRepository, EfSupplierGroupRepository>();
builder.Services.AddScoped<ISupplierRepository, EfSupplierRepository>();
builder.Services.AddScoped<ISupplierContactRepository, EfSupplierContactRepository>();
builder.Services.AddScoped<IStaffDepartmentRepository, EfStaffDepartmentRepository>();
builder.Services.AddScoped<IStaffMemberRepository, EfStaffMemberRepository>();
builder.Services.AddScoped<IIncomeTypeRepository, EfIncomeTypeRepository>();
builder.Services.AddScoped<IExpenseTypeRepository, EfExpenseTypeRepository>();
builder.Services.AddScoped<ITariffRepository, EfTariffRepository>();
builder.Services.AddScoped<IIrregularPaymentRepository, EfIrregularPaymentRepository>();
builder.Services.AddScoped<IChargeServiceSettingRepository, EfChargeServiceSettingRepository>();
builder.Services.AddScoped<IFeeCampaignRepository, EfFeeCampaignRepository>();
builder.Services.AddScoped<IMissingMeterReadingQuery, EfMissingMeterReadingQuery>();
builder.Services.AddScoped<IGarageIncomeWorksheetQuery, EfGarageIncomeWorksheetQuery>();
builder.Services.AddScoped<IGarageBalanceHistoryQuery, EfGarageBalanceHistoryQuery>();
builder.Services.AddScoped<IFinanceAvailableBalanceQuery, EfFinanceAvailableBalanceQuery>();
builder.Services.AddScoped<IExpenseWorksheetQuery, EfExpenseWorksheetQuery>();
builder.Services.AddScoped<IFinancialOperationDisplayQuery, EfFinancialOperationDisplayQuery>();
builder.Services.AddScoped<IFinanceTotalsQuery, EfFinanceTotalsQuery>();
builder.Services.AddScoped<IFinancialReportPeriodQuery, EfFinancialReportPeriodQuery>();
builder.Services.AddScoped<IMeterReadingRepository, EfMeterReadingRepository>();
builder.Services.AddScoped<IFinancialOperationRepository, EfFinancialOperationRepository>();
builder.Services.AddScoped<IAccrualRepository, EfAccrualRepository>();
builder.Services.AddScoped<IAccrualPaymentAllocationRepository, EfAccrualPaymentAllocationRepository>();
builder.Services.AddScoped<ISupplierAccrualRepository, EfSupplierAccrualRepository>();
builder.Services.AddScoped<IFundRepository, EfFundRepository>();
builder.Services.AddScoped<IIncomeFundAssignmentService, IncomeFundAssignmentService>();
builder.Services.AddScoped<IFinanceService>(services => new FinanceService(
    services.GetRequiredService<IStaffMemberRepository>(),
    services.GetRequiredService<IGarageRepository>(),
    services.GetRequiredService<IMissingMeterReadingQuery>(),
    services.GetRequiredService<IGarageIncomeWorksheetQuery>(),
    services.GetRequiredService<IGarageBalanceHistoryQuery>(),
    services.GetRequiredService<IFinanceAvailableBalanceQuery>(),
    services.GetRequiredService<IExpenseWorksheetQuery>(),
    services.GetRequiredService<IFinancialOperationDisplayQuery>(),
    services.GetRequiredService<IFinanceTotalsQuery>(),
    services.GetRequiredService<IFinancialReportPeriodQuery>(),
    services.GetRequiredService<IMeterReadingRepository>(),
    services.GetRequiredService<IFinancialOperationRepository>(),
    services.GetRequiredService<IAccrualRepository>(),
    services.GetRequiredService<IAccrualPaymentAllocationRepository>(),
    services.GetRequiredService<ISupplierAccrualRepository>(),
    services.GetRequiredService<ISupplierGroupRepository>(),
    services.GetRequiredService<ISupplierRepository>(),
    services.GetRequiredService<IExpenseTypeRepository>(),
    services.GetRequiredService<IIncomeTypeRepository>(),
    services.GetRequiredService<IIrregularPaymentRepository>(),
    services.GetRequiredService<ITariffRepository>(),
    services.GetRequiredService<IFeeCampaignRepository>(),
    services.GetRequiredService<IChargeServiceSettingRepository>(),
    services.GetRequiredService<IIncomeFundAssignmentService>(),
    services.GetRequiredService<IApplicationUnitOfWork>(),
    services.GetRequiredService<IAuditEventWriter>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<IBusinessDateProvider>()));
builder.Services.AddScoped<IFundService, FundService>();
builder.Services.AddScoped<IImportRepository, EfImportRepository>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IAccessImportReader, DisabledAccessImportReader>();
builder.Services.AddScoped<IImportFingerprintRepository, EfImportFingerprintRepository>();
builder.Services.AddScoped<IImportFingerprintService, ImportFingerprintService>();
builder.Services.AddScoped<IImportQuarantineRepository, EfImportQuarantineRepository>();
builder.Services.AddScoped<IImportQuarantineService, ImportQuarantineService>();
builder.Services.AddScoped<IIntegrationSecretSettingsRepository, EfIntegrationSecretSettingsRepository>();
builder.Services.AddScoped<IIntegrationSecretSettingsService, IntegrationSecretSettingsService>();
builder.Services.AddScoped<IIntegrationStatusService, IntegrationStatusService>();
builder.Services.AddScoped<IOneCFreshSyncAdapter, DisabledOneCFreshSyncAdapter>();
builder.Services.AddScoped<IOneCFreshSyncService, OneCFreshSyncService>();
builder.Services.AddScoped<IReceiptPrintingAdapter, DisabledReceiptPrintingAdapter>();
builder.Services.AddScoped<IReceiptPrintingRepository, EfReceiptPrintingRepository>();
builder.Services.AddScoped<IReceiptPrintingService, ReceiptPrintingService>();
builder.Services.AddHttpClient<IDadataSuggestionService, DadataSuggestionService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<IAppReleaseService, AppReleaseService>();
builder.Services.AddScoped<IAppReleaseRepository, EfAppReleaseRepository>();
builder.Services
    .AddOptions<DiagnosticLoggingOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticLoggingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<RollingJsonDiagnosticLoggerProvider>();
builder.Services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<RollingJsonDiagnosticLoggerProvider>());
builder.Services.AddSingleton<IDiagnosticLogStore>(provider => provider.GetRequiredService<RollingJsonDiagnosticLoggerProvider>());
builder.Services.AddScoped<IDiagnosticPackageService, DiagnosticPackageService>();
builder.Services
    .AddOptions<DatabaseBackupOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseBackupOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<DatabaseStartupOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseStartupOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IBackupCommandRunner, BackupCommandRunner>();
builder.Services.AddSingleton<IBackupToolLocator, BackupToolLocator>();
builder.Services.AddScoped<IDatabaseBackupService, PostgresDatabaseBackupService>();
builder.Services.AddScoped<DatabaseBackupAutomationRunner>();
builder.Services
    .AddOptions<RegularAccrualAutomationOptions>()
    .Bind(builder.Configuration.GetSection(RegularAccrualAutomationOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => TryResolveTimeZone(options.TimeZoneId),
        "Finance:RegularAccrualAutomation:TimeZoneId must contain a valid system time zone identifier.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBusinessDateProvider, BusinessDateProvider>();
builder.Services.AddHostedService<DatabaseStartupHostedService>();
builder.Services.AddHostedService<BusinessDateSettingsInitializer>();
builder.Services.AddHostedService<AppReleaseCatalogSynchronizer>();
builder.Services.AddHostedService<DatabaseBackupWorker>();
builder.Services.AddScoped<IRegularAccrualAutomationRunner, RegularAccrualAutomationRunner>();
builder.Services.AddHostedService<RegularAccrualAutomationWorker>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ICashMovementReportQuery, EfCashMovementReportQuery>();
builder.Services.AddScoped<IFundChangeReportQuery, EfFundChangeReportQuery>();
builder.Services.AddScoped<IConsolidatedMonthlyReportQuery, EfConsolidatedMonthlyReportQuery>();
builder.Services.AddScoped<IConsolidatedGarageReportQuery, EfConsolidatedGarageReportQuery>();
builder.Services.AddScoped<IGarageReportQuery, EfGarageReportQuery>();
builder.Services.AddScoped<IFeeReportQuery, EfFeeReportQuery>();
builder.Services.AddScoped<IExpenseReportQuery, EfExpenseReportQuery>();
builder.Services.AddScoped<IIncomeReportQuery, EfIncomeReportQuery>();
builder.Services.AddScoped<IUserManagementRepository, EfUserManagementRepository>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IFormStateRepository, EfFormStateRepository>();
builder.Services.AddScoped<IFormStateService, FormStateService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
builder.Services.AddScoped<IAuditEventStore>(services => services.GetRequiredService<GarageBalanceDbContext>());
builder.Services.AddScoped<IAuditEventWriter, AuditEventWriter>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IPasswordPolicyValidator, PasswordPolicyValidator>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<ISensitiveDataProtector, DataProtectionSensitiveDataProtector>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationMiddlewareResultHandler>();
var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("GarageBalance");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                ["http://localhost:5173", "http://127.0.0.1:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("client-diagnostics", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in SystemPermissions.Administrator)
    {
        options.AddPolicy(permission, policy => policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ApiProblemDetails.CreateInvalidModelStateResponse;
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseMiddleware<ApiExceptionHandlingMiddleware>();
app.UseMiddleware<ApiSecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

static bool TryResolveTimeZone(string timeZoneId)
{
    try
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return true;
    }
    catch (TimeZoneNotFoundException)
    {
        return false;
    }
    catch (InvalidTimeZoneException)
    {
        return false;
    }
}
