using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageCreationControllerTests
{
    [Fact]
    public async Task CreateWithAnnualPayments_PassesRequestAndActorAndReturnsCreatedGarage()
    {
        var actorUserId = Guid.NewGuid();
        var garage = new GarageDto(Guid.NewGuid(), "A-17", 2, 1, null, null, 0m, null, null, null, false);
        var service = new FakeGarageCreationService
        {
            Result = DictionaryResult<GarageDto>.Success(garage)
        };
        var controller = CreateController(service, actorUserId);
        var request = new CreateGarageWithAnnualPaymentsRequest(
            new UpsertGarageRequest("A-17", 2, 1, null, 0m, null, null, null),
            [new PaidAnnualPaymentRequest(Guid.NewGuid(), 500m)]);

        using var cancellation = new CancellationTokenSource();
        var result = await controller.CreateWithAnnualPayments(request, cancellation.Token);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(garage, created.Value);
        Assert.Same(request, service.Request);
        Assert.Equal(actorUserId, service.ActorUserId);
        Assert.Equal(cancellation.Token, service.CancellationToken);
    }

    [Fact]
    public async Task CreateWithAnnualPayments_MapsValidationAndConflictErrors()
    {
        var request = new CreateGarageWithAnnualPaymentsRequest(
            new UpsertGarageRequest("A-17", 2, 1, null, 0m, null, null, null),
            [new PaidAnnualPaymentRequest(Guid.NewGuid(), 500m)]);
        var validation = await CreateController(new FakeGarageCreationService
        {
            Result = DictionaryResult<GarageDto>.Failure("garage_annual_payments_invalid", "Ошибка")
        }).CreateWithAnnualPayments(request, CancellationToken.None);
        var conflict = await CreateController(new FakeGarageCreationService
        {
            Result = DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Дубликат")
        }).CreateWithAnnualPayments(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(validation.Result).StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(conflict.Result).StatusCode);
    }

    [Fact]
    public void CreateWithAnnualPayments_RequiresDictionaryAndPaymentWritePermissions()
    {
        var controllerPolicies = typeof(GarageCreationController)
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy);
        var methodPolicies = typeof(GarageCreationController)
            .GetMethod(nameof(GarageCreationController.CreateWithAnnualPayments))!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy);

        Assert.Contains(SystemPermissions.DictionariesRead, controllerPolicies);
        Assert.Contains(SystemPermissions.DictionariesWrite, methodPolicies);
        Assert.Contains(SystemPermissions.PaymentsWrite, methodPolicies);
    }

    private static GarageCreationController CreateController(FakeGarageCreationService service, Guid? actorUserId = null)
    {
        var controller = new GarageCreationController(service);
        var claims = actorUserId.HasValue ? new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) } : [];
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return controller;
    }

    private sealed class FakeGarageCreationService : IGarageCreationService
    {
        public DictionaryResult<GarageDto> Result { get; init; } = DictionaryResult<GarageDto>.Failure("not_configured", "Not configured");
        public CreateGarageWithAnnualPaymentsRequest? Request { get; private set; }
        public Guid? ActorUserId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<DictionaryResult<GarageDto>> CreateWithAnnualPaymentsAsync(
            CreateGarageWithAnnualPaymentsRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            Request = request;
            ActorUserId = actorUserId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
