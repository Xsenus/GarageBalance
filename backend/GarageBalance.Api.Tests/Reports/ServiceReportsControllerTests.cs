using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ServiceReportsControllerTests
{
    [Theory]
    [InlineData(nameof(ServiceReportsController.GetColumns), SystemPermissions.TariffsManage)]
    [InlineData(nameof(ServiceReportsController.SaveColumns), SystemPermissions.TariffsManage)]
    [InlineData(nameof(ServiceReportsController.Payments), SystemPermissions.ReportsRead)]
    [InlineData(nameof(ServiceReportsController.Debt), SystemPermissions.ReportsRead)]
    [InlineData(nameof(ServiceReportsController.ExportPayments), SystemPermissions.ReportsRead)]
    [InlineData(nameof(ServiceReportsController.ExportDebt), SystemPermissions.ReportsRead)]
    public void ActionsEnforcePermissionsAndThinControllerBoundary(string action, string policy)
    {
        var method = typeof(ServiceReportsController).GetMethod(action)!;
        Assert.Equal(policy, method.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/reports/services", typeof(ServiceReportsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(typeof(IServiceReportService), Assert.Single(typeof(ServiceReportsController).GetConstructors()).GetParameters()[0].ParameterType);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReportsAndExportsForwardFiltersCancellationActorAndMapResults(bool debt, bool failure)
    {
        var fake = new FakeService { Fail = failure }; var controller = new ServiceReportsController(fake);
        var actor = Guid.NewGuid(); controller.ControllerContext.HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString())])) };
        var request = new ServiceReportRequest(new(2046, 9, 1), new(2046, 9, 30), Guid.NewGuid(), 10, 50, true);
        using var cancellation = new CancellationTokenSource();
        var result = debt ? await controller.Debt(request, cancellation.Token) : await controller.Payments(request, cancellation.Token);
        Assert.Equal(request, fake.Request); Assert.Equal(debt, fake.Debt); Assert.Equal(cancellation.Token, fake.Token);
        if (failure) Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        else Assert.IsType<ServiceReportDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        foreach (var format in new[] { "xlsx", "pdf" })
        {
            var export = debt ? await controller.ExportDebt(request, format, cancellation.Token) : await controller.ExportPayments(request, format, cancellation.Token);
            Assert.Equal(actor, fake.Actor); Assert.Equal(format == "pdf", fake.Pdf);
            if (failure) Assert.IsType<BadRequestObjectResult>(export); else Assert.Equal([1, 2], Assert.IsType<FileContentResult>(export).FileContents);
        }
        Assert.IsType<BadRequestObjectResult>(await controller.ExportDebt(request, "csv", default));
        Assert.IsType<BadRequestObjectResult>(await controller.ExportPayments(request, "csv", default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsReturnConfigAndMapValidation(bool failure)
    {
        var fake = new FakeService { Fail = failure }; var controller = new ServiceReportsController(fake) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<ServiceReportColumnsDto>(Assert.IsType<OkObjectResult>((await controller.GetColumns(default)).Result).Value);
        var request = new UpdateServiceReportColumnsRequest(Guid.NewGuid(), []);
        var result = await controller.SaveColumns(request, default);
        Assert.Equal(request, fake.Columns); Assert.Null(fake.Actor);
        if (failure) Assert.IsType<BadRequestObjectResult>(result.Result); else Assert.IsType<OkObjectResult>(result.Result);
    }
    private sealed class FakeService : IServiceReportService
    {
        public bool Fail { get; set; }
        public ServiceReportRequest? Request { get; private set; }
        public bool Debt { get; private set; }
        public bool Pdf { get; private set; }
        public Guid? Actor { get; private set; }
        public CancellationToken Token { get; private set; }
        public UpdateServiceReportColumnsRequest? Columns { get; private set; }
        public Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken) => Task.FromResult(new ServiceReportColumnsDto(Guid.NewGuid(), [], []));
        public async Task<ReportResult<ServiceReportColumnsDto>> SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken)
        { Columns = request; Actor = actorId; return Fail ? ReportResult<ServiceReportColumnsDto>.Failure("invalid", "Ошибка") : ReportResult<ServiceReportColumnsDto>.Success(await GetColumnsAsync(cancellationToken)); }
        public Task<ReportResult<ServiceReportDto>> GetAsync(ServiceReportRequest request, bool debt, CancellationToken cancellationToken)
        { Request = request; Debt = debt; Token = cancellationToken; return Task.FromResult(Fail ? ReportResult<ServiceReportDto>.Failure("invalid", "Ошибка") : ReportResult<ServiceReportDto>.Success(new(null, new(2046, 9, 30), [], [], [], [], 0, 0, 50))); }
        public Task<ReportResult<ReportExportFileDto>> ExportAsync(ServiceReportRequest request, bool debt, bool pdf, Guid? actorId, CancellationToken cancellationToken)
        { Request = request; Debt = debt; Pdf = pdf; Actor = actorId; Token = cancellationToken; return Task.FromResult(Fail ? ReportResult<ReportExportFileDto>.Failure("invalid", "Ошибка") : ReportResult<ReportExportFileDto>.Success(new("report", "application/octet-stream", [1, 2]))); }
    }
}
