using System.Security.Claims;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Funds;

public sealed class FundsControllerTests
{
    [Fact]
    public async Task CreateFund_PassesActorAndReturnsCreatedFund()
    {
        var actorUserId = Guid.NewGuid();
        var fund = CreateFund("Резервный фонд");
        var service = new FakeFundService
        {
            CreateFundResult = FundResult<FundDto>.Success(fund)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateFund(new UpsertFundRequest("Резервный фонд"), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Same(fund, created.Value);
        Assert.Equal(nameof(FundsController.GetFunds), created.ActionName);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal("Резервный фонд", service.LastFundRequest?.Name);
    }

    [Fact]
    public async Task UpdateFund_PassesActorAndReturnsUpdatedFund()
    {
        var actorUserId = Guid.NewGuid();
        var fund = CreateFund("Новый резерв");
        var service = new FakeFundService
        {
            UpdateFundResult = FundResult<FundDto>.Success(fund)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateFund(
            fund.Id,
            new UpsertFundRequest("Новый резерв"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(fund, ok.Value);
        Assert.Equal(fund.Id, service.LastUpdatedFundId);
        Assert.Equal("Новый резерв", service.LastFundRequest?.Name);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Theory]
    [InlineData("fund_duplicate", 409)]
    [InlineData("fund_not_found", 404)]
    [InlineData("fund_name_required", 400)]
    public async Task UpdateFund_MapsServiceErrors(string errorCode, int expectedStatusCode)
    {
        var controller = CreateController(new FakeFundService
        {
            UpdateFundResult = FundResult<FundDto>.Failure(errorCode, "Не удалось изменить фонд.")
        });

        var result = await controller.UpdateFund(
            Guid.NewGuid(),
            new UpsertFundRequest("Резерв"),
            CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        Assert.Equal(errorCode, Assert.IsType<ProblemDetails>(objectResult.Value).Title);
    }

    [Fact]
    public async Task GetOperations_PassesBoundedQueryToService()
    {
        var operation = CreateOperation(isCanceled: true);
        var service = new FakeFundService
        {
            Operations = [operation]
        };
        var controller = CreateController(service);

        var result = await controller.GetOperations(limit: 12, includeCanceled: true, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(service.Operations, ok.Value);
        Assert.Equal(12, service.LastOperationsLimit);
        Assert.True(service.LastOperationsIncludeCanceled);
    }

    [Fact]
    public async Task GetOperationsPage_PassesPaginationQueryToService()
    {
        var operation = CreateOperation(isCanceled: true);
        var page = new FundOperationPageDto([operation], 31, 10, 10);
        var service = new FakeFundService { OperationsPage = page };
        var controller = CreateController(service);

        var result = await controller.GetOperationsPage(offset: 10, limit: 10, includeCanceled: true, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(page, ok.Value);
        Assert.Equal(10, service.LastOperationsOffset);
        Assert.Equal(10, service.LastOperationsPageLimit);
        Assert.True(service.LastOperationsPageIncludeCanceled);
    }

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
            isCanceled,
            false);
    }

    private static FundDto CreateFund(string name)
    {
        return new FundDto(Guid.NewGuid(), name, 0m, 0m, 80, true, false);
    }

    private sealed class FakeFundService : IFundService
    {
        public Guid? LastActorUserId { get; private set; }
        public Guid? LastUpdatedFundId { get; private set; }
        public Guid? LastUpdatedOperationId { get; private set; }
        public Guid? LastCanceledOperationId { get; private set; }
        public Guid? LastRestoredOperationId { get; private set; }
        public int? LastOperationsLimit { get; private set; }
        public bool? LastOperationsIncludeCanceled { get; private set; }
        public int? LastOperationsOffset { get; private set; }
        public int? LastOperationsPageLimit { get; private set; }
        public bool? LastOperationsPageIncludeCanceled { get; private set; }
        public UpdateFundOperationRequest? LastUpdateRequest { get; private set; }
        public UpsertFundRequest? LastFundRequest { get; private set; }
        public CancelFundOperationRequest? LastCancelRequest { get; private set; }
        public IReadOnlyList<FundOperationDto> Operations { get; init; } = [];
        public FundOperationPageDto OperationsPage { get; init; } = new([], 0, 0, 25);
        public FundResult<FundDto> CreateFundResult { get; init; } = FundResult<FundDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundDto> UpdateFundResult { get; init; } = FundResult<FundDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundOperationDto> UpdateOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundOperationDto> CancelOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");
        public FundResult<FundOperationDto> RestoreOperationResult { get; init; } = FundResult<FundOperationDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<FundDto>> GetFundsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<FundDto>>([]);
        }

        public Task<FundResult<FundDto>> CreateFundAsync(UpsertFundRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastFundRequest = request;
            return Task.FromResult(CreateFundResult);
        }

        public Task<FundResult<FundDto>> UpdateFundAsync(Guid fundId, UpsertFundRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastUpdatedFundId = fundId;
            LastFundRequest = request;
            return Task.FromResult(UpdateFundResult);
        }

        public Task<IReadOnlyList<FundOperationDto>> GetOperationsAsync(int limit, bool includeCanceled, CancellationToken cancellationToken)
        {
            LastOperationsLimit = limit;
            LastOperationsIncludeCanceled = includeCanceled;
            return Task.FromResult(Operations);
        }

        public Task<FundOperationPageDto> GetOperationsPageAsync(int offset, int limit, bool includeCanceled, CancellationToken cancellationToken)
        {
            LastOperationsOffset = offset;
            LastOperationsPageLimit = limit;
            LastOperationsPageIncludeCanceled = includeCanceled;
            return Task.FromResult(OperationsPage);
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
