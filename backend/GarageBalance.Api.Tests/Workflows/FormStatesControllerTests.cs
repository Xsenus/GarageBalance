using System.Security.Claims;
using System.Text.Json;
using GarageBalance.Api.Application.Workflows;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Workflows;

public sealed class FormStatesControllerTests
{
    [Fact]
    public async Task GetState_ReturnsOkWhenStateExists()
    {
        using var payload = JsonDocument.Parse("""{"rows":[{"id":"row-1"}]}""");
        var expected = new FormStateDto("payments-prototype", payload.RootElement.Clone(), DateTimeOffset.UtcNow, Guid.NewGuid());
        var controller = new FormStatesController(new FakeFormStateService { State = expected });

        var result = await controller.GetState("payments-prototype", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task GetState_ReturnsNoContentWhenStateIsMissing()
    {
        var controller = new FormStatesController(new FakeFormStateService());

        var result = await controller.GetState("unknown-scope", CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task UpsertState_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        using var payload = JsonDocument.Parse("""{"selectedGarageId":"garage-1"}""");
        var request = new UpsertFormStateRequest(payload.RootElement.Clone(), "Сохранение состояния платежей.");
        var service = new FakeFormStateService
        {
            UpsertResult = FormStateResult<FormStateDto>.Success(
                new FormStateDto("payments-prototype", payload.RootElement.Clone(), DateTimeOffset.UtcNow, actorUserId))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpsertState("payments-prototype", request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FormStateDto>(ok.Value);
        Assert.Equal("payments-prototype", dto.Scope);
        Assert.Equal("payments-prototype", service.ReceivedScope);
        Assert.Equal(actorUserId, service.ReceivedActorUserId);
        Assert.Equal("Сохранение состояния платежей.", service.ReceivedRequest?.Summary);
    }

    [Fact]
    public async Task UpsertState_ReturnsBadRequestForServiceValidationError()
    {
        using var payload = JsonDocument.Parse("""{"rows":[]}""");
        var service = new FakeFormStateService
        {
            UpsertResult = FormStateResult<FormStateDto>.Failure("form_state_scope_invalid", "Укажите корректный ключ формы.")
        };
        var controller = CreateController(service, Guid.NewGuid());

        var result = await controller.UpsertState(" ", new UpsertFormStateRequest(payload.RootElement.Clone()), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("form_state_scope_invalid", problem.Extensions["code"]);
    }

    private static FormStatesController CreateController(IFormStateService service, Guid actorUserId)
    {
        var controller = new FormStatesController(service);
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
        };
        return controller;
    }

    private sealed class FakeFormStateService : IFormStateService
    {
        public FormStateDto? State { get; init; }

        public FormStateResult<FormStateDto>? UpsertResult { get; init; }

        public string? ReceivedScope { get; private set; }

        public UpsertFormStateRequest? ReceivedRequest { get; private set; }

        public Guid? ReceivedActorUserId { get; private set; }

        public Task<FormStateDto?> GetStateAsync(string scope, CancellationToken cancellationToken)
        {
            ReceivedScope = scope;
            return Task.FromResult(State);
        }

        public Task<FormStateResult<FormStateDto>> UpsertStateAsync(
            string scope,
            UpsertFormStateRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            ReceivedScope = scope;
            ReceivedRequest = request;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(UpsertResult ?? FormStateResult<FormStateDto>.Failure("test_error", "Ошибка теста."));
        }
    }
}
