using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinanceControllerTests
{
    [Fact]
    public async Task ListEndpoints_PassLimitToService()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        await controller.GetOperations(null, null, "income", "12", 50, CancellationToken.None);
        await controller.GetAccruals(null, null, "12", 51, CancellationToken.None);
        await controller.GetSupplierAccruals(null, null, "water", 52, CancellationToken.None);
        await controller.GetMeterReadings(null, null, "electricity", "12", 53, CancellationToken.None);
        await controller.GetMissingMeterReadings(new DateOnly(2026, 6, 1), "water", "12", 54, CancellationToken.None);

        Assert.Equal(50, service.LastFinancialOperationListRequest?.Limit);
        Assert.Equal(51, service.LastAccrualListRequest?.Limit);
        Assert.Equal(52, service.LastSupplierAccrualListRequest?.Limit);
        Assert.Equal(53, service.LastMeterReadingListRequest?.Limit);
        Assert.Equal(54, service.LastMissingMeterReadingListRequest?.Limit);
        Assert.Equal(new DateOnly(2026, 6, 1), service.LastMissingMeterReadingListRequest?.AccountingMonth);
        Assert.Equal("water", service.LastMissingMeterReadingListRequest?.MeterKind);
    }

    [Fact]
    public async Task GetGarageBalanceHistory_PassesGarageAndPeriodToService()
    {
        var garageId = Guid.NewGuid();
        var history = CreateGarageBalanceHistory(garageId);
        var service = new FakeFinanceService
        {
            GarageBalanceHistoryResult = FinanceResult<GarageBalanceHistoryDto>.Success(history)
        };
        var controller = CreateController(service);

        var result = await controller.GetGarageBalanceHistory(garageId, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(history, ok.Value);
        Assert.Equal(garageId, service.LastGarageBalanceHistoryGarageId);
        Assert.Equal(new DateOnly(2026, 6, 1), service.LastGarageBalanceHistoryRequest?.MonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), service.LastGarageBalanceHistoryRequest?.MonthTo);
    }

    [Fact]
    public async Task GetGarageBalanceHistory_ReturnsNotFoundForMissingGarage()
    {
        var controller = CreateController(new FakeFinanceService
        {
            GarageBalanceHistoryResult = FinanceResult<GarageBalanceHistoryDto>.Failure("garage_not_found", "Гараж не найден.")
        });

        var result = await controller.GetGarageBalanceHistory(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("garage_not_found", problem.Title);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheet_PassesGarageAndPeriodToService()
    {
        var garageId = Guid.NewGuid();
        var worksheet = new GarageIncomeWorksheetDto(
            garageId,
            "12",
            "Иванов Иван",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1),
            250m,
            1000m,
            400m,
            600m,
            850m,
            []);
        var service = new FakeFinanceService
        {
            GarageIncomeWorksheetResult = FinanceResult<GarageIncomeWorksheetDto>.Success(worksheet)
        };
        var controller = CreateController(service);

        var result = await controller.GetGarageIncomeWorksheet(garageId, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(worksheet, ok.Value);
        Assert.Equal(garageId, service.LastGarageIncomeWorksheetGarageId);
        Assert.Equal(new DateOnly(2026, 6, 1), service.LastGarageIncomeWorksheetRequest?.MonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), service.LastGarageIncomeWorksheetRequest?.MonthTo);
    }

    [Fact]
    public async Task GetExpenseWorksheet_PassesAccountingMonthToService()
    {
        var worksheet = new ExpenseWorksheetDto(
            new DateOnly(2026, 6, 1),
            1000m,
            400m,
            600m,
            1200m,
            200m,
            0m,
            800m,
            []);
        var service = new FakeFinanceService
        {
            ExpenseWorksheetResult = FinanceResult<ExpenseWorksheetDto>.Success(worksheet)
        };
        var controller = CreateController(service);

        var result = await controller.GetExpenseWorksheet(new DateOnly(2026, 6, 1), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(worksheet, ok.Value);
        Assert.Equal(new DateOnly(2026, 6, 1), service.LastExpenseWorksheetRequest?.AccountingMonth);
    }

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
    public async Task CreateGarageDebtPayment_PassesActorUserIdAndRequestToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation("income");
        var request = new CreateGarageDebtPaymentRequest(Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 900m, "Оплата долга");
        var service = new FakeFinanceService
        {
            CreateGarageDebtPaymentResult = FinanceResult<FinancialOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateGarageDebtPayment(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastGarageDebtPaymentRequest);
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
    public async Task CreateStaffPayment_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation("expense");
        var request = new CreateStaffPaymentRequest(Guid.NewGuid(), new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1), 25000m, "PAY-1", "Аванс");
        var service = new FakeFinanceService
        {
            CreateStaffPaymentResult = FinanceResult<FinancialOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateStaffPayment(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastStaffPaymentRequest);
    }

    [Fact]
    public async Task CancelOperation_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation("income");
        var service = new FakeFinanceService
        {
            CancelOperationResult = FinanceResult<FinancialOperationDto>.Success(operation)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CancelOperation(operation.Id, new CancelFinanceEntryRequest("Ошибка документа"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(operation, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operation.Id, service.LastCanceledOperationId);
        Assert.Equal("Ошибка документа", service.LastCancelRequest?.Reason);
    }

    [Fact]
    public async Task CancelOperation_ReturnsConflictForAlreadyCanceledOperation()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CancelOperationResult = FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Операция уже отменена.")
        });

        var result = await controller.CancelOperation(Guid.NewGuid(), new CancelFinanceEntryRequest("Повторная отмена"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("operation_already_canceled", problem.Title);
    }

    [Fact]
    public async Task CancelOperation_ReturnsNotFoundForMissingOperation()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CancelOperationResult = FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Операция не найдена.")
        });

        var result = await controller.CancelOperation(Guid.NewGuid(), new CancelFinanceEntryRequest("Нет операции"), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("operation_not_found", problem.Title);
    }

    [Fact]
    public async Task CancelOperation_ReturnsBadRequestForMissingCancelBody()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        var result = await controller.CancelOperation(Guid.NewGuid(), null, CancellationToken.None);

        AssertCancelReasonBadRequest(result, "operation_cancel_reason_required");
        Assert.Null(service.LastCancelRequest);
        Assert.Null(service.LastCanceledOperationId);
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
    public async Task CreateDebtTransfer_PassesActorUserIdAndRequestToService()
    {
        var actorUserId = Guid.NewGuid();
        var request = new CreateDebtTransferRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), 1700m, "Перенос по заявке");
        var service = new FakeFinanceService
        {
            CreateDebtTransferResult = FinanceResult<AccrualDto>.Success(CreateAccrual())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateDebtTransfer(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastDebtTransferRequest);
    }

    [Fact]
    public async Task CancelAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateAccrual();
        var service = new FakeFinanceService
        {
            CancelAccrualResult = FinanceResult<AccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CancelAccrual(accrual.Id, new CancelFinanceEntryRequest("Ошибка начисления"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastCanceledAccrualId);
        Assert.Equal("Ошибка начисления", service.LastCancelRequest?.Reason);
    }

    [Fact]
    public async Task CancelAccrual_ReturnsConflictForAlreadyCanceledAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CancelAccrualResult = FinanceResult<AccrualDto>.Failure("accrual_already_canceled", "Начисление уже отменено.")
        });

        var result = await controller.CancelAccrual(Guid.NewGuid(), new CancelFinanceEntryRequest("Повторная отмена"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("accrual_already_canceled", problem.Title);
    }

    [Fact]
    public async Task CancelAccrual_ReturnsBadRequestForBlankCancelReason()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        var result = await controller.CancelAccrual(Guid.NewGuid(), new CancelFinanceEntryRequest("   "), CancellationToken.None);

        AssertCancelReasonBadRequest(result, "accrual_cancel_reason_required");
        Assert.Null(service.LastCancelRequest);
        Assert.Null(service.LastCanceledAccrualId);
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
    public async Task CancelSupplierAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateSupplierAccrual();
        var service = new FakeFinanceService
        {
            CancelSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CancelSupplierAccrual(accrual.Id, new CancelFinanceEntryRequest("Ошибка счета"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastCanceledSupplierAccrualId);
    }

    [Fact]
    public async Task CancelSupplierAccrual_ReturnsBadRequestForBlankCancelReason()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        var result = await controller.CancelSupplierAccrual(Guid.NewGuid(), new CancelFinanceEntryRequest("   "), CancellationToken.None);

        AssertCancelReasonBadRequest(result, "supplier_accrual_cancel_reason_required");
        Assert.Null(service.LastCancelRequest);
        Assert.Null(service.LastCanceledSupplierAccrualId);
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
    public async Task GenerateRegularCatalogAccruals_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            GenerateRegularCatalogAccrualsResult = FinanceResult<RegularCatalogAccrualGenerationResultDto>.Success(CreateCatalogGenerationResult())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.GenerateRegularCatalogAccruals(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 6, 1), "Каталог"),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccruals_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            GenerateSupplierGroupSalaryAccrualsResult = FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Success(CreateSalaryGenerationResult())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.GenerateSupplierGroupSalaryAccruals(
            new GenerateSupplierGroupSalaryAccrualsRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), 7000m, "PAY-06", null),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccruals_ReturnsConflictWhenNothingCreated()
    {
        var service = new FakeFinanceService
        {
            GenerateSupplierGroupSalaryAccrualsResult = FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("salary_accruals_empty", "Зарплата уже начислена.")
        };
        var controller = CreateController(service, Guid.NewGuid());

        var result = await controller.GenerateSupplierGroupSalaryAccruals(
            new GenerateSupplierGroupSalaryAccrualsRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), 7000m, null, null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("salary_accruals_empty", problem.Title);
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

    [Fact]
    public async Task CancelMeterReading_ReturnsNotFoundForMissingReading()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CancelMeterReadingResult = FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание не найдено.")
        });

        var result = await controller.CancelMeterReading(Guid.NewGuid(), new CancelFinanceEntryRequest("Нет показания"), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("meter_reading_not_found", problem.Title);
    }

    [Fact]
    public async Task CancelMeterReading_ReturnsBadRequestForBlankCancelReason()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        var result = await controller.CancelMeterReading(Guid.NewGuid(), new CancelFinanceEntryRequest("   "), CancellationToken.None);

        AssertCancelReasonBadRequest(result, "meter_reading_cancel_reason_required");
        Assert.Null(service.LastCancelRequest);
        Assert.Null(service.LastCanceledMeterReadingId);
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

    private static void AssertCancelReasonBadRequest<T>(ActionResult<T> result, string errorCode)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(errorCode, problem.Title);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
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
            null,
            null,
            [],
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

    private static RegularCatalogAccrualGenerationResultDto CreateCatalogGenerationResult()
    {
        var serviceResult = CreateGenerationResult();
        return new RegularCatalogAccrualGenerationResultDto(
            serviceResult.AccountingMonth,
            1,
            serviceResult.CreatedCount,
            serviceResult.SkippedCount,
            serviceResult.TotalAmount,
            [serviceResult],
            []);
    }

    private static SupplierGroupSalaryAccrualGenerationResultDto CreateSalaryGenerationResult()
    {
        var accrual = CreateSupplierAccrual();
        return new SupplierGroupSalaryAccrualGenerationResultDto(
            new DateOnly(2026, 6, 1),
            Guid.NewGuid(),
            "Персонал",
            accrual.ExpenseTypeId,
            accrual.ExpenseTypeName,
            1,
            0,
            accrual.Amount,
            [accrual],
            []);
    }

    private static GarageBalanceHistoryDto CreateGarageBalanceHistory(Guid garageId)
    {
        return new GarageBalanceHistoryDto(
            garageId,
            "12",
            "Иванов Иван",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1),
            100m,
            1200m,
            500m,
            800m,
            [
                new GarageBalanceHistoryRowDto(new DateOnly(2026, 6, 1), 100m, 500m, 200m, 400m),
                new GarageBalanceHistoryRowDto(new DateOnly(2026, 7, 1), 400m, 700m, 300m, 800m)
            ]);
    }

    private sealed class FakeFinanceService : IFinanceService
    {
        public Guid? LastActorUserId { get; private set; }
        public Guid? LastCanceledOperationId { get; private set; }
        public Guid? LastCanceledAccrualId { get; private set; }
        public Guid? LastCanceledSupplierAccrualId { get; private set; }
        public Guid? LastCanceledMeterReadingId { get; private set; }
        public Guid? LastGarageBalanceHistoryGarageId { get; private set; }
        public Guid? LastGarageIncomeWorksheetGarageId { get; private set; }
        public CancelFinanceEntryRequest? LastCancelRequest { get; private set; }
        public FinancialOperationListRequest? LastFinancialOperationListRequest { get; private set; }
        public CreateStaffPaymentRequest? LastStaffPaymentRequest { get; private set; }
        public CreateDebtTransferRequest? LastDebtTransferRequest { get; private set; }
        public AccrualListRequest? LastAccrualListRequest { get; private set; }
        public SupplierAccrualListRequest? LastSupplierAccrualListRequest { get; private set; }
        public MeterReadingListRequest? LastMeterReadingListRequest { get; private set; }
        public MissingMeterReadingListRequest? LastMissingMeterReadingListRequest { get; private set; }
        public GarageBalanceHistoryRequest? LastGarageBalanceHistoryRequest { get; private set; }
        public GarageIncomeWorksheetRequest? LastGarageIncomeWorksheetRequest { get; private set; }
        public ExpenseWorksheetRequest? LastExpenseWorksheetRequest { get; private set; }
        public CreateGarageDebtPaymentRequest? LastGarageDebtPaymentRequest { get; private set; }
        public FinanceResult<GarageBalanceHistoryDto> GarageBalanceHistoryResult { get; init; } = FinanceResult<GarageBalanceHistoryDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<GarageIncomeWorksheetDto> GarageIncomeWorksheetResult { get; init; } = FinanceResult<GarageIncomeWorksheetDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<ExpenseWorksheetDto> ExpenseWorksheetResult { get; init; } = FinanceResult<ExpenseWorksheetDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateIncomeResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateGarageDebtPaymentResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> UpdateIncomeResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateExpenseResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateStaffPaymentResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> UpdateExpenseResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CancelOperationResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateDebtTransferResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> UpdateAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CancelAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> CreateSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> UpdateSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> CancelSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<RegularAccrualGenerationResultDto> GenerateRegularAccrualsResult { get; init; } = FinanceResult<RegularAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<RegularCatalogAccrualGenerationResultDto> GenerateRegularCatalogAccrualsResult { get; init; } = FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto> GenerateSupplierGroupSalaryAccrualsResult { get; init; } = FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CreateMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> UpdateMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CancelMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
        {
            LastFinancialOperationListRequest = request;
            return Task.FromResult<IReadOnlyList<FinancialOperationDto>>([]);
        }

        public Task<FinancePagedResult<FinancialOperationDto>> GetOperationsPageAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
        {
            LastFinancialOperationListRequest = request;
            return Task.FromResult(new FinancePagedResult<FinancialOperationDto>([], 0, request.Offset ?? 0, request.Limit ?? 50));
        }

        public Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken)
        {
            LastAccrualListRequest = request;
            return Task.FromResult<IReadOnlyList<AccrualDto>>([]);
        }

        public Task<FinancePagedResult<AccrualDto>> GetAccrualsPageAsync(AccrualListRequest request, CancellationToken cancellationToken)
        {
            LastAccrualListRequest = request;
            return Task.FromResult(new FinancePagedResult<AccrualDto>([], 0, request.Offset ?? 0, request.Limit ?? 50));
        }

        public Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
        {
            LastSupplierAccrualListRequest = request;
            return Task.FromResult<IReadOnlyList<SupplierAccrualDto>>([]);
        }

        public Task<FinancePagedResult<SupplierAccrualDto>> GetSupplierAccrualsPageAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
        {
            LastSupplierAccrualListRequest = request;
            return Task.FromResult(new FinancePagedResult<SupplierAccrualDto>([], 0, request.Offset ?? 0, request.Limit ?? 50));
        }

        public Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
        {
            LastMeterReadingListRequest = request;
            return Task.FromResult<IReadOnlyList<MeterReadingDto>>([]);
        }

        public Task<FinancePagedResult<MeterReadingDto>> GetMeterReadingsPageAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
        {
            LastMeterReadingListRequest = request;
            return Task.FromResult(new FinancePagedResult<MeterReadingDto>([], 0, request.Offset ?? 0, request.Limit ?? 50));
        }

        public Task<IReadOnlyList<MissingMeterReadingDto>> GetMissingMeterReadingsAsync(MissingMeterReadingListRequest request, CancellationToken cancellationToken)
        {
            LastMissingMeterReadingListRequest = request;
            return Task.FromResult<IReadOnlyList<MissingMeterReadingDto>>([]);
        }

        public Task<FinanceResult<GarageBalanceHistoryDto>> GetGarageBalanceHistoryAsync(Guid garageId, GarageBalanceHistoryRequest request, CancellationToken cancellationToken)
        {
            LastGarageBalanceHistoryGarageId = garageId;
            LastGarageBalanceHistoryRequest = request;
            return Task.FromResult(GarageBalanceHistoryResult);
        }

        public Task<FinanceResult<GarageIncomeWorksheetDto>> GetGarageIncomeWorksheetAsync(Guid garageId, GarageIncomeWorksheetRequest request, CancellationToken cancellationToken)
        {
            LastGarageIncomeWorksheetGarageId = garageId;
            LastGarageIncomeWorksheetRequest = request;
            return Task.FromResult(GarageIncomeWorksheetResult);
        }

        public Task<FinanceResult<ExpenseWorksheetDto>> GetExpenseWorksheetAsync(ExpenseWorksheetRequest request, CancellationToken cancellationToken)
        {
            LastExpenseWorksheetRequest = request;
            return Task.FromResult(ExpenseWorksheetResult);
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

        public Task<FinanceResult<FinancialOperationDto>> CreateGarageDebtPaymentAsync(CreateGarageDebtPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastGarageDebtPaymentRequest = request;
            return Task.FromResult(CreateGarageDebtPaymentResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> UpdateIncomeAsync(Guid operationId, CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateIncomeResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateExpenseResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> CreateStaffPaymentAsync(CreateStaffPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastStaffPaymentRequest = request;
            return Task.FromResult(CreateStaffPaymentResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> UpdateExpenseAsync(Guid operationId, CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateExpenseResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> CancelOperationAsync(Guid operationId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledOperationId = operationId;
            LastCancelRequest = request;
            return Task.FromResult(CancelOperationResult);
        }

        public Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateAccrualResult);
        }

        public Task<FinanceResult<AccrualDto>> CreateDebtTransferAsync(CreateDebtTransferRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastDebtTransferRequest = request;
            return Task.FromResult(CreateDebtTransferResult);
        }

        public Task<FinanceResult<AccrualDto>> UpdateAccrualAsync(Guid accrualId, CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateAccrualResult);
        }

        public Task<FinanceResult<AccrualDto>> CancelAccrualAsync(Guid accrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledAccrualId = accrualId;
            LastCancelRequest = request;
            return Task.FromResult(CancelAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> CreateSupplierAccrualAsync(CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateSupplierAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> UpdateSupplierAccrualAsync(Guid supplierAccrualId, CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateSupplierAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> CancelSupplierAccrualAsync(Guid supplierAccrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledSupplierAccrualId = supplierAccrualId;
            LastCancelRequest = request;
            return Task.FromResult(CancelSupplierAccrualResult);
        }

        public Task<FinanceResult<RegularAccrualGenerationResultDto>> GenerateRegularAccrualsAsync(GenerateRegularAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(GenerateRegularAccrualsResult);
        }

        public Task<FinanceResult<RegularCatalogAccrualGenerationResultDto>> GenerateRegularCatalogAccrualsAsync(GenerateRegularCatalogAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(GenerateRegularCatalogAccrualsResult);
        }

        public Task<FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>> GenerateSupplierGroupSalaryAccrualsAsync(GenerateSupplierGroupSalaryAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(GenerateSupplierGroupSalaryAccrualsResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingAsync(Guid meterReadingId, CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(UpdateMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CancelMeterReadingAsync(Guid meterReadingId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledMeterReadingId = meterReadingId;
            LastCancelRequest = request;
            return Task.FromResult(CancelMeterReadingResult);
        }
    }
}
