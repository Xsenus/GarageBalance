using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
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
        var actorUserId = Guid.NewGuid();

        var result = await controller.GetEvents(dateFrom, dateTo, "auth.login_success", "user", 50, "auth", "login", "user", actorUserId, "restores", "12", "2026-06", "supplier-1", "PAY-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<AuditEventDto>>(ok.Value);
        Assert.Single(events);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("auth.login_success", service.LastRequest.Action);
        Assert.Equal("user", service.LastRequest.Search);
        Assert.Equal(50, service.LastRequest.Limit);
        Assert.Equal("auth", service.LastRequest.Section);
        Assert.Equal("login", service.LastRequest.ActionKind);
        Assert.Equal("user", service.LastRequest.EntityType);
        Assert.Equal(actorUserId, service.LastRequest.ActorUserId);
        Assert.Equal("restores", service.LastRequest.QuickFilter);
        Assert.Equal("12", service.LastRequest.RelatedGarage);
        Assert.Equal("2026-06", service.LastRequest.RelatedAccountingMonth);
        Assert.Equal("supplier-1", service.LastRequest.RelatedCounterparty);
        Assert.Equal("PAY-1", service.LastRequest.RelatedDocument);
    }

    [Fact]
    public async Task GetEvents_ReturnsBadRequestWhenDateRangeIsInvalid()
    {
        var service = new FakeAuditService();
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.GetEvents(dateFrom, dateTo, null, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None);

        AssertInvalidDateRangeProblem(result.Result);
        Assert.Null(service.LastRequest);
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
        var actorUserId = Guid.NewGuid();

        var result = await controller.ExportEvents(dateFrom, dateTo, "auth.login_success", "user", "auth", "login", "user", actorUserId, "restores", "12", "2026-06", "supplier-1", "PAY-1", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("audit-events-20260623-100000.csv", file.FileDownloadName);
        Assert.Equal(service.Export.Content, file.FileContents);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("auth.login_success", service.LastRequest.Action);
        Assert.Equal("user", service.LastRequest.Search);
        Assert.Equal("auth", service.LastRequest.Section);
        Assert.Equal("login", service.LastRequest.ActionKind);
        Assert.Equal("user", service.LastRequest.EntityType);
        Assert.Equal(actorUserId, service.LastRequest.ActorUserId);
        Assert.Equal("restores", service.LastRequest.QuickFilter);
        Assert.Equal("12", service.LastRequest.RelatedGarage);
        Assert.Equal("2026-06", service.LastRequest.RelatedAccountingMonth);
        Assert.Equal("supplier-1", service.LastRequest.RelatedCounterparty);
        Assert.Equal("PAY-1", service.LastRequest.RelatedDocument);
    }

    [Fact]
    public async Task ExportEvents_ReturnsBadRequestWhenDateRangeIsInvalid()
    {
        var service = new FakeAuditService();
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.ExportEvents(dateFrom, dateTo, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None);

        AssertInvalidDateRangeProblem(result);
        Assert.Null(service.LastRequest);
    }

    [Fact]
    public async Task ExportEventsXlsx_ReturnsXlsxFileAndPassesFiltersToService()
    {
        var service = new FakeAuditService
        {
            XlsxExport = new AuditEventExportDto(
                "audit-events-20260623-100000.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                [1, 2, 3])
        };
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var actorUserId = Guid.NewGuid();

        var result = await controller.ExportEventsXlsx(dateFrom, dateTo, "auth.login_success", "user", "auth", "login", "user", actorUserId, "restores", "12", "2026-06", "supplier-1", "PAY-1", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal("audit-events-20260623-100000.xlsx", file.FileDownloadName);
        Assert.Equal(service.XlsxExport.Content, file.FileContents);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("auth.login_success", service.LastRequest.Action);
        Assert.Equal("user", service.LastRequest.Search);
        Assert.Equal("auth", service.LastRequest.Section);
        Assert.Equal("login", service.LastRequest.ActionKind);
        Assert.Equal("user", service.LastRequest.EntityType);
        Assert.Equal(actorUserId, service.LastRequest.ActorUserId);
        Assert.Equal("restores", service.LastRequest.QuickFilter);
        Assert.Equal("12", service.LastRequest.RelatedGarage);
        Assert.Equal("2026-06", service.LastRequest.RelatedAccountingMonth);
        Assert.Equal("supplier-1", service.LastRequest.RelatedCounterparty);
        Assert.Equal("PAY-1", service.LastRequest.RelatedDocument);
    }

    [Fact]
    public async Task ExportEventsXlsx_ReturnsBadRequestWhenDateRangeIsInvalid()
    {
        var service = new FakeAuditService();
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.ExportEventsXlsx(dateFrom, dateTo, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None);

        AssertInvalidDateRangeProblem(result);
        Assert.Null(service.LastRequest);
    }

    [Fact]
    public async Task GetEvent_ReturnsEventFromService()
    {
        var auditEvent = new AuditEventDto(Guid.NewGuid(), DateTimeOffset.UtcNow, null, "dictionary.owner_updated", "owner", "owner-1", "Изменен владелец.");
        var service = new FakeAuditService { Event = auditEvent };
        var controller = new AuditController(service);

        var result = await controller.GetEvent(auditEvent.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(auditEvent, ok.Value);
        Assert.Equal(auditEvent.Id, service.LastEventId);
    }

    [Fact]
    public async Task GetEvent_ReturnsNotFoundWhenEventDoesNotExist()
    {
        var service = new FakeAuditService();
        var controller = new AuditController(service);
        var id = Guid.NewGuid();

        var result = await controller.GetEvent(id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(id, service.LastEventId);
    }

    [Fact]
    public async Task GetEventsPage_PassesPagingAndFiltersToService()
    {
        var service = new FakeAuditService
        {
            Page = new AuditEventPageDto(
                [new AuditEventDto(Guid.NewGuid(), DateTimeOffset.UtcNow, null, "finance.income_created", "financial_operation", "operation-1", "Поступление создано.")],
                12,
                10,
                10)
        };
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var actorUserId = Guid.NewGuid();

        var result = await controller.GetEventsPage(dateFrom, dateTo, null, "garage", 10, 10, "finance", "create", "financial_operation", actorUserId, "financial", "12", "2026-06", "supplier-1", "PAY-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<AuditEventPageDto>(ok.Value);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(10, page.Offset);
        Assert.Equal(10, page.Limit);
        Assert.Equal(dateFrom, service.LastRequest!.DateFrom);
        Assert.Equal(dateTo, service.LastRequest.DateTo);
        Assert.Equal("garage", service.LastRequest.Search);
        Assert.Equal(10, service.LastRequest.Offset);
        Assert.Equal(10, service.LastRequest.Limit);
        Assert.Equal("finance", service.LastRequest.Section);
        Assert.Equal("create", service.LastRequest.ActionKind);
        Assert.Equal("financial_operation", service.LastRequest.EntityType);
        Assert.Equal(actorUserId, service.LastRequest.ActorUserId);
        Assert.Equal("financial", service.LastRequest.QuickFilter);
        Assert.Equal("12", service.LastRequest.RelatedGarage);
        Assert.Equal("2026-06", service.LastRequest.RelatedAccountingMonth);
        Assert.Equal("supplier-1", service.LastRequest.RelatedCounterparty);
        Assert.Equal("PAY-1", service.LastRequest.RelatedDocument);
    }

    [Fact]
    public async Task GetEventsPage_ReturnsBadRequestWhenDateRangeIsInvalid()
    {
        var service = new FakeAuditService();
        var controller = new AuditController(service);
        var dateFrom = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);
        var dateTo = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

        var result = await controller.GetEventsPage(dateFrom, dateTo, null, null, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None);

        AssertInvalidDateRangeProblem(result.Result);
        Assert.Null(service.LastRequest);
    }

    private static void AssertInvalidDateRangeProblem(IActionResult? result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Проверьте период истории", problem.Title);
        Assert.Equal("Начало периода истории изменений не может быть позже конца.", problem.Detail);
    }

    private sealed class FakeAuditService : IAuditService
    {
        public AuditEventListRequest? LastRequest { get; private set; }
        public Guid? LastEventId { get; private set; }
        public IReadOnlyList<AuditEventDto> Events { get; init; } = [];
        public AuditEventPageDto Page { get; init; } = new([], 0, 0, 25);
        public AuditEventDto? Event { get; init; }
        public AuditEventExportDto Export { get; init; } = new("audit-events.csv", "text/csv; charset=utf-8", []);
        public AuditEventExportDto XlsxExport { get; init; } = new("audit-events.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", []);

        public Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Events);
        }

        public Task<AuditEventPageDto> GetEventsPageAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Page);
        }

        public Task<AuditEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken)
        {
            LastEventId = id;
            return Task.FromResult(Event);
        }

        public Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Export);
        }

        public Task<AuditEventExportDto> ExportEventsXlsxAsync(AuditEventListRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(XlsxExport);
        }
    }
}
