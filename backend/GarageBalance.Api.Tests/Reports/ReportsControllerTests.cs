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

    private sealed class FakeReportService : IReportService
    {
        public ReportResult<ConsolidatedReportDto> Result { get; init; } = ReportResult<ConsolidatedReportDto>.Failure("not_configured", "Not configured.");

        public Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }
    }
}
