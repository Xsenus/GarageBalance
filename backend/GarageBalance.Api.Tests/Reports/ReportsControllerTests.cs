using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportsControllerTests
{
    [Theory]
    [InlineData(nameof(ReportsController.ExportConsolidatedReportXlsx), "consolidated/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportConsolidatedReportPdf), "consolidated/export/pdf")]
    [InlineData(nameof(ReportsController.ExportIncomeReportXlsx), "income/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportIncomeReportPdf), "income/export/pdf")]
    [InlineData(nameof(ReportsController.ExportExpenseReportXlsx), "expense/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportExpenseReportPdf), "expense/export/pdf")]
    public void ExportReportActions_UsePostBecauseExportsWriteAuditEvents(string actionName, string expectedRoute)
    {
        var method = typeof(ReportsController).GetMethod(actionName)!;
        var attributes = method.GetCustomAttributes(inherit: false);

        var postAttribute = Assert.Single(attributes.OfType<HttpPostAttribute>());
        Assert.Equal(expectedRoute, postAttribute.Template);
        Assert.Empty(attributes.OfType<HttpGetAttribute>());
    }

    [Fact]
    public async Task GetConsolidatedReport_ReturnsOk()
    {
        var report = CreateReport();
        var reportService = new FakeReportService
        {
            Result = ReportResult<ConsolidatedReportDto>.Success(report)
        };
        var actorUserId = Guid.NewGuid();
        var controller = new ReportsController(reportService);
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())]))
        };

        var result = await controller.GetConsolidatedReport(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, 12, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal(12, reportService.ConsolidatedRequest?.Limit);
        Assert.Equal(actorUserId, reportService.ConsolidatedRequest?.ActorUserId);
    }

    [Fact]
    public async Task GetConsolidatedReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            Result = ReportResult<ConsolidatedReportDto>.Failure("period_invalid", "Период неверный.")
        });

        var result = await controller.GetConsolidatedReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportConsolidatedReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 1, 2, 3 };
        var export = new ReportExportFileDto(
            "garagebalance-consolidated-20260601-20260601.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var controller = new ReportsController(new FakeReportService
        {
            ConsolidatedXlsxExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportConsolidatedReportXlsx(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportConsolidatedReportXlsx_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            ConsolidatedXlsxExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportConsolidatedReportXlsx(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportConsolidatedReportPdf_ReturnsFile()
    {
        var content = new byte[] { 4, 5, 6 };
        var export = new ReportExportFileDto("garagebalance-consolidated-20260601-20260601.pdf", "application/pdf", content);
        var controller = new ReportsController(new FakeReportService
        {
            ConsolidatedPdfExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportConsolidatedReportPdf(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportConsolidatedReportPdf_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            ConsolidatedPdfExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportConsolidatedReportPdf(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetIncomeReport_ReturnsOk()
    {
        var report = CreateIncomeReport();
        var service = new FakeReportService
        {
            IncomeResult = ReportResult<IncomeReportDto>.Success(report)
        };
        var controller = new ReportsController(service);

        var result = await controller.GetIncomeReport(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "12",
            [Guid.NewGuid()],
            [],
            [],
            "all",
            16,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal(16, service.IncomeRequest?.Limit);
    }

    [Fact]
    public async Task GetIncomeReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            IncomeResult = ReportResult<IncomeReportDto>.Failure("period_invalid", "Период неверный.")
        });

        var result = await controller.GetIncomeReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all", null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetExpenseReport_ReturnsOk()
    {
        var report = CreateExpenseReport();
        var service = new FakeReportService
        {
            ExpenseResult = ReportResult<ExpenseReportDto>.Success(report)
        };
        var controller = new ReportsController(service);

        var result = await controller.GetExpenseReport(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "Vodokanal",
            [Guid.NewGuid()],
            [],
            "payments",
            16,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal(16, service.ExpenseRequest?.Limit);
    }

    [Fact]
    public async Task ExportIncomeReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 1, 2, 3 };
        var export = new ReportExportFileDto(
            "garagebalance-income-20260601-20260630.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var controller = new ReportsController(new FakeReportService
        {
            IncomeExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportIncomeReportXlsx(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "12",
            [Guid.NewGuid()],
            [],
            [],
            "payments",
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportIncomeReportXlsx_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            IncomeExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportIncomeReportXlsx(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportIncomeReportPdf_ReturnsFile()
    {
        var content = new byte[] { 7, 8, 9 };
        var export = new ReportExportFileDto("garagebalance-income-20260601-20260630.pdf", "application/pdf", content);
        var controller = new ReportsController(new FakeReportService
        {
            IncomePdfExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportIncomeReportPdf(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "12",
            [Guid.NewGuid()],
            [],
            [],
            "payments",
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportIncomeReportPdf_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            IncomePdfExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportIncomeReportPdf(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetExpenseReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            ExpenseResult = ReportResult<ExpenseReportDto>.Failure("period_invalid", "Период неверный.")
        });

        var result = await controller.GetExpenseReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], "all", null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportExpenseReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 4, 5, 6 };
        var export = new ReportExportFileDto(
            "garagebalance-expense-20260601-20260630.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var controller = new ReportsController(new FakeReportService
        {
            ExpenseExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportExpenseReportXlsx(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "Vodokanal",
            [Guid.NewGuid()],
            [],
            "payments",
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportExpenseReportXlsx_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            ExpenseExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportExpenseReportXlsx(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], "all", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportExpenseReportPdf_ReturnsFile()
    {
        var content = new byte[] { 10, 11, 12 };
        var export = new ReportExportFileDto("garagebalance-expense-20260601-20260630.pdf", "application/pdf", content);
        var controller = new ReportsController(new FakeReportService
        {
            ExpensePdfExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportExpenseReportPdf(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "Vodokanal",
            [Guid.NewGuid()],
            [],
            "payments",
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportExpenseReportPdf_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            ExpensePdfExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportExpenseReportPdf(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], "all", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    private static ConsolidatedReportDto CreateReport()
    {
        var month = new DateOnly(2026, 6, 1);
        return new ConsolidatedReportDto(
            month,
            month,
            1500m,
            400m,
            2000m,
            1100m,
            500m,
            2,
            1,
            0,
            [new MonthlyReportRowDto(month, 1500m, 400m, 2000m, 1100m, 500m, 2, 1, 0)],
            1,
            [new GarageReportRowDto(Guid.NewGuid(), "12", "Иванов Иван", 1500m, 2000m, 500m, 0)]);
    }

    private static IncomeReportDto CreateIncomeReport()
    {
        var garageId = Guid.NewGuid();
        var incomeTypeId = Guid.NewGuid();
        return new IncomeReportDto(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            2000m,
            1500m,
            500m,
            2,
            [
                new IncomeReportRowDto("accruals", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), garageId, "12", null, "Иванов Иван", incomeTypeId, "Членский взнос", 2000m, 0m, 2000m, null, null),
                new IncomeReportRowDto("payments", new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), garageId, "12", null, "Иванов Иван", incomeTypeId, "Членский взнос", 0m, 1500m, -1500m, "PKO-1", null)
            ]);
    }

    private static ExpenseReportDto CreateExpenseReport()
    {
        var supplierId = Guid.NewGuid();
        var expenseTypeId = Guid.NewGuid();
        return new ExpenseReportDto(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            0m,
            400m,
            -400m,
            1,
            [
                new ExpenseReportRowDto("payments", new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), supplierId, "Vodokanal", expenseTypeId, "Вода", 0m, 400m, -400m, "RKO-1", null)
            ]);
    }

    private sealed class FakeReportService : IReportService
    {
        public ReportResult<ConsolidatedReportDto> Result { get; init; } = ReportResult<ConsolidatedReportDto>.Failure("not_configured", "Not configured.");
        public ConsolidatedReportRequest? ConsolidatedRequest { get; private set; }

        public ReportResult<ReportExportFileDto> ConsolidatedXlsxExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> ConsolidatedPdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<IncomeReportDto> IncomeResult { get; init; } = ReportResult<IncomeReportDto>.Failure("not_configured", "Not configured.");
        public IncomeReportRequest? IncomeRequest { get; private set; }

        public ReportResult<ExpenseReportDto> ExpenseResult { get; init; } = ReportResult<ExpenseReportDto>.Failure("not_configured", "Not configured.");
        public ExpenseReportRequest? ExpenseRequest { get; private set; }

        public ReportResult<ReportExportFileDto> IncomeExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> ExpenseExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> IncomePdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> ExpensePdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
        {
            ConsolidatedRequest = request;
            return Task.FromResult(Result);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportXlsxAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConsolidatedXlsxExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportPdfAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConsolidatedPdfExportResult);
        }

        public Task<ReportResult<IncomeReportDto>> GetIncomeReportAsync(IncomeReportRequest request, CancellationToken cancellationToken)
        {
            IncomeRequest = request;
            return Task.FromResult(IncomeResult);
        }

        public Task<ReportResult<ExpenseReportDto>> GetExpenseReportAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
        {
            ExpenseRequest = request;
            return Task.FromResult(ExpenseResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportIncomeReportXlsxAsync(IncomeReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(IncomeExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportExpenseReportXlsxAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExpenseExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportIncomeReportPdfAsync(IncomeReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(IncomePdfExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportExpenseReportPdfAsync(ExpenseReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExpensePdfExportResult);
        }
    }
}
