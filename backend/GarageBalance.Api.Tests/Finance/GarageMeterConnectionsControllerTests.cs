using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageMeterConnectionsControllerTests
{
    [Fact]
    public async Task EndpointsReturnDtosAndForwardRequestActorAndCancellation()
    {
        var fake = new FakeService();
        var actor = Guid.NewGuid();
        var controller = CreateController(fake, actor);
        using var cancellation = new CancellationTokenSource();
        var batch = new ConnectGarageMetersBatchRequest("water", new DateOnly(2026, 9, 10), [new(Guid.NewGuid(), 0)]);
        Assert.Same(fake.Batch.Value, Assert.IsType<OkObjectResult>((await controller.ConnectBatch(batch, cancellation.Token)).Result).Value);
        Assert.Same(batch, fake.LastBatch);
        Assert.Equal(actor, fake.Actor);
        Assert.Equal(cancellation.Token, fake.Token);
        var preview = new PreviewGarageMeterConnectionsRequest(Guid.NewGuid(), Guid.NewGuid(), [new("water", 0)]);
        Assert.Same(fake.Preview.Value, Assert.IsType<OkObjectResult>((await controller.Preview(preview, cancellation.Token)).Result).Value);
        Assert.Same(preview, fake.LastPreview);
        Assert.Equal(actor, fake.Actor);
        Assert.Equal(cancellation.Token, fake.Token);
        var anonymous = CreateController(fake, null);
        await anonymous.Preview(preview, CancellationToken.None);
        Assert.Null(fake.Actor);
    }

    [Theory]
    [InlineData("garage_not_found", 404)]
    [InlineData("meter_connection_already_exists", 409)]
    [InlineData("meter_connection_changed", 409)]
    [InlineData("meter_connection_version_required", 409)]
    [InlineData("meter_reading_accrual_paid", 409)]
    [InlineData("meter_start_accrual_paid", 409)]
    [InlineData("meter_connection_batch_invalid", 400)]
    public async Task EndpointsMapValidationMissingAndConflictErrors(string code, int status)
    {
        var fake = new FakeService
        {
            Batch = FinanceResult<GarageMeterConnectionBatchDto>.Failure(code, "Проверка"),
            Preview = FinanceResult<GarageMeterConnectionPreviewDto>.Failure(code, "Проверка")
        };
        var controller = CreateController(fake, null);
        var batch = await controller.ConnectBatch(new("water", new DateOnly(2026, 9, 10), []), CancellationToken.None);
        var preview = await controller.Preview(new(Guid.NewGuid(), Guid.NewGuid(), []), CancellationToken.None);
        foreach (var result in new[] { batch.Result, preview.Result })
        {
            var error = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal(code, Assert.IsType<ProblemDetails>(error.Value).Extensions["code"]);
        }
    }

    [Theory]
    [InlineData(nameof(GarageMeterConnectionsController.ConnectBatch), SystemPermissions.PaymentsWrite, "batch")]
    [InlineData(nameof(GarageMeterConnectionsController.Preview), SystemPermissions.PaymentsRead, "preview")]
    public async Task EndpointPoliciesDenyEveryMissingPermissionAndAllowTheCompleteSet(string actionName, string financialPermission, string route)
    {
        var type = typeof(GarageMeterConnectionsController);
        var action = type.GetMethod(actionName)!;
        Assert.Equal("api/finance/meter-connections", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(route, action.GetCustomAttribute<HttpPostAttribute>()!.Template);
        var policies = type.GetCustomAttributes<AuthorizeAttribute>().Concat(action.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.Policy!).Order().ToArray();
        var expected = new[] { SystemPermissions.DictionariesRead, SystemPermissions.DictionariesWrite, SystemPermissions.OpeningDataAdjust, financialPermission }.Order().ToArray();
        Assert.Equal(expected, policies);
        foreach (var omitted in expected.Cast<string?>().Append(null))
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(expected.Where(permission => permission != omitted)
                .Select(permission => new Claim("permission", permission)), "Test"));
            var requirements = policies.Select(policy => new PermissionRequirement(policy)).ToArray();
            var context = new AuthorizationHandlerContext(requirements, principal, null);
            await new PermissionAuthorizationHandler().HandleAsync(context);
            Assert.Equal(omitted is null, context.HasSucceeded);
        }
    }

    [Fact]
    public async Task PreviewNullItemReturnsTheStructuredValidationError()
    {
        var fake = new FakeService
        {
            Preview = FinanceResult<GarageMeterConnectionPreviewDto>.Failure("meter_connection_preview_invalid", "Проверьте изменения услуг.")
        };
        var request = new PreviewGarageMeterConnectionsRequest(Guid.NewGuid(), Guid.NewGuid(), [null!]);
        var result = await CreateController(fake, null).Preview(request, CancellationToken.None);
        var error = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("meter_connection_preview_invalid", Assert.IsType<ProblemDetails>(error.Value).Extensions["code"]);
        Assert.Same(request, fake.LastPreview);
    }

    [Fact]
    public void RequestContractsRejectOversizedAndMissingLists()
    {
        // ASP.NET Core validates primary-constructor parameters for positional records.
        // TryValidateObject alone inspects properties and misses that contract metadata.
        static bool Valid(object value) => value.GetType().GetConstructors().Single().GetParameters().All(parameter =>
            Validator.TryValidateValue(value.GetType().GetProperty(parameter.Name!)!.GetValue(value),
                new ValidationContext(value) { MemberName = parameter.Name }, [], parameter.GetCustomAttributes<ValidationAttribute>()));
        Assert.False(Valid(new ConnectGarageMetersBatchRequest("water", new DateOnly(2026, 9, 10), [])));
        Assert.False(Valid(new ConnectGarageMetersBatchRequest("water", new DateOnly(2026, 9, 10),
            Enumerable.Range(0, 101).Select(_ => new ConnectGarageMeterItemRequest(Guid.NewGuid(), 0)).ToArray())));
        Assert.False(Valid(new PreviewGarageMeterConnectionsRequest(Guid.NewGuid(), Guid.NewGuid(), [])));
        Assert.False(Valid(new ConnectGarageMeterItemRequest(Guid.NewGuid(), -1)));
        Assert.True(Valid(new ConnectGarageMeterItemRequest(Guid.NewGuid(), null)));
        Assert.True(Valid(new ConnectGarageMeterItemRequest(Guid.NewGuid(), 0)));
    }

    private static GarageMeterConnectionsController CreateController(FakeService service, Guid? actor) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(actor.HasValue ? [new Claim(ClaimTypes.NameIdentifier, actor.ToString()!)] : [], "Test"))
            }
        }
    };

    private sealed class FakeService : IGarageMeterConnectionService
    {
        public FinanceResult<GarageMeterConnectionBatchDto> Batch { get; init; } = FinanceResult<GarageMeterConnectionBatchDto>.Success(new([]));
        public FinanceResult<GarageMeterConnectionPreviewDto> Preview { get; init; } = FinanceResult<GarageMeterConnectionPreviewDto>.Success(new(Guid.NewGuid(), "12", [], []));
        public ConnectGarageMetersBatchRequest? LastBatch { get; private set; }
        public PreviewGarageMeterConnectionsRequest? LastPreview { get; private set; }
        public Guid? Actor { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<FinanceResult<GarageMeterConnectionBatchDto>> ConnectBatchAsync(ConnectGarageMetersBatchRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastBatch = request; Actor = actorUserId; Token = cancellationToken; return Task.FromResult(Batch);
        }
        public Task<FinanceResult<GarageMeterConnectionPreviewDto>> PreviewAsync(PreviewGarageMeterConnectionsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastPreview = request; Actor = actorUserId; Token = cancellationToken; return Task.FromResult(Preview);
        }
    }
}
