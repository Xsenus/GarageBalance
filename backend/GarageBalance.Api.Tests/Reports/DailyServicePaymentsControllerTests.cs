using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Reports;

public sealed class DailyServicePaymentsControllerTests
{
    [Theory]
    [InlineData(nameof(DailyServicePaymentsController.Get), null)]
    [InlineData(nameof(DailyServicePaymentsController.ExportXlsx), "export/xlsx")]
    [InlineData(nameof(DailyServicePaymentsController.ExportPdf), "export/pdf")]
    public void RoutesRequireReportsReadAndExportsUsePost(string action, string? route)
    {
        Assert.Equal(SystemPermissions.ReportsRead, typeof(DailyServicePaymentsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/reports/daily-service-payments", typeof(DailyServicePaymentsController).GetCustomAttribute<RouteAttribute>()!.Template);
        var method = typeof(DailyServicePaymentsController).GetMethod(action)!;
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        if (route is null) Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
        else Assert.Equal(route, method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetForwardsRequestAndCancellationAndMapsFailure(bool fail)
    {
        var fake = new FakeService { Fail = fail };
        var controller = new DailyServicePaymentsController(fake);
        using var cancellation = new CancellationTokenSource();
        var garage = Guid.NewGuid();
        var date = new DateOnly(2046, 9, 18);
        var result = await controller.Get(date, garage, 25, 25, cancellation.Token);
        Assert.Equal(new(date, garage, 25, 25), fake.Request);
        Assert.Equal(cancellation.Token, fake.Token);
        if (fail) Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        else Assert.IsType<DailyServicePaymentReportDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExportsForwardActorAndMapFileOrProblem(bool pdf, bool fail)
    {
        var fake = new FakeService { Fail = fail };
        var controller = new DailyServicePaymentsController(fake);
        var actor = Guid.NewGuid();
        controller.ControllerContext.HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString())])) };
        var date = new DateOnly(2046, 9, 18);
        var garage = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var result = pdf ? await controller.ExportPdf(date, garage, cancellation.Token) : await controller.ExportXlsx(date, garage, cancellation.Token);
        Assert.Equal(new(date, garage, ActorUserId: actor), fake.Request);
        Assert.Equal(pdf, fake.Pdf);
        Assert.Equal(cancellation.Token, fake.Token);
        if (fail) Assert.IsType<ProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        else
        {
            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("fixture", file.FileDownloadName);
            Assert.Equal([1, 2, 3], file.FileContents);
        }
        controller.HttpContext.User = new ClaimsPrincipal();
        await controller.ExportXlsx(null, null, cancellation.Token);
        Assert.Null(fake.Request!.ActorUserId);
    }

    private sealed class FakeService : IDailyServicePaymentReportService
    {
        public bool Fail { get; set; }
        public DailyServicePaymentReportRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Pdf { get; private set; }
        public Task<ReportResult<DailyServicePaymentReportDto>> GetAsync(DailyServicePaymentReportRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Token = cancellationToken;
            return Task.FromResult(Fail ? ReportResult<DailyServicePaymentReportDto>.Failure("test", "Ошибка проверки")
                : ReportResult<DailyServicePaymentReportDto>.Success(new(new(2046, 9, 1), new(2046, 9, 18), new([], [], new(0, 0, 0, 0, 0, 0, 0), 0, false, 0, 25))));
        }
        public Task<ReportResult<ReportExportFileDto>> ExportAsync(DailyServicePaymentReportRequest request, bool pdf, CancellationToken cancellationToken)
        {
            Request = request;
            Token = cancellationToken;
            Pdf = pdf;
            return Task.FromResult(Fail ? ReportResult<ReportExportFileDto>.Failure("test", "Ошибка проверки")
                : ReportResult<ReportExportFileDto>.Success(new("fixture", "application/octet-stream", [1, 2, 3])));
        }
    }
}
