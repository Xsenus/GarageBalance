using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinanceControllerTests
{
    [Fact]
    public async Task CreateIncome_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation("income");
        var service = new FakeFinanceService
        {
            CreateIncomeResult = FinanceResult<FinancialOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateIncome(
            new CreateIncomeOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, "1", null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task CreateExpense_ReturnsConflictForDuplicateOperation()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateExpenseResult = FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция уже внесена.")
        });

        var result = await controller.CreateExpense(
            new CreateExpenseOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, "1", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("operation_duplicate", problem.Title);
    }

    [Fact]
    public async Task CreateIncome_ReturnsNotFoundForMissingGarage()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateIncomeResult = FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж не найден.")
        });

        var result = await controller.CreateIncome(
            new CreateIncomeOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, null, null),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("garage_not_found", problem.Title);
    }

    [Fact]
    public async Task CreateAccrual_ReturnsConflictForDuplicateAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateAccrualResult = FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Начисление уже внесено.")
        });

        var result = await controller.CreateAccrual(
            new CreateAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), 100m, "regular", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("accrual_duplicate", problem.Title);
    }

    [Fact]
    public async Task CreateAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            CreateAccrualResult = FinanceResult<AccrualDto>.Success(CreateAccrual())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateAccrual(
            new CreateAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), 100m, "regular", null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task CreateSupplierAccrual_ReturnsConflictForDuplicateAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_duplicate", "Начисление поставщику уже внесено.")
        });

        var result = await controller.CreateSupplierAccrual(
            new CreateSupplierAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), 100m, "regular", "INV-1", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("supplier_accrual_duplicate", problem.Title);
    }

    [Fact]
    public async Task CreateSupplierAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            CreateSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Success(CreateSupplierAccrual())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateSupplierAccrual(
            new CreateSupplierAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), 100m, "regular", "INV-1", null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task GenerateRegularAccruals_ReturnsConflictWhenNothingCreated()
    {
        var controller = CreateController(new FakeFinanceService
        {
            GenerateRegularAccrualsResult = FinanceResult<RegularAccrualGenerationResultDto>.Failure("regular_accruals_empty", "Начисления не созданы.")
        });

        var result = await controller.GenerateRegularAccruals(
            new GenerateRegularAccrualsRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("regular_accruals_empty", problem.Title);
    }

    [Fact]
    public async Task GenerateRegularAccruals_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            GenerateRegularAccrualsResult = FinanceResult<RegularAccrualGenerationResultDto>.Success(CreateGenerationResult())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.GenerateRegularAccruals(
            new GenerateRegularAccrualsRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task CreateMeterReading_ReturnsConflictForDuplicateReading()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateMeterReadingResult = FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание уже внесено.")
        });

        var result = await controller.CreateMeterReading(
            new CreateMeterReadingRequest(Guid.NewGuid(), "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 10m, null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("meter_reading_duplicate", problem.Title);
    }

    [Fact]
    public async Task CreateMeterReading_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            CreateMeterReadingResult = FinanceResult<MeterReadingDto>.Success(CreateMeterReading())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateMeterReading(
            new CreateMeterReadingRequest(Guid.NewGuid(), "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 10m, null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    private static FinanceController CreateController(FakeFinanceService service, Guid? actorUserId = null)
    {
        var controller = new FinanceController(service);
        var claims = actorUserId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) };
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return controller;
    }

    private static FinancialOperationDto CreateOperation(string kind)
    {
        return new FinancialOperationDto(
            Guid.NewGuid(),
            kind,
            new DateOnly(2026, 6, 19),
            new DateOnly(2026, 6, 1),
            100m,
            "1",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);
    }

    private static AccrualDto CreateAccrual()
    {
        return new AccrualDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "12",
            "Иванов Иван",
            Guid.NewGuid(),
            "Членский взнос",
            new DateOnly(2026, 6, 1),
            100m,
            "regular",
            null,
            false);
    }

    private static MeterReadingDto CreateMeterReading()
    {
        return new MeterReadingDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "12",
            "Иванов Иван",
            "water",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 20),
            15m,
            10m,
            5m,
            false,
            null,
            false);
    }

    private static SupplierAccrualDto CreateSupplierAccrual()
    {
        return new SupplierAccrualDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Водоканал",
            Guid.NewGuid(),
            "Вода",
            new DateOnly(2026, 6, 1),
            100m,
            "regular",
            "INV-1",
            null,
            false);
    }

    private static RegularAccrualGenerationResultDto CreateGenerationResult()
    {
        var accrual = CreateAccrual();
        return new RegularAccrualGenerationResultDto(
            new DateOnly(2026, 6, 1),
            accrual.IncomeTypeId,
            accrual.IncomeTypeName,
            Guid.NewGuid(),
            "Членский тариф",
            "fixed",
            1,
            0,
            accrual.Amount,
            [accrual],
            []);
    }

    private sealed class FakeFinanceService : IFinanceService
    {
        public Guid? LastActorUserId { get; private set; }
        public FinanceResult<FinancialOperationDto> CreateIncomeResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateExpenseResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> CreateSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<RegularAccrualGenerationResultDto> GenerateRegularAccrualsResult { get; init; } = FinanceResult<RegularAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CreateMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<FinancialOperationDto>>([]);
        }

        public Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AccrualDto>>([]);
        }

        public Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SupplierAccrualDto>>([]);
        }

        public Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<MeterReadingDto>>([]);
        }

        public Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new FinanceSummaryDto(0, 0, 0, 0, 0, 0, 0, 0));
        }

        public Task<FinanceResult<FinancialOperationDto>> CreateIncomeAsync(CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateIncomeResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateExpenseResult);
        }

        public Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> CreateSupplierAccrualAsync(CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateSupplierAccrualResult);
        }

        public Task<FinanceResult<RegularAccrualGenerationResultDto>> GenerateRegularAccrualsAsync(GenerateRegularAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(GenerateRegularAccrualsResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateMeterReadingResult);
        }
    }
}
