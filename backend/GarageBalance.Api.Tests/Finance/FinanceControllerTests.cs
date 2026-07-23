using System.Security.Claims;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinanceControllerTests
{
    [Fact]
    public async Task GetFinancialReportPeriod_ReturnsValueAndPassesTargetToService()
    {
        var supplierId = Guid.NewGuid();
        var dto = new FinancialReportPeriodDto(new DateOnly(2024, 3, 1), new DateOnly(2026, 7, 1));
        var service = new FakeFinanceService
        {
            FinancialReportPeriodResult = FinanceResult<FinancialReportPeriodDto>.Success(dto)
        };

        var result = await CreateController(service).GetFinancialReportPeriod(null, supplierId, null, CancellationToken.None);

        Assert.Same(dto, Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(supplierId, service.LastFinancialReportPeriodRequest?.SupplierId);
    }

    [Fact]
    public async Task GetFinancialReportPeriod_MapsInvalidAndMissingTargets()
    {
        var badRequest = await CreateController(new FakeFinanceService
        {
            FinancialReportPeriodResult = FinanceResult<FinancialReportPeriodDto>.Failure("financial_report_target_invalid", "Неверная цель.")
        }).GetFinancialReportPeriod(null, null, null, CancellationToken.None);
        var notFound = await CreateController(new FakeFinanceService
        {
            FinancialReportPeriodResult = FinanceResult<FinancialReportPeriodDto>.Failure("financial_report_target_not_found", "Не найдено.")
        }).GetFinancialReportPeriod(Guid.NewGuid(), null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(badRequest.Result);
        Assert.IsType<NotFoundObjectResult>(notFound.Result);
    }

    [Fact]
    public async Task GetSupplierOpeningBalance_ReturnsValueAndPassesPeriodToService()
    {
        var supplierId = Guid.NewGuid();
        var monthFrom = new DateOnly(2026, 6, 1);
        var dto = new SupplierOpeningBalanceDto(supplierId, monthFrom, 250m, 500m, 100m, 650m);
        var service = new FakeFinanceService
        {
            SupplierOpeningBalanceResult = FinanceResult<SupplierOpeningBalanceDto>.Success(dto)
        };

        var result = await CreateController(service).GetSupplierOpeningBalance(supplierId, monthFrom, CancellationToken.None);

        Assert.Same(dto, Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(supplierId, service.LastSupplierOpeningBalanceSupplierId);
        Assert.Equal(monthFrom, service.LastSupplierOpeningBalanceRequest?.MonthFrom);
    }

    [Fact]
    public async Task GetSupplierOpeningBalance_ReturnsNotFoundForMissingSupplier()
    {
        var controller = CreateController(new FakeFinanceService
        {
            SupplierOpeningBalanceResult = FinanceResult<SupplierOpeningBalanceDto>.Failure("supplier_not_found", "Не найден.")
        });

        var result = await controller.GetSupplierOpeningBalance(Guid.NewGuid(), null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("supplier_not_found", Assert.IsType<ProblemDetails>(notFound.Value).Extensions["code"]);
    }

    [Fact]
    public async Task ListEndpoints_PassLimitToService()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        await controller.GetOperations(null, null, "income", "12", 50, null, null, null, CancellationToken.None);
        await controller.GetAccruals(null, null, "12", 51, CancellationToken.None);
        await controller.GetSupplierAccruals(null, null, "water", 52, null, CancellationToken.None);
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
    public async Task ListEndpoints_PassCounterpartyFiltersToService()
    {
        var supplierId = Guid.NewGuid();
        var staffMemberId = Guid.NewGuid();
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        await controller.GetOperationsPage(null, null, "expense", null, 0, 25, null, supplierId, staffMemberId, CancellationToken.None);
        await controller.GetSupplierAccrualsPage(null, null, null, 0, 25, supplierId, CancellationToken.None);

        Assert.Equal(supplierId, service.LastFinancialOperationListRequest?.SupplierId);
        Assert.Equal(staffMemberId, service.LastFinancialOperationListRequest?.StaffMemberId);
        Assert.Equal(supplierId, service.LastSupplierAccrualListRequest?.SupplierId);
    }

    [Fact]
    public async Task PagedListEndpoints_PassOffsetAndLimitToService()
    {
        var service = new FakeFinanceService();
        var controller = CreateController(service);

        await controller.GetOperationsPage(null, null, "income", "12", 10, 25, null, null, null, CancellationToken.None);
        Assert.Equal(10, service.LastFinancialOperationListRequest?.Offset);
        Assert.Equal(25, service.LastFinancialOperationListRequest?.Limit);

        await controller.GetAccrualsPage(null, null, "12", 20, 30, CancellationToken.None);
        Assert.Equal(20, service.LastAccrualListRequest?.Offset);
        Assert.Equal(30, service.LastAccrualListRequest?.Limit);

        await controller.GetAccrualDueDateReviewPage(21, 31, CancellationToken.None);
        Assert.Equal((21, 31), service.LastAccrualDueDateReviewRequest);

        await controller.GetSupplierAccrualsPage(null, null, "water", 40, 50, null, CancellationToken.None);
        Assert.Equal(40, service.LastSupplierAccrualListRequest?.Offset);
        Assert.Equal(50, service.LastSupplierAccrualListRequest?.Limit);

        await controller.GetMeterReadingsPage(null, null, "electricity", "12", 60, 70, CancellationToken.None);
        Assert.Equal(60, service.LastMeterReadingListRequest?.Offset);
        Assert.Equal(70, service.LastMeterReadingListRequest?.Limit);
    }

    [Fact]
    public async Task GetSummary_ReturnsSectionCountsAndPassesFiltersToService()
    {
        var summary = new FinanceSummaryDto(1500m, 400m, 2000m, 1100m, 500m, 2, 1, 3)
        {
            IncomeCount = 1,
            ExpenseCount = 1,
            SupplierAccrualCount = 4
        };
        var service = new FakeFinanceService { SummaryResult = summary };
        var controller = CreateController(service);

        var result = await controller.GetSummary(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "income",
            "гараж 12",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(summary, ok.Value);
        Assert.Equal(1, Assert.IsType<FinanceSummaryDto>(ok.Value).IncomeCount);
        Assert.Equal(4, Assert.IsType<FinanceSummaryDto>(ok.Value).SupplierAccrualCount);
        Assert.Equal(new DateOnly(2026, 6, 1), service.LastSummaryRequest?.DateFrom);
        Assert.Equal(new DateOnly(2026, 6, 30), service.LastSummaryRequest?.DateTo);
        Assert.Equal("income", service.LastSummaryRequest?.OperationKind);
        Assert.Equal("гараж 12", service.LastSummaryRequest?.Search);
    }

    [Fact]
    public async Task GetIncomePaymentWarning_ReturnsPreviewAndPassesRequestToService()
    {
        var request = new IncomePaymentWarningRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            Guid.NewGuid());
        var service = new FakeFinanceService
        {
            IncomePaymentWarningResult = FinanceResult<IncomePaymentWarningDto>.Success(
                new IncomePaymentWarningDto(true, new DateOnly(2026, 6, 1), 29, true))
        };
        var controller = CreateController(service);

        var result = await controller.GetIncomePaymentWarning(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var warning = Assert.IsType<IncomePaymentWarningDto>(ok.Value);
        Assert.True(warning.RequiresConfirmation);
        Assert.Equal(29, warning.DaysSincePreviousPayment);
        Assert.Same(request, service.LastIncomePaymentWarningRequest);
    }

    [Fact]
    public async Task GetIncomePaymentWarning_ReturnsNotFoundForMissingGarage()
    {
        var controller = CreateController(new FakeFinanceService
        {
            IncomePaymentWarningResult = FinanceResult<IncomePaymentWarningDto>.Failure(
                "garage_not_found",
                "Гараж для поступления не найден.")
        });

        var result = await controller.GetIncomePaymentWarning(
            new IncomePaymentWarningRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("garage_not_found", problem.Title);
    }

    [Fact]
    public async Task GetMeterReadingYearPage_PassesCompactYearRequestToService()
    {
        var page = new MeterReadingYearPageDto([], [], 0, 25, 50);
        var service = new FakeFinanceService
        {
            MeterReadingYearPageResult = FinanceResult<MeterReadingYearPageDto>.Success(page)
        };
        var controller = CreateController(service);

        var result = await controller.GetMeterReadingYearPage(2026, "electricity", 25, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(page, ok.Value);
        Assert.Equal(new MeterReadingYearRequest(2026, "electricity", 50, 25), service.LastMeterReadingYearRequest);
    }

    [Fact]
    public async Task GetMeterReadingYearPage_ReturnsBadRequestForInvalidFilter()
    {
        var service = new FakeFinanceService
        {
            MeterReadingYearPageResult = FinanceResult<MeterReadingYearPageDto>.Failure("meter_kind_invalid", "Некорректный тип счетчика.")
        };
        var controller = CreateController(service);

        var result = await controller.GetMeterReadingYearPage(2026, "gas", 0, 25, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
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
    public async Task GetGarageBalanceHistory_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = CreateController(new FakeFinanceService
        {
            GarageBalanceHistoryResult = FinanceResult<GarageBalanceHistoryDto>.Failure("balance_history_period_invalid", "Дата начала истории баланса не может быть позже даты окончания.")
        });

        var result = await controller.GetGarageBalanceHistory(Guid.NewGuid(), new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("balance_history_period_invalid", problem.Title);
    }

    [Fact]
    public async Task GetGarageOverdueDebt_ReturnsBreakdownAndPassesGarageToService()
    {
        var garageId = Guid.NewGuid();
        var breakdown = new GarageOverdueDebtDto(
            garageId,
            "12",
            "Иванов Иван",
            new DateOnly(2026, 7, 17),
            300m,
            [new GarageOverdueDebtRowDto("accrual", Guid.NewGuid(), "Вода", new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 11), 500m, 200m, 300m)]);
        var service = new FakeFinanceService
        {
            GarageOverdueDebtResult = FinanceResult<GarageOverdueDebtDto>.Success(breakdown)
        };
        var controller = CreateController(service);

        var result = await controller.GetGarageOverdueDebt(garageId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(breakdown, ok.Value);
        Assert.Equal(garageId, service.LastGarageOverdueDebtGarageId);
    }

    [Fact]
    public async Task GetGarageOverdueDebt_ReturnsNotFoundForMissingGarage()
    {
        var service = new FakeFinanceService
        {
            GarageOverdueDebtResult = FinanceResult<GarageOverdueDebtDto>.Failure("garage_not_found", "Гараж не найден.")
        };
        var controller = CreateController(service);

        var result = await controller.GetGarageOverdueDebt(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
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
            250m,
            1000m,
            400m,
            0m,
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
    public async Task GetGarageIncomeWorksheet_ReturnsNotFoundForMissingGarage()
    {
        var controller = CreateController(new FakeFinanceService
        {
            GarageIncomeWorksheetResult = FinanceResult<GarageIncomeWorksheetDto>.Failure("garage_not_found", "Гараж не найден.")
        });

        var result = await controller.GetGarageIncomeWorksheet(Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("garage_not_found", problem.Title);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheet_ReturnsBadRequestForInvalidPeriod()
    {
        var controller = CreateController(new FakeFinanceService
        {
            GarageIncomeWorksheetResult = FinanceResult<GarageIncomeWorksheetDto>.Failure("income_worksheet_period_invalid", "Дата начала формы поступлений не может быть позже даты окончания.")
        });

        var result = await controller.GetGarageIncomeWorksheet(Guid.NewGuid(), new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("income_worksheet_period_invalid", problem.Title);
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
    public async Task CreateGarageDebtPayment_ReturnsNotFoundForMissingGarage()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateGarageDebtPaymentResult = FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж не найден.")
        });

        var result = await controller.CreateGarageDebtPayment(
            new CreateGarageDebtPaymentRequest(Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 900m, "Оплата долга"),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("garage_not_found", problem.Title);
    }

    [Fact]
    public async Task CreateGarageDebtPayment_ReturnsConflictForDuplicateOperation()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateGarageDebtPaymentResult = FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция уже внесена.")
        });

        var result = await controller.CreateGarageDebtPayment(
            new CreateGarageDebtPaymentRequest(Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 900m, "Оплата долга"),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("operation_duplicate", problem.Title);
    }

    [Fact]
    public async Task UpdateIncome_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            UpdateIncomeResult = FinanceResult<FinancialOperationDto>.Success(CreateOperation("income", operationId, amount: 350m))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateIncome(
            operationId,
            new CreateIncomeOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 350m, "PAY-20", "Исправили сумму"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FinancialOperationDto>(ok.Value);
        Assert.Equal(operationId, dto.Id);
        Assert.Equal("income", dto.OperationKind);
        Assert.Equal(350m, dto.Amount);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operationId, service.LastUpdatedOperationId);
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
    public async Task CreateExpense_ReturnsConflictWhenBankAmountIsInsufficient()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateExpenseResult = FinanceResult<FinancialOperationDto>.Failure("bank_amount_insufficient", "Недостаточно денег на банковском счете.")
        });

        var result = await controller.CreateExpense(
            new CreateExpenseOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, "1", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("bank_amount_insufficient", problem.Title);
    }

    [Fact]
    public async Task CreateExpense_ReturnsConflictWhenCashAmountIsInsufficient()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateExpenseResult = FinanceResult<FinancialOperationDto>.Failure("cash_amount_insufficient", "Недостаточно денег в кассе.")
        });

        var result = await controller.CreateExpense(
            new CreateExpenseOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, "1", null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("cash_amount_insufficient", problem.Title);
    }

    [Fact]
    public async Task UpdateExpense_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            UpdateExpenseResult = FinanceResult<FinancialOperationDto>.Success(CreateOperation("expense", operationId, amount: 700m))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateExpense(
            operationId,
            new CreateExpenseOperationRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 700m, "EXP-21", "Исправили выплату"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<FinancialOperationDto>(ok.Value);
        Assert.Equal(operationId, dto.Id);
        Assert.Equal("expense", dto.OperationKind);
        Assert.Equal(700m, dto.Amount);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(operationId, service.LastUpdatedOperationId);
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
    public async Task RestoreOperation_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var operation = CreateOperation("income") with { IsCanceled = false };
        var service = new FakeFinanceService
        {
            RestoreOperationResult = FinanceResult<FinancialOperationDto>.Success(operation)
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
        var controller = CreateController(new FakeFinanceService
        {
            RestoreOperationResult = FinanceResult<FinancialOperationDto>.Failure("operation_not_canceled", "Операция уже активна.")
        });

        var result = await controller.RestoreOperation(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("operation_not_canceled", problem.Title);
    }

    [Fact]
    public async Task RestoreOperation_ReturnsNotFoundForMissingOperation()
    {
        var controller = CreateController(new FakeFinanceService
        {
            RestoreOperationResult = FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Операция не найдена.")
        });

        var result = await controller.RestoreOperation(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("operation_not_found", problem.Title);
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
    public async Task CreateIrregularAccrual_PassesActorAndRequestToService()
    {
        var actorUserId = Guid.NewGuid();
        var request = new CreateIrregularAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1), "Новая карта");
        var service = new FakeFinanceService
        {
            CreateIrregularAccrualResult = FinanceResult<AccrualDto>.Success(CreateAccrual())
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateIrregularAccrual(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastIrregularAccrualRequest);
    }

    [Fact]
    public async Task CreateIrregularAccrual_ReturnsNotFoundForUnavailableTemplate()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateIrregularAccrualResult = FinanceResult<AccrualDto>.Failure("irregular_payment_not_found", "Платёж не найден.")
        });

        var result = await controller.CreateIrregularAccrual(
            new CreateIrregularAccrualRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1), null),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("irregular_payment_not_found", Assert.IsType<ProblemDetails>(notFound.Value).Title);
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
    public async Task UpdateAccrual_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateAccrual(amount: 250m);
        var service = new FakeFinanceService
        {
            UpdateAccrualResult = FinanceResult<AccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateAccrual(
            accrual.Id,
            new CreateAccrualRequest(accrual.GarageId, accrual.IncomeTypeId, accrual.AccountingMonth, accrual.Amount, accrual.Source, "Recalculation"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastUpdatedAccrualId);
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
    public async Task RestoreAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateAccrual(isCanceled: false);
        var service = new FakeFinanceService
        {
            RestoreAccrualResult = FinanceResult<AccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreAccrual(accrual.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastRestoredAccrualId);
    }

    [Fact]
    public async Task RestoreAccrual_ReturnsConflictForActiveAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            RestoreAccrualResult = FinanceResult<AccrualDto>.Failure("accrual_not_canceled", "Начисление уже активно.")
        });

        var result = await controller.RestoreAccrual(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("accrual_not_canceled", problem.Title);
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
    public async Task UpdateSupplierAccrual_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateSupplierAccrual(amount: 320m);
        var service = new FakeFinanceService
        {
            UpdateSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateSupplierAccrual(
            accrual.Id,
            new CreateSupplierAccrualRequest(accrual.SupplierId, accrual.ExpenseTypeId, accrual.AccountingMonth, accrual.Amount, accrual.Source, accrual.DocumentNumber, "Invoice recalculation"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastUpdatedSupplierAccrualId);
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
    public async Task RestoreSupplierAccrual_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var accrual = CreateSupplierAccrual(isCanceled: false);
        var service = new FakeFinanceService
        {
            RestoreSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Success(accrual)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreSupplierAccrual(accrual.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(accrual, ok.Value);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(accrual.Id, service.LastRestoredSupplierAccrualId);
    }

    [Fact]
    public async Task RestoreSupplierAccrual_ReturnsConflictForActiveAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            RestoreSupplierAccrualResult = FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_canceled", "Начисление поставщику уже активно.")
        });

        var result = await controller.RestoreSupplierAccrual(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("supplier_accrual_not_canceled", problem.Title);
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
        var request = new GenerateRegularAccrualsRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), null);

        var result = await controller.GenerateRegularAccruals(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastRegularAccrualGenerationRequest);
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
        var request = new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 6, 1), "Каталог");

        var result = await controller.GenerateRegularCatalogAccruals(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastRegularCatalogAccrualGenerationRequest);
    }

    [Fact]
    public async Task GenerateFeeCampaignAccruals_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            GenerateFeeCampaignAccrualsResult = FinanceResult<FeeCampaignAccrualGenerationResultDto>.Success(CreateFeeCampaignGenerationResult())
        };
        var controller = CreateController(service, actorUserId);
        var request = new GenerateFeeCampaignAccrualsRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), "Сбор");

        var result = await controller.GenerateFeeCampaignAccruals(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastFeeCampaignAccrualGenerationRequest);
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
        var request = new GenerateSupplierGroupSalaryAccrualsRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), 7000m, "PAY-06", null);

        var result = await controller.GenerateSupplierGroupSalaryAccruals(request, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Same(request, service.LastSupplierGroupSalaryAccrualGenerationRequest);
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
    public async Task CreateMeterReading_ReturnsBadRequestForFutureMonth()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CreateMeterReadingResult = FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_future_month_not_allowed",
                "Показание будущего учетного месяца вводить нельзя.")
        });

        var result = await controller.CreateMeterReading(
            new CreateMeterReadingRequest(Guid.NewGuid(), "water", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 10m, null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("meter_reading_future_month_not_allowed", problem.Title);
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
    public async Task UpdateMeterReading_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var meterReadingId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            UpdateMeterReadingResult = FinanceResult<MeterReadingDto>.Success(CreateMeterReading(id: meterReadingId, currentValue: 18m, consumption: 3m))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.UpdateMeterReading(
            meterReadingId,
            new CreateMeterReadingRequest(Guid.NewGuid(), "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 18m, "Исправили после сверки"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MeterReadingDto>(ok.Value);
        Assert.Equal(meterReadingId, dto.Id);
        Assert.Equal(18m, dto.CurrentValue);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(meterReadingId, service.LastUpdatedMeterReadingId);
    }

    [Fact]
    public async Task CorrectHistoricalMeterReading_ReturnsOkAndPassesReasonAndActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var meterReadingId = Guid.NewGuid();
        var request = new CorrectHistoricalMeterReadingRequest(
            new DateOnly(2026, 6, 21),
            18m,
            "После сверки",
            "Сверка с бумажным журналом",
            Guid.NewGuid());
        var service = new FakeFinanceService
        {
            CorrectHistoricalMeterReadingResult = FinanceResult<MeterReadingDto>.Success(
                CreateMeterReading(id: meterReadingId, currentValue: 18m, consumption: 8m))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CorrectHistoricalMeterReading(meterReadingId, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(meterReadingId, service.LastCorrectedHistoricalMeterReadingId);
        Assert.Equal(request, service.LastHistoricalMeterReadingCorrectionRequest);
    }

    [Fact]
    public async Task CorrectHistoricalMeterReading_ReturnsConflictForCurrentMonth()
    {
        var controller = CreateController(new FakeFinanceService
        {
            CorrectHistoricalMeterReadingResult = FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_historical_month_required",
                "Корректировка доступна только для прошлого месяца.")
        });

        var result = await controller.CorrectHistoricalMeterReading(
            Guid.NewGuid(),
            new CorrectHistoricalMeterReadingRequest(
                new DateOnly(2026, 7, 17),
                18m,
                null,
                "Причина",
                Guid.NewGuid()),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("meter_reading_historical_month_required", problem.Title);
    }

    [Fact]
    public async Task SavePaymentFormMeterReading_ReturnsOkAndPassesVersionAndActorToService()
    {
        var actorUserId = Guid.NewGuid();
        var meterReadingId = Guid.NewGuid();
        var version = Guid.NewGuid();
        var request = new SavePaymentFormMeterReadingRequest(
            Guid.NewGuid(),
            "water",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 20),
            18m,
            null,
            meterReadingId,
            version);
        var service = new FakeFinanceService
        {
            SavePaymentFormMeterReadingResult = FinanceResult<MeterReadingDto>.Success(
                CreateMeterReading(id: meterReadingId, currentValue: 18m, consumption: 8m))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.SavePaymentFormMeterReading(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(request, service.LastSavePaymentFormMeterReadingRequest);
    }

    [Fact]
    public async Task SavePaymentFormMeterReading_ReturnsConflictForStaleVersion()
    {
        var controller = CreateController(new FakeFinanceService
        {
            SavePaymentFormMeterReadingResult = FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_conflict",
                "Показание уже изменено другим пользователем.")
        });

        var result = await controller.SavePaymentFormMeterReading(
            new SavePaymentFormMeterReadingRequest(
                Guid.NewGuid(),
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                18m,
                null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("meter_reading_conflict", problem.Title);
    }

    [Fact]
    public async Task SavePaymentFormMeterReading_ReturnsBadRequestWhenManualValueIsMissing()
    {
        var service = new FakeFinanceService
        {
            SavePaymentFormMeterReadingResult = FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_value_required",
                "Введите показание счетчика вручную.")
        };
        var controller = CreateController(service);
        var request = new SavePaymentFormMeterReadingRequest(
            Guid.NewGuid(),
            MeterKinds.Water,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 17),
            null,
            null);

        var result = await controller.SavePaymentFormMeterReading(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("meter_reading_value_required", problem.Title);
        Assert.Same(request, service.LastSavePaymentFormMeterReadingRequest);
    }

    [Fact]
    public async Task SavePaymentFormMeterReading_ReturnsConflictForPaidLinkedAccrual()
    {
        var controller = CreateController(new FakeFinanceService
        {
            SavePaymentFormMeterReadingResult = FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_accrual_paid",
                "Связанное начисление уже полностью или частично оплачено.")
        });

        var result = await controller.SavePaymentFormMeterReading(
            new SavePaymentFormMeterReadingRequest(
                Guid.NewGuid(),
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                18m,
                null),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("meter_reading_accrual_paid", problem.Title);
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
    public async Task CancelMeterReading_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var meterReadingId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            CancelMeterReadingResult = FinanceResult<MeterReadingDto>.Success(CreateMeterReading(id: meterReadingId, isCanceled: true))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CancelMeterReading(meterReadingId, new CancelFinanceEntryRequest("Ошибочное показание"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MeterReadingDto>(ok.Value);
        Assert.Equal(meterReadingId, dto.Id);
        Assert.True(dto.IsCanceled);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(meterReadingId, service.LastCanceledMeterReadingId);
        Assert.Equal("Ошибочное показание", service.LastCancelRequest?.Reason);
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

    [Fact]
    public async Task RestoreMeterReading_ReturnsNotFoundForMissingReading()
    {
        var controller = CreateController(new FakeFinanceService
        {
            RestoreMeterReadingResult = FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание не найдено.")
        });

        var result = await controller.RestoreMeterReading(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("meter_reading_not_found", problem.Title);
    }

    [Fact]
    public async Task RestoreMeterReading_ReturnsConflictForActiveReading()
    {
        var controller = CreateController(new FakeFinanceService
        {
            RestoreMeterReadingResult = FinanceResult<MeterReadingDto>.Failure("meter_reading_not_canceled", "Показание уже активно.")
        });

        var result = await controller.RestoreMeterReading(Guid.NewGuid(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("meter_reading_not_canceled", problem.Title);
    }

    [Fact]
    public async Task RestoreMeterReading_ReturnsOkAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var meterReadingId = Guid.NewGuid();
        var service = new FakeFinanceService
        {
            RestoreMeterReadingResult = FinanceResult<MeterReadingDto>.Success(CreateMeterReading(id: meterReadingId, isCanceled: false))
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreMeterReading(meterReadingId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MeterReadingDto>(ok.Value);
        Assert.Equal(meterReadingId, dto.Id);
        Assert.False(dto.IsCanceled);
        Assert.Equal(actorUserId, service.LastActorUserId);
        Assert.Equal(meterReadingId, service.LastRestoredMeterReadingId);
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

    private static FinancialOperationDto CreateOperation(string kind, Guid? id = null, decimal amount = 100m)
    {
        return new FinancialOperationDto(
            id ?? Guid.NewGuid(),
            kind,
            new DateOnly(2026, 6, 19),
            new DateOnly(2026, 6, 1),
            amount,
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
            false,
            DateTimeOffset.UnixEpoch);
    }

    private static AccrualDto CreateAccrual(Guid? id = null, decimal amount = 100m, bool isCanceled = false)
    {
        return new AccrualDto(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "12",
            "Иванов Иван",
            Guid.NewGuid(),
            "Членский взнос",
            new DateOnly(2026, 6, 1),
            2026,
            amount,
            "regular",
            null,
            isCanceled,
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 8, 31));
    }

    private static MeterReadingDto CreateMeterReading(Guid? id = null, decimal currentValue = 15m, decimal previousValue = 10m, decimal consumption = 5m, bool isCanceled = false)
    {
        return new MeterReadingDto(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "12",
            "Иванов Иван",
            "water",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 20),
            currentValue,
            previousValue,
            consumption,
            false,
            null,
            isCanceled,
            Guid.NewGuid());
    }

    private static SupplierAccrualDto CreateSupplierAccrual(Guid? id = null, decimal amount = 100m, bool isCanceled = false)
    {
        return new SupplierAccrualDto(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Водоканал",
            Guid.NewGuid(),
            "Вода",
            new DateOnly(2026, 6, 1),
            amount,
            "regular",
            "INV-1",
            null,
            isCanceled);
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

    private static FeeCampaignAccrualGenerationResultDto CreateFeeCampaignGenerationResult()
    {
        var accrual = CreateAccrual();
        return new FeeCampaignAccrualGenerationResultDto(
            new DateOnly(2026, 6, 1),
            Guid.NewGuid(),
            "Сбор на ворота",
            accrual.IncomeTypeId,
            accrual.IncomeTypeName,
            accrual.Amount,
            1,
            0,
            accrual.Amount,
            [accrual],
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
        public Guid? LastRestoredOperationId { get; private set; }
        public Guid? LastUpdatedOperationId { get; private set; }
        public Guid? LastUpdatedAccrualId { get; private set; }
        public Guid? LastCanceledAccrualId { get; private set; }
        public Guid? LastRestoredAccrualId { get; private set; }
        public Guid? LastUpdatedSupplierAccrualId { get; private set; }
        public Guid? LastCanceledSupplierAccrualId { get; private set; }
        public Guid? LastRestoredSupplierAccrualId { get; private set; }
        public Guid? LastUpdatedMeterReadingId { get; private set; }
        public Guid? LastCorrectedHistoricalMeterReadingId { get; private set; }
        public Guid? LastCanceledMeterReadingId { get; private set; }
        public Guid? LastRestoredMeterReadingId { get; private set; }
        public SavePaymentFormMeterReadingRequest? LastSavePaymentFormMeterReadingRequest { get; private set; }
        public CorrectHistoricalMeterReadingRequest? LastHistoricalMeterReadingCorrectionRequest { get; private set; }
        public Guid? LastGarageBalanceHistoryGarageId { get; private set; }
        public Guid? LastGarageOverdueDebtGarageId { get; private set; }
        public Guid? LastGarageIncomeWorksheetGarageId { get; private set; }
        public Guid? LastSupplierOpeningBalanceSupplierId { get; private set; }
        public CancelFinanceEntryRequest? LastCancelRequest { get; private set; }
        public FinancialOperationListRequest? LastFinancialOperationListRequest { get; private set; }
        public CreateStaffPaymentRequest? LastStaffPaymentRequest { get; private set; }
        public CreateDebtTransferRequest? LastDebtTransferRequest { get; private set; }
        public AccrualListRequest? LastAccrualListRequest { get; private set; }
        public (int? Offset, int? Limit)? LastAccrualDueDateReviewRequest { get; private set; }
        public SupplierAccrualListRequest? LastSupplierAccrualListRequest { get; private set; }
        public MeterReadingListRequest? LastMeterReadingListRequest { get; private set; }
        public MeterReadingYearRequest? LastMeterReadingYearRequest { get; private set; }
        public MissingMeterReadingListRequest? LastMissingMeterReadingListRequest { get; private set; }
        public GarageBalanceHistoryRequest? LastGarageBalanceHistoryRequest { get; private set; }
        public GarageIncomeWorksheetRequest? LastGarageIncomeWorksheetRequest { get; private set; }
        public ExpenseWorksheetRequest? LastExpenseWorksheetRequest { get; private set; }
        public SupplierOpeningBalanceRequest? LastSupplierOpeningBalanceRequest { get; private set; }
        public FinancialReportPeriodRequest? LastFinancialReportPeriodRequest { get; private set; }
        public FinancialOperationListRequest? LastSummaryRequest { get; private set; }
        public CreateGarageDebtPaymentRequest? LastGarageDebtPaymentRequest { get; private set; }
        public IncomePaymentWarningRequest? LastIncomePaymentWarningRequest { get; private set; }
        public GenerateRegularAccrualsRequest? LastRegularAccrualGenerationRequest { get; private set; }
        public GenerateRegularCatalogAccrualsRequest? LastRegularCatalogAccrualGenerationRequest { get; private set; }
        public GenerateFeeCampaignAccrualsRequest? LastFeeCampaignAccrualGenerationRequest { get; private set; }
        public GenerateSupplierGroupSalaryAccrualsRequest? LastSupplierGroupSalaryAccrualGenerationRequest { get; private set; }
        public CreateIrregularAccrualRequest? LastIrregularAccrualRequest { get; private set; }
        public FinanceResult<GarageBalanceHistoryDto> GarageBalanceHistoryResult { get; init; } = FinanceResult<GarageBalanceHistoryDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<GarageOverdueDebtDto> GarageOverdueDebtResult { get; init; } = FinanceResult<GarageOverdueDebtDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<GarageIncomeWorksheetDto> GarageIncomeWorksheetResult { get; init; } = FinanceResult<GarageIncomeWorksheetDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<ExpenseWorksheetDto> ExpenseWorksheetResult { get; init; } = FinanceResult<ExpenseWorksheetDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierOpeningBalanceDto> SupplierOpeningBalanceResult { get; init; } = FinanceResult<SupplierOpeningBalanceDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialReportPeriodDto> FinancialReportPeriodResult { get; init; } = FinanceResult<FinancialReportPeriodDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateIncomeResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<IncomePaymentWarningDto> IncomePaymentWarningResult { get; init; } = FinanceResult<IncomePaymentWarningDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateGarageDebtPaymentResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> UpdateIncomeResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateExpenseResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CreateStaffPaymentResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> UpdateExpenseResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> CancelOperationResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FinancialOperationDto> RestoreOperationResult { get; init; } = FinanceResult<FinancialOperationDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateIrregularAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CreateDebtTransferResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> UpdateAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> CancelAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<AccrualDto> RestoreAccrualResult { get; init; } = FinanceResult<AccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> CreateSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> UpdateSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> CancelSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierAccrualDto> RestoreSupplierAccrualResult { get; init; } = FinanceResult<SupplierAccrualDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<RegularAccrualGenerationResultDto> GenerateRegularAccrualsResult { get; init; } = FinanceResult<RegularAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<RegularCatalogAccrualGenerationResultDto> GenerateRegularCatalogAccrualsResult { get; init; } = FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<FeeCampaignAccrualGenerationResultDto> GenerateFeeCampaignAccrualsResult { get; init; } = FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto> GenerateSupplierGroupSalaryAccrualsResult { get; init; } = FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CreateMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> SavePaymentFormMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> UpdateMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CorrectHistoricalMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> CancelMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingDto> RestoreMeterReadingResult { get; init; } = FinanceResult<MeterReadingDto>.Failure("not_configured", "Not configured.");
        public FinanceResult<MeterReadingYearPageDto> MeterReadingYearPageResult { get; init; } = FinanceResult<MeterReadingYearPageDto>.Failure("not_configured", "Not configured.");
        public FinanceSummaryDto SummaryResult { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0);

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

        public Task<FinancePagedResult<AccrualDueDateReviewDto>> GetAccrualDueDateReviewPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
        {
            LastAccrualDueDateReviewRequest = (offset, limit);
            return Task.FromResult(new FinancePagedResult<AccrualDueDateReviewDto>([], 0, offset ?? 0, limit ?? 50));
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

        public Task<FinanceResult<MeterReadingYearPageDto>> GetMeterReadingYearPageAsync(MeterReadingYearRequest request, CancellationToken cancellationToken)
        {
            LastMeterReadingYearRequest = request;
            return Task.FromResult(MeterReadingYearPageResult);
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

        public Task<FinanceResult<GarageOverdueDebtDto>> GetGarageOverdueDebtAsync(Guid garageId, CancellationToken cancellationToken)
        {
            LastGarageOverdueDebtGarageId = garageId;
            return Task.FromResult(GarageOverdueDebtResult);
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

        public Task<FinanceResult<SupplierOpeningBalanceDto>> GetSupplierOpeningBalanceAsync(Guid supplierId, SupplierOpeningBalanceRequest request, CancellationToken cancellationToken)
        {
            LastSupplierOpeningBalanceSupplierId = supplierId;
            LastSupplierOpeningBalanceRequest = request;
            return Task.FromResult(SupplierOpeningBalanceResult);
        }

        public Task<FinanceResult<FinancialReportPeriodDto>> GetFinancialReportPeriodAsync(FinancialReportPeriodRequest request, CancellationToken cancellationToken)
        {
            LastFinancialReportPeriodRequest = request;
            return Task.FromResult(FinancialReportPeriodResult);
        }

        public Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
        {
            LastSummaryRequest = request;
            return Task.FromResult(SummaryResult);
        }

        public Task<FinanceResult<IncomePaymentWarningDto>> GetIncomePaymentWarningAsync(
            IncomePaymentWarningRequest request,
            CancellationToken cancellationToken)
        {
            LastIncomePaymentWarningRequest = request;
            return Task.FromResult(IncomePaymentWarningResult);
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
            LastUpdatedOperationId = operationId;
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
            LastUpdatedOperationId = operationId;
            return Task.FromResult(UpdateExpenseResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> CancelOperationAsync(Guid operationId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledOperationId = operationId;
            LastCancelRequest = request;
            return Task.FromResult(CancelOperationResult);
        }

        public Task<FinanceResult<FinancialOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoredOperationId = operationId;
            return Task.FromResult(RestoreOperationResult);
        }

        public Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateAccrualResult);
        }

        public Task<FinanceResult<AccrualDto>> CreateIrregularAccrualAsync(CreateIrregularAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastIrregularAccrualRequest = request;
            return Task.FromResult(CreateIrregularAccrualResult);
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
            LastUpdatedAccrualId = accrualId;
            return Task.FromResult(UpdateAccrualResult);
        }

        public Task<FinanceResult<AccrualDto>> CancelAccrualAsync(Guid accrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledAccrualId = accrualId;
            LastCancelRequest = request;
            return Task.FromResult(CancelAccrualResult);
        }

        public Task<FinanceResult<AccrualDto>> RestoreAccrualAsync(Guid accrualId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoredAccrualId = accrualId;
            return Task.FromResult(RestoreAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> CreateSupplierAccrualAsync(CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateSupplierAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> UpdateSupplierAccrualAsync(Guid supplierAccrualId, CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastUpdatedSupplierAccrualId = supplierAccrualId;
            return Task.FromResult(UpdateSupplierAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> CancelSupplierAccrualAsync(Guid supplierAccrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledSupplierAccrualId = supplierAccrualId;
            LastCancelRequest = request;
            return Task.FromResult(CancelSupplierAccrualResult);
        }

        public Task<FinanceResult<SupplierAccrualDto>> RestoreSupplierAccrualAsync(Guid supplierAccrualId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoredSupplierAccrualId = supplierAccrualId;
            return Task.FromResult(RestoreSupplierAccrualResult);
        }

        public Task<FinanceResult<RegularAccrualGenerationResultDto>> GenerateRegularAccrualsAsync(GenerateRegularAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRegularAccrualGenerationRequest = request;
            return Task.FromResult(GenerateRegularAccrualsResult);
        }

        public Task<FinanceResult<RegularCatalogAccrualGenerationResultDto>> GenerateRegularCatalogAccrualsAsync(GenerateRegularCatalogAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRegularCatalogAccrualGenerationRequest = request;
            return Task.FromResult(GenerateRegularCatalogAccrualsResult);
        }

        public Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> GenerateFeeCampaignAccrualsAsync(GenerateFeeCampaignAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastFeeCampaignAccrualGenerationRequest = request;
            return Task.FromResult(GenerateFeeCampaignAccrualsResult);
        }

        public Task<FinanceResult<ActiveFeeCampaignAccrualGenerationResultDto>> GenerateActiveFeeCampaignAccrualsAsync(GenerateActiveFeeCampaignAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(FinanceResult<ActiveFeeCampaignAccrualGenerationResultDto>.Success(
                new ActiveFeeCampaignAccrualGenerationResultDto(
                    request.AccountingMonth,
                    0,
                    0,
                    0,
                    0m,
                    [],
                    [],
                    [])));

        public Task<FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>> GenerateSupplierGroupSalaryAccrualsAsync(GenerateSupplierGroupSalaryAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastSupplierGroupSalaryAccrualGenerationRequest = request;
            return Task.FromResult(GenerateSupplierGroupSalaryAccrualsResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> SavePaymentFormMeterReadingAsync(SavePaymentFormMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastSavePaymentFormMeterReadingRequest = request;
            return Task.FromResult(SavePaymentFormMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingAsync(Guid meterReadingId, CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastUpdatedMeterReadingId = meterReadingId;
            return Task.FromResult(UpdateMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CorrectHistoricalMeterReadingAsync(Guid meterReadingId, CorrectHistoricalMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCorrectedHistoricalMeterReadingId = meterReadingId;
            LastHistoricalMeterReadingCorrectionRequest = request;
            return Task.FromResult(CorrectHistoricalMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> CancelMeterReadingAsync(Guid meterReadingId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastCanceledMeterReadingId = meterReadingId;
            LastCancelRequest = request;
            return Task.FromResult(CancelMeterReadingResult);
        }

        public Task<FinanceResult<MeterReadingDto>> RestoreMeterReadingAsync(Guid meterReadingId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoredMeterReadingId = meterReadingId;
            return Task.FromResult(RestoreMeterReadingResult);
        }
    }
}
