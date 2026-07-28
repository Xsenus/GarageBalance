using System.Security.Claims;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Reports;

public sealed class GarageReportQuickListsControllerTests
{
    [Fact]
    public void Controller_RequiresReportsReadPermission()
    {
        var authorize = Assert.Single(typeof(GarageReportQuickListsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(SystemPermissions.ReportsRead, authorize.Policy);
    }

    [Fact]
    public async Task GetAll_ReturnsServiceItems()
    {
        var dto = CreateDto();
        var service = new FakeService { GetAllResult = [dto] };
        var controller = CreateController(service, null);

        var result = await controller.GetAll(CancellationToken.None);

        var items = Assert.IsAssignableFrom<IReadOnlyList<GarageReportQuickListDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([dto], items);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAndPassesActor()
    {
        var actorUserId = Guid.NewGuid();
        var dto = CreateDto();
        var service = new FakeService
        {
            CreateResult = ReportResult<GarageReportQuickListDto>.Success(dto)
        };
        var controller = CreateController(service, actorUserId);
        var request = new UpsertGarageReportQuickListRequest("Должники", [Guid.NewGuid()]);

        var result = await controller.Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(dto, created.Value);
        Assert.Equal(actorUserId, service.ActorUserId);
        Assert.Same(request, service.Request);
    }

    [Theory]
    [InlineData("garage_quick_list_name_required", StatusCodes.Status400BadRequest)]
    [InlineData("garage_quick_list_name_conflict", StatusCodes.Status409Conflict)]
    [InlineData("garage_quick_list_not_found", StatusCodes.Status404NotFound)]
    public async Task Update_MapsApplicationErrors(string code, int expectedStatus)
    {
        var service = new FakeService
        {
            UpdateResult = ReportResult<GarageReportQuickListDto>.Failure(code, "Ошибка")
        };
        var controller = CreateController(service, null);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpsertGarageReportQuickListRequest("Список", [Guid.NewGuid()]),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(code, Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task Delete_ReturnsNoContentAndMissingReturnsNotFound()
    {
        var service = new FakeService
        {
            DeleteResult = ReportResult<bool>.Success(true)
        };
        var controller = CreateController(service, null);

        var deleted = await controller.Delete(
            Guid.NewGuid(),
            new DeleteGarageReportQuickListRequest("Список больше не используется"),
            CancellationToken.None);
        service.DeleteResult = ReportResult<bool>.Failure("garage_quick_list_not_found", "Не найден");
        var missing = await controller.Delete(
            Guid.NewGuid(),
            new DeleteGarageReportQuickListRequest("Проверка отсутствующего списка"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(deleted);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(missing).StatusCode);
    }

    [Fact]
    public async Task Delete_MapsMissingReasonValidationToBadRequest()
    {
        var service = new FakeService
        {
            DeleteResult = ReportResult<bool>.Failure(
                "garage_quick_list_delete_reason_required",
                "Укажите причину удаления.")
        };
        var controller = CreateController(service, null);

        var result = await controller.Delete(
            Guid.NewGuid(),
            new DeleteGarageReportQuickListRequest(string.Empty),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            "garage_quick_list_delete_reason_required",
            Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    private static GarageReportQuickListsController CreateController(FakeService service, Guid? actorUserId)
    {
        var controller = new GarageReportQuickListsController(service);
        var claims = actorUserId.HasValue
            ? new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) }
            : [];
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims))
        };
        return controller;
    }

    private static GarageReportQuickListDto CreateDto()
    {
        return new GarageReportQuickListDto(
            Guid.NewGuid(),
            "Должники",
            [],
            DateTimeOffset.UtcNow,
            null);
    }

    private sealed class FakeService : IGarageReportQuickListService
    {
        public UpsertGarageReportQuickListRequest? Request { get; private set; }
        public Guid? ActorUserId { get; private set; }
        public ReportResult<GarageReportQuickListDto> CreateResult { get; set; } =
            ReportResult<GarageReportQuickListDto>.Failure("not_configured", "Не настроено");
        public ReportResult<GarageReportQuickListDto> UpdateResult { get; set; } =
            ReportResult<GarageReportQuickListDto>.Failure("not_configured", "Не настроено");
        public ReportResult<bool> DeleteResult { get; set; } =
            ReportResult<bool>.Failure("not_configured", "Не настроено");
        public IReadOnlyList<GarageReportQuickListDto> GetAllResult { get; set; } = [];

        public Task<IReadOnlyList<GarageReportQuickListDto>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetAllResult);
        }

        public Task<ReportResult<GarageReportQuickListDto>> CreateAsync(
            UpsertGarageReportQuickListRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            Request = request;
            ActorUserId = actorUserId;
            return Task.FromResult(CreateResult);
        }

        public Task<ReportResult<GarageReportQuickListDto>> UpdateAsync(
            Guid id,
            UpsertGarageReportQuickListRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateResult);
        }

        public Task<ReportResult<bool>> DeleteAsync(
            Guid id,
            DeleteGarageReportQuickListRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeleteResult);
        }
    }
}
