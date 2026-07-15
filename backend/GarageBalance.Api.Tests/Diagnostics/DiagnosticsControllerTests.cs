using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Contracts.Diagnostics;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace GarageBalance.Api.Tests.Diagnostics;

public sealed class DiagnosticsControllerTests
{
    [Fact]
    public void ReportClientError_LogsOnlySanitizedDiagnosticAndReturnsAccepted()
    {
        var logger = new CaptureLogger<DiagnosticsController>();
        var controller = CreateController(logger);

        var result = controller.ReportClientError(new ClientErrorReportRequest(
            "client-error-123",
            "TypeError",
            "ФИО и адрес из введенной формы не должны попасть в журнал",
            "at OwnerForm password=real-secret owner@example.org +7 913 111-22-33 token:abc123",
            "/settings"));

        Assert.IsType<AcceptedResult>(result);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("[redacted]", message, StringComparison.Ordinal);
        Assert.Contains("[email]", message, StringComparison.Ordinal);
        Assert.Contains("[phone]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("real-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.org", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ФИО и адрес", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrativeEndpointsRequireUsersManagePolicyAndClientReportRequiresAuthentication()
    {
        var type = typeof(DiagnosticsController);
        var report = type.GetMethod(nameof(DiagnosticsController.ReportClientError))!;
        var status = type.GetMethod(nameof(DiagnosticsController.GetStatus))!;
        var package = type.GetMethod(nameof(DiagnosticsController.CreatePackage))!;

        Assert.Contains(report.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>(), attribute => attribute.Policy is null);
        Assert.Contains(report.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true).Cast<EnableRateLimitingAttribute>(), attribute => attribute.PolicyName == "client-diagnostics");
        Assert.Contains(status.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>(), attribute => attribute.Policy == SystemPermissions.UsersManage);
        Assert.Contains(package.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>(), attribute => attribute.Policy == SystemPermissions.UsersManage);
    }

    [Fact]
    public void GetStatus_ReturnsSafeStoreSummary()
    {
        var expected = new DiagnosticLogStatusDto(true, 14, 7, 20, 2, 4096, DateTimeOffset.Parse("2026-07-15T05:00:00Z"), null);
        var controller = new DiagnosticsController(new FakePackageService(expected, null), new CaptureLogger<DiagnosticsController>());

        var result = controller.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task CreatePackage_ReturnsZipOrSafeUnavailableProblem()
    {
        var status = new DiagnosticLogStatusDto(true, 14, 7, 20, 1, 3, null, null);
        var available = new DiagnosticsController(
            new FakePackageService(status, new DiagnosticPackage("diagnostics.zip", [1, 2, 3])),
            new CaptureLogger<DiagnosticsController>());

        var file = Assert.IsType<FileContentResult>(await available.CreatePackage(CancellationToken.None));
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("diagnostics.zip", file.FileDownloadName);

        var unavailableController = new DiagnosticsController(new FakePackageService(status, null), new CaptureLogger<DiagnosticsController>());
        var unavailable = Assert.IsType<ObjectResult>(await unavailableController.CreatePackage(CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(unavailable.Value);
        Assert.Equal("diagnostic_package_unavailable", problem.Extensions[ApiProblemDetails.CodeExtensionKey]);

        var disabledStatus = status with { Enabled = false };
        var disabledController = new DiagnosticsController(new FakePackageService(disabledStatus, null), new CaptureLogger<DiagnosticsController>());
        var disabled = Assert.IsType<ObjectResult>(await disabledController.CreatePackage(CancellationToken.None));
        var disabledProblem = Assert.IsType<ProblemDetails>(disabled.Value);
        Assert.Equal("diagnostic_logging_disabled", disabledProblem.Extensions[ApiProblemDetails.CodeExtensionKey]);
    }

    private static DiagnosticsController CreateController(ILogger<DiagnosticsController> logger)
    {
        return new DiagnosticsController(new EmptyPackageService(), logger);
    }

    private sealed class EmptyPackageService : IDiagnosticPackageService
    {
        public DiagnosticLogStatusDto GetStatus() => new(true, 14, 7, 20, 0, 0, null, null);
        public Task<DiagnosticPackage?> CreatePackageAsync(Guid? actorUserId, CancellationToken cancellationToken) => Task.FromResult<DiagnosticPackage?>(null);
    }

    private sealed class FakePackageService(DiagnosticLogStatusDto status, DiagnosticPackage? package) : IDiagnosticPackageService
    {
        public DiagnosticLogStatusDto GetStatus() => status;
        public Task<DiagnosticPackage?> CreatePackageAsync(Guid? actorUserId, CancellationToken cancellationToken) => Task.FromResult(package);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
