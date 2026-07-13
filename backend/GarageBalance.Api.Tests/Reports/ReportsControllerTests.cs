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
    [InlineData(nameof(ReportsController.ExportFundChangeReportXlsx), "fund-changes/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportFundChangeReportPdf), "fund-changes/export/pdf")]
    [InlineData(nameof(ReportsController.ExportCashPaymentReportXlsx), "cash-payments/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportCashPaymentReportPdf), "cash-payments/export/pdf")]
    [InlineData(nameof(ReportsController.ExportBankDepositReportXlsx), "bank-deposits/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportBankDepositReportPdf), "bank-deposits/export/pdf")]
    [InlineData(nameof(ReportsController.ExportFeeReportXlsx), "fees/export/xlsx")]
    [InlineData(nameof(ReportsController.ExportFeeReportPdf), "fees/export/pdf")]
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
    public async Task GetFundChangeReport_ReturnsOk()
    {
        var report = CreateFundChangeReport();
        var service = new FakeReportService
        {
            FundChangeResult = ReportResult<FundChangeReportDto>.Success(report)
        };
        var actorUserId = Guid.NewGuid();
        var controller = new ReportsController(service);
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())]))
        };

        var result = await controller.GetFundChangeReport(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "Электро",
            16,
            8,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal(16, service.FundChangeRequest?.Limit);
        Assert.Equal(8, service.FundChangeRequest?.Offset);
        Assert.Equal("Электро", service.FundChangeRequest?.Search);
        Assert.Equal(actorUserId, service.FundChangeRequest?.ActorUserId);
    }

    [Fact]
    public async Task GetFundChangeReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            FundChangeResult = ReportResult<FundChangeReportDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.GetFundChangeReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, null, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportFundChangeReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 25, 26, 27 };
        var export = new ReportExportFileDto(
            "garagebalance-fund-changes-20260601-20260630.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var service = new FakeReportService
        {
            FundChangeXlsxExportResult = ReportResult<ReportExportFileDto>.Success(export)
        };
        var actorUserId = Guid.NewGuid();
        var controller = new ReportsController(service);
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())]))
        };

        var result = await controller.ExportFundChangeReportXlsx(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "электро", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
        Assert.Equal("электро", service.FundChangeRequest?.Search);
        Assert.Equal(actorUserId, service.FundChangeRequest?.ActorUserId);
    }

    [Fact]
    public async Task ExportFundChangeReportPdf_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            FundChangePdfExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportFundChangeReportPdf(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetCashPaymentReport_ReturnsOk()
    {
        var report = CreateCashPaymentReport();
        var service = new FakeReportService
        {
            CashPaymentResult = ReportResult<CashPaymentReportDto>.Success(report)
        };
        var controller = new ReportsController(service);

        var result = await controller.GetCashPaymentReport(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "чек", 16, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal("чек", service.CashPaymentRequest?.Search);
        Assert.Equal(16, service.CashPaymentRequest?.Limit);
    }

    [Fact]
    public async Task GetCashPaymentReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            CashPaymentResult = ReportResult<CashPaymentReportDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.GetCashPaymentReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetBankDepositReport_ReturnsOk()
    {
        var report = CreateBankDepositReport();
        var service = new FakeReportService
        {
            BankDepositResult = ReportResult<BankDepositReportDto>.Success(report)
        };
        var controller = new ReportsController(service);

        var result = await controller.GetBankDepositReport(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк", 16, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal("банк", service.BankDepositRequest?.Search);
        Assert.Equal(16, service.BankDepositRequest?.Limit);
    }

    [Fact]
    public async Task GetBankDepositReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            BankDepositResult = ReportResult<BankDepositReportDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.GetBankDepositReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportCashPaymentReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 13, 14, 15 };
        var export = new ReportExportFileDto(
            "garagebalance-cash-payments-20260601-20260630.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var controller = new ReportsController(new FakeReportService
        {
            CashPaymentXlsxExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportCashPaymentReportXlsx(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "чек", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportCashPaymentReportPdf_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            CashPaymentPdfExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportCashPaymentReportPdf(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task ExportBankDepositReportPdf_ReturnsFile()
    {
        var content = new byte[] { 16, 17, 18 };
        var export = new ReportExportFileDto("garagebalance-bank-deposits-20260601-20260630.pdf", "application/pdf", content);
        var controller = new ReportsController(new FakeReportService
        {
            BankDepositPdfExportResult = ReportResult<ReportExportFileDto>.Success(export)
        });

        var result = await controller.ExportBankDepositReportPdf(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
    }

    [Fact]
    public async Task ExportBankDepositReportXlsx_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            BankDepositXlsxExportResult = ReportResult<ReportExportFileDto>.Failure("period_invalid", "Invalid period.")
        });

        var result = await controller.ExportBankDepositReportXlsx(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetFeeReport_ReturnsOk()
    {
        var report = CreateFeeReport();
        var service = new FakeReportService
        {
            FeeResult = ReportResult<FeeReportDto>.Success(report)
        };
        var controller = new ReportsController(service);

        var result = await controller.GetFeeReport("ворота", 16, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
        Assert.Equal("ворота", service.FeeRequest?.Variation);
        Assert.Equal(16, service.FeeRequest?.Limit);
    }

    [Fact]
    public async Task ExportFeeReportXlsx_ReturnsFile()
    {
        var content = new byte[] { 19, 20, 21 };
        var export = new ReportExportFileDto(
            "garagebalance-fees.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content);
        var service = new FakeReportService
        {
            FeeXlsxExportResult = ReportResult<ReportExportFileDto>.Success(export)
        };
        var controller = new ReportsController(service);

        var result = await controller.ExportFeeReportXlsx("ворота", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
        Assert.Equal("ворота", service.FeeRequest?.Variation);
    }

    [Fact]
    public async Task ExportFeeReportPdf_ReturnsFile()
    {
        var content = new byte[] { 22, 23, 24 };
        var export = new ReportExportFileDto("garagebalance-fees.pdf", "application/pdf", content);
        var service = new FakeReportService
        {
            FeePdfExportResult = ReportResult<ReportExportFileDto>.Success(export)
        };
        var controller = new ReportsController(service);

        var result = await controller.ExportFeeReportPdf("ворота", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(export.FileName, file.FileDownloadName);
        Assert.Equal(export.ContentType, file.ContentType);
        Assert.Same(content, file.FileContents);
        Assert.Equal("ворота", service.FeeRequest?.Variation);
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

    private static FundChangeReportDto CreateFundChangeReport()
    {
        var fundId = Guid.NewGuid();
        return new FundChangeReportDto(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            1500m,
            0m,
            1,
            0,
            25,
            [
                new FundChangeReportRowDto(
                    Guid.NewGuid(),
                    fundId,
                    "Электроэнергия",
                    new DateOnly(2026, 6, 10),
                    "deposit",
                    "Пополнение",
                    1500m,
                    0m,
                    1500m,
                    Guid.NewGuid(),
                    "Администратор",
                    "Распределение средств")
            ]);
    }

    private static CashPaymentReportDto CreateCashPaymentReport()
    {
        return new CashPaymentReportDto(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            1200m,
            1,
            [
                new CashPaymentReportRowDto(
                    Guid.NewGuid(),
                    new DateOnly(2026, 6, 12),
                    1200m,
                    true,
                    "Вода: Vodokanal",
                    "Vodokanal",
                    "Вода",
                    "RKO-1",
                    "Оплата")
            ]);
    }

    private static BankDepositReportDto CreateBankDepositReport()
    {
        return new BankDepositReportDto(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            3000m,
            1,
            [
                new BankDepositReportRowDto(Guid.NewGuid(), new DateOnly(2026, 6, 15), 3000m, "Прочее", "Сдача наличных")
            ]);
    }

    private static FeeReportDto CreateFeeReport()
    {
        var incomeTypeId = Guid.NewGuid();
        var garageId = Guid.NewGuid();
        return new FeeReportDto(
            "Сбор на ворота",
            500m,
            200m,
            300m,
            2,
            [new FeeReportSummaryRowDto(incomeTypeId, "Сбор на ворота", "Сбор", 500m, 200m)],
            [new FeeReportGarageRowDto(garageId, "12", "Иванов Иван", incomeTypeId, "Сбор на ворота", 500m, 200m, new DateOnly(2026, 6, 10), 300m)],
            [new FeeReportDebtorRowDto(garageId, "12", "Иванов Иван", incomeTypeId, "Сбор на ворота", 200m, new DateOnly(2026, 6, 10), 300m)]);
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

        public ReportResult<FundChangeReportDto> FundChangeResult { get; init; } = ReportResult<FundChangeReportDto>.Failure("not_configured", "Not configured.");
        public FundChangeReportRequest? FundChangeRequest { get; private set; }

        public ReportResult<ReportExportFileDto> FundChangeXlsxExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> FundChangePdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<CashPaymentReportDto> CashPaymentResult { get; init; } = ReportResult<CashPaymentReportDto>.Failure("not_configured", "Not configured.");
        public CashPaymentReportRequest? CashPaymentRequest { get; private set; }

        public ReportResult<ReportExportFileDto> CashPaymentXlsxExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> CashPaymentPdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<BankDepositReportDto> BankDepositResult { get; init; } = ReportResult<BankDepositReportDto>.Failure("not_configured", "Not configured.");
        public BankDepositReportRequest? BankDepositRequest { get; private set; }

        public ReportResult<ReportExportFileDto> BankDepositXlsxExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> BankDepositPdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<FeeReportDto> FeeResult { get; init; } = ReportResult<FeeReportDto>.Failure("not_configured", "Not configured.");
        public FeeReportRequest? FeeRequest { get; private set; }

        public ReportResult<ReportExportFileDto> FeeXlsxExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

        public ReportResult<ReportExportFileDto> FeePdfExportResult { get; init; } = ReportResult<ReportExportFileDto>.Failure("not_configured", "Not configured.");

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

        public Task<ReportResult<FundChangeReportDto>> GetFundChangeReportAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
        {
            FundChangeRequest = request;
            return Task.FromResult(FundChangeResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportXlsxAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
        {
            FundChangeRequest = request;
            return Task.FromResult(FundChangeXlsxExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportPdfAsync(FundChangeReportRequest request, CancellationToken cancellationToken)
        {
            FundChangeRequest = request;
            return Task.FromResult(FundChangePdfExportResult);
        }

        public Task<ReportResult<CashPaymentReportDto>> GetCashPaymentReportAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
        {
            CashPaymentRequest = request;
            return Task.FromResult(CashPaymentResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportXlsxAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
        {
            CashPaymentRequest = request;
            return Task.FromResult(CashPaymentXlsxExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportPdfAsync(CashPaymentReportRequest request, CancellationToken cancellationToken)
        {
            CashPaymentRequest = request;
            return Task.FromResult(CashPaymentPdfExportResult);
        }

        public Task<ReportResult<BankDepositReportDto>> GetBankDepositReportAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
        {
            BankDepositRequest = request;
            return Task.FromResult(BankDepositResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportXlsxAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
        {
            BankDepositRequest = request;
            return Task.FromResult(BankDepositXlsxExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportPdfAsync(BankDepositReportRequest request, CancellationToken cancellationToken)
        {
            BankDepositRequest = request;
            return Task.FromResult(BankDepositPdfExportResult);
        }

        public Task<ReportResult<FeeReportDto>> GetFeeReportAsync(FeeReportRequest request, CancellationToken cancellationToken)
        {
            FeeRequest = request;
            return Task.FromResult(FeeResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportFeeReportXlsxAsync(FeeReportRequest request, CancellationToken cancellationToken)
        {
            FeeRequest = request;
            return Task.FromResult(FeeXlsxExportResult);
        }

        public Task<ReportResult<ReportExportFileDto>> ExportFeeReportPdfAsync(FeeReportRequest request, CancellationToken cancellationToken)
        {
            FeeRequest = request;
            return Task.FromResult(FeePdfExportResult);
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
