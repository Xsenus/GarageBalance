using System.Security.Claims;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Funds;

public sealed class FundsControllerTests
{
    [Fact]
    public async Task UpdateOperation_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation(isCanceled: false);
        var service = new FakeFundService
        {
            UpdateOperationResult = FundResult<FundOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateOperation(operation.Id, new UpdateFundOperationRequest(600m, "Уточнение"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(operation, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operation.Id, service.LastUpdatedOperationId);
        Assert.Equal(600m, service.LastUpdateRequest?.Amount);
        Assert.Equal("Уточнение", service.LastUpdateRequest?.Reason);
    }

    [Fact]
    public async Task UpdateOperation_ReturnsConflictForCanceledOperation()
    {
        var controller = CreateController(new FakeFundService
        {
            UpdateOperationResult = FundResult<FundOperationDto>.Failure("fund_operation_canceled", "Нельзя изменить отмененную операцию фонда.")
        });

        var result = await controller.UpdateOperation(Guid.NewGuid(), new UpdateFundOperationRequest(600m, "Уточнение"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("fund_operation_canceled", problem.Title);
    }

    [Fact]
    public async Task CancelOperation_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation(isCanceled: true);
        var service = new FakeFundService
        {
            CancelOperationResult = FundResult<FundOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CancelOperation(operation.Id, new CancelFundOperationRequest("Ошибка распределения"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(operation, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operation.Id, service.LastCanceledOperationId);
        Assert.Equal("Ошибка распределения", service.LastCancelRequest?.Reason);
    }

    [Fact]
    public async Task CancelOperation_ReturnsBadRequestForMissingReason()
    {
        var service = new FakeFundService();
        var controller = CreateController(service);

        var result = await controller.CancelOperation(Guid.NewGuid(), new CancelFundOperationRequest("   "), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("fund_operation_cancel_reason_required", problem.Title);
        Assert.Null(service.LastCancelRequest);
        Assert.Null(service.LastCanceledOperationId);
    }

    [Fact]
    public async Task CancelOperation_ReturnsNotFoundForMissingOperation()
    {
        var controller = CreateController(new FakeFundService
        {
            CancelOperationResult = FundResult<FundOperationDto>.Failure("fund_operation_not_found", "Операция фонда не найдена.")
        });

        var result = await controller.CancelOperation(Guid.NewGuid(), new CancelFundOperationRequest("Нет операции"), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("fund_operation_not_found", problem.Title);
    }

    [Fact]
    public async Task CancelOperation_ReturnsConflictForAlreadyCanceledOperation()
    {
        var controller = CreateController(new FakeFundService
        {
            CancelOperationResult = FundResult<FundOperationDto>.Failure("fund_operation_already_canceled", "Операция фонда уже отменена.")
        });

        var result = await controller.CancelOperation(Guid.NewGuid(), new CancelFundOperationRequest("Повтор"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("fund_operation_already_canceled", problem.Title);
    }

    [Fact]
    public async Task RestoreOperation_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation(isCanceled: false);
        var service = new FakeFundService
        {
            RestoreOperationResult = FundResult<FundOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreOperation(operation.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(operation, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operation.Id, service.LastRestoredOperationId);
    }

    [Fact]
    public async Task RestoreOperation_ReturnsConflictForActiveOperation()
    {
        var controller = CreateController(new FakeFundService
        {
            RestoreOperationResult = FundResult<FundOperationDto>.Failure("fund_operation_not_canceled", "Операция фонда уже активна.")
        });

        var result = await controller.RestoreOperation(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("fund_operation_not_canceled", problem.Title);
    }

    private static FundsController CreateController(FakeFundService service, Guid? actorUserId = null)
    {
        var controller = new FundsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        if (actorUserId is not null)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString())],
                "Test"));
        }

        return controller;
    }

    private static FundOperationDto CreateOperation(bool isCanceled)
    {
        return new FundOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Электроэнергия",
            "deposit",
            500m,
            0m,
            500m,
            "Решение правления",
            DateTimeOffset.UtcNow,
            isCanceled);
    }

    private sealed class FakeFundService : IFundService
    {
        public Guid? LastActorUserId { get; private set; }
        public Guid? LastUpdatedOperationId { get; private set; }
        public Guid? LastCanceledOperationId { get; private set; }
        public Guid? LastRestoredOperationId { get; private set; }
        public UpdateFundOperationRequest? LastUpdateRequest { get; private set; }
        public CancelFundOperationRequest? LastCancelRequest { get; private set; }
        public FundResult<FundOperationDto> UpdateOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundOperationDto> CancelOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundOperationDto> RestoreOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<FundDto>>([]);
        }

        public Task<FundResult<FundOperationDto>> CreateOperationAsync(Guid fundId, CreateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(FundResult<FundOperationDto>.Failure("not_configured", "Not configured."));
        }

        public Task<FundResult<FundOperationDto>> UpdateOperationAsync(Guid operationId, UpdateFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastUpdatedOperationId = operationId;
            LastUpdateRequest = request;
            return Task.FromResult(UpdateOperationResult);
        }

        public Task<FundResult<FundOperationDto>> CancelOperationAsync(Guid operationId, CancelFundOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledOperationId = operationId;
            LastCancelRequest = request;
            return Task.FromResult(CancelOperationResult);
        }

        public Task<FundResult<FundOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoredOperationId = operationId;
            return Task.FromResult(RestoreOperationResult);
        }
    }
}
