using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportsControllerTests
{
    [Fact]
    public async Task GetConsolidatedReport_ReturnsOk()
    {
        var report = CreateReport();
        var controller = new ReportsController(new FakeReportService
        {
            Result = ReportResult<ConsolidatedReportDto>.Success(report)
        });

        var result = await controller.GetConsolidatedReport(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
    }

    [Fact]
    public async Task GetConsolidatedReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            Result = ReportResult<ConsolidatedReportDto>.Failure("period_invalid", "Период неверный.")
        });

        var result = await controller.GetConsolidatedReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetIncomeReport_ReturnsOk()
    {
        var report = CreateIncomeReport();
        var controller = new ReportsController(new FakeReportService
        {
            IncomeResult = ReportResult<IncomeReportDto>.Success(report)
        });

        var result = await controller.GetIncomeReport(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "12",
            [Guid.NewGuid()],
            [],
            [],
            "all",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(report, ok.Value);
    }

    [Fact]
    public async Task GetIncomeReport_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = new ReportsController(new FakeReportService
        {
            IncomeResult = ReportResult<IncomeReportDto>.Failure("period_invalid", "Период неверный.")
        });

        var result = await controller.GetIncomeReport(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
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

    private sealed class FakeReportService : IReportService
    {
        public ReportResult<ConsolidatedReportDto> Result { get; init; } = ReportResult<ConsolidatedReportDto>.Failure("not_configured", "Not configured.");

        public ReportResult<IncomeReportDto> IncomeResult { get; init; } = ReportResult<IncomeReportDto>.Failure("not_configured", "Not configured.");

        public Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }

        public Task<ReportResult<IncomeReportDto>> GetIncomeReportAsync(IncomeReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(IncomeResult);
        }
    }
}
