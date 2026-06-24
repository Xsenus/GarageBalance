using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace GarageBalance.Api.Tests.Audit;

public sealed class AuditControllerTests
{
    [Fact]
    public async Task GetEvents_PassesFiltersToService()
    {
        var service = new FakeAuditService
        {
            Events =
            [
                new AuditEventDto(Guid.NewGuid(), DateTimeOffset.UtcNow, null, "auth.login_success", "user", null, "Вход пользователя.")
            ]
        };
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.GetEvents(dateFrom, dateTo, "auth.login_success", "user", 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<AuditEventDto>>(ok.Value);
        Assert.Single(events);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("auth.login_success", service.LastRequest.Action);
        Assert.Equal("user", service.LastRequest.Search);
        Assert.Equal(50, service.LastRequest.Limit);
    }

    [Fact]
    public async Task ExportEvents_ReturnsCsvFileAndPassesFiltersToService()
    {
        var service = new FakeAuditService
        {
            Export = new AuditEventExportDto(
                "audit-events-20260623-100000.csv",
                "text/csv; charset=utf-8",
                Encoding.UTF8.GetBytes("createdAtUtc,action\r\n2026-06-23T10:00:00Z,auth.login_success\r\n"))
        };
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.ExportEvents(dateFrom, dateTo, "auth.login_success", "user", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("audit-events-20260623-100000.csv", file.FileDownloadName);
        Assert.Equal(service.Export.Content, file.FileContents);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("auth.login_success", service.LastRequest.Action);
        Assert.Equal("user", service.LastRequest.Search);
    }

    private sealed class FakeAuditService : IAuditService
    {
        public AuditEventListRequest? LastRequest { get; private set; }
        public IReadOnlyList<AuditEventDto> Events { get; init; } = [];
        public AuditEventExportDto Export { get; init; } = new("audit-events.csv", "text/csv; charset=utf-8", []);

        public Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Events);
        }

        public Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Export);
        }
    }
}
