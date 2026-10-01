using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ServiceReportsHttpTests
{
    [PostgreSqlFact]
    public async Task RealHttpEnforcesPermissionsVersionsFiltersExportsAndAtomicAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('r', 64)));
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new()
        { ValidateIssuer = true, ValidIssuer = "service-report-test", ValidateAudience = true, ValidAudience = "service-report-test", ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in new[] { SystemPermissions.ReportsRead, SystemPermissions.TariffsManage }) options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddScoped(_ => database.CreateContext());
        builder.Services.AddScoped<IAuditEventStore>(provider => provider.GetRequiredService<GarageBalanceDbContext>());
        builder.Services.AddScoped<IAuditEventWriter, AuditEventWriter>(); builder.Services.AddScoped<IApplicationUnitOfWork, EfApplicationUnitOfWork>();
        builder.Services.AddSingleton<IBusinessDateProvider>(new TestBusinessDateProvider(new(2046, 9, 18)));
        builder.Services.AddScoped<IServiceReportRepository, EfServiceReportRepository>(); builder.Services.AddScoped<IServiceReportService, ServiceReportService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ServiceReportsController).Assembly);
        await using var app = builder.Build(); app.UseMiddleware<ApiExceptionHandlingMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            const string root = "/api/reports/services/";
            var routes = new[] { "columns", "payments", "debt", "payments/export/xlsx", "payments/export/pdf", "debt/export/xlsx", "debt/export/pdf" };
            foreach (var route in routes)
            {
                using var response = await Send(route); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            Authorize([]);
            foreach (var route in routes)
            {
                using var response = await Send(route); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
            using (var denied = await client.PutAsJsonAsync(root + "columns", new { version = Guid.NewGuid(), columns = Array.Empty<object>() })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Authorize([SystemPermissions.ReportsRead]);
            using (var denied = await client.GetAsync(root + "columns")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            foreach (var route in routes.Skip(1)) { using var response = await Send(route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
            using (var invalid = await client.GetAsync(root + "payments?dateFrom=2046-10-01&dateTo=2046-09-30")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await client.GetAsync(root + "debt?garageId=00000000-0000-0000-0000-000000000000")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await client.PostAsync(root + "debt/export/csv", null)) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Authorize([SystemPermissions.TariffsManage]);
            using (var denied = await client.GetAsync(root + "payments")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var configuration = (await client.GetFromJsonAsync<ServiceReportColumnsDto>(root + "columns"))!;
            var columns = new[] { new ServiceReportColumn(Guid.NewGuid(), "Общие услуги", []) };
            using (var missingVersion = await client.PutAsJsonAsync(root + "columns", new { columns })) Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
            using (var saved = await client.PutAsJsonAsync(root + "columns", new UpdateServiceReportColumnsRequest(configuration.Version, columns))) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using (var stale = await client.PutAsJsonAsync(root + "columns", new UpdateServiceReportColumnsRequest(configuration.Version, columns))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            await using var check = database.CreateContext();
            Assert.Equal(1, await check.AuditEvents.CountAsync(item => item.Action == "settings.service_report_columns_updated"));
            Assert.Equal(4, await check.AuditEvents.CountAsync(item => item.Action == "reports.service_report_exported"));
            Assert.Empty(await check.FinancialOperations.ToArrayAsync());
            using (var invalid = await client.GetAsync(root + "columns?report=unknown")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await client.PutAsJsonAsync(root + "columns", new UpdateServiceReportColumnsRequest(Guid.NewGuid(), columns, "unknown"))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            foreach (var scope in new[] { "payments", "accrued", "overdue" })
            {
                var config = (await client.GetFromJsonAsync<ServiceReportColumnsDto>(root + $"columns?report={scope}"))!;
                Assert.Equal(scope, config.Report);
                using var saved = await client.PutAsJsonAsync(root + "columns", new UpdateServiceReportColumnsRequest(config.Version, [new(Guid.NewGuid(), scope, [])], scope));
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            }
            Authorize([SystemPermissions.ReportsRead, SystemPermissions.TariffsManage]);
            foreach (var scope in new[] { "payments", "accrued", "overdue" })
            {
                var route = scope == "payments" ? "payments" : $"debt?overdueOnly={scope == "overdue"}";
                var report = (await client.GetFromJsonAsync<ServiceReportDto>(root + route))!;
                Assert.Equal(scope, Assert.Single(report.Columns).Name);
                var exportRoute = scope == "payments" ? "payments/export/" : "debt/export/";
                foreach (var format in new[] { "xlsx", "pdf" })
                {
                    using var export = await client.PostAsync(root + exportRoute + format + $"?overdueOnly={scope == "overdue"}", null);
                    Assert.Equal(HttpStatusCode.OK, export.StatusCode);
                    var bytes = await export.Content.ReadAsByteArrayAsync();
                    if (format == "xlsx")
                    {
                        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
                        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                        Assert.Contains(scope, await reader.ReadToEndAsync(), StringComparison.Ordinal);
                    }
                    else
                    {
                        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
                        Assert.Contains(scope, string.Concat(pdf.GetPages().Select(page => page.Text)), StringComparison.Ordinal);
                    }
                }
            }
            Assert.Equal(4, await check.AuditEvents.CountAsync(item => item.Action == "settings.service_report_columns_updated"));
            Assert.Equal(10, await check.AuditEvents.CountAsync(item => item.Action == "reports.service_report_exported"));
            Authorize([SystemPermissions.ReportsRead]);
            foreach (var scope in new[] { "payments", "accrued", "overdue" })
            {
                using var denied = await client.GetAsync(root + $"columns?report={scope}");
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            Task<HttpResponseMessage> Send(string route) => route.Contains("export", StringComparison.Ordinal) ? client.PostAsync(root + route, null) : client.GetAsync(root + route);
            void Authorize(string[] permissions)
            {
                var token = new JwtSecurityToken("service-report-test", "service-report-test", permissions.Select(permission => new Claim("permission", permission)), expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
        }
        finally { await app.StopAsync(); }
    }
}
