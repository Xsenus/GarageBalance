using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Security;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Import;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddScoped<IUserRepository, EfUserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDictionaryService, DictionaryService>();
builder.Services.AddScoped<IOwnerRepository, EfOwnerRepository>();
builder.Services.AddScoped<ISupplierGroupRepository, EfSupplierGroupRepository>();
builder.Services.AddScoped<ISupplierRepository, EfSupplierRepository>();
builder.Services.AddScoped<ISupplierContactRepository, EfSupplierContactRepository>();
builder.Services.AddScoped<IStaffDepartmentRepository, EfStaffDepartmentRepository>();
builder.Services.AddScoped<IStaffMemberRepository, EfStaffMemberRepository>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IFundRepository, EfFundRepository>();
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
builder.Services.AddScoped<IAppReleaseService, AppReleaseService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ICashMovementReportQuery, EfCashMovementReportQuery>();
builder.Services.AddScoped<IFundChangeReportQuery, EfFundChangeReportQuery>();
builder.Services.AddScoped<IConsolidatedMonthlyReportQuery, EfConsolidatedMonthlyReportQuery>();
builder.Services.AddScoped<IConsolidatedGarageReportQuery, EfConsolidatedGarageReportQuery>();
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

app.UseMiddleware<ApiExceptionHandlingMiddleware>();
app.UseMiddleware<ApiSecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
