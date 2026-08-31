using System.Data.Common;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinanceServiceTests
{
    private const decimal SeededBankAmount = 1000000m;

    [Fact]
    public async Task GetFinancialReportPeriodAsync_ReturnsFullActivePeriodForEachCounterpartyType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия периода" };
        var staffMember = new StaffMember { FullName = "Сотрудник периода", Department = department, Rate = 100m };
        var paidGarageAccrual = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = fixtures.IncomeType,
            AccountingMonth = new DateOnly(2023, 2, 1),
            Amount = 100m,
            Source = AccrualSources.Manual,
            Comment = "Начало обслуживания гаража"
        };
        var garagePayment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            Garage = fixtures.Garage,
            IncomeType = fixtures.IncomeType,
            OperationDate = new DateOnly(2027, 3, 1),
            AccountingMonth = new DateOnly(2027, 3, 1),
            Amount = 100m
        };
        database.Context.AddRange(
            department,
            staffMember,
            paidGarageAccrual,
            new Accrual
            {
                Garage = fixtures.Garage,
                IncomeType = fixtures.IncomeType,
                AccountingMonth = new DateOnly(2024, 5, 1),
                Amount = 250m,
                Source = AccrualSources.Manual,
                Comment = "Первое непогашенное начисление"
            },
            new Accrual
            {
                Garage = fixtures.Garage,
                IncomeType = fixtures.IncomeType,
                AccountingMonth = new DateOnly(2022, 1, 1),
                Amount = 100m,
                Source = AccrualSources.Manual,
                Comment = "Отменённое начисление гаража",
                IsCanceled = true
            },
            garagePayment,
            new AccrualPaymentAllocation
            {
                Accrual = paidGarageAccrual,
                FinancialOperation = garagePayment,
                Amount = 100m
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                Garage = fixtures.Garage,
                IncomeType = fixtures.IncomeType,
                OperationDate = new DateOnly(2021, 3, 1),
                AccountingMonth = new DateOnly(2021, 3, 1),
                Amount = 100m,
                IsCanceled = true
            },
            new SupplierAccrual
            {
                Supplier = fixtures.Supplier,
                ExpenseType = fixtures.ExpenseType,
                AccountingMonth = new DateOnly(2024, 4, 1),
                Amount = 200m,
                Source = AccrualSources.Manual,
                Comment = "Начало обслуживания поставщика"
            },
            new SupplierAccrual
            {
                Supplier = fixtures.Supplier,
                ExpenseType = fixtures.ExpenseType,
                AccountingMonth = new DateOnly(2022, 1, 1),
                Amount = 200m,
                Source = AccrualSources.Manual,
                Comment = "Отменённое начисление",
                IsCanceled = true
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                Supplier = fixtures.Supplier,
                ExpenseType = fixtures.ExpenseType,
                OperationDate = new DateOnly(2026, 5, 1),
                AccountingMonth = new DateOnly(2026, 5, 1),
                Amount = 100m
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                StaffMember = staffMember,
                ExpenseType = fixtures.ExpenseType,
                OperationDate = new DateOnly(2025, 6, 1),
                AccountingMonth = new DateOnly(2025, 6, 1),
                Amount = 100m
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero)));

        var garage = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(fixtures.Garage.Id, null, null), CancellationToken.None);
        var supplier = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(null, fixtures.Supplier.Id, null), CancellationToken.None);
        var staff = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(null, null, staffMember.Id), CancellationToken.None);

        Assert.Equal(new FinancialReportPeriodDto(
            new DateOnly(2023, 2, 1),
            new DateOnly(2027, 3, 1),
            new DateOnly(2024, 5, 1),
            new DateOnly(2026, 7, 1)), garage.Value);
        Assert.Equal(new FinancialReportPeriodDto(new DateOnly(2024, 4, 1), new DateOnly(2026, 7, 1)), supplier.Value);
        Assert.Equal(new FinancialReportPeriodDto(new DateOnly(2025, 6, 1), new DateOnly(2026, 7, 1)), staff.Value);
    }

    [Fact]
    public async Task GetFinancialReportPeriodAsync_UsesCurrentMonthWithoutRowsAndRejectsInvalidOrMissingTarget()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero)));

        var empty = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(null, fixtures.Supplier.Id, null), CancellationToken.None);
        var emptyGarage = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(fixtures.Garage.Id, null, null), CancellationToken.None);
        var invalid = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(fixtures.Garage.Id, fixtures.Supplier.Id, null), CancellationToken.None);
        var missing = await service.GetFinancialReportPeriodAsync(new FinancialReportPeriodRequest(null, Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal(new FinancialReportPeriodDto(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)), empty.Value);
        Assert.Equal(new FinancialReportPeriodDto(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1)), emptyGarage.Value);
        Assert.Equal("financial_report_target_invalid", invalid.ErrorCode);
        Assert.Equal("financial_report_target_not_found", missing.ErrorCode);
    }

    [Fact]
    public async Task GetFinancialReportPeriodAsync_DoesNotOpenGarageAtFutureUnpaidAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.Add(new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = fixtures.IncomeType,
            AccountingMonth = new DateOnly(2027, 2, 1),
            Amount = 100m,
            Source = AccrualSources.Manual,
            Comment = "Будущее непогашенное начисление"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero)));

        var result = await service.GetFinancialReportPeriodAsync(
            new FinancialReportPeriodRequest(fixtures.Garage.Id, null, null),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 1), result.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2027, 2, 1), result.Value.MonthTo);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.DefaultMonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.DefaultMonthTo);
    }

    [Fact]
    public async Task FinancialReportPeriodQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfFinancialReportPeriodQuery(database.Context).GetAsync(
            fixtures.Garage.Id,
            null,
            null,
            cancellationSource.Token));
    }

    [Fact]
    public async Task GetSupplierOpeningBalanceAsync_UsesOnlyActiveHistoryBeforeSelectedPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 250m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var priorAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 5, 1), 500m, "manual", "INV-prior", "Предыдущее начисление"),
            null,
            CancellationToken.None);
        var canceledAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 4, 1), 70m, "manual", "INV-canceled", "Отменённое начисление"),
            null,
            CancellationToken.None);
        Assert.True(priorAccrual.Succeeded);
        Assert.True(canceledAccrual.Succeeded);
        Assert.True((await service.CancelSupplierAccrualAsync(canceledAccrual.Value!.Id, new CancelFinanceEntryRequest("Ошибка"), null, CancellationToken.None)).Succeeded);

        var priorPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 1), 100m, "RKO-prior", null),
            null,
            CancellationToken.None);
        var canceledPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 1), 30m, "RKO-canceled", null),
            null,
            CancellationToken.None);
        Assert.True(priorPayment.Succeeded);
        Assert.True(canceledPayment.Succeeded);
        Assert.True((await service.CancelOperationAsync(canceledPayment.Value!.Id, new CancelFinanceEntryRequest("Ошибка"), null, CancellationToken.None)).Succeeded);

        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1000m, "manual", "INV-current", "Начисление периода"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 600m, "RKO-current", null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetSupplierOpeningBalanceAsync(
            fixtures.Supplier.Id,
            new SupplierOpeningBalanceRequest(new DateOnly(2026, 6, 18)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.MonthFrom);
        Assert.Equal(250m, result.Value.StartingBalance);
        Assert.Equal(500m, result.Value.PriorAccrualTotal);
        Assert.Equal(100m, result.Value.PriorPaymentTotal);
        Assert.Equal(650m, result.Value.OpeningBalance);
    }

    [Fact]
    public async Task GetSupplierOpeningBalanceAsync_ReturnsFailureForMissingSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).GetSupplierOpeningBalanceAsync(
            Guid.NewGuid(),
            new SupplierOpeningBalanceRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_not_found", result.ErrorCode);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    public async Task GetIncomePaymentWarningAsync_UsesCalendarDayBoundary(int daysSincePreviousPayment, bool requiresConfirmation)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Electricity;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var previousPaymentDate = new DateOnly(2026, 6, 1);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                previousPaymentDate,
                new DateOnly(2026, 6, 1),
                500m,
                "PKO-electricity-previous",
                null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                previousPaymentDate.AddDays(daysSincePreviousPayment)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Value!.IsElectricityPayment);
        Assert.Equal(previousPaymentDate, result.Value.PreviousPaymentDate);
        Assert.Equal(daysSincePreviousPayment, result.Value.DaysSincePreviousPayment);
        Assert.Equal(requiresConfirmation, result.Value.RequiresConfirmation);
    }

    [Fact]
    public async Task GetIncomePaymentWarningAsync_ReturnsNoWarningWithoutPreviousElectricityPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Electricity;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Value!.IsElectricityPayment);
        Assert.Null(result.Value.PreviousPaymentDate);
        Assert.Null(result.Value.DaysSincePreviousPayment);
        Assert.False(result.Value.RequiresConfirmation);
    }

    [Fact]
    public async Task GetIncomePaymentWarningAsync_DoesNotApplyToOtherIncomeTypes()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 30)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(result.Value!.IsElectricityPayment);
        Assert.Null(result.Value.PreviousPaymentDate);
        Assert.False(result.Value.RequiresConfirmation);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task GetIncomePaymentWarningAsync_ExcludesEditedCanceledAndFuturePayments()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Electricity;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var first = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), 500m, "PKO-electricity-first", null),
            null,
            CancellationToken.None);
        var edited = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 500m, "PKO-electricity-edited", null),
            null,
            CancellationToken.None);
        var canceled = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1), 500m, "PKO-electricity-canceled", null),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        database.Context.FinancialOperations.Single(operation => operation.Id == canceled.Value!.Id).IsCanceled = true;
        await database.Context.SaveChangesAsync();
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 1), 500m, "PKO-electricity-future", null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 20),
                edited.Value!.Id),
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(edited.Succeeded, edited.ErrorMessage);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.PreviousPaymentDate);
        Assert.Equal(19, result.Value.DaysSincePreviousPayment);
        Assert.True(result.Value.RequiresConfirmation);
    }

    [Fact]
    public async Task GetIncomePaymentWarningAsync_ReturnsMissingDictionaryErrorsAndPropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var missingGarage = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(Guid.NewGuid(), fixtures.IncomeType.Id, new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        var missingIncomeType = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(fixtures.Garage.Id, Guid.NewGuid(), new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Equal("garage_not_found", missingGarage.ErrorCode);
        Assert.Equal("income_type_not_found", missingIncomeType.ErrorCode);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 30)),
            cancellation.Token));
    }

    [Fact]
    public async Task CreateIncomeAsync_CreatesOperationAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var receiptBatchId = Guid.NewGuid();

        var result = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 19),
                new DateOnly(2026, 6, 15),
                1500.50m,
                "PKO-19",
                "Авансовый платеж",
                receiptBatchId),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("income", result.Value!.OperationKind);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.AccountingMonth);
        Assert.Equal("12", result.Value.GarageNumber);
        Assert.Equal("Членский взнос", result.Value.IncomeTypeName);
        Assert.Equal(receiptBatchId, result.Value.ReceiptBatchId);
        Assert.Equal(receiptBatchId, database.Context.FinancialOperations.Single(item => item.Id == result.Value.Id).ReceiptBatchId);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.income_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано поступление 1 500.50", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 19.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("вид Членский взнос", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-19", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Авансовый платеж", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFullGaragePaymentAsync_CreatesOneReceiptBatchAndAssignsFunds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var routedIncomeType = AddOtherIncomeDestination(database.Context);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var receiptBatchId = Guid.NewGuid();

        var request = new CreateFullGaragePaymentRequest(
                fixtures.Garage.Id,
                new DateOnly(2026, 7, 12),
                [
                    new CreateFullGaragePaymentLineRequest(
                        fixtures.IncomeType.Id,
                        new DateOnly(2026, 6, 15),
                        300m,
                        "Членский взнос"),
                    new CreateFullGaragePaymentLineRequest(
                        routedIncomeType.Id,
                        new DateOnly(2026, 7, 1),
                        450m,
                        "Прочие доходы")
                ],
                receiptBatchId);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var result = await service.CreateFullGaragePaymentAsync(
            request,
            actorUserId,
            CancellationToken.None);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var retry = await service.CreateFullGaragePaymentAsync(request, actorUserId, CancellationToken.None);
        var conflictingRetry = await service.CreateFullGaragePaymentAsync(
            request with
            {
                Lines =
                [
                    request.Lines[0],
                    request.Lines[1] with { Amount = 451m }
                ]
            },
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(result.Value!.ReceiptBatchId, retry.Value!.ReceiptBatchId);
        Assert.Equal(result.Value.TotalAmount, retry.Value.TotalAmount);
        Assert.Equal(
            result.Value.Operations.Select(operation => operation.Id).Order(),
            retry.Value.Operations.Select(operation => operation.Id).Order());
        Assert.False(conflictingRetry.Succeeded);
        Assert.Equal("receipt_batch_conflict", conflictingRetry.ErrorCode);
        Assert.Equal(receiptBatchId, result.Value.ReceiptBatchId);
        Assert.Equal(750m, result.Value.TotalAmount);
        Assert.Equal(2, result.Value.Operations.Count);
        Assert.All(result.Value.Operations, operation => Assert.Equal(receiptBatchId, operation.ReceiptBatchId));
        Assert.InRange(result.Value.Operations[0].CreatedAtUtc, startedAtUtc, completedAtUtc);
        Assert.Equal(
            result.Value.Operations[0].CreatedAtUtc.AddTicks(TimeSpan.TicksPerMicrosecond),
            result.Value.Operations[1].CreatedAtUtc);
        Assert.Equal(
            result.Value.Operations[0].GarageDebtAfter,
            result.Value.Operations[1].GarageDebtBefore);
        Assert.Equal(
            result.Value.Operations[0].GarageDebtBefore - result.Value.TotalAmount,
            result.Value.Operations[1].GarageDebtAfter);
        Assert.Equal(2, await database.Context.FinancialOperations.CountAsync(operation => operation.ReceiptBatchId == receiptBatchId));
        var assignment = await database.Context.FundOperations
            .SingleAsync(operation => operation.SourceFinancialOperationId != null);
        Assert.Equal(routedIncomeType.DestinationFundId, assignment.FundId);
        Assert.Equal(450m, assignment.Amount);
        Assert.Equal(450m, routedIncomeType.DestinationFund!.Balance);
        Assert.Equal(2, await database.Context.AuditEvents.CountAsync(item => item.Action == "finance.income_created"));
        var batchAudit = await database.Context.AuditEvents
            .SingleAsync(item => item.Action == "finance.full_garage_payment_created");
        Assert.Equal(actorUserId, batchAudit.ActorUserId);
        Assert.Equal(receiptBatchId.ToString(), batchAudit.RelatedDocumentId);
    }

    [Fact]
    public async Task CreateFullGaragePaymentAsync_CampaignLineReachesTargetClosesSettlesAndRetriesIdempotently()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор из полной оплаты",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = campaign,
            Garage = fixtures.Garage
        });
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var generated = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var quote = await service.GetGarageFullPaymentQuoteAsync(fixtures.Garage.Id, CancellationToken.None);
        var quoteLine = Assert.Single(quote.Value!.Lines, line => line.FeeCampaignId == campaign.Id);
        var receiptBatchId = Guid.NewGuid();
        var request = new CreateFullGaragePaymentRequest(
            fixtures.Garage.Id,
            new DateOnly(2026, 8, 15),
            [new CreateFullGaragePaymentLineRequest(
                quoteLine.IncomeTypeId,
                quoteLine.AccountingMonth,
                quoteLine.OutstandingAmount,
                "Оплата сбора одной квитанцией",
                FeeCampaignId: quoteLine.FeeCampaignId)],
            receiptBatchId);

        var result = await service.CreateFullGaragePaymentAsync(request, null, CancellationToken.None);
        var retry = await service.CreateFullGaragePaymentAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(result.Value!.Operations.Select(item => item.Id), retry.Value!.Operations.Select(item => item.Id));
        Assert.Equal(500m, result.Value.TotalAmount);
        Assert.NotNull(campaign.ClosedAtUtc);
        var operation = Assert.Single(database.Context.FinancialOperations, item => item.ReceiptBatchId == receiptBatchId);
        Assert.Equal(campaign.Id, operation.FeeCampaignId);
        var principal = Assert.Single(database.Context.Accruals, item => item.FeeCampaignId == campaign.Id && !item.IsCanceled);
        Assert.Equal(500m, principal.Amount);
        Assert.Equal(500m, Assert.Single(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.AccrualId == principal.Id).Amount);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        Assert.DoesNotContain(worksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(500m, worksheet.Value.AccrualTotal);
        Assert.Equal(500m, worksheet.Value.IncomeTotal);
        Assert.Equal(0m, worksheet.Value.DebtTotal);
    }

    [Fact]
    public async Task CreateFullGaragePaymentAsync_RejectsClosedCampaignQuoteAndMismatchedIncomeType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор со старой квитанцией",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var generated = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);

        var mismatch = await service.CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(
                fixtures.Garage.Id,
                new DateOnly(2026, 8, 10),
                [new CreateFullGaragePaymentLineRequest(
                    fixtures.IncomeType.Id,
                    new DateOnly(2026, 8, 1),
                    100m,
                    null,
                    FeeCampaignId: campaign.Id)]),
            null,
            CancellationToken.None);
        Assert.False(mismatch.Succeeded);
        Assert.Equal("fee_campaign_payment_invalid", mismatch.ErrorCode);

        var quote = await service.GetGarageFullPaymentQuoteAsync(fixtures.Garage.Id, CancellationToken.None);
        var quoteLine = Assert.Single(quote.Value!.Lines, line => line.FeeCampaignId == campaign.Id);
        var closed = await DictionaryServiceTestFactory.Create(database.Context).CloseFeeCampaignAsync(
            campaign.Id,
            new CloseFeeCampaignRequest("Квитанция устарела после досрочного закрытия"),
            null,
            CancellationToken.None);
        Assert.True(closed.Succeeded, closed.ErrorMessage);
        var staleBatchId = Guid.NewGuid();
        var stale = await service.CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(
                fixtures.Garage.Id,
                new DateOnly(2026, 8, 11),
                [new CreateFullGaragePaymentLineRequest(
                    quoteLine.IncomeTypeId,
                    quoteLine.AccountingMonth,
                    quoteLine.OutstandingAmount,
                    null,
                    FeeCampaignId: quoteLine.FeeCampaignId)],
                staleBatchId),
            null,
            CancellationToken.None);
        Assert.False(stale.Succeeded);
        Assert.Equal("fee_campaign_closed", stale.ErrorCode);
        Assert.DoesNotContain(database.Context.FinancialOperations, item => item.ReceiptBatchId == staleBatchId);
    }

    [Fact]
    public async Task CreateFullGaragePaymentAsync_DoesNotPersistAnyLineWhenFundAssignmentFails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var validIncomeType = AddOtherIncomeDestination(database.Context);
        var archivedFund = new Fund
        {
            Name = "Архивный фонд",
            NormalizedName = "АРХИВНЫЙ ФОНД",
            IsArchived = true
        };
        var invalidIncomeType = new IncomeType
        {
            Name = "Поступление в архивный фонд",
            Code = "archived_fund_income",
            DestinationFund = archivedFund,
            DestinationFundId = archivedFund.Id
        };
        database.Context.AddRange(archivedFund, invalidIncomeType);
        await database.Context.SaveChangesAsync();
        var baselineOperationCount = await database.Context.FinancialOperations.CountAsync();
        var baselineFundOperationCount = await database.Context.FundOperations.CountAsync();
        var baselineAuditCount = await database.Context.AuditEvents.CountAsync();
        var receiptBatchId = Guid.NewGuid();

        var result = await FinanceServiceTestFactory.Create(database.Context).CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(
                fixtures.Garage.Id,
                new DateOnly(2026, 7, 12),
                [
                    new CreateFullGaragePaymentLineRequest(validIncomeType.Id, new DateOnly(2026, 7, 1), 200m, null),
                    new CreateFullGaragePaymentLineRequest(invalidIncomeType.Id, new DateOnly(2026, 7, 1), 300m, null)
                ],
                receiptBatchId),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_destination_fund_not_found", result.ErrorCode);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(baselineOperationCount, await database.Context.FinancialOperations.CountAsync());
        Assert.Equal(baselineFundOperationCount, await database.Context.FundOperations.CountAsync());
        Assert.Equal(baselineAuditCount, await database.Context.AuditEvents.CountAsync());
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.ReceiptBatchId == receiptBatchId);
        Assert.Equal(0m, await database.Context.Funds
            .Where(fund => fund.Id == validIncomeType.DestinationFundId)
            .Select(fund => fund.Balance)
            .SingleAsync());
    }

    [Theory]
    [InlineData(0, "full_payment_lines_invalid")]
    [InlineData(1, "full_payment_amount_invalid")]
    [InlineData(2, "full_payment_line_kind_invalid")]
    [InlineData(3, "full_payment_line_duplicate")]
    [InlineData(4, "full_payment_line_kind_invalid")]
    [InlineData(5, "full_payment_line_kind_invalid")]
    public async Task CreateFullGaragePaymentAsync_RejectsInvalidBatch(int scenario, string expectedCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        IReadOnlyList<CreateFullGaragePaymentLineRequest> lines = scenario switch
        {
            0 => [],
            1 => [new CreateFullGaragePaymentLineRequest(fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 0m, null)],
            2 => [new CreateFullGaragePaymentLineRequest(null, new DateOnly(2026, 7, 1), 100m, null)],
            4 => [new CreateFullGaragePaymentLineRequest(null, new DateOnly(2026, 7, 1), 100m, null, true, Guid.NewGuid())],
            5 => [new CreateFullGaragePaymentLineRequest(null, new DateOnly(2026, 7, 1), 100m, null, true, IrregularPaymentId: Guid.NewGuid())],
            _ =>
            [
                new CreateFullGaragePaymentLineRequest(fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 100m, null),
                new CreateFullGaragePaymentLineRequest(fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 200m, null)
            ]
        };

        var result = await FinanceServiceTestFactory.Create(database.Context).CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(fixtures.Garage.Id, new DateOnly(2026, 7, 12), lines),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeAsync_AllowsOneReceiptBatchForSameGarageAndDateButRejectsReuse()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var receiptBatchId = Guid.NewGuid();

        var first = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 19),
                new DateOnly(2026, 5, 1),
                100m,
                null,
                "Первая позиция",
                receiptBatchId),
            null,
            CancellationToken.None);
        var second = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 19),
                new DateOnly(2026, 6, 1),
                200m,
                null,
                "Вторая позиция",
                receiptBatchId),
            null,
            CancellationToken.None);
        var conflicting = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                null,
                "Другой день",
                receiptBatchId),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(receiptBatchId, first.Value!.ReceiptBatchId);
        Assert.Equal(receiptBatchId, second.Value!.ReceiptBatchId);
        Assert.False(conflicting.Succeeded);
        Assert.Equal("receipt_batch_conflict", conflicting.ErrorCode);
        Assert.Equal(2, database.Context.FinancialOperations.Count(item => item.ReceiptBatchId == receiptBatchId));
    }

    [Fact]
    public async Task OperationAudit_UsesWriterStructuredFieldsAndCancelReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 19),
                new DateOnly(2026, 6, 1),
                500m,
                "PKO-writer",
                "writer smoke"),
            actorUserId,
            CancellationToken.None);
        var canceled = await service.CancelOperationAsync(
            created.Value!.Id,
            new CancelFinanceEntryRequest("duplicate document"),
            actorUserId,
            CancellationToken.None);

        Assert.True(canceled.Succeeded);
        var createAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.income_created");
        Assert.Equal(actorUserId, createAudit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), createAudit.EntityId);
        Assert.Equal("finance", createAudit.Section);
        Assert.Equal("create", createAudit.ActionKind);
        Assert.Contains("PKO-writer", createAudit.EntityDisplayName, StringComparison.Ordinal);
        Assert.Equal(fixtures.Garage.Id.ToString(), createAudit.RelatedGarageId);
        Assert.Equal("12", createAudit.RelatedGarageNumber);
        Assert.Equal("2026-06", createAudit.RelatedAccountingMonth);
        Assert.Equal(created.Value.Id.ToString(), createAudit.RelatedDocumentId);
        Assert.Equal("PKO-writer", createAudit.RelatedDocumentNumber);
        using var createMetadata = JsonDocument.Parse(createAudit.MetadataJson!);
        Assert.Equal("financial_operation", createMetadata.RootElement.GetProperty("financeEntityType").GetString());
        Assert.Equal("income", createMetadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("500", createMetadata.RootElement.GetProperty("amount").GetString());

        var cancelAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.operation_canceled");
        Assert.Equal(actorUserId, cancelAudit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), cancelAudit.EntityId);
        Assert.Equal("finance", cancelAudit.Section);
        Assert.Equal("cancel", cancelAudit.ActionKind);
        Assert.Equal("2026-06", cancelAudit.RelatedAccountingMonth);
        Assert.Equal(created.Value.Id.ToString(), cancelAudit.RelatedDocumentId);
        Assert.Equal("PKO-writer", cancelAudit.RelatedDocumentNumber);
        Assert.Contains("duplicate document", cancelAudit.Summary, StringComparison.Ordinal);
        using var cancelMetadata = JsonDocument.Parse(cancelAudit.MetadataJson!);
        Assert.Equal("financial_operation", cancelMetadata.RootElement.GetProperty("financeEntityType").GetString());
        Assert.Equal("Отмена финансовой записи.", cancelMetadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FinanceAudit_WritesRelatedContextForAccrualsSuppliersAndReadings()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var accrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", "Начисление"),
            actorUserId,
            CancellationToken.None);
        var supplierAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 900m, "manual", "INV-audit", "Счет"),
            actorUserId,
            CancellationToken.None);
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 150m, "Показание"),
            actorUserId,
            CancellationToken.None);

        Assert.True(accrual.Succeeded);
        Assert.True(supplierAccrual.Succeeded);
        Assert.True(reading.Succeeded);

        var accrualAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
        Assert.Equal(fixtures.Garage.Id.ToString(), accrualAudit.RelatedGarageId);
        Assert.Equal("12", accrualAudit.RelatedGarageNumber);
        Assert.Equal("2026-06", accrualAudit.RelatedAccountingMonth);
        Assert.Equal(accrual.Value!.Id.ToString(), accrualAudit.RelatedDocumentId);
        using var accrualMetadata = JsonDocument.Parse(accrualAudit.MetadataJson!);
        Assert.Equal("700", accrualMetadata.RootElement.GetProperty("amount").GetString());
        Assert.Equal(fixtures.IncomeType.Name, accrualMetadata.RootElement.GetProperty("incomeTypeName").GetString());

        var supplierAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_created");
        Assert.Equal(fixtures.Supplier.Id.ToString(), supplierAudit.RelatedCounterpartyId);
        Assert.Equal(fixtures.Supplier.Name, supplierAudit.RelatedCounterpartyName);
        Assert.Equal("2026-06", supplierAudit.RelatedAccountingMonth);
        Assert.Equal(supplierAccrual.Value!.Id.ToString(), supplierAudit.RelatedDocumentId);
        Assert.Equal("INV-audit", supplierAudit.RelatedDocumentNumber);
        using var supplierMetadata = JsonDocument.Parse(supplierAudit.MetadataJson!);
        Assert.Equal("900", supplierMetadata.RootElement.GetProperty("amount").GetString());
        Assert.Equal(fixtures.ExpenseType.Name, supplierMetadata.RootElement.GetProperty("expenseTypeName").GetString());

        var readingAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created");
        Assert.Equal(fixtures.Garage.Id.ToString(), readingAudit.RelatedGarageId);
        Assert.Equal("12", readingAudit.RelatedGarageNumber);
        Assert.Equal("2026-06", readingAudit.RelatedAccountingMonth);
        Assert.Equal(reading.Value!.Id.ToString(), readingAudit.RelatedDocumentId);
        Assert.Equal("water", readingAudit.RelatedDocumentNumber);
        using var readingMetadata = JsonDocument.Parse(readingAudit.MetadataJson!);
        Assert.Equal("150", readingMetadata.RootElement.GetProperty("currentValue").GetString());
        Assert.Equal("water", readingMetadata.RootElement.GetProperty("meterKind").GetString());
    }

    [Fact]
    public async Task CreateStaffPaymentAsync_CreatesExpenseOperationWithAuditAndAvailableAmountCheck()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember { FullName = "Петрова Ольга", Department = department, Rate = 40000m };
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        database.Context.AddRange(
            department,
            staffMember,
            salaryType,
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 20),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 50_000m,
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(
                staffMember.Id,
                new DateOnly(2026, 6, 25),
                new DateOnly(2026, 6, 1),
                25000m,
                "PAY-STAFF-1",
                "Аванс сотруднику"),
            actorUserId,
            CancellationToken.None);
        var tooLarge = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(
                staffMember.Id,
                new DateOnly(2026, 6, 26),
                new DateOnly(2026, 6, 1),
                16000m,
                "PAY-STAFF-2",
                null),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("expense", result.Value!.OperationKind);
        Assert.Equal(staffMember.Id, result.Value.StaffMemberId);
        Assert.Equal("Петрова Ольга", result.Value.StaffMemberName);
        Assert.Equal("Бухгалтерия", result.Value.StaffDepartmentName);
        Assert.Null(result.Value.SupplierId);
        Assert.Equal(salaryType.Id, result.Value.ExpenseTypeId);
        Assert.Equal(ExpensePaymentSources.Cash, result.Value.ExpensePaymentSource);
        Assert.Null(result.Value.ExpenseFundId);
        Assert.False(tooLarge.Succeeded);
        Assert.Equal("staff_payment_amount_exceeds_available", tooLarge.ErrorCode);
        var operation = Assert.Single(database.Context.FinancialOperations.Where(item => item.StaffMemberId == staffMember.Id));
        Assert.Equal(25000m, operation.Amount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.staff_payment_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(staffMember.Id.ToString(), audit.RelatedCounterpartyId);
        Assert.Equal("Петрова Ольга", audit.RelatedCounterpartyName);
        Assert.Contains("Создана выплата 25 000.00 сотруднику Петрова Ольга", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("доступно до выплаты 40 000.00", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Петрова Ольга", metadata.RootElement.GetProperty("staffMemberName").GetString());
        Assert.Equal("Бухгалтерия", metadata.RootElement.GetProperty("staffDepartmentName").GetString());
    }

    [Fact]
    public async Task CreateStaffPaymentAsync_DoesNotCreateOperationWhenCashAmountIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember { FullName = "Петрова Ольга", Department = department, Rate = 40000m };
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        database.Context.AddRange(department, staffMember, salaryType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(
                staffMember.Id,
                new DateOnly(2026, 6, 25),
                new DateOnly(2026, 6, 1),
                1m,
                "PAY-no-bank",
                null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("cash_amount_insufficient", result.ErrorCode);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var actorUserId = Guid.NewGuid();

        var income = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 15), 300.005m, "PKO-noop", "Платеж"),
            null,
            CancellationToken.None);
        var expense = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 15), 200.005m, "RKO-noop", "Выплата"),
            null,
            CancellationToken.None);
        var accrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 15), 1000.005m, "manual", "Начисление"),
            null,
            CancellationToken.None);
        var supplierAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 15), 1200.005m, "manual", "INV-noop", "Счет"),
            null,
            CancellationToken.None);
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20), 100.004m, "Показание"),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.UpdateIncomeAsync(
            income.Value!.Id,
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 300.005m, " PKO-noop ", " Платеж "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateExpenseAsync(
            expense.Value!.Id,
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 200.005m, " RKO-noop ", " Выплата "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateAccrualAsync(
            accrual.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000.005m, " manual ", " Начисление "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateSupplierAccrualAsync(
            supplierAccrual.Value!.Id,
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200.005m, " manual ", " INV-noop ", " Счет "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, " water ", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 100.004m, " Показание "),
            actorUserId,
            CancellationToken.None)).Succeeded);

        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateFinanceDocuments_RoundsManualMoneyAmountsAwayFromZero()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var income = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100.005m, "PKO-round", null),
            null,
            CancellationToken.None);
        var expense = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 200.005m, "RKO-round", null),
            null,
            CancellationToken.None);
        var accrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 300.005m, "manual", "Округление ручного начисления"),
            null,
            CancellationToken.None);
        var supplierAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), 400.005m, "manual", "INV-round", "Округление начисления поставщику"),
            null,
            CancellationToken.None);

        Assert.True(income.Succeeded);
        Assert.Equal(100.01m, income.Value!.Amount);
        Assert.True(expense.Succeeded);
        Assert.Equal(200.01m, expense.Value!.Amount);
        Assert.True(accrual.Succeeded);
        Assert.Equal(300.01m, accrual.Value!.Amount);
        Assert.True(supplierAccrual.Succeeded);
        Assert.Equal(400.01m, supplierAccrual.Value!.Amount);
    }

    [Fact]
    public async Task ListMethods_ApplyExplicitLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)));

        for (var index = 0; index < 3; index++)
        {
            var month = new DateOnly(2026, 6, 1).AddMonths(index);
            var day = new DateOnly(2026, 6, 19).AddDays(index);
            Assert.True((await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, day, month, 100m + index, $"PKO-limit-{index}", null),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateAccrualAsync(
                new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, month, 200m + index, "manual", $"Ручное начисление {index}"),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateSupplierAccrualAsync(
                new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, month, 300m + index, "manual", $"INV-limit-{index}", $"Ручное начисление поставщику {index}"),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateMeterReadingAsync(
                new CreateMeterReadingRequest(fixtures.Garage.Id, "water", month, day, 20m + index, null),
                null,
                CancellationToken.None)).Succeeded);
        }

        var operations = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, null, 2), CancellationToken.None);
        var accruals = await service.GetAccrualsAsync(new AccrualListRequest(null, null, null, 2), CancellationToken.None);
        var supplierAccruals = await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, null, 2), CancellationToken.None);
        var meterReadings = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, null, null, 2), CancellationToken.None);

        Assert.Equal(2, operations.Count);
        Assert.Equal(2, accruals.Count);
        Assert.Equal(2, supplierAccruals.Count);
        Assert.Equal(2, meterReadings.Count);
    }

    [Fact]
    public async Task PageMethods_ReturnTotalCountAndRequestedSlice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)));

        for (var index = 0; index < 3; index++)
        {
            var month = new DateOnly(2026, 6, 1).AddMonths(index);
            var day = new DateOnly(2026, 6, 19).AddDays(index);
            Assert.True((await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, day, month, 100m + index, $"PKO-page-{index}", null),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateAccrualAsync(
                new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, month, 200m + index, "manual", $"Ручное начисление страницы {index}"),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateSupplierAccrualAsync(
                new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, month, 300m + index, "manual", $"INV-page-{index}", $"Ручное начисление поставщику страницы {index}"),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateMeterReadingAsync(
                new CreateMeterReadingRequest(fixtures.Garage.Id, "water", month, day, 20m + index, null),
                null,
                CancellationToken.None)).Succeeded);
        }

        var operations = await service.GetOperationsPageAsync(new FinancialOperationListRequest(null, null, "income", null, 1, 1), CancellationToken.None);
        var accruals = await service.GetAccrualsPageAsync(new AccrualListRequest(null, null, null, 1, 1), CancellationToken.None);
        var supplierAccruals = await service.GetSupplierAccrualsPageAsync(new SupplierAccrualListRequest(null, null, null, 1, 1), CancellationToken.None);
        var meterReadings = await service.GetMeterReadingsPageAsync(new MeterReadingListRequest(null, null, "water", null, 1, 1), CancellationToken.None);

        Assert.Equal(3, operations.TotalCount);
        Assert.Equal(1, operations.Offset);
        Assert.Equal(1, operations.Limit);
        var operation = Assert.Single(operations.Items);
        Assert.Equal("PKO-page-1", operation.DocumentNumber);

        Assert.Equal(3, accruals.TotalCount);
        Assert.Equal(1, accruals.Offset);
        Assert.Equal(1, accruals.Limit);
        var accrual = Assert.Single(accruals.Items);
        Assert.Equal(new DateOnly(2026, 7, 1), accrual.AccountingMonth);

        Assert.Equal(3, supplierAccruals.TotalCount);
        Assert.Equal(1, supplierAccruals.Offset);
        Assert.Equal(1, supplierAccruals.Limit);
        var supplierAccrual = Assert.Single(supplierAccruals.Items);
        Assert.Equal(new DateOnly(2026, 7, 1), supplierAccrual.AccountingMonth);

        Assert.Equal(3, meterReadings.TotalCount);
        Assert.Equal(1, meterReadings.Offset);
        Assert.Equal(1, meterReadings.Limit);
        var meterReading = Assert.Single(meterReadings.Items);
        Assert.Equal(new DateOnly(2026, 7, 1), meterReading.AccountingMonth);
    }

    [Fact]
    public async Task GetMeterReadingYearPageAsync_ReturnsOnlyPagedActiveGaragesAndCompactYearValues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var secondGarage = new Garage { Number = "20", PeopleCount = 1, FloorCount = 1 };
        var archivedGarage = new Garage { Number = "30", PeopleCount = 1, FloorCount = 1, IsArchived = true };
        database.Context.Garages.AddRange(secondGarage, archivedGarage);
        var previousDevice = new MeterDevice { GarageId = secondGarage.Id, Garage = secondGarage, MeterKind = "electricity", SerialNumber = "OLD-20", InstalledOn = new DateOnly(2025, 1, 1), RemovedOn = new DateOnly(2026, 2, 10), InitialValue = 0m, FinalValue = 120m };
        var replacementDevice = new MeterDevice { GarageId = secondGarage.Id, Garage = secondGarage, MeterKind = "electricity", SerialNumber = "NEW-20", InstalledOn = new DateOnly(2026, 2, 10), InitialValue = 0m };
        database.Context.MeterDevices.AddRange(previousDevice, replacementDevice);
        database.Context.MeterReadings.AddRange(
            new MeterReading { GarageId = secondGarage.Id, MeterKind = "electricity", AccountingMonth = new DateOnly(2026, 2, 1), ReadingDate = new DateOnly(2026, 2, 20), CurrentValue = 125m, MeterDeviceId = replacementDevice.Id, MeterDevice = replacementDevice, IsMeterReplacement = true },
            new MeterReading { GarageId = secondGarage.Id, MeterKind = "water", AccountingMonth = new DateOnly(2026, 2, 1), ReadingDate = new DateOnly(2026, 2, 20), CurrentValue = 25m },
            new MeterReading { GarageId = secondGarage.Id, MeterKind = "electricity", AccountingMonth = new DateOnly(2025, 12, 1), ReadingDate = new DateOnly(2025, 12, 20), CurrentValue = 100m },
            new MeterReading { GarageId = archivedGarage.Id, MeterKind = "electricity", AccountingMonth = new DateOnly(2026, 2, 1), ReadingDate = new DateOnly(2026, 2, 20), CurrentValue = 500m });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetMeterReadingYearPageAsync(
            new MeterReadingYearRequest(2026, " ELECTRICITY ", 1, 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
        var garage = Assert.Single(result.Value.Garages);
        Assert.Equal(secondGarage.Id, garage.Id);
        var reading = Assert.Single(result.Value.Readings);
        Assert.Equal(secondGarage.Id, reading.GarageId);
        Assert.Equal(new DateOnly(2026, 2, 1), reading.AccountingMonth);
        Assert.Equal(125m, reading.CurrentValue);
        Assert.NotEqual(Guid.Empty, reading.Version);
        Assert.Equal(replacementDevice.Id, reading.MeterDeviceId);
        Assert.Equal("NEW-20", reading.MeterDeviceSerialNumber);
        Assert.True(reading.IsMeterReplacement);
        Assert.DoesNotContain(result.Value.Garages, item => item.Id == fixtures.Garage.Id);
    }

    [Fact]
    public async Task GetMeterReadingYearPageAsync_SortsNumericGarageNumbersNaturallyBeforePaging()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        database.Context.Garages.AddRange(
            new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "10", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "13", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "3", PeopleCount = 1, FloorCount = 1 });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var firstPage = await service.GetMeterReadingYearPageAsync(
            new MeterReadingYearRequest(2026, "electricity", 3, 0),
            CancellationToken.None);
        var secondPage = await service.GetMeterReadingYearPageAsync(
            new MeterReadingYearRequest(2026, "electricity", 3, 3),
            CancellationToken.None);

        Assert.True(firstPage.Succeeded);
        Assert.True(secondPage.Succeeded);
        Assert.Equal(["1", "2", "3"], firstPage.Value!.Garages.Select(garage => garage.Number));
        Assert.Equal(["10", "12", "13"], secondPage.Value!.Garages.Select(garage => garage.Number));
    }

    [Theory]
    [InlineData(1899, "electricity", "meter_reading_year_invalid")]
    [InlineData(2026, "gas", "meter_kind_invalid")]
    public async Task GetMeterReadingYearPageAsync_ValidatesYearAndMeterKind(int year, string meterKind, string errorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetMeterReadingYearPageAsync(new MeterReadingYearRequest(year, meterKind), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task PageEndpoints_NormalizeInvalidPaging()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var operations = await service.GetOperationsPageAsync(new FinancialOperationListRequest(null, null, null, null, 999, -5), CancellationToken.None);
        var accruals = await service.GetAccrualsPageAsync(new AccrualListRequest(null, null, null, 999, -5), CancellationToken.None);
        var supplierAccruals = await service.GetSupplierAccrualsPageAsync(new SupplierAccrualListRequest(null, null, null, 999, -5), CancellationToken.None);
        var meterReadings = await service.GetMeterReadingsPageAsync(new MeterReadingListRequest(null, null, null, null, 999, -5), CancellationToken.None);

        Assert.Equal(0, operations.Offset);
        Assert.Equal(500, operations.Limit);
        Assert.Equal(0, accruals.Offset);
        Assert.Equal(500, accruals.Limit);
        Assert.Equal(0, supplierAccruals.Offset);
        Assert.Equal(500, supplierAccruals.Limit);
        Assert.Equal(0, meterReadings.Offset);
        Assert.Equal(500, meterReadings.Limit);
    }

    [Fact]
    public async Task GetOperationsPageAsync_FiltersIncomeHistoryByGarageId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
        var secondGarage = new Garage { Number = "99", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        database.Context.Garages.Add(secondGarage);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 100m, "PKO-garage-1", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(secondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 200m, "PKO-garage-99", null),
            null,
            CancellationToken.None)).Succeeded);

        var page = await service.GetOperationsPageAsync(
            new FinancialOperationListRequest(null, null, "income", null, 25, 0, fixtures.Garage.Id),
            CancellationToken.None);

        var operation = Assert.Single(page.Items);
        Assert.Equal(fixtures.Garage.Id, operation.GarageId);
        Assert.Equal("PKO-garage-1", operation.DocumentNumber);
        Assert.NotEqual(default, operation.CreatedAtUtc);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetOperationsPageAsync_FiltersExpenseHistoryBySupplierAndStaffMember()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var secondSupplier = new Supplier
        {
            Name = "Teploset",
            GroupId = fixtures.Supplier.GroupId,
            ChargeServiceSettingId = fixtures.Supplier.ChargeServiceSettingId,
            ChargeServiceSetting = fixtures.Supplier.ChargeServiceSetting,
            ExpenseTypeId = fixtures.Supplier.ExpenseTypeId,
            ExpenseFundId = fixtures.Supplier.ExpenseFundId
        };
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var firstStaff = new StaffMember { FullName = "Петрова Ольга", Department = department, Rate = 40000m };
        var secondStaff = new StaffMember { FullName = "Иванов Сергей", Department = department, Rate = 20000m };
        var salaryExpenseType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        database.Context.AddRange(
            secondSupplier,
            department,
            firstStaff,
            secondStaff,
            salaryExpenseType,
            OpeningCashBalance(SeededBankAmount + 1_000m));
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 100m, "RKO-supplier-1", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(secondSupplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 200m, "RKO-supplier-2", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(firstStaff.Id, new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 1), 300m, "RKO-staff-1", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(secondStaff.Id, new DateOnly(2026, 6, 23), new DateOnly(2026, 6, 1), 400m, "RKO-staff-2", null),
            null,
            CancellationToken.None)).Succeeded);

        var supplierPage = await service.GetOperationsPageAsync(
            new FinancialOperationListRequest(null, null, "expense", null, 25, 0, null, fixtures.Supplier.Id),
            CancellationToken.None);
        var staffPage = await service.GetOperationsPageAsync(
            new FinancialOperationListRequest(null, null, "expense", null, 25, 0, null, null, firstStaff.Id),
            CancellationToken.None);

        var supplierOperation = Assert.Single(supplierPage.Items);
        Assert.Equal(fixtures.Supplier.Id, supplierOperation.SupplierId);
        Assert.Equal("RKO-supplier-1", supplierOperation.DocumentNumber);
        var staffOperation = Assert.Single(staffPage.Items);
        Assert.Equal(firstStaff.Id, staffOperation.StaffMemberId);
        Assert.Equal("RKO-staff-1", staffOperation.DocumentNumber);
    }

    [Fact]
    public async Task GetOperationsPageAsync_LoadsDebtAndAllocationsInThreeSelectsRegardlessOfRowCount()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 100m;
        fixtures.Supplier.StartingBalance = 200m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);

        database.Context.AddRange(
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = month,
                Amount = 500m,
                Source = "manual"
            },
            new SupplierAccrual
            {
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id,
                AccountingMonth = month,
                Amount = 700m,
                Source = "manual"
            });
        await database.Context.SaveChangesAsync();
        for (var index = 0; index < 3; index++)
        {
            Assert.True((await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    fixtures.Garage.Id,
                    fixtures.IncomeType.Id,
                    new DateOnly(2026, 6, 10 + index),
                    month,
                    100m,
                    $"PKO-BATCH-{index}",
                    null),
                null,
                CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateExpenseAsync(
                new CreateExpenseOperationRequest(
                    fixtures.Supplier.Id,
                    fixtures.ExpenseType.Id,
                    new DateOnly(2026, 6, 20 + index),
                    month,
                    50m,
                    $"RKO-BATCH-{index}",
                    null),
                null,
                CancellationToken.None)).Succeeded);
        }

        commandCounter.Reset();
        var page = await service.GetOperationsPageAsync(
            new FinancialOperationListRequest(null, null, null, null, 25, 0),
            CancellationToken.None);

        Assert.Equal(3, commandCounter.Count);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(6, page.Items.Count);
        var firstIncome = Assert.Single(page.Items, item => item.DocumentNumber == "PKO-BATCH-0");
        Assert.Equal(600m, firstIncome.GarageDebtBefore);
        Assert.Equal(500m, firstIncome.GarageDebtAfter);
        Assert.NotEmpty(firstIncome.PaymentAllocations);
        var lastIncome = Assert.Single(page.Items, item => item.DocumentNumber == "PKO-BATCH-2");
        Assert.Equal(400m, lastIncome.GarageDebtBefore);
        Assert.Equal(300m, lastIncome.GarageDebtAfter);
        var firstExpense = Assert.Single(page.Items, item => item.DocumentNumber == "RKO-BATCH-0");
        Assert.Equal(900m, firstExpense.SupplierDebtBefore);
        Assert.Equal(850m, firstExpense.SupplierDebtAfter);
        var lastExpense = Assert.Single(page.Items, item => item.DocumentNumber == "RKO-BATCH-2");
        Assert.Equal(800m, lastExpense.SupplierDebtBefore);
        Assert.Equal(750m, lastExpense.SupplierDebtAfter);
    }

    [Fact]
    public async Task FinancialOperationDisplayQuery_ReturnsEmptyWithoutDatabaseAccess()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfFinancialOperationDisplayQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetAsync([], CancellationToken.None);

        Assert.Equal(0, commandCounter.Count);
        Assert.Empty(result.Calculations);
        Assert.Empty(result.AccrualBuckets);
    }

    [Fact]
    public async Task FinancialOperationDisplayQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfFinancialOperationDisplayQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            query.GetAsync([Guid.NewGuid()], cancellationSource.Token));
    }

    [Fact]
    public async Task CreateIncomeAsync_ReturnsGarageDebtBeforeAndAfterPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 200m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var firstPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 300m, "PKO-1", null),
            null,
            CancellationToken.None);
        var secondPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "PKO-2", null),
            null,
            CancellationToken.None);

        Assert.True(firstPayment.Succeeded);
        Assert.Equal(1200m, firstPayment.Value!.GarageDebtBefore);
        Assert.Equal(900m, firstPayment.Value.GarageDebtAfter);
        Assert.True(secondPayment.Succeeded);
        Assert.Equal(900m, secondPayment.Value!.GarageDebtBefore);
        Assert.Equal(500m, secondPayment.Value.GarageDebtAfter);

        var history = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, "income", null), CancellationToken.None);
        Assert.Contains(history, item => item.DocumentNumber == "PKO-2" && item.GarageDebtBefore == 900m && item.GarageDebtAfter == 500m);
    }

    [Fact]
    public async Task CreateGarageDebtPaymentAsync_CreatesSystemIncomeAndReducesOpeningDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 900m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var receiptBatchId = Guid.NewGuid();

        var result = await service.CreateGarageDebtPaymentAsync(
            new CreateGarageDebtPaymentRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 500m, "Оплата старого долга", receiptBatchId),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("income", result.Value!.OperationKind);
        Assert.Equal("Перенос задолженности", result.Value.IncomeTypeName);
        Assert.Equal(900m, result.Value.GarageDebtBefore);
        Assert.Equal(400m, result.Value.GarageDebtAfter);
        Assert.Equal(receiptBatchId, result.Value.ReceiptBatchId);
        var allocation = Assert.Single(result.Value.PaymentAllocations);
        Assert.Equal("starting_balance", allocation.AllocationKind);
        Assert.Equal(900m, allocation.DebtBefore);
        Assert.Equal(500m, allocation.PaidAmount);
        Assert.Equal(400m, allocation.DebtAfter);

        var incomeType = Assert.Single(database.Context.IncomeTypes, item => item.Code == "debt_transfer");
        Assert.True(incomeType.IsSystem);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.income_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Оплата входящего долга периода", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateGarageDebtPaymentAsync_RejectsAmountAboveRemainingOpeningDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 900m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var firstPayment = await service.CreateGarageDebtPaymentAsync(
            new CreateGarageDebtPaymentRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 500m, null),
            null,
            CancellationToken.None);
        var secondPayment = await service.CreateGarageDebtPaymentAsync(
            new CreateGarageDebtPaymentRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 500m, null),
            null,
            CancellationToken.None);

        Assert.True(firstPayment.Succeeded);
        Assert.False(secondPayment.Succeeded);
        Assert.Equal("debt_payment_amount_exceeds_opening_debt", secondPayment.ErrorCode);
        Assert.Equal(1, await database.Context.FinancialOperations.CountAsync(operation => operation.OperationKind == "income"));
    }

    [Fact]
    public async Task CreateIncomeAsync_AllocatesPaymentToSelectedAccountingMonthBeforeOlderDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Июнь"), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 700m, "manual", "Июль"), null, CancellationToken.None);

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 1), 800m, "PKO-alloc", null),
            null,
            CancellationToken.None);

        Assert.True(payment.Succeeded);
        Assert.Collection(
            payment.Value!.PaymentAllocations,
            first =>
            {
                Assert.Equal("month", first.AllocationKind);
                Assert.Equal(new DateOnly(2026, 7, 1), first.AccountingMonth);
                Assert.Equal(700m, first.DebtBefore);
                Assert.Equal(700m, first.PaidAmount);
                Assert.Equal(0m, first.DebtAfter);
            },
            second =>
            {
                Assert.Equal("month", second.AllocationKind);
                Assert.Equal(new DateOnly(2026, 6, 1), second.AccountingMonth);
                Assert.Equal(500m, second.DebtBefore);
                Assert.Equal(100m, second.PaidAmount);
                Assert.Equal(400m, second.DebtAfter);
            });

        var persistedAllocations = await database.Context.AccrualPaymentAllocations
            .OrderBy(item => item.Accrual.DueDate)
            .ToListAsync();
        Assert.Equal([100m, 700m], persistedAllocations.Select(item => item.Amount));

        var canceled = await service.CancelOperationAsync(
            payment.Value.Id,
            new CancelFinanceEntryRequest("Ошибочный платёж"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);
        Assert.Empty(await database.Context.AccrualPaymentAllocations.Where(item => item.IsActive).ToListAsync());
        Assert.Equal(2, await database.Context.AccrualPaymentAllocations.CountAsync(item => !item.IsActive));

        var allocationAudits = await database.Context.AuditEvents
            .Where(item =>
                item.Action == "finance.payment_allocations_rebuilt" &&
                item.EntityId == payment.Value.Id.ToString())
            .ToListAsync();
        Assert.Equal(2, allocationAudits.Count);
        Assert.Single(allocationAudits, audit => audit.Summary.Contains("Создание поступления", StringComparison.Ordinal));
        Assert.Single(allocationAudits, audit => audit.Summary.Contains("Отмена поступления", StringComparison.Ordinal));
        Assert.All(allocationAudits, audit =>
        {
            Assert.Equal("payment_allocation", audit.EntityType);
            Assert.DoesNotContain("PKO-alloc", audit.MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain(fixtures.Garage.Number, audit.MetadataJson, StringComparison.Ordinal);
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.True(metadata.RootElement.TryGetProperty("activeAllocationCount", out _));
            Assert.True(metadata.RootElement.TryGetProperty("previousActiveAllocationCount", out _));
        });
    }

    [Fact]
    public async Task IncomeAllocation_RebuildsEarlyExcessPaymentAfterPartialFullUpdateAndCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "connection";
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateIncomeOperationRequest(
            fixtures.Garage.Id,
            fixtures.IncomeType.Id,
            new DateOnly(2026, 5, 20),
            new DateOnly(2026, 7, 1),
            1500m,
            "PKO-early-excess",
            "Досрочная оплата до начислений");

        var payment = await service.CreateIncomeAsync(request, null, CancellationToken.None);
        Assert.True(payment.Succeeded);
        Assert.Empty(await ActiveAllocationsAsync());

        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Июнь"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 700m, "manual", "Июль"),
            null,
            CancellationToken.None)).Succeeded);

        var excessAllocations = await ActiveAllocationsAsync();
        Assert.Equal([500m, 700m], excessAllocations.Select(item => item.Amount));
        Assert.Equal(300m, payment.Value!.Amount - excessAllocations.Sum(item => item.Amount));

        var partial = await service.UpdateIncomeAsync(
            payment.Value.Id,
            request with { Amount = 800m, Comment = "Частичная оплата двух начислений" },
            null,
            CancellationToken.None);
        Assert.True(partial.Succeeded);
        Assert.Equal([100m, 700m], (await ActiveAllocationsAsync()).Select(item => item.Amount));

        var full = await service.UpdateIncomeAsync(
            payment.Value.Id,
            request with { Amount = 1200m, Comment = "Полная оплата двух начислений" },
            null,
            CancellationToken.None);
        Assert.True(full.Succeeded);
        Assert.Equal([500m, 700m], (await ActiveAllocationsAsync()).Select(item => item.Amount));

        var canceled = await service.CancelOperationAsync(
            payment.Value.Id,
            new CancelFinanceEntryRequest("Отмена проверочного платежа"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);
        Assert.Empty(await ActiveAllocationsAsync());
        Assert.Equal(7, await database.Context.AccrualPaymentAllocations.CountAsync(item => !item.IsActive));

        Task<List<AccrualPaymentAllocation>> ActiveAllocationsAsync() =>
            database.Context.AccrualPaymentAllocations
                .AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Accrual.DueDate)
                .ToListAsync();
    }

    [Fact]
    public async Task GetGarageOverdueDebtAsync_ReturnsOnlyOutstandingMaturedDebtInOldestFirstOrder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 100m;
        var overdue = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 5, 1),
            DueDate = new DateOnly(2026, 6, 10),
            OverdueFromDate = new DateOnly(2026, 6, 11),
            Amount = 500m,
            Source = "overdue-breakdown-test"
        };
        var future = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 8, 10),
            OverdueFromDate = new DateOnly(2026, 8, 11),
            Amount = 700m,
            Source = "overdue-breakdown-test"
        };
        var canceled = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 4, 1),
            DueDate = new DateOnly(2026, 5, 10),
            OverdueFromDate = new DateOnly(2026, 5, 11),
            Amount = 900m,
            Source = "overdue-breakdown-test",
            IsCanceled = true
        };
        var needsReview = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 3, 1),
            DueDate = new DateOnly(2026, 4, 30),
            OverdueFromDate = new DateOnly(2026, 6, 1),
            DueDateNeedsReview = true,
            DueDateReviewReason = "historical_source_unknown",
            Amount = 800m,
            Source = "legacy"
        };
        database.Context.AddRange(overdue, future, canceled, needsReview);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                200m,
                "PKO-overdue-breakdown",
                null),
            null,
            CancellationToken.None);
        var result = await service.GetGarageOverdueDebtAsync(fixtures.Garage.Id, CancellationToken.None);

        Assert.True(payment.Succeeded);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 7, 17), result.Value!.AsOfDate);
        Assert.Equal(600m, result.Value.Total);
        Assert.Collection(
            result.Value.Rows,
            opening =>
            {
                Assert.Equal("opening_balance", opening.RowKind);
                Assert.Equal("Входящий долг", opening.IncomeTypeName);
                Assert.Null(opening.AccountingMonth);
                Assert.Equal(100m, opening.OutstandingAmount);
            },
            accrual =>
            {
                Assert.Equal("accrual", accrual.RowKind);
                Assert.Equal(fixtures.IncomeType.Id, accrual.IncomeTypeId);
                Assert.Equal(new DateOnly(2026, 5, 1), accrual.AccountingMonth);
                Assert.Equal(new DateOnly(2026, 6, 10), accrual.DueDate);
                Assert.Equal(new DateOnly(2026, 6, 11), accrual.OverdueFromDate);
                Assert.Equal(500m, accrual.OriginalAmount);
                Assert.Equal(0m, accrual.PaidAmount);
                Assert.Equal(500m, accrual.OutstandingAmount);
            });
    }

    [Theory]
    [InlineData(2026, 6, 29, false)]
    [InlineData(2026, 6, 30, false)]
    [InlineData(2026, 7, 30, false)]
    [InlineData(2026, 7, 31, true)]
    [InlineData(2026, 8, 1, true)]
    public async Task GetGarageOverdueDebtAsync_IncludesAccrualOnlyFromOverdueDate(
        int year,
        int month,
        int day,
        bool expectedOverdue)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var accrual = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 5, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 500m,
            Source = "overdue-boundary-test"
        };
        database.Context.Accruals.Add(accrual);
        await database.Context.SaveChangesAsync();
        var asOfDate = new DateOnly(year, month, day);
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.GetGarageOverdueDebtAsync(fixtures.Garage.Id, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(asOfDate, result.Value!.AsOfDate);
        if (expectedOverdue)
        {
            var row = Assert.Single(result.Value.Rows);
            Assert.Equal(fixtures.IncomeType.Id, row.IncomeTypeId);
            Assert.Equal(accrual.DueDate, row.DueDate);
            Assert.Equal(accrual.OverdueFromDate, row.OverdueFromDate);
            Assert.Equal(500m, row.OutstandingAmount);
            Assert.Equal(500m, result.Value.Total);
        }
        else
        {
            Assert.Empty(result.Value.Rows);
            Assert.Equal(0m, result.Value.Total);
        }
    }

    [Fact]
    public async Task GetAccrualDueDateReviewPageAsync_ReturnsOnlyActiveFlaggedRowsWithStablePagination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var first = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2025, 1, 1),
            DueDate = new DateOnly(2025, 2, 28),
            OverdueFromDate = new DateOnly(2025, 3, 31),
            DueDateNeedsReview = true,
            DueDateReviewReason = "regular_service_not_unique",
            Amount = 500m,
            Source = AccrualSources.Regular
        };
        var second = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2025, 2, 1),
            DueDate = new DateOnly(2025, 3, 31),
            OverdueFromDate = new DateOnly(2025, 5, 1),
            DueDateNeedsReview = true,
            DueDateReviewReason = "fee_campaign_not_unique",
            Amount = 700m,
            Source = AccrualSources.FeeCampaign
        };
        var canceled = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2024, 12, 1),
            DueDate = new DateOnly(2025, 1, 31),
            OverdueFromDate = new DateOnly(2025, 3, 3),
            DueDateNeedsReview = true,
            DueDateReviewReason = "historical_source_unknown",
            Amount = 900m,
            Source = "legacy",
            IsCanceled = true
        };
        database.Context.AddRange(first, second, canceled);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var firstPage = await service.GetAccrualDueDateReviewPageAsync(0, 1, CancellationToken.None);
        var secondPage = await service.GetAccrualDueDateReviewPageAsync(1, 1, CancellationToken.None);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(first.Id, Assert.Single(firstPage.Items).AccrualId);
        Assert.Equal("regular_service_not_unique", firstPage.Items[0].ReasonCode);
        Assert.Equal(fixtures.Garage.Number, firstPage.Items[0].GarageNumber);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Equal(second.Id, Assert.Single(secondPage.Items).AccrualId);
        Assert.Equal("fee_campaign_not_unique", secondPage.Items[0].ReasonCode);
    }

    [Fact]
    public async Task GetGarageOverdueDebtAsync_ReturnsFailureForMissingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.GetGarageOverdueDebtAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task GetGarageBalanceHistoryAsync_ReturnsMonthlyRunningDebt()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "connection";
        fixtures.Garage.StartingBalance = 100m;
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 5, 1),
            Amount = 300m,
            Source = "history-test"
        });
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 5, 20),
            AccountingMonth = new DateOnly(2026, 5, 1),
            Amount = 100m,
            DocumentNumber = "PKO-history-opening",
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        Assert.True((await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "regular", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 200m, "PKO-history-1", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 700m, "regular", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 1), 300m, "PKO-history-2", null), null, CancellationToken.None)).Succeeded);
        commandCounter.Reset();

        var result = await service.GetGarageBalanceHistoryAsync(
            fixtures.Garage.Id,
            new GarageBalanceHistoryRequest(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 31)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal("12", result.Value!.GarageNumber);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.MonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.MonthTo);
        Assert.Equal(100m, result.Value.StartingBalance);
        Assert.Equal(1200m, result.Value.AccrualTotal);
        Assert.Equal(500m, result.Value.IncomeTotal);
        Assert.Equal(1000m, result.Value.Debt);
        Assert.Collection(
            result.Value.Rows,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 6, 1), first.AccountingMonth);
                Assert.Equal(300m, first.OpeningDebt);
                Assert.Equal(500m, first.AccrualAmount);
                Assert.Equal(200m, first.IncomeAmount);
                Assert.Equal(600m, first.ClosingDebt);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 7, 1), second.AccountingMonth);
                Assert.Equal(600m, second.OpeningDebt);
                Assert.Equal(700m, second.AccrualAmount);
                Assert.Equal(300m, second.IncomeAmount);
                Assert.Equal(1000m, second.ClosingDebt);
            });
    }

    [Fact]
    public async Task FinanceDefaults_UseConfiguredBusinessDateAcrossOperationalViews()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2031, 4, 15, 12, 0, 0, TimeSpan.Zero)));

        var missingReadings = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(null, MeterKinds.Water, null),
            CancellationToken.None);
        var balanceHistory = await service.GetGarageBalanceHistoryAsync(
            fixtures.Garage.Id,
            new GarageBalanceHistoryRequest(null, null),
            CancellationToken.None);
        var expenseWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(null),
            CancellationToken.None);
        var supplierOpeningBalance = await service.GetSupplierOpeningBalanceAsync(
            fixtures.Supplier.Id,
            new SupplierOpeningBalanceRequest(null),
            CancellationToken.None);

        Assert.All(missingReadings, item => Assert.Equal(new DateOnly(2031, 4, 1), item.AccountingMonth));
        Assert.True(balanceHistory.Succeeded);
        Assert.Equal(new DateOnly(2030, 11, 1), balanceHistory.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2031, 4, 1), balanceHistory.Value.MonthTo);
        Assert.True(expenseWorksheet.Succeeded);
        Assert.Equal(new DateOnly(2031, 4, 1), expenseWorksheet.Value!.AccountingMonth);
        Assert.True(supplierOpeningBalance.Succeeded);
        Assert.Equal(new DateOnly(2031, 4, 1), supplierOpeningBalance.Value!.MonthFrom);
    }

    [Fact]
    public async Task GarageBalanceHistoryQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfGarageBalanceHistoryQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetAsync(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            cancellationSource.Token));
    }

    [Fact]
    public async Task GetGarageBalanceHistoryAsync_ReturnsFailureForMissingGarage()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageBalanceHistoryAsync(Guid.NewGuid(), new GarageBalanceHistoryRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_not_found", result.ErrorCode);
        Assert.Equal(1, commandCounter.Count);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 400m, "manual", "INV-6", "Июнь"), null, CancellationToken.None);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), 600m, "manual", "INV-7", "Июль"), null, CancellationToken.None);

        var payment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 1), 650m, "RKO-alloc", null),
            null,
            CancellationToken.None);

        Assert.True(payment.Succeeded);
        Assert.Collection(
            payment.Value!.PaymentAllocations,
            first =>
            {
                Assert.Equal("month", first.AllocationKind);
                Assert.Equal(new DateOnly(2026, 6, 1), first.AccountingMonth);
                Assert.Equal(400m, first.DebtBefore);
                Assert.Equal(400m, first.PaidAmount);
                Assert.Equal(0m, first.DebtAfter);
            },
            second =>
            {
                Assert.Equal("month", second.AllocationKind);
                Assert.Equal(new DateOnly(2026, 7, 1), second.AccountingMonth);
                Assert.Equal(600m, second.DebtBefore);
                Assert.Equal(250m, second.PaidAmount);
                Assert.Equal(350m, second.DebtAfter);
            });
    }

    [Fact]
    public async Task CreateIncomeAsync_RejectsDuplicateDocumentForSameDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1000m, "PKO-19", null);
        await service.CreateIncomeAsync(request, null, CancellationToken.None);

        var result = await service.CreateIncomeAsync(request with { Amount = 2000m }, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("operation_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null),
            null,
            CancellationToken.None);
        var request = new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-replace", "Ошибочный платеж");
        var firstPayment = await service.CreateIncomeAsync(request, null, CancellationToken.None);
        Assert.True(firstPayment.Succeeded);
        var canceled = await service.CancelOperationAsync(
            firstPayment.Value!.Id,
            new CancelFinanceEntryRequest("Платеж заменен"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);

        var replacement = await service.CreateIncomeAsync(request with { Amount = 600m, Comment = "Корректный платеж" }, null, CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal(600m, replacement.Value!.Amount);
        Assert.Equal(1000m, replacement.Value.GarageDebtBefore);
        Assert.Equal(400m, replacement.Value.GarageDebtAfter);
        Assert.Equal(2, await database.Context.FinancialOperations.CountAsync());
        Assert.Equal(1, await database.Context.FinancialOperations.CountAsync(operation => operation.IsCanceled));
        var activeOperation = Assert.Single(await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None));
        Assert.Equal(600m, activeOperation.Amount);
        Assert.Equal(600m, (await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None)).IncomeTotal);
    }

    [Fact]
    public async Task UpdateIncomeAsync_UpdatesOperationAndWritesBeforeAfterAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "manual", "Начисление месяца"),
            null,
            CancellationToken.None);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 300m, "PKO-old", null),
            null,
            CancellationToken.None);
        var actorUserId = Guid.NewGuid();

        var updated = await service.UpdateIncomeAsync(
            created.Value!.Id,
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 450m, "PKO-new", "Исправлена сумма"),
            actorUserId,
            CancellationToken.None);

        Assert.True(updated.Succeeded);
        Assert.Equal(450m, updated.Value!.Amount);
        Assert.Equal("PKO-new", updated.Value.DocumentNumber);
        Assert.Equal(1000m, updated.Value.GarageDebtBefore);
        Assert.Equal(550m, updated.Value.GarageDebtAfter);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.income_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("было 300.00 по гаражу 12 от 19.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-old", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 450.00 по гаражу 12 от 20.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-new", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Исправлена сумма", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("financial_operation", metadata.RootElement.GetProperty("financeEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Дата операции", changedFields, StringComparison.Ordinal);
        Assert.Contains("Сумма", changedFields, StringComparison.Ordinal);
        Assert.Contains("Документ", changedFields, StringComparison.Ordinal);
        Assert.Contains("Комментарий", changedFields, StringComparison.Ordinal);
        Assert.Equal("4", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateIncomeAsync_RejectsOpenOrClosedCampaignTargetWithoutChangingOperation(bool closed)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var otherGarage = new Garage
        {
            Number = closed ? "TARGET-CLOSED" : "TARGET-OPEN",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = fixtures.Garage.Owner
        };
        var campaign = new FeeCampaign
        {
            Name = closed ? "Закрытый target edit" : "Открытый target edit",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30,
            ClosedAtUtc = closed ? DateTimeOffset.UtcNow : null
        };
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 15),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 300m,
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign,
            Comment = "Исходный целевой платеж"
        };
        database.Context.AddRange(otherGarage, campaign, operation);
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).UpdateIncomeAsync(
            operation.Id,
            new CreateIncomeOperationRequest(
                otherGarage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 7, 20),
                new DateOnly(2026, 7, 1),
                999m,
                null,
                "Подмена маршрута"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("targeted_income_update_forbidden", result.ErrorCode);
        Assert.Equal(fixtures.Garage.Id, operation.GarageId);
        Assert.Equal(otherIncome.Id, operation.IncomeTypeId);
        Assert.Equal(new DateOnly(2026, 6, 1), operation.AccountingMonth);
        Assert.Equal(300m, operation.Amount);
        Assert.Equal(campaign.Id, operation.FeeCampaignId);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.income_updated");
    }

    [Fact]
    public async Task UpdateIncomeAsync_RejectsIrregularPaymentTargetWithoutChangingOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var irregularPayment = new IrregularPayment { Name = "Целевой нерегулярный платеж", Amount = 250m };
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 15),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 250m,
            Garage = fixtures.Garage,
            IncomeType = fixtures.IncomeType,
            IrregularPayment = irregularPayment
        };
        database.Context.AddRange(irregularPayment, operation);
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).UpdateIncomeAsync(
            operation.Id,
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                300m,
                null,
                null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("targeted_income_update_forbidden", result.ErrorCode);
        Assert.Equal(250m, operation.Amount);
        Assert.Equal(new DateOnly(2026, 6, 1), operation.AccountingMonth);
        Assert.Equal(irregularPayment.Id, operation.IrregularPaymentId);
    }

    [Fact]
    public async Task UpdateIncomeAsync_RejectsReductionAboveAvailableCash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        fixtures.Supplier.ExpenseTypeId = null;
        fixtures.Supplier.ExpenseType = null;
        fixtures.Supplier.ExpenseFundId = null;
        fixtures.Supplier.ExpenseFund = null;
        database.Context.Funds.RemoveRange(database.Context.Funds);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 100m, "PKO-reduction", null),
            null,
            CancellationToken.None);
        database.Context.Add(
            new CashBankTransfer
            {
                TransferDate = new DateOnly(2026, 6, 19),
                Amount = 80m,
                Comment = "Сдача кассы в банк"
            });
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateIncomeAsync(
            created.Value!.Id,
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 50m, "PKO-reduction", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("cash_amount_insufficient", result.ErrorCode);
        Assert.Equal(100m, (await database.Context.FinancialOperations.SingleAsync(operation => operation.Id == created.Value.Id)).Amount);
        Assert.DoesNotContain(database.Context.AuditEvents, audit => audit.Action == "finance.income_updated");
    }

    [Fact]
    public async Task CancelOperationAsync_CancelsOperationAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-cancel", "Ошибочный платеж"),
            null,
            CancellationToken.None);

        var result = await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("Дублирующий документ"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCanceled);
        Assert.Contains("Ошибочный платеж", result.Value.Comment);
        Assert.Contains("Отменено: Дублирующий документ", result.Value.Comment);
        Assert.Empty(await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(0m, summary.IncomeTotal);
        Assert.Equal(1000m, summary.Debt);
        Assert.Equal(0, summary.OperationCount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.operation_canceled");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Отменено поступление 400.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 19.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-cancel", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Дублирующий документ", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelOperationAsync_RequiresReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-empty-reason", null),
            null,
            CancellationToken.None);

        var result = await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("   "), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("operation_cancel_reason_required", result.ErrorCode);
    }

    [Fact]
    public async Task CancelOperationAsync_RejectsAlreadyCanceledOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-already-canceled", null),
            null,
            CancellationToken.None);
        await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("Первая отмена"), null, CancellationToken.None);

        var result = await service.CancelOperationAsync(created.Value.Id, new CancelFinanceEntryRequest("Вторая отмена"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("operation_already_canceled", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreOperationAsync_RestoresCanceledIncomeAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);
        var created = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-restore", "Ошибочно отменили"),
            null,
            CancellationToken.None);
        await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("Проверка восстановления"), null, CancellationToken.None);

        var result = await service.RestoreOperationAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCanceled);
        Assert.Single(await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(400m, summary.IncomeTotal);
        Assert.Equal(600m, summary.Debt);
        Assert.Equal(1, summary.OperationCount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.operation_restored");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("restore", audit.ActionKind);
        Assert.Contains("Восстановлено поступление 400.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-restore", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreOperationAsync_RejectsActiveDocumentDuplicate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 400m, "PKO-restore-duplicate", null);
        var created = await service.CreateIncomeAsync(request, null, CancellationToken.None);
        await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("Заменили документ"), null, CancellationToken.None);
        Assert.True((await service.CreateIncomeAsync(request with { Amount = 500m }, null, CancellationToken.None)).Succeeded);

        var result = await service.RestoreOperationAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("operation_duplicate", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.operation_restored");
    }

    [Fact]
    public async Task RestoreOperationAsync_RejectsExpenseWhenBankAmountIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 200m, "RKO-restore-bank", null),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded);
        await service.CancelOperationAsync(created.Value!.Id, new CancelFinanceEntryRequest("Проверка остатка"), null, CancellationToken.None);
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        await database.Context.SaveChangesAsync();

        var result = await service.RestoreOperationAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("bank_amount_insufficient", result.ErrorCode);
        Assert.True(await database.Context.FinancialOperations.AnyAsync(operation => operation.Id == created.Value.Id && operation.IsCanceled));
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.operation_restored");
    }

    [Fact]
    public async Task RestoreStaffPayment_UsesMonthlyBonusAndPenaltyInAvailableSalary()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var department = new StaffDepartment { Name = "Отдел восстановления зарплаты" };
        var staffMember = new StaffMember
        {
            FullName = "Сотрудник восстановления зарплаты",
            Department = department,
            Rate = 100m,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(
            department,
            staffMember,
            new ExpenseType { Name = "Зарплата восстановления", Code = "salary" },
            OpeningCashBalance(SeededBankAmount + 200m));
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);

        Assert.True((await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, "bonus", 50m, "BONUS-RESTORE", "Премия"),
            null,
            CancellationToken.None)).Succeeded);
        var payment = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, month.AddDays(20), month, 120m, "SALARY-RESTORE", null),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        Assert.True((await service.CancelOperationAsync(
            payment.Value!.Id,
            new CancelFinanceEntryRequest("Проверка премии"),
            null,
            CancellationToken.None)).Succeeded);

        var restoredWithBonus = await service.RestoreOperationAsync(payment.Value.Id, null, CancellationToken.None);

        Assert.True(restoredWithBonus.Succeeded, restoredWithBonus.ErrorMessage);
        Assert.True((await service.CancelOperationAsync(
            payment.Value.Id,
            new CancelFinanceEntryRequest("Проверка штрафа"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, "penalty", 40m, "PENALTY-RESTORE", "Штраф"),
            null,
            CancellationToken.None)).Succeeded);

        var rejectedAfterPenalty = await service.RestoreOperationAsync(payment.Value.Id, null, CancellationToken.None);

        Assert.False(rejectedAfterPenalty.Succeeded);
        Assert.Equal("staff_payment_amount_exceeds_available", rejectedAfterPenalty.ErrorCode);
        Assert.True(await database.Context.FinancialOperations.AnyAsync(operation =>
            operation.Id == payment.Value.Id && operation.IsCanceled));
    }

    [Fact]
    public async Task CreateExpenseAsync_ReturnsNotFoundForMissingSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(Guid.NewGuid(), fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 300m, null, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateExpenseAsync_CreatesOperationAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 15),
                400.75m,
                "RKO-20",
                "Оплата воды",
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Bank,
                fixtures.ExpenseFund.Id),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("expense", result.Value!.OperationKind);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.AccountingMonth);
        Assert.Equal("Vodokanal", result.Value.SupplierName);
        Assert.Equal("Вода", result.Value.ExpenseTypeName);
        Assert.Equal(ExpensePaymentSources.Bank, result.Value.ExpensePaymentSource);
        Assert.Equal(fixtures.ExpenseFund.Id, result.Value.ExpenseFundId);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создана выплата 400.75", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("получателю Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 20.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("услуга/статья Вода", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("тип с чеком", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-20", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Оплата воды", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateExpenseAsync_DoesNotCreateOperationWhenBankAmountIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                1m,
                "RKO-no-bank",
                null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("bank_amount_insufficient", result.ErrorCode);
        Assert.Equal("На банковском счёте недостаточно средств. Доступно 0.00.", result.ErrorMessage);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateStaffSalaryAdjustmentAsync_AddsBonusAndPenaltyToMonthlySalaryAndAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = department,
            Rate = 40000m,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(
            department,
            staffMember,
            new ExpenseType { Name = "Зарплата", Code = "salary" },
            OpeningCashBalance(SeededBankAmount + 50_000m));
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var month = new DateOnly(2026, 6, 1);

        var bonus = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, " BONUS ", 5000.005m, "PR-1", "За качественную работу"),
            actorUserId,
            CancellationToken.None);
        var penalty = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, "penalty", 2000m, null, "Нарушение срока"),
            actorUserId,
            CancellationToken.None);
        var payment = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, new DateOnly(2026, 6, 25), month, 43000.02m, "PAY-1", null),
            actorUserId,
            CancellationToken.None);
        var fullPayment = await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, new DateOnly(2026, 6, 25), month, 43000.01m, "PAY-2", null),
            actorUserId,
            CancellationToken.None);

        Assert.True(bonus.Succeeded);
        Assert.Equal(5000.01m, bonus.Value!.Amount);
        Assert.Equal(StaffSalaryAdjustmentTypes.Bonus, bonus.Value.AdjustmentType);
        Assert.True(penalty.Succeeded);
        Assert.False(payment.Succeeded);
        Assert.Equal("staff_payment_amount_exceeds_available", payment.ErrorCode);
        Assert.True(fullPayment.Succeeded);
        Assert.Equal(2, database.Context.StaffSalaryAdjustments.Count());
        var audits = database.Context.AuditEvents
            .Where(item => item.Action == "finance.staff_salary_adjustment_created")
            .AsEnumerable()
            .OrderBy(item => item.CreatedAtUtc)
            .ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit => Assert.Equal(actorUserId, audit.ActorUserId));
        Assert.Contains("За качественную работу", audits[0].Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audits[0].MetadataJson!);
        Assert.Equal("bonus", metadata.RootElement.GetProperty("adjustmentType").GetString());
        Assert.Equal("45000.01", metadata.RootElement.GetProperty("salaryAccrualAfterAdjustment").GetString());
    }

    [Theory]
    [InlineData("", 100, "Основание", "staff_salary_adjustment_type_invalid")]
    [InlineData("gift", 100, "Основание", "staff_salary_adjustment_type_invalid")]
    [InlineData("bonus", 0, "Основание", "staff_salary_adjustment_amount_invalid")]
    [InlineData("bonus", 100, "   ", "staff_salary_adjustment_reason_required")]
    public async Task CreateStaffSalaryAdjustmentAsync_RejectsInvalidRequest(
        string adjustmentType,
        decimal amount,
        string reason,
        string expectedError)
    {
        await using var database = await TestDatabase.CreateAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = department,
            Rate = 40000m,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(department, staffMember);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, new DateOnly(2026, 6, 1), adjustmentType, amount, null, reason),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Empty(database.Context.StaffSalaryAdjustments);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateStaffSalaryAdjustmentAsync_RejectsMonthBeforeEmploymentAndPenaltyBelowPaidAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = department,
            Rate = 40000m,
            CreatedAtUtc = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(
            department,
            staffMember,
            new ExpenseType { Name = "Зарплата", Code = "salary" },
            OpeningCashBalance(SeededBankAmount + 35_000m));
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        Assert.True((await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 35000m, null, null),
            null,
            CancellationToken.None)).Succeeded);

        var missingStaff = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(Guid.NewGuid(), new DateOnly(2026, 6, 1), "bonus", 100m, null, "Нет сотрудника"),
            null,
            CancellationToken.None);
        var earlyMonth = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, new DateOnly(2026, 4, 1), "bonus", 100m, null, "До приема"),
            null,
            CancellationToken.None);
        var excessivePenalty = await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, new DateOnly(2026, 6, 1), "penalty", 5000.01m, null, "Штраф"),
            null,
            CancellationToken.None);

        Assert.Equal("staff_member_not_found", missingStaff.ErrorCode);
        Assert.Equal("staff_salary_adjustment_month_invalid", earlyMonth.ErrorCode);
        Assert.Equal("staff_penalty_exceeds_available", excessivePenalty.ErrorCode);
        Assert.Empty(database.Context.StaffSalaryAdjustments);
    }

    [Fact]
    public async Task CreateCashBankTransferAsync_MovesCashToBankWithoutChangingFundsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = new Garage { Number = "CASH-BANK-1", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = "Поступление для сдачи кассы" };
        var fund = new Fund { Name = "Резерв", NormalizedName = "РЕЗЕРВ", Balance = 125m };
        database.Context.AddRange(garage, incomeType, fund);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                incomeType.Id,
                new DateOnly(2026, 6, 14),
                new DateOnly(2026, 6, 1),
                1000m,
                "PKO-CASH-BANK",
                null),
            null,
            CancellationToken.None)).Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateCashBankTransferAsync(
            new CreateCashBankTransferRequest(new DateOnly(2026, 6, 15), 400.126m, "Инкассация"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(400.13m, result.Value!.Amount);
        Assert.Equal(new DateOnly(2026, 6, 15), result.Value.TransferDate);
        Assert.Equal("Инкассация", result.Value.Comment);
        Assert.Empty(database.Context.FundOperations);
        Assert.Equal(125m, (await database.Context.Funds.SingleAsync()).Balance);
        var stored = await database.Context.CashBankTransfers.SingleAsync();
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal(actorUserId, stored.ActorUserId);
        var worksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded);
        Assert.Equal(599.87m, worksheet.Value!.CashAmount);
        Assert.Equal(400.13m, worksheet.Value.BankAmount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.cash_bank_transfer_created");
        Assert.Equal("cash_bank_transfer", audit.EntityType);
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var auditMetadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Инкассация", auditMetadata.RootElement.GetProperty("reason").GetString());
        Assert.Contains("\"source\":\"cash\"", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"destination\":\"bank\"", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 0, "cash_bank_transfer_date_required")]
    [InlineData(true, 0, "cash_bank_transfer_amount_invalid")]
    [InlineData(true, -1, "cash_bank_transfer_amount_invalid")]
    public async Task CreateCashBankTransferAsync_RejectsInvalidDateOrAmount(
        bool hasDate,
        decimal amount,
        string expectedErrorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateCashBankTransferAsync(
            new CreateCashBankTransferRequest(hasDate ? new DateOnly(2026, 6, 15) : default, amount, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(database.Context.CashBankTransfers);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateCashBankTransferAsync_RejectsAmountAboveAvailableCash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = new Garage { Number = "CASH-BANK-2", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = "Ограниченное поступление" };
        database.Context.AddRange(garage, incomeType, new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 14),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 100m,
            Garage = garage,
            IncomeType = incomeType
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateCashBankTransferAsync(
            new CreateCashBankTransferRequest(new DateOnly(2026, 6, 15), 100.01m, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("cash_amount_insufficient", result.ErrorCode);
        Assert.Contains("100.00", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(database.Context.CashBankTransfers);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsBankPaymentWhenServiceCollectionsAreInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var waterIncomeType = new IncomeType { Name = "Вода", Code = "water" };
        database.Context.Add(waterIncomeType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 1),
                500m,
                AccrualSources.Manual,
                "WATER-INVOICE",
                "Счет больше собранной суммы"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                waterIncomeType.Id,
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 1),
                100m,
                "WATER-INCOME",
                null),
            null,
            CancellationToken.None)).Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                "WATER-BANK-PAYMENT",
                "Оплата при отрицательной разнице"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var worksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.ExpenseTypeId == fixtures.ExpenseType.Id);
        Assert.Equal(1_000_000m, row.CollectedAmount);
        Assert.Equal(999_700m, row.Difference);
        Assert.Equal(300m, row.ExpenseAmount);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
    }

    [Theory]
    [InlineData(ExpensePaymentTypes.WithReceipt)]
    [InlineData(ExpensePaymentTypes.WithoutReceipt)]
    public async Task CreateExpenseAsync_AllowsCashExpenseWithAnyReceiptTypeWhenCashIsAvailable(string paymentType)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var fundBalanceBefore = fixtures.ExpenseFund.Balance;
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        var cashExpenseType = fixtures.ExpenseType;
        database.Context.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 10),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 500m,
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                cashExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                "CASH-ADVANCE",
                "Аванс из кассы",
                paymentType,
                ExpensePaymentSources.Cash,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(300m, result.Value!.Amount);
        Assert.Equal("Вода", result.Value.ExpenseTypeName);
        Assert.Equal(paymentType, result.Value.ExpensePaymentType);
        Assert.Equal(ExpensePaymentSources.Cash, result.Value.ExpensePaymentSource);
        Assert.Equal(fixtures.ExpenseFund.Id, result.Value.ExpenseFundId);
        Assert.Equal(fixtures.ExpenseFund.Id, Assert.Single(database.Context.SupplierAccruals).ExpenseFundId);
        var accrual = Assert.Single(database.Context.SupplierAccruals);
        Assert.Equal(300m, accrual.Amount);
        Assert.Equal(result.Value.Id, accrual.SourceFinancialOperationId);
        Assert.Equal(result.Value.AccountingMonth, accrual.AccountingMonth);
        Assert.Equal("CASH-ADVANCE", accrual.DocumentNumber);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.atomic_cash_expense_created");
        Assert.Equal("finance.atomic_cash_expense_created", audit.Action);
        Assert.Contains("Атомарно созданы стоимость и оплата выплаты", audit.Summary, StringComparison.Ordinal);
        var worksheet = await service.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)), CancellationToken.None);
        Assert.True(worksheet.Succeeded);
        Assert.Equal(0m, worksheet.Value!.BankAmount);
        Assert.Equal(200m, worksheet.Value.CashAmount);
        var row = Assert.Single(worksheet.Value.Rows, item => item.ExpenseTypeId == cashExpenseType.Id);
        Assert.Equal(300m, row.AccrualAmount);
        Assert.Equal(300m, row.ExpenseAmount);
        Assert.Equal(0m, row.ClosingDebt);
        Assert.Equal(0m, row.ClosingAdvance);
        Assert.Equal(fundBalanceBefore - 300m, fixtures.ExpenseFund.Balance);
        var fundOperation = Assert.Single(
            database.Context.FundOperations,
            operation => operation.SourceFinancialOperationId == result.Value.Id);
        Assert.Equal(FundOperationKinds.Withdraw, fundOperation.OperationKind);
        Assert.Equal(300m, fundOperation.Amount);
        Assert.False(fundOperation.IsCanceled);
    }

    [Fact]
    public async Task CreateExpenseAsync_RollsBackOperationAccrualAndAuditWhenAccrualInsertFails()
    {
        var interceptor = new SupplierAccrualInsertFailureInterceptor();
        await using var database = await TestDatabase.CreateAsync(interceptor);
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        var cashExpenseType = fixtures.ExpenseType;
        database.Context.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 10),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 500m,
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id
            });
        await database.Context.SaveChangesAsync();
        var operationCountBefore = await database.Context.FinancialOperations.CountAsync();
        var accrualCountBefore = await database.Context.SupplierAccruals.CountAsync();
        var auditCountBefore = await database.Context.AuditEvents.CountAsync();
        interceptor.Enabled = true;

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => FinanceServiceTestFactory.Create(database.Context).CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                cashExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                "CASH-ROLLBACK",
                "Проверка отката",
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Cash,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        database.Context.ChangeTracker.Clear();
        Assert.Equal(operationCountBefore, await database.Context.FinancialOperations.CountAsync());
        Assert.Equal(accrualCountBefore, await database.Context.SupplierAccruals.CountAsync());
        Assert.Equal(auditCountBefore, await database.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task CreateExpenseAsync_DoesNotUseBankForCashExpenseWhenCashIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var cashExpenseType = fixtures.ExpenseType;
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                cashExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                1m,
                "CASH-NO-RECEIPT",
                null,
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Cash,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("cash_amount_insufficient", result.ErrorCode);
        Assert.Equal("В кассе недостаточно средств. Доступно 0.00.", result.ErrorMessage);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateExpenseAsync_RejectsInvalidLinksAndCashWithoutConfiguredFund()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherExpenseType = new ExpenseType { Name = "Ремонт", Code = "repair" };
        var episodicSupplier = new Supplier
        {
            Name = "Разовый подрядчик",
            GroupId = fixtures.Supplier.GroupId
        };
        database.Context.AddRange(otherExpenseType, episodicSupplier);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = SeededBankAmount + 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();

        var mismatch = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                otherExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "UNLINKED-ARTICLE",
                null,
                ExpensePaymentTypes.WithReceipt),
            Guid.NewGuid(),
            CancellationToken.None);
        var invalidType = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "INVALID-PAYMENT-TYPE",
                null,
                "cash"),
            Guid.NewGuid(),
            CancellationToken.None);
        var invalidSource = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "INVALID-SOURCE",
                null,
                ExpensePaymentTypes.WithReceipt,
                "wallet",
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);
        var missingCashFund = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                episodicSupplier.Id,
                otherExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "MISSING-CASH-FUND",
                null,
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Cash),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(mismatch.Succeeded);
        Assert.Equal("supplier_expense_type_mismatch", mismatch.ErrorCode);
        Assert.False(invalidType.Succeeded);
        Assert.Equal("expense_payment_type_invalid", invalidType.ErrorCode);
        Assert.False(invalidSource.Succeeded);
        Assert.Equal("expense_payment_source_invalid", invalidSource.ErrorCode);
        Assert.False(missingCashFund.Succeeded);
        Assert.Equal("supplier_service_not_configured", missingCashFund.ErrorCode);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task ExpenseWithoutReceipt_KeepsGeneratedAccrualInSyncThroughUpdateCancelAndRestore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = SeededBankAmount + 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var fundBalanceBefore = fixtures.ExpenseFund.Balance;
        var service = FinanceServiceTestFactory.Create(database.Context);

        var created = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "SYNC-CASH",
                "Исходный комментарий",
                ExpensePaymentTypes.WithoutReceipt,
                ExpensePaymentSources.Cash,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(fixtures.ExpenseFund.Id, created.Value!.ExpenseFundId);
        Assert.Equal(fixtures.ExpenseFund.Name, created.Value.ExpenseFundName);
        Assert.Equal(fundBalanceBefore - 100m, fixtures.ExpenseFund.Balance);
        var linkedFundOperation = await database.Context.FundOperations
            .SingleAsync(operation => operation.SourceFinancialOperationId == created.Value.Id);
        Assert.Equal(100m, linkedFundOperation.Amount);
        Assert.False(linkedFundOperation.IsCanceled);

        var updated = await service.UpdateExpenseAsync(
            created.Value!.Id,
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 7, 1),
                125m,
                "SYNC-CASH-UPDATED",
                "Обновленный комментарий",
                ExpensePaymentTypes.WithoutReceipt,
                ExpensePaymentSources.Cash,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(updated.Succeeded);
        Assert.Equal(fixtures.ExpenseFund.Id, updated.Value!.ExpenseFundId);
        Assert.Equal(fundBalanceBefore - 125m, fixtures.ExpenseFund.Balance);
        Assert.Equal(125m, linkedFundOperation.Amount);
        Assert.False(linkedFundOperation.IsCanceled);
        var linkedAccrual = await database.Context.SupplierAccruals.SingleAsync(accrual => accrual.SourceFinancialOperationId == created.Value.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), linkedAccrual.AccountingMonth);
        Assert.Equal(125m, linkedAccrual.Amount);
        Assert.Equal("SYNC-CASH-UPDATED", linkedAccrual.DocumentNumber);
        Assert.Equal("Обновленный комментарий", linkedAccrual.Comment);

        var canceled = await service.CancelOperationAsync(
            created.Value.Id,
            new CancelFinanceEntryRequest("Проверка синхронной отмены"),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(canceled.Succeeded);
        Assert.True(linkedAccrual.IsCanceled);
        Assert.Equal(fundBalanceBefore, fixtures.ExpenseFund.Balance);
        Assert.True(linkedFundOperation.IsCanceled);

        var restored = await service.RestoreOperationAsync(created.Value.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.True(restored.Succeeded);
        Assert.False(linkedAccrual.IsCanceled);
        Assert.Equal(fundBalanceBefore - 125m, fixtures.ExpenseFund.Balance);
        Assert.False(linkedFundOperation.IsCanceled);

        var converted = await service.UpdateExpenseAsync(
            created.Value.Id,
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 7, 1),
                125m,
                "SYNC-CASH-UPDATED",
                "Теперь с чеком",
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Bank,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(converted.Succeeded);
        Assert.Equal(ExpensePaymentTypes.WithReceipt, converted.Value!.ExpensePaymentType);
        Assert.Equal(ExpensePaymentSources.Bank, converted.Value.ExpensePaymentSource);
        Assert.Equal(fixtures.ExpenseFund.Id, converted.Value.ExpenseFundId);
        Assert.True(linkedAccrual.IsCanceled);
        Assert.Equal(fundBalanceBefore - 125m, fixtures.ExpenseFund.Balance);
        Assert.False(linkedFundOperation.IsCanceled);
        Assert.Equal(125m, linkedFundOperation.Amount);

        var returnedToCash = await service.UpdateExpenseAsync(
            created.Value.Id,
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 7, 1),
                125m,
                "SYNC-CASH-UPDATED",
                "Снова из кассы",
                ExpensePaymentTypes.WithoutReceipt,
                ExpensePaymentSources.Cash),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(returnedToCash.Succeeded, returnedToCash.ErrorMessage);
        Assert.Equal(ExpensePaymentSources.Cash, returnedToCash.Value!.ExpensePaymentSource);
        Assert.Equal(fixtures.ExpenseFund.Id, returnedToCash.Value.ExpenseFundId);
        Assert.False(linkedAccrual.IsCanceled);
        Assert.False(linkedFundOperation.IsCanceled);
        Assert.Equal(fundBalanceBefore - 125m, fixtures.ExpenseFund.Balance);
    }

    [Theory]
    [InlineData(ExpensePaymentSources.Bank)]
    [InlineData(ExpensePaymentSources.Cash)]
    public async Task CreateExpenseAsync_RejectsPaymentAboveConfiguredExpenseFundBalance(string paymentSource)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var openingFundOperation = await database.Context.FundOperations
            .SingleAsync(operation => operation.FundId == fixtures.ExpenseFund.Id);
        fixtures.ExpenseFund.Balance = 50m;
        openingFundOperation.Amount = 50m;
        openingFundOperation.BalanceAfter = 50m;
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = SeededBankAmount + 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "FUND-LIMIT",
                null,
                ExpensePaymentTypes.WithReceipt,
                paymentSource,
                fixtures.ExpenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.Equal(50m, fixtures.ExpenseFund.Balance);
        Assert.DoesNotContain(
            database.Context.FinancialOperations,
            operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.DoesNotContain(
            database.Context.FundOperations,
            operation => operation.SourceFinancialOperationId.HasValue);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task GetGarageFullPaymentQuoteAsync_IncludesEveryOutstandingDebtExactlyOnce()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 100m;
        var accrual = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 9, 20),
            OverdueFromDate = new DateOnly(2026, 10, 21),
            Amount = 500m,
            Source = "full-payment-quote-test"
        };
        database.Context.Accruals.Add(accrual);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 8, 15),
                new DateOnly(2026, 8, 1),
                200m,
                "PKO-full-payment-quote",
                null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetGarageFullPaymentQuoteAsync(fixtures.Garage.Id, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(400m, result.Value!.TotalAmount);
        Assert.Collection(
            result.Value.Lines,
            opening =>
            {
                Assert.True(opening.IsOpeningDebt);
                Assert.Null(opening.IncomeTypeId);
                Assert.Equal(100m, opening.OutstandingAmount);
            },
            line =>
            {
                Assert.False(line.IsOpeningDebt);
                Assert.Equal(fixtures.IncomeType.Id, line.IncomeTypeId);
                Assert.Equal(accrual.AccountingMonth, line.AccountingMonth);
                Assert.Equal(300m, line.OutstandingAmount);
            });
    }

    [Fact]
    public async Task GetGarageFullPaymentQuoteAsync_AppliesExcessAllocationAfterAccrualReduction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var overpaidAccrual = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 8, 20),
            OverdueFromDate = new DateOnly(2026, 9, 20),
            Amount = 93.22m,
            Source = "recalculated-after-payment"
        };
        var outstandingAccrual = new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 9, 20),
            OverdueFromDate = new DateOnly(2026, 10, 21),
            Amount = 100m,
            Source = "full-payment-after-recalculation"
        };
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            OperationDate = new DateOnly(2026, 7, 15),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 100m
        };
        database.Context.AddRange(
            overpaidAccrual,
            outstandingAccrual,
            payment,
            new AccrualPaymentAllocation
            {
                Accrual = overpaidAccrual,
                FinancialOperation = payment,
                Amount = 100m
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetGarageFullPaymentQuoteAsync(fixtures.Garage.Id, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(93.22m, result.Value!.TotalAmount);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(93.22m, line.OutstandingAmount);
        Assert.Equal(outstandingAccrual.AccountingMonth, line.AccountingMonth);
    }

    [Fact]
    public async Task CreateIncomeAsync_AllocatesOrdinaryPaymentToSelectedAccountingMonthBeforeOlderDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = new Garage { Number = "PAYMENT-MONTH", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = "Помесячная услуга" };
        var january = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 1, 1),
            DueDate = new DateOnly(2026, 1, 31),
            OverdueFromDate = new DateOnly(2026, 3, 1),
            Amount = 300m,
            Source = AccrualSources.Manual
        };
        var february = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 2, 1),
            DueDate = new DateOnly(2026, 2, 28),
            OverdueFromDate = new DateOnly(2026, 4, 1),
            Amount = 300m,
            Source = AccrualSources.Manual
        };
        database.Context.AddRange(january, february);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                garage.Id,
                incomeType.Id,
                new DateOnly(2026, 2, 20),
                new DateOnly(2026, 2, 1),
                100m,
                "PAYMENT-MONTH-FEB",
                null),
            null,
            CancellationToken.None);

        Assert.True(payment.Succeeded, payment.ErrorMessage);
        var allocation = Assert.Single(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.FinancialOperationId == payment.Value!.Id);
        Assert.Equal(february.Id, allocation.AccrualId);
        Assert.Equal(100m, allocation.Amount);
        Assert.DoesNotContain(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.FinancialOperationId == payment.Value!.Id && item.AccrualId == january.Id);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsConfirmedBankPaymentToMakeExpenseFundNegative()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var openingFundOperation = await database.Context.FundOperations
            .SingleAsync(operation => operation.FundId == fixtures.ExpenseFund.Id);
        fixtures.ExpenseFund.Balance = 50m;
        openingFundOperation.Amount = 50m;
        openingFundOperation.BalanceAfter = 50m;
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = SeededBankAmount + 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                100m,
                "FUND-NEGATIVE-CONFIRMED",
                null,
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Bank,
                fixtures.ExpenseFund.Id,
                null,
                true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Value!.NegativeFundBalanceConfirmed);
        Assert.Equal(-50m, fixtures.ExpenseFund.Balance);
        var disbursement = Assert.Single(database.Context.FundOperations, operation => operation.SourceFinancialOperationId == result.Value.Id);
        Assert.Equal(-50m, disbursement.BalanceAfter);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
        Assert.Contains("negativeFundBalanceConfirmed", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("true", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreOperationAsync_RejectsPositiveTailConfirmationWhenLaterRestoreWouldBecomeNegative()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var openingFundOperation = await database.Context.FundOperations
            .SingleAsync(operation => operation.FundId == fixtures.ExpenseFund.Id);
        fixtures.ExpenseFund.Balance = 100m;
        openingFundOperation.Amount = 100m;
        openingFundOperation.BalanceAfter = 100m;
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = SeededBankAmount + 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var earlyExpense = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                50m,
                "FUND-NEGATIVE-RESTORE-EARLY",
                null,
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Bank,
                fixtures.ExpenseFund.Id,
                null,
                true),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(earlyExpense.Succeeded, earlyExpense.ErrorMessage);
        Assert.False(earlyExpense.Value!.NegativeFundBalanceConfirmed);
        Assert.Equal(50m, fixtures.ExpenseFund.Balance);

        var laterExpense = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 6, 1),
                100m,
                "FUND-NEGATIVE-RESTORE-LATER",
                null,
                ExpensePaymentTypes.WithReceipt,
                ExpensePaymentSources.Bank,
                fixtures.ExpenseFund.Id,
                null,
                true),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(laterExpense.Succeeded, laterExpense.ErrorMessage);
        Assert.True(laterExpense.Value!.NegativeFundBalanceConfirmed);
        Assert.Equal(-50m, fixtures.ExpenseFund.Balance);

        Assert.True((await service.CancelOperationAsync(
            earlyExpense.Value.Id,
            new CancelFinanceEntryRequest("Проверка восстановления раннего списания"),
            Guid.NewGuid(),
            CancellationToken.None)).Succeeded);
        Assert.Equal(0m, fixtures.ExpenseFund.Balance);

        var restored = await service.RestoreOperationAsync(
            earlyExpense.Value.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(restored.Succeeded);
        Assert.Equal("fund_balance_insufficient", restored.ErrorCode);
        Assert.Equal(0m, fixtures.ExpenseFund.Balance);
        var earlyDisbursement = Assert.Single(
            database.Context.FundOperations,
            operation => operation.SourceFinancialOperationId == earlyExpense.Value.Id);
        var laterDisbursement = Assert.Single(
            database.Context.FundOperations,
            operation => operation.SourceFinancialOperationId == laterExpense.Value!.Id);
        Assert.True(earlyDisbursement.IsCanceled);
        Assert.Equal(100m, earlyDisbursement.BalanceBefore);
        Assert.Equal(100m, earlyDisbursement.BalanceAfter);
        Assert.Equal(100m, laterDisbursement.BalanceBefore);
        Assert.Equal(0m, laterDisbursement.BalanceAfter);
        Assert.DoesNotContain(
            database.Context.AuditEvents,
            item => item.Action == "fund.expense_disbursement_restored");
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsSupplierlessCashExpenseWithOptionalCounterparty()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.Add(OpeningCashBalance(SeededBankAmount + 500m));
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                null,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                "CASH-FREE-RECIPIENT",
                null,
                ExpensePaymentTypes.WithoutReceipt,
                ExpensePaymentSources.Cash,
                null,
                "Разовый исполнитель"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.Value!.SupplierId);
        Assert.Equal("Разовый исполнитель", result.Value.CounterpartyName);
        Assert.Equal(ExpensePaymentSources.Cash, result.Value.ExpensePaymentSource);
        Assert.DoesNotContain(database.Context.SupplierAccruals, accrual => accrual.SourceFinancialOperationId == result.Value.Id);
        Assert.DoesNotContain(database.Context.FundOperations, operation => operation.SourceFinancialOperationId == result.Value.Id);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
        Assert.Equal("Разовый исполнитель", audit.RelatedCounterpartyName);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsSupplierlessCashExpenseWithoutCounterpartyName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.Add(OpeningCashBalance(SeededBankAmount + 500m));
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 500m,
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                null,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                300m,
                "CASH-WITHOUT-RECIPIENT",
                null,
                ExpensePaymentTypes.WithoutReceipt,
                ExpensePaymentSources.Cash,
                null,
                "   "),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.Value!.SupplierId);
        Assert.Null(result.Value.CounterpartyName);
        Assert.Equal(ExpensePaymentSources.Cash, result.Value.ExpensePaymentSource);
        Assert.DoesNotContain(database.Context.SupplierAccruals, accrual => accrual.SourceFinancialOperationId == result.Value.Id);
        Assert.DoesNotContain(database.Context.FundOperations, operation => operation.SourceFinancialOperationId == result.Value.Id);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
        Assert.Null(audit.RelatedCounterpartyName);
        Assert.Contains("получателю не указан", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupplierManualExpenseFundOverride_DrivesAccrualPaymentAndWorksheet()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var manualFund = new Fund
        {
            Name = "Ручной фонд поставщика",
            NormalizedName = "РУЧНОЙ ФОНД ПОСТАВЩИКА",
            Balance = 1000m
        };
        database.Context.Add(manualFund);
        fixtures.Supplier.ExpenseFund = manualFund;
        fixtures.Supplier.ExpenseFundId = manualFund.Id;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);

        var accrualRequest = new CreateSupplierAccrualRequest(
            fixtures.Supplier.Id,
            fixtures.ExpenseType.Id,
            month,
            400m,
            "manual",
            "INV-MANUAL-FUND",
            null);
        var accrual = await service.CreateSupplierAccrualAsync(
            accrualRequest,
            null,
            CancellationToken.None);
        Assert.True(accrual.Succeeded);
        Assert.Equal(manualFund.Id, accrual.Value!.ExpenseFundId);
        Assert.Equal("Ручной фонд поставщика", accrual.Value.ExpenseFundName);

        var updatedAccrual = await service.UpdateSupplierAccrualAsync(
            accrual.Value.Id,
            accrualRequest with { Amount = 450m, Comment = "Уточненная сумма" },
            null,
            CancellationToken.None);
        Assert.True(updatedAccrual.Succeeded);
        Assert.Equal(manualFund.Id, updatedAccrual.Value!.ExpenseFundId);
        Assert.Equal("Ручной фонд поставщика", updatedAccrual.Value.ExpenseFundName);

        var payment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), month, 250m, "RKO-MANUAL-FUND", null),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded);
        Assert.Equal(manualFund.Id, payment.Value!.ExpenseFundId);
        Assert.Equal("Ручной фонд поставщика", payment.Value.ExpenseFundName);
        Assert.Equal(750m, manualFund.Balance);
        Assert.Equal(SeededBankAmount, fixtures.ExpenseFund.Balance);
        database.Context.ChangeTracker.Clear();
        var worksheet = await service.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(month), CancellationToken.None);
        Assert.True(worksheet.Succeeded);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.SupplierId == fixtures.Supplier.Id && item.ExpenseTypeId == fixtures.ExpenseType.Id);
        Assert.Equal(manualFund.Id, row.ExpenseFundId);
        Assert.Equal("Ручной фонд поставщика", row.ExpenseFundName);
        Assert.Equal(1000m, row.CollectedAmount);
        Assert.Equal(750m, row.Difference);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        fixtures.Supplier.StartingBalance = 300m;
        await database.Context.SaveChangesAsync();
        await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 800m, "manual", "INV-replace", "Счет за месяц"),
            null,
            CancellationToken.None);
        var request = new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 250m, "RKO-replace", "Ошибочная выплата");
        var firstPayment = await service.CreateExpenseAsync(request, null, CancellationToken.None);
        Assert.True(firstPayment.Succeeded);
        var canceled = await service.CancelOperationAsync(
            firstPayment.Value!.Id,
            new CancelFinanceEntryRequest("Выплата заменена"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);

        var replacement = await service.CreateExpenseAsync(request with { Amount = 350m, Comment = "Корректная выплата" }, null, CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal(350m, replacement.Value!.Amount);
        Assert.Equal(1100m, replacement.Value.SupplierDebtBefore);
        Assert.Equal(750m, replacement.Value.SupplierDebtAfter);
        Assert.Equal(2, await database.Context.FinancialOperations.CountAsync());
        Assert.Equal(1, await database.Context.FinancialOperations.CountAsync(operation => operation.IsCanceled));
        var activeOperation = Assert.Single(await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None));
        Assert.Equal(350m, activeOperation.Amount);
    }

    [Fact]
    public async Task UpdateExpenseAsync_UpdatesOperationAndWritesBeforeAfterAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        fixtures.Supplier.StartingBalance = 300m;
        await database.Context.SaveChangesAsync();
        await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 900m, "manual", "INV-update", "Счет месяца"),
            null,
            CancellationToken.None);
        var created = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 250m, "RKO-old", null),
            null,
            CancellationToken.None);
        var actorUserId = Guid.NewGuid();

        var updated = await service.UpdateExpenseAsync(
            created.Value!.Id,
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 400m, "RKO-new", "Исправлена выплата"),
            actorUserId,
            CancellationToken.None);

        Assert.True(updated.Succeeded);
        Assert.Equal(400m, updated.Value!.Amount);
        Assert.Equal("RKO-new", updated.Value.DocumentNumber);
        Assert.Equal(1200m, updated.Value.SupplierDebtBefore);
        Assert.Equal(800m, updated.Value.SupplierDebtAfter);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("было 250.00 получателю Vodokanal от 20.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-old", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 400.00 получателю Vodokanal от 21.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-new", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Исправлена выплата", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("financial_operation", metadata.RootElement.GetProperty("financeEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Дата операции", changedFields, StringComparison.Ordinal);
        Assert.Contains("Сумма", changedFields, StringComparison.Ordinal);
        Assert.Contains("Документ", changedFields, StringComparison.Ordinal);
        Assert.Contains("Комментарий", changedFields, StringComparison.Ordinal);
        Assert.Equal("4", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Fact]
    public async Task UpdateExpenseAsync_DoesNotIncreasePaymentAboveAvailableBankAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        database.Context.Add(new CashBankTransfer
        {
            TransferDate = new DateOnly(2026, 6, 1),
            Amount = 300m,
            Comment = "Сумма на банковском счете",
            CreatedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 250m, "RKO-bank-limit", null),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateExpenseAsync(
            created.Value!.Id,
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 300.01m, "RKO-bank-limit-new", "Сверх банка"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(updated.Succeeded);
        Assert.Equal("bank_amount_insufficient", updated.ErrorCode);
        var stored = await database.Context.FinancialOperations.SingleAsync(operation => operation.Id == created.Value.Id);
        Assert.Equal(250m, stored.Amount);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateExpenseAsync_DoesNotConvertBankPaymentToCashAboveAvailableCash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var cashExpenseType = fixtures.ExpenseType;
        database.Context.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 10),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 200m,
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 100m, "RKO-bank-to-cash", null),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateExpenseAsync(
            created.Value!.Id,
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, cashExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 200.01m, "RKO-bank-to-cash-new", "Сверх кассы", ExpensePaymentTypes.WithoutReceipt),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(updated.Succeeded);
        Assert.Equal("cash_amount_insufficient", updated.ErrorCode);
        var stored = await database.Context.FinancialOperations.SingleAsync(operation => operation.Id == created.Value.Id);
        Assert.Equal(fixtures.ExpenseType.Id, stored.ExpenseTypeId);
        Assert.Equal(100m, stored.Amount);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateExpenseAsync_ReturnsSupplierDebtBeforeAndAfterPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 300m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 800m, "manual", "INV-1", "Счет за месяц"),
            null,
            CancellationToken.None);

        var firstPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 250m, "RKO-1", "Оплата поставщику"),
            null,
            CancellationToken.None);
        var secondPayment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 350m, "RKO-2", "Доплата"),
            null,
            CancellationToken.None);

        Assert.True(firstPayment.Succeeded);
        Assert.Equal(1100m, firstPayment.Value!.SupplierDebtBefore);
        Assert.Equal(850m, firstPayment.Value.SupplierDebtAfter);
        Assert.True(secondPayment.Succeeded);
        Assert.Equal(850m, secondPayment.Value!.SupplierDebtBefore);
        Assert.Equal(500m, secondPayment.Value.SupplierDebtAfter);

        var history = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, "expense", null), CancellationToken.None);
        Assert.Contains(history, item => item.DocumentNumber == "RKO-2" && item.SupplierDebtBefore == 850m && item.SupplierDebtAfter == 500m);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsIncomeExpenseAndBalance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "1", null), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "2", null), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetSummaryAsync(new FinancialOperationListRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, null), CancellationToken.None);

        Assert.Equal(1500m, result.IncomeTotal);
        Assert.Equal(400m, result.ExpenseTotal);
        Assert.Equal(2000m, result.AccrualTotal);
        Assert.Equal(1100m, result.Balance);
        Assert.Equal(500m, result.Debt);
        Assert.Equal(2, result.OperationCount);
        Assert.Equal(1, result.IncomeCount);
        Assert.Equal(1, result.ExpenseCount);
        Assert.Equal(1, result.AccrualCount);
        Assert.Equal(0, result.SupplierAccrualCount);
        Assert.Equal(0, result.MeterReadingCount);
    }

    [Fact]
    public async Task GetSummaryAsync_UsesOneAggregateSelectAndReturnsSectionCounts()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "IN-1", null), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "OUT-1", null), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 400m, "regular", "SUP-1", null), null, CancellationToken.None);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 10m, null), null, CancellationToken.None);
        commandCounter.Reset();

        var result = await service.GetSummaryAsync(
            new FinancialOperationListRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, null),
            CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(1, result.IncomeCount);
        Assert.Equal(1, result.ExpenseCount);
        Assert.Equal(1, result.AccrualCount);
        Assert.Equal(1, result.SupplierAccrualCount);
        Assert.Equal(1, result.MeterReadingCount);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersCombinedSectionCountsByPeriodAndSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 400m, "regular", "SUP-MATCH", "supplier marker"), null, CancellationToken.None);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 10m, "meter marker"), null, CancellationToken.None);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 20m, "meter marker"), null, CancellationToken.None);

        var supplierResult = await service.GetSummaryAsync(
            new FinancialOperationListRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, "supplier marker"),
            CancellationToken.None);
        var meterResult = await service.GetSummaryAsync(
            new FinancialOperationListRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, "meter marker"),
            CancellationToken.None);

        Assert.Equal(1, supplierResult.SupplierAccrualCount);
        Assert.Equal(0, supplierResult.MeterReadingCount);
        Assert.Equal(0, meterResult.SupplierAccrualCount);
        Assert.Equal(1, meterResult.MeterReadingCount);
    }

    [Fact]
    public async Task GetSummaryAsync_SearchesEpisodicCounterpartyAndTreatsWildcardsLiterally()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 20),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 300m,
                CounterpartyName = "Разовый исполнитель %_"
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 21),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 400m,
                CounterpartyName = "Разовый исполнитель без маркера"
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetSummaryAsync(
            new FinancialOperationListRequest(null, null, null, "%_"),
            CancellationToken.None);

        Assert.Equal(1, result.OperationCount);
        Assert.Equal(1, result.ExpenseCount);
        Assert.Equal(300m, result.ExpenseTotal);
    }

    [Fact]
    public async Task GetSummaryAsync_AppliesOperationKindWithoutHidingAccrualTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "IN-FILTER", null), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "OUT-FILTER", null), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetSummaryAsync(
            new FinancialOperationListRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "income", null),
            CancellationToken.None);

        Assert.Equal(1500m, result.IncomeTotal);
        Assert.Equal(0m, result.ExpenseTotal);
        Assert.Equal(2000m, result.AccrualTotal);
        Assert.Equal(1, result.OperationCount);
        Assert.Equal(1, result.IncomeCount);
        Assert.Equal(0, result.ExpenseCount);
        Assert.Equal(1, result.AccrualCount);
    }

    [Fact]
    public async Task GetOperationsAsync_SearchesByGarageSupplierDocumentAndComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "DOC-12", "Оплата по квитанции"), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "DOC-20", "Компенсация поставщику"), null, CancellationToken.None);
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 6, 21),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 100m,
            CounterpartyName = "Разовый исполнитель %_"
        });
        await database.Context.SaveChangesAsync();

        var garageResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "12"), CancellationToken.None);
        var supplierResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "vodokanal"), CancellationToken.None);
        var commentResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "квитанции"), CancellationToken.None);
        var counterpartyResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "разовый исполнитель"), CancellationToken.None);
        var literalWildcardResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "%_"), CancellationToken.None);

        Assert.Single(garageResult);
        Assert.Equal("income", garageResult[0].OperationKind);
        Assert.Single(supplierResult);
        Assert.Equal("expense", supplierResult[0].OperationKind);
        Assert.Single(commentResult);
        Assert.Equal("Оплата по квитанции", commentResult[0].Comment);
        Assert.Equal(counterpartyResult, literalWildcardResult);
        Assert.Equal("Разовый исполнитель %_", Assert.Single(counterpartyResult).CounterpartyName);
    }

    [Fact]
    public async Task CreateAccrualAsync_CreatesManualAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 15), 700m, "manual", "Целевой сбор"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal(2026, result.Value.AccountingYear);
        Assert.Equal("manual", result.Value.Source);
        Assert.Equal("12", result.Value.GarageNumber);
        Assert.Equal(new DateOnly(2026, 6, 30), result.Value.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.Value.OverdueFromDate);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано начисление 700.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Целевой сбор", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("учетный год 2026", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAccrualAsync_CreatesArbitraryPenaltyWithReasonAndAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var penaltyIncomeType = new IncomeType
        {
            Name = "Штраф",
            Code = "penalty",
            IsSystem = true
        };
        database.Context.IncomeTypes.Add(penaltyIncomeType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(
                fixtures.Garage.Id,
                penaltyIncomeType.Id,
                new DateOnly(2026, 7, 23),
                1234.56m,
                "manual",
                "Нарушение правил проезда"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(penaltyIncomeType.Id, result.Value!.IncomeTypeId);
        Assert.Equal("Штраф", result.Value.IncomeTypeName);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.AccountingMonth);
        Assert.Equal(1234.56m, result.Value.Amount);
        Assert.Equal("Нарушение правил проезда", result.Value.Comment);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(fixtures.Garage.Id.ToString(), audit.RelatedGarageId);
        Assert.Contains("Создано начисление 1 234.56", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("вид Штраф", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Нарушение правил проезда", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("1234.56", metadata.RootElement.GetProperty("amount").GetString());
        Assert.Equal("Штраф", metadata.RootElement.GetProperty("incomeTypeName").GetString());
    }

    [Fact]
    public async Task CreateIrregularAccrualAsync_UsesTemplateAmountAndOtherPaymentsDestination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var destinationFund = new Fund
        {
            Name = "Прочее",
            NormalizedName = "ПРОЧЕЕ",
            Balance = 0m
        };
        var otherPayments = new IncomeType
        {
            Name = "Переименованное назначение",
            Code = "other_payments",
            IsSystem = true,
            DestinationFund = destinationFund
        };
        var parkingCard = new IrregularPayment { Name = "Карта доступа", Amount = 1250.555m };
        var lockRepair = new IrregularPayment { Name = "Ремонт замка", Amount = 700m };
        database.Context.AddRange(destinationFund, otherPayments, parkingCard, lockRepair);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var first = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, parkingCard.Id, "Подменённое основание", 1m, new DateOnly(2026, 8, 17), "Выдана новая карта"),
            Guid.NewGuid(),
            CancellationToken.None);
        var second = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, lockRepair.Id, lockRepair.Name, lockRepair.Amount, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);
        var duplicate = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, parkingCard.Id, parkingCard.Name, parkingCard.Amount, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1250.56m, first.Value!.Amount);
        Assert.Equal(otherPayments.Id, first.Value.IncomeTypeId);
        Assert.Equal("Переименованное назначение", first.Value.IncomeTypeName);
        Assert.Equal(parkingCard.Id, first.Value.IrregularPaymentId);
        Assert.Equal("Карта доступа", first.Value.IrregularPaymentName);
        Assert.Equal("Карта доступа", first.Value.Basis);
        Assert.Equal(new DateOnly(2026, 8, 1), first.Value.AccountingMonth);
        Assert.False(duplicate.Succeeded);
        Assert.Equal("accrual_duplicate", duplicate.ErrorCode);
        var stored = await database.Context.Accruals.SingleAsync(item => item.Id == first.Value.Id);
        Assert.Equal(otherPayments.Id, stored.IncomeTypeId);
        Assert.Equal(parkingCard.Id, stored.IrregularPaymentId);
        Assert.Equal("Карта доступа", stored.Basis);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.irregular_accrual_created" && item.EntityId == first.Value.Id.ToString());
        Assert.Contains("Карта доступа", audit.Summary, StringComparison.Ordinal);
        Assert.Contains(AuditTextMasker.Mask(destinationFund.Id.ToString())!, audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IrregularPayment_AppearsUntilFullyPaidAndDisappearsWhenInactive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var destinationFund = new Fund { Name = "Other", NormalizedName = "OTHER" };
        var otherPayments = new IncomeType
        {
            Name = "Other payments",
            Code = "other_payments",
            IsSystem = true,
            DestinationFund = destinationFund
        };
        var template = new IrregularPayment { Name = "Access card", Amount = 1_000m };
        database.Context.AddRange(destinationFund, otherPayments, template);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 8, 1);

        var accrual = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, template.Id, template.Name, template.Amount, month, null),
            null,
            CancellationToken.None);
        Assert.True(accrual.Succeeded);

        var initialWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(month, month),
            CancellationToken.None);
        var initialRow = Assert.Single(initialWorksheet.Value!.Rows, row => row.IrregularPaymentId == template.Id);
        Assert.Equal((1_000m, 1_000m), (initialRow.Debt, initialRow.IrregularPaymentRemainingAmount));

        var partialPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherPayments.Id,
                new DateOnly(2026, 8, 11),
                month,
                400m,
                null,
                null,
                IrregularPaymentId: template.Id),
            null,
            CancellationToken.None);
        Assert.True(partialPayment.Succeeded);
        Assert.Equal(template.Id, await database.Context.FinancialOperations
            .Where(operation => operation.Id == partialPayment.Value!.Id)
            .Select(operation => operation.IrregularPaymentId)
            .SingleAsync());

        var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(month, month),
            CancellationToken.None);
        var partialRow = Assert.Single(partialWorksheet.Value!.Rows, row => row.IrregularPaymentId == template.Id);
        Assert.Equal((400m, 600m, 600m),
            (partialRow.IncomeAmount, partialRow.Debt, partialRow.IrregularPaymentRemainingAmount));

        var overpayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherPayments.Id,
                new DateOnly(2026, 8, 11),
                month,
                600.01m,
                null,
                null,
                IrregularPaymentId: template.Id),
            null,
            CancellationToken.None);
        Assert.False(overpayment.Succeeded);
        Assert.Equal("irregular_payment_amount_exceeds_remaining", overpayment.ErrorCode);

        template.IsActive = false;
        await database.Context.SaveChangesAsync();
        var inactiveWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(month, month),
            CancellationToken.None);
        Assert.DoesNotContain(inactiveWorksheet.Value!.Rows, row => row.IrregularPaymentId == template.Id);

        template.IsActive = true;
        await database.Context.SaveChangesAsync();
        var fullPayment = await service.CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(
                fixtures.Garage.Id,
                new DateOnly(2026, 8, 11),
                [new CreateFullGaragePaymentLineRequest(otherPayments.Id, month, 600m, null, IrregularPaymentId: template.Id)]),
            null,
            CancellationToken.None);
        Assert.True(fullPayment.Succeeded);
        Assert.Single(fullPayment.Value!.Operations);
        var storedFullPaymentTarget = await database.Context.FinancialOperations
            .Where(operation => operation.ReceiptBatchId == fullPayment.Value.ReceiptBatchId)
            .Select(operation => operation.IrregularPaymentId)
            .SingleAsync();
        Assert.Equal(template.Id, storedFullPaymentTarget);

        var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(month, month),
            CancellationToken.None);
        Assert.DoesNotContain(paidWorksheet.Value!.Rows, row => row.IrregularPaymentId == template.Id);
    }

    [Fact]
    public async Task CreateIrregularAccrualAsync_AcceptsCustomBasisAndAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var destinationFund = new Fund { Name = "Прочее", NormalizedName = "ПРОЧЕЕ" };
        var otherPayments = new IncomeType
        {
            Name = "Прочие оплаты",
            Code = "other_payments",
            IsSystem = true,
            DestinationFund = destinationFund
        };
        database.Context.AddRange(destinationFund, otherPayments);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(
                fixtures.Garage.Id,
                null,
                "  Замена пульта ворот  ",
                915.255m,
                new DateOnly(2026, 8, 17),
                "Выдан новый пульт"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.IrregularPaymentId);
        Assert.Null(result.Value.IrregularPaymentName);
        Assert.Equal("Замена пульта ворот", result.Value.Basis);
        Assert.Equal(915.26m, result.Value.Amount);
        var stored = await database.Context.Accruals.SingleAsync(item => item.Id == result.Value.Id);
        Assert.Equal("Замена пульта ворот", stored.Basis);
        Assert.Null(stored.IrregularPaymentId);
        var foundByBasis = await service.GetAccrualsAsync(
            new AccrualListRequest(null, null, "пульта", 10, null),
            CancellationToken.None);
        Assert.Equal(result.Value.Id, Assert.Single(foundByBasis).Id);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded);
        Assert.Equal(result.Value.Basis, Assert.Single(worksheet.Value!.Rows).IncomeTypeName);
        var income = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherPayments.Id,
                new DateOnly(2026, 8, 18),
                new DateOnly(2026, 8, 1),
                1_000m,
                null,
                null),
            actorUserId,
            CancellationToken.None);
        Assert.True(income.Succeeded);
        var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        Assert.True(paidWorksheet.Succeeded);
        var customRow = Assert.Single(paidWorksheet.Value!.Rows, row => row.IncomeTypeName == result.Value.Basis);
        Assert.Equal((915.26m, 0m), (customRow.IncomeAmount, customRow.AdvanceAmount));
        var advanceRow = Assert.Single(paidWorksheet.Value.Rows, row => row.IncomeTypeName == otherPayments.Name);
        Assert.Equal((0m, 84.74m), (advanceRow.IncomeAmount, advanceRow.AdvanceAmount));
        Assert.Equal(84.74m, paidWorksheet.Value.AdvanceTotal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.irregular_accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Замена пульта ворот", audit.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 100, "irregular_accrual_basis_required")]
    [InlineData("Основание", 0, "irregular_payment_amount_invalid")]
    public async Task CreateIrregularAccrualAsync_RejectsInvalidCustomValues(
        string basis,
        decimal amount,
        string expectedErrorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var destinationFund = new Fund { Name = "Прочее", NormalizedName = "ПРОЧЕЕ" };
        database.Context.AddRange(
            destinationFund,
            new IncomeType
            {
                Name = "Прочие оплаты",
                Code = "other_payments",
                IsSystem = true,
                DestinationFund = destinationFund
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, null, basis, amount, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(database.Context.Accruals.Where(item => item.Basis != null));
    }

    [Theory]
    [InlineData(false, false, "irregular_payment_not_found")]
    [InlineData(true, true, "other_payments_destination_not_configured")]
    public async Task CreateIrregularAccrualAsync_RejectsUnavailableTemplateOrDestination(
        bool templateIsActive,
        bool addIncomeType,
        string expectedErrorCode)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var payment = new IrregularPayment { Name = "Разовая услуга", Amount = 500m, IsActive = templateIsActive };
        database.Context.Add(payment);
        if (addIncomeType)
        {
            database.Context.Add(new IncomeType
            {
                Name = "Прочие оплаты без фонда",
                Code = "other_payments",
                IsSystem = true
            });
        }
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(fixtures.Garage.Id, payment.Id, payment.Name, payment.Amount, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Empty(database.Context.Accruals.Where(item => item.IrregularPaymentId == payment.Id));
    }

    [Fact]
    public async Task CreateDebtTransferAsync_CreatesAndAccumulatesSystemAccrualWithAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateDebtTransferAsync(
            new CreateDebtTransferRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 20), 1700m, "Первичный перенос"),
            actorUserId,
            CancellationToken.None);
        var updated = await service.CreateDebtTransferAsync(
            new CreateDebtTransferRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), 300m, "Доначислили остаток"),
            actorUserId,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.True(updated.Succeeded);
        Assert.Equal(created.Value!.Id, updated.Value!.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), updated.Value.AccountingMonth);
        Assert.Equal(2000m, updated.Value.Amount);
        Assert.Equal("Перенос задолженности", updated.Value.IncomeTypeName);
        Assert.Equal(AccrualSources.DebtTransfer, updated.Value.Source);
        var incomeType = Assert.Single(database.Context.IncomeTypes, item => item.Code == "debt_transfer");
        Assert.True(incomeType.IsSystem);
        var createAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.debt_transfer_created");
        var updateAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.debt_transfer_updated");
        Assert.Equal(actorUserId, createAudit.ActorUserId);
        Assert.Equal(actorUserId, updateAudit.ActorUserId);
        Assert.Contains("Создан перенос задолженности 1 700.00", createAudit.Summary, StringComparison.Ordinal);
        Assert.Contains("из 06.2026 в 07.2026", createAudit.Summary, StringComparison.Ordinal);
        Assert.Contains("добавлено 300.00", updateAudit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(updateAudit.MetadataJson!);
        Assert.Equal("debt_transfer", metadata.RootElement.GetProperty("source").GetString());
        Assert.Equal("2000", metadata.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task CreateAccrualAsync_AllowsManualAccrualWithoutComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.Value!.Comment);
        Assert.Single(database.Context.Accruals);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
    }

    [Fact]
    public async Task UpdateAccrualAsync_AllowsRegularAccrualCorrectionWithoutComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null),
            null,
            CancellationToken.None);

        var result = await service.UpdateAccrualAsync(
            created.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 750m, "regular", " "),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(750m, result.Value!.Amount);
        Assert.Null(result.Value.Comment);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated");
    }

    [Fact]
    public async Task UpdateAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", "Исходная ручная сумма"),
            null,
            CancellationToken.None);
        var persisted = await database.Context.Accruals.SingleAsync(item => item.Id == created.Value!.Id);
        persisted.DueDateNeedsReview = true;
        persisted.DueDateReviewReason = "historical_source_unknown";
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateAccrualAsync(
            created.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 750m, "manual", "Исправили после сверки"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(persisted.DueDateNeedsReview);
        Assert.Null(persisted.DueDateReviewReason);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("было 700.00 по гаражу 12 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; комментарий Исходная ручная сумма", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 750.00 по гаражу 12 за 07.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; комментарий Исправили после сверки", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("accrual", metadata.RootElement.GetProperty("financeEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Расчетный месяц", changedFields, StringComparison.Ordinal);
        Assert.Contains("Сумма", changedFields, StringComparison.Ordinal);
        Assert.Contains("Комментарий", changedFields, StringComparison.Ordinal);
        Assert.Contains("Срок требует сверки", changedFields, StringComparison.Ordinal);
        Assert.Equal("4", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Fact]
    public async Task CreateAccrualAsync_RejectsDuplicateGarageTypeMonthAndSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null);
        await service.CreateAccrualAsync(request, null, CancellationToken.None);

        var result = await service.CreateAccrualAsync(request with { Amount = 800m }, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task AnnualRegularAccrualDuplicateValidation_UsesAccountingYearForCreateUpdateAndRestore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var first = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 1, 1), 700m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);

        var duplicateCreate = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 800m, "regular", null),
            null,
            CancellationToken.None);
        Assert.False(duplicateCreate.Succeeded);
        Assert.Equal("accrual_duplicate", duplicateCreate.ErrorCode);
        Assert.Contains("за 2026 год", duplicateCreate.ErrorMessage, StringComparison.Ordinal);

        var nextYear = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2027, 1, 1), 900m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(nextYear.Succeeded, nextYear.ErrorMessage);
        var duplicateUpdate = await service.UpdateAccrualAsync(
            nextYear.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 8, 1), 900m, "regular", "Исправление года"),
            null,
            CancellationToken.None);
        Assert.False(duplicateUpdate.Succeeded);
        Assert.Equal("accrual_duplicate", duplicateUpdate.ErrorCode);
        Assert.Contains("за 2026 год", duplicateUpdate.ErrorMessage, StringComparison.Ordinal);

        var canceled = await service.CancelAccrualAsync(
            first.Value!.Id,
            new CancelFinanceEntryRequest("Заменили начисление"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        var replacement = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 9, 1), 750m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);

        var duplicateRestore = await service.RestoreAccrualAsync(first.Value.Id, null, CancellationToken.None);
        Assert.False(duplicateRestore.Succeeded);
        Assert.Equal("accrual_duplicate", duplicateRestore.ErrorCode);
        Assert.Contains("за 2026 год", duplicateRestore.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAccrualAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null);
        var firstAccrual = await service.CreateAccrualAsync(request, null, CancellationToken.None);
        Assert.True(firstAccrual.Succeeded);
        var canceled = await service.CancelAccrualAsync(
            firstAccrual.Value!.Id,
            new CancelFinanceEntryRequest("Начисление заменено"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);

        var replacement = await service.CreateAccrualAsync(request with { Amount = 800m }, null, CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal(800m, replacement.Value!.Amount);
        Assert.Equal(2, await database.Context.Accruals.CountAsync());
        Assert.Equal(1, await database.Context.Accruals.CountAsync(accrual => accrual.IsCanceled));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(800m, summary.AccrualTotal);
        Assert.Equal(800m, summary.Debt);
        Assert.Equal(1, summary.AccrualCount);
    }

    [Fact]
    public async Task CancelAccrualAsync_CancelsAccrualAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", "Ручная корректировка"),
            null,
            CancellationToken.None);

        var result = await service.CancelAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Начислено не тому гаражу"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCanceled);
        Assert.Contains("Отменено: Начислено не тому гаражу", result.Value.Comment);
        Assert.Empty(await service.GetAccrualsAsync(new AccrualListRequest(null, null, null), CancellationToken.None));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(0m, summary.AccrualTotal);
        Assert.Equal(0m, summary.Debt);
        Assert.Equal(0, summary.AccrualCount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_canceled");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Отменено начисление 700.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Начислено не тому гаражу", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAccrualAsync_RestoresCanceledAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", "Ручная корректировка"),
            null,
            CancellationToken.None);
        await service.CancelAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Временно исключили"), null, CancellationToken.None);

        var result = await service.RestoreAccrualAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCanceled);
        Assert.Single(await service.GetAccrualsAsync(new AccrualListRequest(null, null, null), CancellationToken.None));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(700m, summary.AccrualTotal);
        Assert.Equal(700m, summary.Debt);
        Assert.Equal(1, summary.AccrualCount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_restored");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("restore", audit.ActionKind);
        Assert.Contains("Восстановлено начисление 700.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAccrualAsync_RejectsDuplicateActiveAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null);
        var created = await service.CreateAccrualAsync(request, null, CancellationToken.None);
        await service.CancelAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Начисление заменено"), null, CancellationToken.None);
        Assert.True((await service.CreateAccrualAsync(request with { Amount = 800m }, null, CancellationToken.None)).Succeeded);

        var result = await service.RestoreAccrualAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_duplicate", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.accrual_restored");
    }

    [Fact]
    public async Task RestoreAccrualAsync_RejectsCanceledCampaignPrincipalWhenActivePrincipalExists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор с заменённым обязательством",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        var canceledPrincipal = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 1),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name,
            IsCanceled = true
        };
        var activePrincipal = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 31),
            OverdueFromDate = new DateOnly(2026, 8, 1),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };
        database.Context.AddRange(campaign, canceledPrincipal, activePrincipal);
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).RestoreAccrualAsync(
            canceledPrincipal.Id,
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_duplicate", result.ErrorCode);
        Assert.True(canceledPrincipal.IsCanceled);
        Assert.Equal(activePrincipal.Id, Assert.Single(
            database.Context.Accruals,
            item => item.FeeCampaignId == campaign.Id && !item.IsCanceled).Id);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.accrual_restored");
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_CreatesManualAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 15), 1200m, "manual", "INV-1", "Счет за воду"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal("manual", result.Value.Source);
        Assert.Equal("Vodokanal", result.Value.SupplierName);
        Assert.Equal("INV-1", result.Value.DocumentNumber);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано начисление 1 200.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ INV-1", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Счет за воду", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsSupplierWithoutConfiguredService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.ChargeServiceSettingId = null;
        fixtures.Supplier.ChargeServiceSetting = null;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-no-service", "Счет поставщика"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_service_not_configured", result.ErrorCode);
        Assert.Empty(database.Context.SupplierAccruals);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsServiceWithoutExpenseType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.ExpenseTypeId = null;
        fixtures.Supplier.ExpenseType = null;
        fixtures.Supplier.ExpenseFundId = null;
        fixtures.Supplier.ExpenseFund = null;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-no-type", "Счет поставщика"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_service_expense_type_not_configured", result.ErrorCode);
        Assert.Empty(database.Context.SupplierAccruals);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsExpenseTypeOutsideSupplierServiceLink()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var unrelatedExpenseType = new ExpenseType
        {
            Id = Guid.NewGuid(),
            Name = "Вывоз снега",
            Code = "snow_removal",
        };
        database.Context.ExpenseTypes.Add(unrelatedExpenseType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, unrelatedExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-mismatch", "Счет поставщика"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_expense_type_mismatch", result.ErrorCode);
        Assert.Empty(database.Context.SupplierAccruals);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateSupplierAccrualAsync_RejectsExpenseTypeOutsideSupplierServiceLinkAndKeepsOriginal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-original", "Исходный счет"),
            null,
            CancellationToken.None);
        var unrelatedExpenseType = new ExpenseType
        {
            Id = Guid.NewGuid(),
            Name = "Вывоз снега",
            Code = "snow_removal",
        };
        database.Context.ExpenseTypes.Add(unrelatedExpenseType);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateSupplierAccrualAsync(
            created.Value!.Id,
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, unrelatedExpenseType.Id, new DateOnly(2026, 7, 1), 1500m, "manual", "INV-changed", "Измененный счет"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_expense_type_mismatch", result.ErrorCode);
        var stored = await database.Context.SupplierAccruals.SingleAsync();
        Assert.Equal(fixtures.ExpenseType.Id, stored.ExpenseTypeId);
        Assert.Equal(new DateOnly(2026, 6, 1), stored.AccountingMonth);
        Assert.Equal(1200m, stored.Amount);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_created");
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_updated");
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_AllowsManualAccrualWithoutComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-no-comment", "   "),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.Value!.Comment);
        Assert.Single(database.Context.SupplierAccruals);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_created");
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsUnsupportedSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "imported", "INV-source", "Счет поставщика"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_source_invalid", result.ErrorCode);
        Assert.Empty(database.Context.SupplierAccruals);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateSupplierAccrualAsync_AllowsRegularAccrualCorrectionWithoutComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", "INV-regular", null),
            null,
            CancellationToken.None);

        var result = await service.UpdateSupplierAccrualAsync(
            created.Value!.Id,
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1250m, "regular", "INV-regular", " "),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1250m, result.Value!.Amount);
        Assert.Null(result.Value.Comment);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_updated");
    }

    [Fact]
    public async Task UpdateSupplierAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-old", "Исходный счет"),
            null,
            CancellationToken.None);

        var result = await service.UpdateSupplierAccrualAsync(
            created.Value!.Id,
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), 1250m, "manual", "INV-new", "Уточненный счет"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("было 1 200.00 поставщику Vodokanal за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; документ INV-old; комментарий Исходный счет", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 1 250.00 поставщику Vodokanal за 07.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; документ INV-new; комментарий Уточненный счет", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("supplier_accrual", metadata.RootElement.GetProperty("financeEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Расчетный месяц", changedFields, StringComparison.Ordinal);
        Assert.Contains("Сумма", changedFields, StringComparison.Ordinal);
        Assert.Contains("Документ", changedFields, StringComparison.Ordinal);
        Assert.Contains("Комментарий", changedFields, StringComparison.Ordinal);
        Assert.Equal("4", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsDuplicateSupplierTypeMonthSourceAndDocument()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", "INV-1", null);
        await service.CreateSupplierAccrualAsync(request, null, CancellationToken.None);

        var result = await service.CreateSupplierAccrualAsync(request with { Amount = 1300m }, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", "INV-1", null);
        var firstAccrual = await service.CreateSupplierAccrualAsync(request, null, CancellationToken.None);
        Assert.True(firstAccrual.Succeeded);
        var canceled = await service.CancelSupplierAccrualAsync(
            firstAccrual.Value!.Id,
            new CancelFinanceEntryRequest("Счет заменен"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);

        var replacement = await service.CreateSupplierAccrualAsync(request with { Amount = 1300m }, null, CancellationToken.None);

        Assert.True(replacement.Succeeded);
        Assert.Equal(1300m, replacement.Value!.Amount);
        Assert.Equal(2, await database.Context.SupplierAccruals.CountAsync());
        Assert.Equal(1, await database.Context.SupplierAccruals.CountAsync(accrual => accrual.IsCanceled));
        var activeAccrual = Assert.Single(await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, null), CancellationToken.None));
        Assert.Equal(1300m, activeAccrual.Amount);
    }

    [Fact]
    public async Task CancelSupplierAccrualAsync_CancelsSupplierAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-cancel", "Счет поставщика"),
            null,
            CancellationToken.None);

        var result = await service.CancelSupplierAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Счет заменен"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCanceled);
        Assert.Contains("Отменено: Счет заменен", result.Value.Comment);
        Assert.Empty(await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, null), CancellationToken.None));
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_canceled");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Отменено начисление 1 200.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ INV-cancel", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Счет заменен", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreSupplierAccrualAsync_RestoresCanceledAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-restore", "Счет поставщика"),
            null,
            CancellationToken.None);
        await service.CancelSupplierAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Временно исключили"), null, CancellationToken.None);

        var result = await service.RestoreSupplierAccrualAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCanceled);
        var activeAccrual = Assert.Single(await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, null), CancellationToken.None));
        Assert.Equal(1200m, activeAccrual.Amount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_restored");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("restore", audit.ActionKind);
        Assert.Contains("Восстановлено начисление 1 200.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ INV-restore", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreSupplierAccrualAsync_RejectsDuplicateActiveAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", "INV-supplier-restore-duplicate", null);
        var created = await service.CreateSupplierAccrualAsync(request, null, CancellationToken.None);
        await service.CancelSupplierAccrualAsync(created.Value!.Id, new CancelFinanceEntryRequest("Счет заменен"), null, CancellationToken.None);
        Assert.True((await service.CreateSupplierAccrualAsync(request with { Amount = 1300m }, null, CancellationToken.None)).Succeeded);

        var result = await service.RestoreSupplierAccrualAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_duplicate", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_restored");
    }

    [Fact]
    public async Task GetSupplierAccrualsAsync_SearchesAndOrdersByMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 5, 1), 900m, "regular", "INV-05", null), null, CancellationToken.None);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-06", "Ежемесячная корректировка поставщика %_"), null, CancellationToken.None);

        var result = await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, "ежемесячная"), CancellationToken.None);
        var literalWildcard = await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, "%_"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(result, literalWildcard);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(1200m, result[0].Amount);
    }

    [Fact]
    public async Task GetSupplierAccrualsPageAsync_FiltersBySupplierId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var secondService = new ChargeServiceSetting
        {
            Name = "Теплоснабжение"
        };
        var secondSupplier = new Supplier
        {
            Name = "Teploset",
            GroupId = fixtures.Supplier.GroupId,
            ChargeServiceSetting = secondService,
            ExpenseType = fixtures.ExpenseType,
            ExpenseFund = fixtures.ExpenseFund
        };
        database.Context.AddRange(secondService, secondSupplier);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 900m, "manual", "INV-1", "Счет первого поставщика"), null, CancellationToken.None)).Succeeded);
        var secondAccrual = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(secondSupplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-2", "Счет второго поставщика"),
            null,
            CancellationToken.None);
        Assert.True(secondAccrual.Succeeded, $"{secondAccrual.ErrorCode}: {secondAccrual.ErrorMessage}");

        var page = await service.GetSupplierAccrualsPageAsync(new SupplierAccrualListRequest(null, null, null, 25, 0, fixtures.Supplier.Id), CancellationToken.None);

        var accrual = Assert.Single(page.Items);
        Assert.Equal(fixtures.Supplier.Id, accrual.SupplierId);
        Assert.Equal("INV-1", accrual.DocumentNumber);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CreatesFixedAccrualsForActiveGarages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 15), "Июнь"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal(1, result.Value.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(300m, result.Value.TotalAmount);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal("regular", accrual.Source);
        Assert.Equal(300m, accrual.Amount);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Equal("Июнь; тариф Членский тариф: ставка 300.00, действует с 01.01.2026.", accrual.Comment);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано регулярных начислений: 1", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("на сумму 300.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("тариф Членский тариф, база fixed, ставка 300", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("пропущено 0", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CalculatesPeopleAmountForEachActiveGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "trash";
        fixtures.Garage.PeopleCount = 2;
        var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
        var secondGarage = new Garage { Number = "22", PeopleCount = 3, FloorCount = 1, Owner = secondOwner };
        var archivedGarage = new Garage { Number = "99", PeopleCount = 4, FloorCount = 1, Owner = secondOwner, IsArchived = true };
        var tariff = new Tariff { Name = "Вывоз мусора", CalculationBase = "people", Rate = 125m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.AddRange(secondOwner, secondGarage, archivedGarage, tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(625m, result.Value.TotalAmount);
        Assert.Equal("people", result.Value.CalculationBase);
        Assert.Contains(result.Value.CreatedAccruals, item => item.GarageNumber == fixtures.Garage.Number && item.Amount == 250m);
        Assert.Contains(result.Value.CreatedAccruals, item => item.GarageNumber == secondGarage.Number && item.Amount == 375m);
        Assert.DoesNotContain(result.Value.CreatedAccruals, item => item.GarageNumber == archivedGarage.Number);
        Assert.All(database.Context.Accruals, item => Assert.Equal(tariff.Id, item.TariffId));
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_AppliesTariffOnlyFromEffectiveMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff
        {
            Name = "Членский тариф",
            CalculationBase = "fixed",
            Rate = 450m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var beforeEffectiveDate = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 7, 15), null),
            Guid.NewGuid(),
            CancellationToken.None);
        var effectiveMonth = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 8, 31), null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(beforeEffectiveDate.Succeeded);
        Assert.Equal("tariff_not_effective", beforeEffectiveDate.ErrorCode);
        Assert.True(effectiveMonth.Succeeded);
        Assert.Equal(new DateOnly(2026, 8, 1), effectiveMonth.Value!.AccountingMonth);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(new DateOnly(2026, 8, 1), accrual.AccountingMonth);
        Assert.Equal(450m, accrual.Amount);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Contains("действует с 01.08.2026", accrual.Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularCatalogAccrualsAsync_CreatesAccrualsFromLinkedChargeServices()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        database.Context.ChargeServiceSettings.AddRange(
            new ChargeServiceSetting
            {
                Name = "Членский взнос",
                IsRegular = true,
                PeriodicityMonths = 12,
                AccrualStartMonth = 6,
                PaymentDueDay = 30,
                PaymentDueMonth = 6,
                OverdueGraceDays = 30,
                IncomeTypeId = fixtures.IncomeType.Id,
                TariffId = tariff.Id,
                UnitName = "руб."
            },
            new ChargeServiceSetting
            {
                Name = "Годовой сбор",
                IsRegular = true,
                PeriodicityMonths = 12,
                AccrualStartMonth = 7,
                PaymentDueDay = 30,
                PaymentDueMonth = 7,
                OverdueGraceDays = 30,
                IncomeTypeId = fixtures.IncomeType.Id,
                TariffId = tariff.Id,
                UnitName = "руб."
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 6, 1), "Июнь"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal(1, result.Value.ServiceCount);
        Assert.Equal(1, result.Value.CreatedCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Equal(300m, result.Value.TotalAmount);
        Assert.Contains(result.Value.SkippedServices, item => item.Contains("Годовой сбор", StringComparison.Ordinal));
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(fixtures.IncomeType.Id, accrual.IncomeTypeId);
        Assert.Equal(2026, accrual.AccountingYear);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Equal(new DateOnly(2026, 6, 30), accrual.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), accrual.OverdueFromDate);
        Assert.Equal("Каталог услуг: Членский взнос; Июнь; тариф Членский тариф: ставка 300.00, действует с 01.01.2026.", accrual.Comment);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.regular_catalog_accruals_generated" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GenerateRegularCatalogAccrualsAsync_SelectsServiceTariffVersionByAccountingMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "monthly_custom";
        var oldTariff = new Tariff { Name = "Услуга до сентября", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var newTariff = new Tariff { Name = "Услуга с сентября", CalculationBase = "fixed", Rate = 450m, EffectiveFrom = new DateOnly(2026, 9, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Помесячная услуга",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = fixtures.IncomeType.Id,
            TariffId = newTariff.Id,
            Tariff = newTariff,
            UnitName = "руб."
        };
        database.Context.AddRange(oldTariff, newTariff, setting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = oldTariff.Id,
                EffectiveFrom = oldTariff.EffectiveFrom
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = newTariff.Id,
                EffectiveFrom = newTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var july = await service.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 7, 1), null),
            Guid.NewGuid(),
            CancellationToken.None);
        var september = await service.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 9, 1), null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(july.Succeeded, july.ErrorMessage);
        Assert.True(september.Succeeded, september.ErrorMessage);
        var accruals = await database.Context.Accruals.OrderBy(item => item.AccountingMonth).ToListAsync();
        Assert.Collection(
            accruals,
            item =>
            {
                Assert.Equal(new DateOnly(2026, 7, 1), item.AccountingMonth);
                Assert.Equal(oldTariff.Id, item.TariffId);
                Assert.Equal(300m, item.Amount);
            },
            item =>
            {
                Assert.Equal(new DateOnly(2026, 9, 1), item.AccountingMonth);
                Assert.Equal(newTariff.Id, item.TariffId);
                Assert.Equal(450m, item.Amount);
            });
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_CalculatesOnlySelectedGarageAndRecalculatesOnlyUnpaidAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "worksheet_fixed";
        var secondOwner = new Owner { LastName = "Second", FirstName = "Owner" };
        var secondGarage = new Garage { Number = "22", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        var tariff = new Tariff
        {
            Name = "Worksheet fixed tariff",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 100m,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Worksheet fixed service",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 15,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            UnitName = "rub."
        };
        database.Context.AddRange(secondOwner, secondGarage, tariff, setting);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var period = new GarageIncomeWorksheetRequest(
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 1));

        var initial = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(initial.Succeeded, initial.ErrorMessage);
        var initialRow = Assert.Single(initial.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(100m, initialRow.AccrualAmount);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(fixtures.Garage.Id, accrual.GarageId);
        Assert.DoesNotContain(database.Context.Accruals, item => item.GarageId == secondGarage.Id);

        tariff.Rate = 125m;
        await database.Context.SaveChangesAsync();
        var recalculated = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(recalculated.Succeeded, recalculated.ErrorMessage);
        Assert.Equal(125m, Assert.Single(recalculated.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        Assert.Equal(125m, accrual.Amount);

        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            Garage = fixtures.Garage,
            IncomeType = fixtures.IncomeType,
            OperationDate = new DateOnly(2024, 2, 20),
            AccountingMonth = new DateOnly(2024, 2, 1),
            Amount = 25m
        };
        database.Context.AddRange(
            payment,
            new AccrualPaymentAllocation
            {
                Accrual = accrual,
                FinancialOperation = payment,
                Amount = 25m
            });
        tariff.Rate = 150m;
        setting.IsArchived = true;
        await database.Context.SaveChangesAsync();

        var afterPayment = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(afterPayment.Succeeded, afterPayment.ErrorMessage);
        Assert.Equal(125m, accrual.Amount);
        Assert.Equal(125m, Assert.Single(afterPayment.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_KeepsIssuedUnpaidAccrualWhenRegularServiceStops()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "worksheet_stopped";
        var tariff = new Tariff
        {
            Name = "Stopped service tariff",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 200m,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Stopped service",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 15,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            UnitName = "rub."
        };
        database.Context.AddRange(tariff, setting);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var period = new GarageIncomeWorksheetRequest(
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 1));

        var initial = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(initial.Succeeded, initial.ErrorMessage);
        Assert.Equal(200m, Assert.Single(initial.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        var accrual = Assert.Single(database.Context.Accruals);

        setting.IsArchived = true;
        await database.Context.SaveChangesAsync();
        var recalculated = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(recalculated.Succeeded, recalculated.ErrorMessage);
        Assert.Equal(200m, Assert.Single(recalculated.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        Assert.False(accrual.IsCanceled);

        var followingMonth = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 1)),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(followingMonth.Succeeded, followingMonth.ErrorMessage);
        Assert.DoesNotContain(followingMonth.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_UsesEveryTariffSegmentInsideSelectedMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "worksheet_mid_month_rate";
        var firstTariff = new Tariff
        {
            Name = "First half",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 310m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var secondTariff = new Tariff
        {
            Name = "Second half",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 620m,
            EffectiveFrom = new DateOnly(2026, 8, 16)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Mid-month service",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = secondTariff,
            UnitName = "rub."
        };
        database.Context.AddRange(firstTariff, secondTariff, setting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                Tariff = firstTariff,
                EffectiveFrom = new DateOnly(2026, 8, 1),
                EffectiveTo = new DateOnly(2026, 8, 15)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                Tariff = secondTariff,
                EffectiveFrom = new DateOnly(2026, 8, 16)
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(470m, row.AccrualAmount);
        Assert.NotNull(row.CalculationDetails);
        Assert.Collection(
            row.CalculationDetails!.Lines,
            line =>
            {
                Assert.Equal(new DateOnly(2026, 8, 1), line.EffectiveFrom);
                Assert.Equal(new DateOnly(2026, 8, 15), line.EffectiveTo);
                Assert.Equal(150m, line.Amount);
            },
            line =>
            {
                Assert.Equal(new DateOnly(2026, 8, 16), line.EffectiveFrom);
                Assert.Equal(new DateOnly(2026, 8, 31), line.EffectiveTo);
                Assert.Equal(320m, line.Amount);
            });
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_ShowsMissingHistoricalMeterReadingForSelectedMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Water;
        var tariff = new Tariff
        {
            Name = "Historical water",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 10m,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Historical water",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 15,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            UnitName = "m3"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 1)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(new DateOnly(2024, 2, 1), row.AccountingMonth);
        Assert.Equal(MeterKinds.Water, row.MeterKind);
        Assert.Null(row.MeterReadingId);
        Assert.Null(row.MeterValue);
        Assert.Equal(0m, row.AccrualAmount);
        Assert.Empty(database.Context.Accruals);
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_KeepsIssuedUnpaidAccrualWhenMeterReadingBecomesUnavailable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "worksheet_missing_meter_after_issue";
        var tariff = new Tariff
        {
            Name = "Issued metered tariff",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 10m,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Issued metered service",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 15,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            UnitName = "m3"
        });
        var reading = new MeterReading
        {
            Garage = fixtures.Garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new DateOnly(2024, 2, 1),
            ReadingDate = new DateOnly(2024, 2, 29),
            PreviousValue = 100m,
            CurrentValue = 110m,
            Consumption = 10m
        };
        database.Context.MeterReadings.Add(reading);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var period = new GarageIncomeWorksheetRequest(
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 1));

        var issued = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(issued.Succeeded, issued.ErrorMessage);
        Assert.Equal(100m, Assert.Single(issued.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        var accrual = Assert.Single(database.Context.Accruals);

        reading.IsCanceled = true;
        await database.Context.SaveChangesAsync();
        var recalculatedWithoutReading = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            period,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(recalculatedWithoutReading.Succeeded, recalculatedWithoutReading.ErrorMessage);
        var row = Assert.Single(recalculatedWithoutReading.Value!.Rows, item => item.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(100m, row.AccrualAmount);
        Assert.Null(row.MeterReadingId);
        Assert.Null(row.MeterValue);
        Assert.False(accrual.IsCanceled);
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_RejectsReversedAndOversizedPeriods()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var reversed = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1)),
            null,
            CancellationToken.None);
        var oversized = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(1970, 1, 1), new DateOnly(2026, 8, 1)),
            null,
            CancellationToken.None);

        Assert.False(reversed.Succeeded);
        Assert.Equal("income_worksheet_period_invalid", reversed.ErrorCode);
        Assert.False(oversized.Succeeded);
        Assert.Equal("income_worksheet_period_too_large", oversized.ErrorCode);
    }

    [Fact]
    public async Task CalculateGarageIncomeWorksheetAsync_UsesConstantSelectCountForLongMeteredPeriod()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "worksheet_metered_performance";
        var tariff = new Tariff
        {
            Name = "Worksheet metered performance",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Worksheet metered performance",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 15,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            UnitName = "unit"
        };
        database.Context.AddRange(tariff, setting);
        for (var month = new DateOnly(2026, 1, 1); month <= new DateOnly(2026, 12, 1); month = month.AddMonths(1))
        {
            database.Context.MeterReadings.Add(new MeterReading
            {
                Garage = fixtures.Garage,
                MeterKind = setting.MeterKind,
                AccountingMonth = month,
                ReadingDate = month.AddMonths(1).AddDays(-1),
                PreviousValue = 100m,
                CurrentValue = 110m,
                Consumption = 10m
            });
        }
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        commandCounter.Reset();
        var oneMonth = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)),
            Guid.NewGuid(),
            CancellationToken.None);
        var oneMonthSelectCount = commandCounter.Count;

        commandCounter.Reset();
        var fullYear = await service.CalculateGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1)),
            Guid.NewGuid(),
            CancellationToken.None);
        var fullYearSelectCount = commandCounter.Count;

        Assert.True(oneMonth.Succeeded, oneMonth.ErrorMessage);
        Assert.True(fullYear.Succeeded, fullYear.ErrorMessage);
        Assert.Equal(12, fullYear.Value!.Rows.Count(row => row.IncomeTypeId == fixtures.IncomeType.Id));
        Assert.InRange(fullYearSelectCount, oneMonthSelectCount, oneMonthSelectCount + 1);
        Assert.InRange(fullYearSelectCount, 1, 10);
    }

    [Fact]
    public async Task GenerateRegularCatalogAccrualsAsync_ProreratesFixedToMeteredTransitionAndExposesDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "mixed_service";
        var fixedTariff = new Tariff
        {
            Name = "Охрана фиксированная",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 310m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var meteredTariff = new Tariff
        {
            Name = "Охрана по счётчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 10m,
            EffectiveFrom = new DateOnly(2026, 8, 16)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Охрана",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            IncomeTypeId = fixtures.IncomeType.Id,
            Tariff = meteredTariff,
            TariffId = meteredTariff.Id,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            UnitName = "м³"
        };
        var reading = new MeterReading
        {
            Garage = fixtures.Garage,
            GarageId = fixtures.Garage.Id,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new DateOnly(2026, 8, 1),
            ReadingDate = new DateOnly(2026, 8, 31),
            PreviousValue = 100m,
            CurrentValue = 131m,
            Consumption = 31m
        };
        database.Context.AddRange(fixedTariff, meteredTariff, setting, reading);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                Tariff = fixedTariff,
                EffectiveFrom = new DateOnly(2026, 8, 1),
                EffectiveTo = new DateOnly(2026, 8, 15)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                Tariff = meteredTariff,
                EffectiveFrom = new DateOnly(2026, 8, 16)
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var generated = await service.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 8, 1), null),
            Guid.NewGuid(),
            CancellationToken.None);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);

        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(310m, accrual.Amount);
        Assert.True(accrual.RequiresMeterReading);
        Assert.Equal(MeterKinds.Water, accrual.CalculationMeterKind);
        Assert.NotNull(accrual.CalculationDetailsJson);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows);
        Assert.Equal(310m, row.PayableAmount);
        Assert.NotNull(row.CalculationDetails);
        Assert.Collection(
            row.CalculationDetails!.Lines,
            line =>
            {
                Assert.Equal("fixed", line.CalculationMode);
                Assert.Equal(150m, line.Amount);
            },
            line =>
            {
                Assert.Equal("metered", line.CalculationMode);
                Assert.Equal(16m, line.Quantity);
                Assert.Equal(160m, line.Amount);
            });
    }

    [Fact]
    public async Task GenerateRegularCatalogAccrualsAsync_CreatesPartialAccrualWhenFirstTariffStartsMidMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "partial_metered_service";
        var meteredTariff = new Tariff
        {
            Name = "Счётчик со второй половины месяца",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 10m,
            EffectiveFrom = new DateOnly(2026, 8, 16)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Частичная услуга",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            IncomeTypeId = fixtures.IncomeType.Id,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            UnitName = "м³"
        };
        database.Context.AddRange(
            meteredTariff,
            setting,
            new MeterReading
            {
                Garage = fixtures.Garage,
                GarageId = fixtures.Garage.Id,
                MeterKind = MeterKinds.Water,
                AccountingMonth = new DateOnly(2026, 8, 1),
                ReadingDate = new DateOnly(2026, 8, 31),
                PreviousValue = 50m,
                CurrentValue = 81m,
                Consumption = 31m
            });
        database.Context.ChargeServiceTariffVersions.Add(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                Tariff = meteredTariff,
                EffectiveFrom = new DateOnly(2026, 8, 16)
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var generated = await service.GenerateRegularCatalogAccrualsAsync(
            new GenerateRegularCatalogAccrualsRequest(new DateOnly(2026, 8, 1), null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(160m, accrual.Amount);
        var details = RegularAccrualCalculator.Deserialize(accrual.CalculationDetailsJson);
        Assert.NotNull(details);
        Assert.Collection(
            details!.Lines,
            line =>
            {
                Assert.False(line.HasTariff);
                Assert.Equal(new DateOnly(2026, 8, 1), line.EffectiveFrom);
                Assert.Equal(new DateOnly(2026, 8, 15), line.EffectiveTo);
                Assert.Equal(0m, line.Amount);
            },
            line =>
            {
                Assert.True(line.HasTariff);
                Assert.Equal(new DateOnly(2026, 8, 16), line.EffectiveFrom);
                Assert.Equal(new DateOnly(2026, 8, 31), line.EffectiveTo);
                Assert.Equal(16m, line.Quantity);
                Assert.Equal(160m, line.Amount);
            });
    }

    [Fact]
    public async Task MeteredTariffHistory_AccruesAllocatesPaymentsAndRoutesIncomeToFund()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Electricity;
        fixtures.IncomeType.IsSystem = true;
        var destinationFund = new Fund
        {
            Name = "Электроэнергия",
            NormalizedName = "ЭЛЕКТРОЭНЕРГИЯ"
        };
        fixtures.IncomeType.DestinationFund = destinationFund;
        fixtures.IncomeType.DestinationFundId = destinationFund.Id;
        var tieredTariff = new Tariff
        {
            Name = "Электроэнергия по порогам",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            ElectricityTiersJson = """
                [
                  {"Id":"11111111-1111-1111-1111-111111111111","Name":"До 50","UpperBound":50,"Rate":2,"IsCustom":false},
                  {"Id":"22222222-2222-2222-2222-222222222222","Name":"До 100","UpperBound":100,"Rate":3,"IsCustom":false},
                  {"Id":"33333333-3333-3333-3333-333333333333","Name":"Без границы","UpperBound":null,"Rate":5,"IsCustom":false}
                ]
                """,
            EffectiveFrom = new DateOnly(2026, 6, 1)
        };
        var ordinaryTariff = new Tariff
        {
            Name = "Электроэнергия по одной ставке",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 4m,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Электроэнергия",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            IncomeTypeId = fixtures.IncomeType.Id,
            Tariff = ordinaryTariff,
            TariffId = ordinaryTariff.Id,
            IsMetered = true,
            HasTieredTariff = false,
            UnitName = "кВт·ч"
        };
        database.Context.AddRange(destinationFund, tieredTariff, ordinaryTariff, setting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                ChargeServiceSettingId = setting.Id,
                Tariff = tieredTariff,
                TariffId = tieredTariff.Id,
                EffectiveFrom = tieredTariff.EffectiveFrom
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = setting,
                ChargeServiceSettingId = setting.Id,
                Tariff = ordinaryTariff,
                TariffId = ordinaryTariff.Id,
                EffectiveFrom = ordinaryTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();
        var actorUserId = Guid.NewGuid();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero)));

        var juneReading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                230m,
                null),
            actorUserId,
            CancellationToken.None);
        var junePayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 21),
                new DateOnly(2026, 6, 1),
                400m,
                "PKO-METER-JUNE",
                null),
            actorUserId,
            CancellationToken.None);
        var julyReading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 20),
                260m,
                null),
            actorUserId,
            CancellationToken.None);
        var julyPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 7, 21),
                new DateOnly(2026, 7, 1),
                120m,
                "PKO-METER-JULY",
                null),
            actorUserId,
            CancellationToken.None);

        Assert.True(juneReading.Succeeded, juneReading.ErrorMessage);
        Assert.True(junePayment.Succeeded, junePayment.ErrorMessage);
        Assert.True(julyReading.Succeeded, julyReading.ErrorMessage);
        Assert.True(julyPayment.Succeeded, julyPayment.ErrorMessage);
        var accruals = await database.Context.Accruals.OrderBy(item => item.AccountingMonth).ToListAsync();
        Assert.Collection(
            accruals,
            item =>
            {
                Assert.Equal(tieredTariff.Id, item.TariffId);
                Assert.Equal(650m, item.Amount);
            },
            item =>
            {
                Assert.Equal(ordinaryTariff.Id, item.TariffId);
                Assert.Equal(120m, item.Amount);
            });
        var activeAllocations = await database.Context.AccrualPaymentAllocations
            .Where(item => item.IsActive)
            .OrderBy(item => item.Accrual.AccountingMonth)
            .ToListAsync();
        Assert.Collection(
            activeAllocations,
            item => Assert.Equal(400m, item.Amount),
            item => Assert.Equal(120m, item.Amount));
        var fundAssignments = await database.Context.FundOperations
            .Where(item => item.SourceFinancialOperationId != null && !item.IsCanceled)
            .ToListAsync();
        Assert.Equal([120m, 400m], fundAssignments.Select(item => item.Amount).OrderBy(item => item));
        Assert.All(fundAssignments, item => Assert.Equal(destinationFund.Id, item.FundId));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.metered_accrual_created_from_reading");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.payment_allocations_rebuilt");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_created");
    }

    [Fact]
    public async Task PreviewRegularAccrualAutomationAsync_ReturnsDueScopeWithoutWritingAccruals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff
        {
            Name = "Предпросмотр",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 300m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.AddRange(
            new ChargeServiceSetting
            {
                Name = "Ежемесячная услуга",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                IncomeType = fixtures.IncomeType,
                Tariff = tariff
            },
            new ChargeServiceSetting
            {
                Name = "Услуга другого месяца",
                IsRegular = true,
                PeriodicityMonths = 12,
                AccrualStartMonth = 9,
                IncomeType = fixtures.IncomeType,
                Tariff = tariff
            });
        database.Context.FeeCampaigns.Add(new FeeCampaign
        {
            Name = "Августовский сбор",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = true
        });
        await database.Context.SaveChangesAsync();
        var accrualCountBefore = await database.Context.Accruals.CountAsync();
        var auditCountBefore = await database.Context.AuditEvents.CountAsync();

        var preview = await FinanceServiceTestFactory.Create(database.Context)
            .PreviewRegularAccrualAutomationAsync(new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 8, 1), preview.AccountingMonth);
        Assert.Equal(1, preview.ActiveGarageCount);
        Assert.Equal(2, preview.ActiveRegularServiceCount);
        Assert.Equal(1, preview.DueRegularServiceCount);
        Assert.Equal(1, preview.ActiveFeeCampaignCount);
        Assert.Equal(2, preview.MaximumGarageChecks);
        Assert.Empty(preview.Warnings);
        Assert.Equal(accrualCountBefore, await database.Context.Accruals.CountAsync());
        Assert.Equal(auditCountBefore, await database.Context.AuditEvents.CountAsync());
        Assert.False(database.Context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_DoesNotDuplicateAnnualObligationAcrossMonthsOrOwnerChange()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var tariff = new Tariff
        {
            Name = "Годовой членский тариф",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 700m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var financeService = FinanceServiceTestFactory.Create(database.Context);

        var firstGeneration = await financeService.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 1, 1), "Первое формирование"),
            null,
            CancellationToken.None);
        Assert.True(firstGeneration.Succeeded, firstGeneration.ErrorMessage);
        var originalAccrual = Assert.Single(database.Context.Accruals);
        var originalAccrualId = originalAccrual.Id;

        var replacementOwner = new Owner { LastName = "Новый", FirstName = "Владелец" };
        database.Context.Owners.Add(replacementOwner);
        await database.Context.SaveChangesAsync();
        var ownerChange = await DictionaryServiceTestFactory.Create(database.Context).UpdateGarageAsync(
            fixtures.Garage.Id,
            new UpsertGarageRequest(
                fixtures.Garage.Number,
                fixtures.Garage.PeopleCount,
                fixtures.Garage.FloorCount,
                replacementOwner.Id,
                fixtures.Garage.StartingBalance,
                fixtures.Garage.InitialWaterMeterValue,
                fixtures.Garage.InitialElectricityMeterValue,
                fixtures.Garage.Comment),
            null,
            CancellationToken.None);
        Assert.True(ownerChange.Succeeded, ownerChange.ErrorMessage);

        var repeatedGeneration = await financeService.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 7, 1), "Повторное формирование"),
            null,
            CancellationToken.None);
        Assert.False(repeatedGeneration.Succeeded);
        Assert.Equal("regular_accruals_empty", repeatedGeneration.ErrorCode);
        Assert.Contains("за 2026 год уже сформированы", repeatedGeneration.ErrorMessage, StringComparison.Ordinal);
        var accrualAfterOwnerChange = Assert.Single(database.Context.Accruals);
        Assert.Equal(originalAccrualId, accrualAfterOwnerChange.Id);
        Assert.Equal(fixtures.Garage.Id, accrualAfterOwnerChange.GarageId);

        var worksheet = await financeService.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal("Новый Владелец", worksheet.Value!.OwnerName);
        Assert.Contains(worksheet.Value.Rows, row => row.AnnualAccrualId == originalAccrualId);

        var nextYearGeneration = await financeService.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2027, 1, 1), "Новый учетный год"),
            null,
            CancellationToken.None);
        Assert.True(nextYearGeneration.Succeeded, nextYearGeneration.ErrorMessage);
        Assert.Equal(2, database.Context.Accruals.Count());
        Assert.Equal([2026, 2027], database.Context.Accruals.OrderBy(item => item.AccountingYear).Select(item => item.AccountingYear!.Value).ToArray());
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_TreatsLegacyAnnualAccrualWithoutAccountingYearAsExisting()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        fixtures.Garage.CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var tariff = new Tariff
        {
            Name = "Годовой членский тариф",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 700m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = fixtures.Garage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            TariffId = tariff.Id,
            AccountingMonth = new DateOnly(2026, 1, 1),
            AccountingYear = null,
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 700m,
            Source = AccrualSources.Regular
        });
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("regular_accruals_empty", result.ErrorCode);
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_KeepsAnnualDeadlineInAccountingYearWhenGeneratedLate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        fixtures.Garage.CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var tariff = new Tariff
        {
            Name = "Годовой членский тариф",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 700m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Годовой членский взнос",
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            PaymentDueMonth = 6,
            OverdueGraceDays = 30,
            IncomeTypeId = fixtures.IncomeType.Id,
            Tariff = tariff,
            UnitName = "руб."
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 9, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(2026, accrual.AccountingYear);
        Assert.Equal(new DateOnly(2026, 6, 30), accrual.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), accrual.OverdueFromDate);
    }

    [Fact]
    public async Task CreateAndUpdateAccrualAsync_UseStableAnnualDeadlinesWithoutLinkedSetting()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        fixtures.Garage.CreatedAtUtc = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var outdoorLighting = new IncomeType
        {
            Name = "Наружное освещение",
            Code = "outdoor_lighting"
        };
        database.Context.IncomeTypes.Add(outdoorLighting);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 9, 1),
                700m,
                AccrualSources.Manual,
                "Ручной членский взнос"),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 6, 30), created.Value!.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), created.Value.OverdueFromDate);

        var updated = await service.UpdateAccrualAsync(
            created.Value.Id,
            new CreateAccrualRequest(
                fixtures.Garage.Id,
                outdoorLighting.Id,
                new DateOnly(2026, 10, 1),
                700m,
                AccrualSources.Manual,
                "Перенесено на наружное освещение"),
            null,
            CancellationToken.None);

        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 12, 31), updated.Value!.DueDate);
        Assert.Equal(new DateOnly(2027, 1, 1), updated.Value.OverdueFromDate);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CreatesAnnualObligationForMissingGarageDespiteHistoricalDuplicates()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var secondGarage = new Garage { Number = "13", PeopleCount = 1, FloorCount = 1 };
        var tariff = new Tariff
        {
            Name = "Годовой членский тариф",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 700m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.AddRange(secondGarage, tariff);
        database.Context.Accruals.AddRange(
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = new DateOnly(2026, 1, 1),
                AccountingYear = 2026,
                DueDate = new DateOnly(2026, 6, 30),
                OverdueFromDate = new DateOnly(2026, 7, 31),
                Amount = 700m,
                Source = AccrualSources.Regular
            },
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                AccountingYear = 2026,
                DueDate = new DateOnly(2026, 6, 30),
                OverdueFromDate = new DateOnly(2026, 7, 31),
                Amount = 700m,
                Source = AccrualSources.Regular
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 9, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(secondGarage.Id, Assert.Single(result.Value.CreatedAccruals).GarageId);
        Assert.Equal(2, database.Context.Accruals.Count(item => item.GarageId == fixtures.Garage.Id));
        Assert.Equal(1, database.Context.Accruals.Count(item => item.GarageId == secondGarage.Id));
    }

    [Fact]
    public async Task RegularAccrualAutomationRunner_AppliesAllCurrentTariffsAndFeeCampaignsWithoutDuplicates()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var secondGarage = new Garage
        {
            Number = "AUTO-FEE-SECOND",
            PeopleCount = 1,
            FloorCount = 1
        };
        database.Context.Garages.Add(secondGarage);
        var tariff = new Tariff
        {
            Name = "Ежемесячный членский тариф",
            CalculationBase = "fixed",
            Rate = 500m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Ежемесячный членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 10,
            PaymentDueMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            UnitName = "руб."
        });
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var allGaragesCampaign = new FeeCampaign
        {
            Name = "Автоматический сбор на ворота",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 700m,
            TargetAmount = 700m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        var selectedCampaign = new FeeCampaign
        {
            Name = "Автоматический выборочный сбор",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 300m,
            TargetAmount = 300m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        selectedCampaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = selectedCampaign,
            Garage = secondGarage
        });
        database.Context.FeeCampaigns.AddRange(allGaragesCampaign, selectedCampaign);
        await database.Context.SaveChangesAsync();

        var financeService = FinanceServiceTestFactory.Create(database.Context);
        var advance = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                300m,
                "AUTO-FEE-ADVANCE",
                null),
            null,
            CancellationToken.None);
        Assert.True(advance.Succeeded, advance.ErrorMessage);
        Assert.Empty(database.Context.AccrualPaymentAllocations.Where(item => item.IsActive));
        var runner = new RegularAccrualAutomationRunner(
            financeService,
            new TestBusinessDateProvider(new DateOnly(2026, 8, 1)),
            new EfRegularAccrualAutomationLock(database.Context),
            NullLogger<RegularAccrualAutomationRunner>.Instance);

        var firstRun = await runner.RunCurrentMonthAsync(CancellationToken.None);
        var secondRun = await runner.RunCurrentMonthAsync(CancellationToken.None);

        var regularAccruals = database.Context.Accruals.Where(item => item.FeeCampaignId == null).ToArray();
        Assert.Equal(2, regularAccruals.Length);
        Assert.All(regularAccruals, accrual =>
        {
            Assert.Equal(new DateOnly(2026, 8, 1), accrual.AccountingMonth);
            Assert.Equal(500m, accrual.Amount);
            Assert.Contains("Автоматическое ежемесячное формирование", accrual.Comment, StringComparison.Ordinal);
        });
        var allGaragesAccruals = database.Context.Accruals
            .Where(item => item.FeeCampaignId == allGaragesCampaign.Id)
            .ToArray();
        Assert.Equal(2, allGaragesAccruals.Length);
        Assert.All(allGaragesAccruals, accrual =>
        {
            Assert.Equal(700m, accrual.Amount);
            Assert.Contains("Автоматическое начисление действующих сборов", accrual.Comment, StringComparison.Ordinal);
        });
        var selectedAccrual = Assert.Single(database.Context.Accruals, item => item.FeeCampaignId == selectedCampaign.Id);
        Assert.Equal(secondGarage.Id, selectedAccrual.GarageId);
        Assert.Equal(300m, selectedAccrual.Amount);
        Assert.True(firstRun.Succeeded);
        Assert.Equal(5, firstRun.CreatedCount);
        Assert.Contains("действующие сборы — создано 3", firstRun.Message, StringComparison.Ordinal);
        Assert.True(secondRun.Succeeded);
        Assert.Equal(0, secondRun.CreatedCount);
        Assert.Equal(5, database.Context.Accruals.Count());
        var advanceAllocation = Assert.Single(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.FinancialOperationId == advance.Value!.Id);
        Assert.Equal(300m, advanceAllocation.Amount);
        Assert.Equal(allGaragesCampaign.Id, advanceAllocation.Accrual.FeeCampaignId);
        var firstWorksheet = await financeService.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        Assert.True(firstWorksheet.Succeeded, firstWorksheet.ErrorMessage);
        Assert.Equal(500m, Assert.Single(firstWorksheet.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        var firstFees = Assert.Single(firstWorksheet.Value.Rows, row => row.IncomeTypeId == otherIncome.Id);
        Assert.Equal(700m, firstFees.AccrualAmount);
        Assert.Equal(300m, firstFees.IncomeAmount);
        Assert.Equal(400m, firstFees.Debt);
        var secondWorksheet = await financeService.GetGarageIncomeWorksheetAsync(
            secondGarage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        Assert.True(secondWorksheet.Succeeded, secondWorksheet.ErrorMessage);
        Assert.Equal(500m, Assert.Single(secondWorksheet.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id).AccrualAmount);
        Assert.Equal(700m, secondWorksheet.Value.Rows.Where(row => row.IncomeTypeId == otherIncome.Id).Sum(row => row.AccrualAmount));
        Assert.Contains(
            database.Context.AuditEvents,
            item => item.Action == "finance.regular_catalog_accruals_generated" && item.ActorUserId == null);
        Assert.Equal(
            2,
            database.Context.AuditEvents.Count(
                item => item.Action == "finance.fee_campaign_accruals_generated" && item.ActorUserId == null));
    }

    [Fact]
    public async Task GenerateActiveFeeCampaignAccrualsAsync_ProcessesOnlyDueCampaignsAndTreatsRepeatAsNoOp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        AddOtherIncomeDestination(database.Context);
        var dueCampaign = new FeeCampaign
        {
            Name = "Действующий сбор",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 600m,
            TargetAmount = 600m,
            StartsOn = new DateOnly(2026, 8, 15),
            EndsOn = new DateOnly(2026, 8, 31),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.FeeCampaigns.AddRange(
            dueCampaign,
            new FeeCampaign
            {
                Name = "Будущий сбор",
                IncomeType = fixtures.IncomeType,
                ContributionAmount = 700m,
                TargetAmount = 700m,
                StartsOn = new DateOnly(2026, 9, 1),
                AppliesToAllGarages = true,
                OverdueGraceDays = 30
            },
            new FeeCampaign
            {
                Name = "Завершенный сбор",
                IncomeType = fixtures.IncomeType,
                ContributionAmount = 800m,
                TargetAmount = 800m,
                StartsOn = new DateOnly(2026, 1, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                AppliesToAllGarages = true,
                OverdueGraceDays = 30
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateActiveFeeCampaignAccrualsRequest(new DateOnly(2026, 8, 15), "Автоматический запуск");

        var first = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
        var second = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(1, first.Value!.CampaignCount);
        Assert.Equal(1, first.Value.CreatedCount);
        Assert.Equal(600m, first.Value.TotalAmount);
        Assert.Empty(first.Value.FailedCampaigns);
        Assert.Equal(dueCampaign.Id, Assert.Single(first.Value.CampaignResults).FeeCampaignId);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(0, second.Value!.CreatedCount);
        Assert.Contains(second.Value.SkippedCampaigns, item => item.Contains("Действующий сбор", StringComparison.Ordinal));
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateActiveFeeCampaignAccrualsAsync_RejectsMoreThanBoundedCampaignLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        for (var index = 0; index < 501; index++)
        {
            database.Context.FeeCampaigns.Add(new FeeCampaign
            {
                Name = $"Массовый сбор {index:D3}",
                IncomeType = fixtures.IncomeType,
                ContributionAmount = 100m,
                TargetAmount = 100m,
                StartsOn = new DateOnly(2026, 1, 1),
                AppliesToAllGarages = true,
                OverdueGraceDays = 30
            });
        }
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).GenerateActiveFeeCampaignAccrualsAsync(
            new GenerateActiveFeeCampaignAccrualsRequest(new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("active_fee_campaign_limit_exceeded", result.ErrorCode);
        Assert.Empty(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateActiveFeeCampaignAccrualsAsync_ReportsCampaignFailureForAutomationRetry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FeeCampaigns.Add(new FeeCampaign
        {
            Name = "Сбор без назначения дохода",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        });
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).GenerateActiveFeeCampaignAccrualsAsync(
            new GenerateActiveFeeCampaignAccrualsRequest(new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Contains(result.Value.FailedCampaigns, item => item.Contains("Прочие доходы", StringComparison.Ordinal));
        Assert.Empty(database.Context.Accruals);
    }

    [Fact]
    public async Task RegularAccrualAutomationRunner_ReportsFailedFeeCampaignForRetry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FeeCampaigns.Add(new FeeCampaign
        {
            Name = "Проблемный автоматический сбор",
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        });
        await database.Context.SaveChangesAsync();
        var runner = new RegularAccrualAutomationRunner(
            FinanceServiceTestFactory.Create(database.Context),
            new TestBusinessDateProvider(new DateOnly(2026, 8, 1)),
            new EfRegularAccrualAutomationLock(database.Context),
            NullLogger<RegularAccrualAutomationRunner>.Instance);

        var result = await runner.RunCurrentMonthAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.CreatedCount);
        Assert.Contains("Проблемный автоматический сбор", result.Message, StringComparison.Ordinal);
        Assert.Contains("Фоновая задача повторит попытку", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegularAccrualAutomationRunner_SkipsMonthAlreadyRunningInAnotherInstance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var businessDate = new DateOnly(2026, 8, 15);
        var lockOwner = new EfRegularAccrualAutomationLock(database.Context);
        await using var ownerLease = await lockOwner.TryAcquireAsync(businessDate, CancellationToken.None);
        Assert.NotNull(ownerLease);
        var runner = new RegularAccrualAutomationRunner(
            FinanceServiceTestFactory.Create(database.Context),
            new TestBusinessDateProvider(businessDate),
            new EfRegularAccrualAutomationLock(database.Context),
            NullLogger<RegularAccrualAutomationRunner>.Instance);

        var result = await runner.RunCurrentMonthAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Contains("уже выполняется", result.Message, StringComparison.Ordinal);
        Assert.Empty(database.Context.Accruals);
    }

    [Fact]
    public void RegularAccrualAutomationOptions_ChecksNewMonthlyDataWithinFifteenMinutesAndRetriesFailuresSooner()
    {
        var options = new RegularAccrualAutomationOptions
        {
            FailureRetryMinutes = 5
        };

        Assert.Equal(TimeSpan.FromMinutes(15), options.GetDelayAfterRun(failed: false));
        Assert.Equal(TimeSpan.FromMinutes(5), options.GetDelayAfterRun(failed: true));
    }

    [Fact]
    public void RegularAccrualAutomationWorker_RetriesUnsuccessfulRunSooner()
    {
        Assert.False(RegularAccrualAutomationWorker.DidRunFail(new RegularAccrualAutomationRunResult(true, 2, 1, "Готово")));
        Assert.True(RegularAccrualAutomationWorker.DidRunFail(new RegularAccrualAutomationRunResult(false, 1, 0, "Нужен повтор")));
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_CreatesAccrualsForActiveGaragesAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        otherIncome.Name = "Переименованные прочие доходы";
        var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
        var secondGarage = new Garage { Number = "22", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        var archivedGarage = new Garage { Number = "99", PeopleCount = 1, FloorCount = 1, Owner = secondOwner, IsArchived = true };
        var campaign = new FeeCampaign
        {
            Name = "Сбор на ворота",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            Goal = "Замена ворот",
            ContributionAmount = 500m,
            TargetAmount = 33500m,
            StartsOn = new DateOnly(2026, 5, 1),
            EndsOn = new DateOnly(2026, 7, 31),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.AddRange(secondOwner, secondGarage, archivedGarage, campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 15), "Июньский сбор"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal(campaign.Id, result.Value.FeeCampaignId);
        Assert.Equal(otherIncome.Id, result.Value.IncomeTypeId);
        Assert.Equal("Переименованные прочие доходы", result.Value.IncomeTypeName);
        Assert.Equal(2, result.Value.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(1000m, result.Value.TotalAmount);
        Assert.All(result.Value.CreatedAccruals, accrual =>
        {
            Assert.Equal(500m, accrual.Amount);
            Assert.Null(accrual.AccountingYear);
            Assert.Equal("fee_campaign", accrual.Source);
            Assert.Equal(otherIncome.Id, accrual.IncomeTypeId);
            Assert.Equal(campaign.Id, accrual.FeeCampaignId);
            Assert.Equal(campaign.Name, accrual.FeeCampaignName);
            Assert.Equal(new DateOnly(2026, 7, 31), accrual.DueDate);
            Assert.Equal(new DateOnly(2026, 8, 31), accrual.OverdueFromDate);
            Assert.Contains("Сбор на ворота", accrual.Comment, StringComparison.Ordinal);
            Assert.Contains("Июньский сбор", accrual.Comment, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(result.Value.CreatedAccruals, accrual => accrual.GarageNumber == archivedGarage.Number);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.fee_campaign_accruals_generated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(campaign.Id.ToString(), audit.EntityId);
        Assert.Contains("Сбор на ворота", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("createdCount", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("destinationFundId", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_RejectsClosedCampaign()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Закрытый сбор",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 5, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30,
            ClosedAtUtc = DateTimeOffset.UtcNow
        };
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fee_campaign_closed", result.ErrorCode);
        Assert.Empty(database.Context.Accruals.Where(item => item.FeeCampaignId == campaign.Id));
    }

    [Fact]
    public async Task FeeCampaignPayment_UsesGlobalRemainderAndClosesCampaignWithoutStaleWorksheetRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var secondGarage = new Garage
        {
            Number = "FEE-SECOND",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = fixtures.Garage.Owner
        };
        var campaign = new FeeCampaign
        {
            Name = "Сбор на общую сумму",
            IncomeTypeId = otherIncome.Id,
            IncomeType = otherIncome,
            ContributionAmount = 10m,
            TargetAmount = 1000m,
            StartsOn = new DateOnly(2026, 8, 1),
            EndsOn = new DateOnly(2026, 8, 31),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.AddRange(secondGarage, campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var period = new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1));

        var firstWorksheet = await service.GetGarageIncomeWorksheetAsync(fixtures.Garage.Id, period, CancellationToken.None);
        var firstFee = Assert.Single(firstWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(10m, firstFee.Debt);
        Assert.Equal(1000m, firstFee.FeeCampaignRemainingAmount);

        var largePayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 8, 11),
                new DateOnly(2026, 8, 1),
                995m,
                "FEE-995",
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);
        Assert.True(largePayment.Succeeded, largePayment.ErrorMessage);
        Assert.Null(campaign.ClosedAtUtc);

        var secondWorksheet = await service.GetGarageIncomeWorksheetAsync(secondGarage.Id, period, CancellationToken.None);
        var remainder = Assert.Single(secondWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(5m, remainder.Debt);
        Assert.Equal(5m, remainder.FeeCampaignRemainingAmount);

        var finalPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                secondGarage.Id,
                otherIncome.Id,
                new DateOnly(2026, 8, 11),
                new DateOnly(2026, 8, 1),
                5m,
                "FEE-5",
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);
        Assert.True(finalPayment.Succeeded, finalPayment.ErrorMessage);
        Assert.NotNull(campaign.ClosedAtUtc);
        Assert.False(campaign.IsClosedEarly);

        var closedFirstWorksheet = await service.GetGarageIncomeWorksheetAsync(fixtures.Garage.Id, period, CancellationToken.None);
        var closedSecondWorksheet = await service.GetGarageIncomeWorksheetAsync(secondGarage.Id, period, CancellationToken.None);
        Assert.DoesNotContain(closedFirstWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.DoesNotContain(closedSecondWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(995m, closedFirstWorksheet.Value.AccrualTotal);
        Assert.Equal(995m, closedFirstWorksheet.Value.IncomeTotal);
        Assert.Equal(0m, closedFirstWorksheet.Value.DebtTotal);
        Assert.Equal(5m, closedSecondWorksheet.Value.AccrualTotal);
        Assert.Equal(5m, closedSecondWorksheet.Value.IncomeTotal);
        Assert.Equal(0m, closedSecondWorksheet.Value.DebtTotal);
        Assert.Equal(1000m, await database.Context.FinancialOperations
            .Where(item => item.FeeCampaignId == campaign.Id && !item.IsCanceled)
            .SumAsync(item => item.Amount));
        Assert.Equal(1000m, await database.Context.Accruals
            .Where(item => item.FeeCampaignId == campaign.Id && !item.IsCanceled)
            .SumAsync(item => item.Amount));
        Assert.Equal(1000m, await database.Context.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.Accrual.FeeCampaignId == campaign.Id)
            .SumAsync(item => item.Amount));
        Assert.DoesNotContain(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.Accrual.IsCanceled);
    }

    [Fact]
    public async Task FeeCampaignPayment_RejectsAmountAboveCurrentGlobalRemainder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор с ограничением остатка",
            IncomeTypeId = otherIncome.Id,
            IncomeType = otherIncome,
            ContributionAmount = 10m,
            TargetAmount = 5m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 8, 11),
                new DateOnly(2026, 8, 1),
                6m,
                null,
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fee_campaign_amount_exceeds_remaining", result.ErrorCode);
        Assert.Empty(database.Context.FinancialOperations.Where(item => item.FeeCampaignId == campaign.Id));
    }

    [Fact]
    public async Task ClosedFeeCampaign_RejectsPaymentAndPrincipalCancelOrRestoreAndPreservesSettlement()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);

        var settledCampaign = new FeeCampaign
        {
            Name = "Замороженный оплаченный сбор",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        var restorableCampaign = new FeeCampaign
        {
            Name = "Замороженный отменённый сбор",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.AddRange(settledCampaign, restorableCampaign);
        await database.Context.SaveChangesAsync();
        Assert.True((await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(settledCampaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(restorableCampaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);

        var settledPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 1),
                300m,
                null,
                null,
                FeeCampaignId: settledCampaign.Id),
            null,
            CancellationToken.None);
        Assert.True(settledPayment.Succeeded, settledPayment.ErrorMessage);
        var restorablePayment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 16),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 700m,
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = restorableCampaign,
            IsCanceled = true
        };
        database.Context.FinancialOperations.Add(restorablePayment);
        await database.Context.SaveChangesAsync();

        var openPrincipal = Assert.Single(
            database.Context.Accruals,
            item => item.FeeCampaignId == settledCampaign.Id && !item.IsCanceled);
        var openCancelPayment = await service.CancelOperationAsync(
            settledPayment.Value!.Id,
            new CancelFinanceEntryRequest("Нельзя менять открытый целевой платеж"),
            null,
            CancellationToken.None);
        var openCancelPrincipal = await service.CancelAccrualAsync(
            openPrincipal.Id,
            new CancelFinanceEntryRequest("Нельзя менять открытый principal"),
            null,
            CancellationToken.None);
        var openRestorePayment = await service.RestoreOperationAsync(
            restorablePayment.Id,
            null,
            CancellationToken.None);
        Assert.False(openCancelPayment.Succeeded);
        Assert.False(openCancelPrincipal.Succeeded);
        Assert.False(openRestorePayment.Succeeded);
        Assert.Equal("fee_campaign_payment_mutation_forbidden", openCancelPayment.ErrorCode);
        Assert.Equal("fee_campaign_accrual_mutation_forbidden", openCancelPrincipal.ErrorCode);
        Assert.Equal("fee_campaign_payment_mutation_forbidden", openRestorePayment.ErrorCode);

        var dictionaries = DictionaryServiceTestFactory.Create(database.Context);
        Assert.True((await dictionaries.CloseFeeCampaignAsync(
            settledCampaign.Id,
            new CloseFeeCampaignRequest("Фиксируем фактически собранную сумму"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await dictionaries.CloseFeeCampaignAsync(
            restorableCampaign.Id,
            new CloseFeeCampaignRequest("Закрываем без оплаты"),
            null,
            CancellationToken.None)).Succeeded);

        var settledPrincipal = Assert.Single(
            database.Context.Accruals,
            item => item.FeeCampaignId == settledCampaign.Id && !item.IsCanceled);
        Assert.Equal(300m, settledPrincipal.Amount);
        var cancelPayment = await service.CancelOperationAsync(
            settledPayment.Value!.Id,
            new CancelFinanceEntryRequest("Попытка изменить закрытый итог"),
            null,
            CancellationToken.None);
        var cancelPrincipal = await service.CancelAccrualAsync(
            settledPrincipal.Id,
            new CancelFinanceEntryRequest("Попытка убрать settled principal"),
            null,
            CancellationToken.None);
        var restorePayment = await service.RestoreOperationAsync(
            restorablePayment.Id,
            null,
            CancellationToken.None);
        var canceledPrincipal = Assert.Single(
            database.Context.Accruals,
            item => item.FeeCampaignId == restorableCampaign.Id);
        var restorePrincipal = await service.RestoreAccrualAsync(
            canceledPrincipal.Id,
            null,
            CancellationToken.None);

        Assert.All(new[] { cancelPayment.Succeeded, cancelPrincipal.Succeeded, restorePayment.Succeeded, restorePrincipal.Succeeded }, Assert.False);
        Assert.Equal("fee_campaign_payment_mutation_forbidden", cancelPayment.ErrorCode);
        Assert.Equal("fee_campaign_accrual_mutation_forbidden", cancelPrincipal.ErrorCode);
        Assert.Equal("fee_campaign_payment_mutation_forbidden", restorePayment.ErrorCode);
        Assert.Equal("fee_campaign_accrual_mutation_forbidden", restorePrincipal.ErrorCode);
        Assert.False(database.Context.FinancialOperations.Single(item => item.Id == settledPayment.Value.Id).IsCanceled);
        Assert.True(database.Context.FinancialOperations.Single(item => item.Id == restorablePayment.Id).IsCanceled);
        Assert.False(settledPrincipal.IsCanceled);
        Assert.True(canceledPrincipal.IsCanceled);
        Assert.Equal(300m, database.Context.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.AccrualId == settledPrincipal.Id)
            .Sum(item => item.Amount));
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_AllowsDifferentCampaignsInSameMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var firstCampaign = new FeeCampaign
        {
            Name = "Сбор на ворота",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        var secondCampaign = new FeeCampaign
        {
            Name = "Сбор на камеры",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 700m,
            TargetAmount = 7000m,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.AddRange(firstCampaign, secondCampaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var first = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(firstCampaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        var second = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(secondCampaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(2, database.Context.Accruals.Count());
        Assert.All(database.Context.Accruals, accrual => Assert.Equal(otherIncome.Id, accrual.IncomeTypeId));
        Assert.Equal(2, database.Context.Accruals.Select(accrual => accrual.FeeCampaignId).Distinct().Count());
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_RejectsMissingOtherIncomeDestination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var campaign = new FeeCampaign
        {
            Name = "Сбор без назначения",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.Add(campaign);
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context).GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("other_income_destination_not_configured", result.ErrorCode);
        Assert.Empty(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_UsesConstantSelectCountForManyGarages()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Mass fee",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 100000m,
            StartsOn = new DateOnly(2026, 5, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        for (var index = 1; index < 200; index++)
        {
            database.Context.Garages.Add(new Garage
            {
                Number = $"F-{index:D3}",
                PeopleCount = 1,
                FloorCount = 1,
                Owner = fixtures.Garage.Owner
            });
        }

        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null);

        commandCounter.Reset();
        var firstRun = await service.GenerateFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
        var firstRunSelectCount = commandCounter.Count;

        commandCounter.Reset();
        var secondRun = await service.GenerateFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
        var secondRunSelectCount = commandCounter.Count;

        Assert.True(firstRun.Succeeded, firstRun.ErrorMessage);
        Assert.Equal(200, firstRun.Value!.CreatedCount);
        Assert.Equal(100000m, firstRun.Value.TotalAmount);
        Assert.InRange(firstRunSelectCount, 1, 7);
        Assert.False(secondRun.Succeeded);
        Assert.Equal("fee_campaign_accruals_empty", secondRun.ErrorCode);
        Assert.InRange(secondRunSelectCount, 1, 6);
        Assert.Equal(200, database.Context.Accruals.Count());
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_RejectsSecondRunForSameMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор на ворота",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 500m,
            TargetAmount = 33500m,
            StartsOn = new DateOnly(2026, 5, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        database.Context.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null);

        var first = await service.GenerateFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
        var second = await service.GenerateFeeCampaignAccrualsAsync(request, null, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("fee_campaign_accruals_empty", second.ErrorCode);
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task FeeCampaignObligation_CarriesPartialAndUnpaidWithoutDuplicateAndHidesPaidOrClosed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var owner = new Owner { LastName = "Участник", FirstName = "Сбора" };
        var partiallyPaidGarage = new Garage { Number = "FEE-PARTIAL", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var unpaidGarage = new Garage { Number = "FEE-UNPAID", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var campaign = new FeeCampaign
        {
            Name = "Сбор с повтором для неплательщиков",
            IncomeTypeId = otherIncome.Id,
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 1500m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = fixtures.Garage });
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = partiallyPaidGarage });
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = unpaidGarage });
        database.Context.AddRange(owner, partiallyPaidGarage, unpaidGarage, campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var firstMonth = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        Assert.True(firstMonth.Succeeded, firstMonth.ErrorMessage);
        Assert.Equal(3, firstMonth.Value!.CreatedCount);

        var fullPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 1),
                500m,
                "FEE-FULL",
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);
        var partialPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                partiallyPaidGarage.Id,
                otherIncome.Id,
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 1),
                200m,
                "FEE-PARTIAL",
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);
        Assert.True(fullPayment.Succeeded, fullPayment.ErrorMessage);
        Assert.True(partialPayment.Succeeded, partialPayment.ErrorMessage);

        var secondMonth = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 7, 1), null),
            null,
            CancellationToken.None);

        Assert.False(secondMonth.Succeeded);
        Assert.Equal("fee_campaign_accruals_empty", secondMonth.ErrorCode);
        var campaignAccruals = database.Context.Accruals
            .Where(item => item.FeeCampaignId == campaign.Id)
            .ToArray();
        Assert.Equal(3, campaignAccruals.Length);
        Assert.All(campaignAccruals, item => Assert.Equal(new DateOnly(2026, 6, 1), item.AccountingMonth));

        var range = new GarageIncomeWorksheetRequest(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1));
        var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(
            partiallyPaidGarage.Id,
            range,
            CancellationToken.None);
        var unpaidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            unpaidGarage.Id,
            range,
            CancellationToken.None);
        var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            range,
            CancellationToken.None);

        var partialProjection = Assert.Single(
            partialWorksheet.Value!.Rows,
            row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), partialProjection.AccountingMonth);
        Assert.Equal(500m, partialProjection.AccrualAmount);
        Assert.Equal(500m, partialProjection.PayableAmount);
        Assert.Equal(200m, partialProjection.IncomeAmount);
        Assert.Equal(300m, partialProjection.Debt);
        var unpaidProjection = Assert.Single(
            unpaidWorksheet.Value!.Rows,
            row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), unpaidProjection.AccountingMonth);
        Assert.Equal(500m, unpaidProjection.Debt);
        Assert.DoesNotContain(paidWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);

        var julyOnlyWorksheet = await service.GetGarageIncomeWorksheetAsync(
            partiallyPaidGarage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        Assert.True(julyOnlyWorksheet.Succeeded, julyOnlyWorksheet.ErrorMessage);
        var julyOnlyProjection = Assert.Single(
            julyOnlyWorksheet.Value!.Rows,
            row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(0m, julyOnlyProjection.AccrualAmount);
        Assert.Equal(300m, julyOnlyProjection.PayableAmount);
        Assert.Equal(0m, julyOnlyProjection.IncomeAmount);
        Assert.Equal(300m, julyOnlyProjection.Debt);
        Assert.Equal(300m, julyOnlyWorksheet.Value.OpeningDebt);
        Assert.Equal(0m, julyOnlyWorksheet.Value.AccrualTotal);
        Assert.Equal(0m, julyOnlyWorksheet.Value.IncomeTotal);
        Assert.Equal(300m, julyOnlyWorksheet.Value.DebtTotal);

        var closeResult = await DictionaryServiceTestFactory.Create(database.Context).CloseFeeCampaignAsync(
            campaign.Id,
            new CloseFeeCampaignRequest("Решение правления: остаток взносов списан"),
            null,
            CancellationToken.None);
        Assert.True(closeResult.Succeeded, closeResult.ErrorMessage);

        var closedPartialWorksheet = await service.GetGarageIncomeWorksheetAsync(
            partiallyPaidGarage.Id,
            range,
            CancellationToken.None);
        var closedUnpaidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            unpaidGarage.Id,
            range,
            CancellationToken.None);
        Assert.DoesNotContain(closedPartialWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.DoesNotContain(closedUnpaidWorksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(200m, closedPartialWorksheet.Value.AccrualTotal);
        Assert.Equal(200m, closedPartialWorksheet.Value.IncomeTotal);
        Assert.Equal(0m, closedPartialWorksheet.Value.ClosingBalance);
        Assert.Equal(0m, closedPartialWorksheet.Value.DebtTotal);
        Assert.Equal(0m, closedUnpaidWorksheet.Value!.AccrualTotal);
        Assert.Equal(0m, closedUnpaidWorksheet.Value.IncomeTotal);
        Assert.Equal(0m, closedUnpaidWorksheet.Value.ClosingBalance);
        Assert.Equal(0m, closedUnpaidWorksheet.Value.DebtTotal);

        var settledAccruals = database.Context.Accruals
            .Where(item => item.FeeCampaignId == campaign.Id)
            .ToArray();
        Assert.Equal(200m, Assert.Single(settledAccruals, item => item.GarageId == partiallyPaidGarage.Id).Amount);
        Assert.True(Assert.Single(settledAccruals, item => item.GarageId == unpaidGarage.Id).IsCanceled);
        Assert.Equal(
            200m,
            database.Context.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.Accrual.GarageId == partiallyPaidGarage.Id)
                .Sum(item => item.Amount));
        Assert.Contains(
            database.Context.AuditEvents,
            item => item.Action == "dictionary.fee_campaign_closed" && item.EntityId == campaign.Id.ToString());
    }

    [Fact]
    public async Task FeeCampaignWorksheet_DoesNotHideManualAccrualWhoseBasisMatchesCampaignName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Совпадающее название сбора",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = campaign,
            Garage = fixtures.Garage
        });
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var generated = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);

        database.Context.Accruals.Add(new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 1),
            Amount = 75m,
            Source = AccrualSources.Manual,
            Basis = campaign.Name,
            Comment = "Ручное начисление с одноимённым основанием"
        });
        await database.Context.SaveChangesAsync();

        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var campaignRow = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(500m, campaignRow.AccrualAmount);
        Assert.Equal(500m, campaignRow.Debt);
        var manualRow = Assert.Single(worksheet.Value.Rows, row =>
            row.FeeCampaignId == null &&
            row.IncomeTypeId == otherIncome.Id &&
            row.IncomeTypeName == campaign.Name);
        Assert.Equal(new DateOnly(2026, 6, 1), manualRow.AccountingMonth);
        Assert.Equal(75m, manualRow.AccrualAmount);
        Assert.Equal(75m, manualRow.Debt);
        Assert.Equal("Ручное начисление с одноимённым основанием", manualRow.Reason);
        Assert.Equal(575m, worksheet.Value.AccrualTotal);
        Assert.Equal(575m, worksheet.Value.DebtTotal);
        Assert.Equal(worksheet.Value.AccrualTotal, worksheet.Value.Rows.Sum(row => row.AccrualAmount));
    }

    [Fact]
    public async Task CreateIncomeAsync_CollapsesLegacyCampaignDuplicatesBeforeAllocationRebuild()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        await database.Context.Database.ExecuteSqlRawAsync(
            "DROP INDEX \"IX_accruals_GarageId_FeeCampaignId\"");
        var campaign = new FeeCampaign
        {
            Name = "Legacy duplicate campaign",
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = campaign,
            Garage = fixtures.Garage
        });
        var principal = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 1),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };
        var duplicate = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 31),
            OverdueFromDate = new DateOnly(2026, 8, 1),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };
        var existingPayment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 15),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 200m,
            Garage = fixtures.Garage,
            IncomeType = otherIncome,
            FeeCampaign = campaign
        };
        var existingAllocation = new AccrualPaymentAllocation
        {
            FinancialOperation = existingPayment,
            Accrual = principal,
            Amount = 200m
        };
        database.Context.AddRange(campaign, principal, duplicate, existingPayment, existingAllocation);
        await database.Context.SaveChangesAsync();

        var service = FinanceServiceTestFactory.Create(database.Context);
        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 8, 15),
                new DateOnly(2026, 8, 1),
                100m,
                "FEE-LEGACY-REPAIR",
                null,
                null,
                campaign.Id),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);

        Assert.Equal(principal.Id, Assert.Single(
            database.Context.Accruals,
            item => item.FeeCampaignId == campaign.Id && !item.IsCanceled).Id);
        Assert.True(duplicate.IsCanceled);
        var activeAllocations = database.Context.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.Accrual.FeeCampaignId == campaign.Id)
            .ToArray();
        Assert.Equal(2, activeAllocations.Length);
        Assert.All(activeAllocations, item => Assert.Equal(principal.Id, item.AccrualId));
        Assert.Equal(300m, activeAllocations.Sum(item => item.Amount));
        Assert.False(existingAllocation.IsActive);

        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);
        var projection = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), projection.AccountingMonth);
        Assert.Equal(200m, projection.Debt);
        Assert.Equal(500m, worksheet.Value.AccrualTotal);
        Assert.Equal(300m, worksheet.Value.IncomeTotal);
        Assert.Equal(200m, worksheet.Value.ClosingBalance);
        Assert.Equal(projection.Debt, worksheet.Value.DebtTotal);
    }

    [Fact]
    public async Task CreateIncomeAsync_NormalizesLegacyCampaignPrincipalAndRebuildsOldAndStableKeys()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var stableIncome = AddOtherIncomeDestination(database.Context);
        var legacyFund = new Fund { Name = "Legacy route fund", NormalizedName = "legacy route fund" };
        var legacyIncome = new IncomeType
        {
            Name = "Legacy route income",
            Code = "legacy_route_income",
            DestinationFund = legacyFund
        };
        var campaign = new FeeCampaign
        {
            Name = "Stable campaign with legacy principal",
            IncomeType = stableIncome,
            ContributionAmount = 500m,
            TargetAmount = 5000m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = campaign,
            Garage = fixtures.Garage
        });
        var principal = new Accrual
        {
            Garage = fixtures.Garage,
            IncomeType = legacyIncome,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };
        var legacyPayment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 15),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 100m,
            Garage = fixtures.Garage,
            IncomeType = legacyIncome,
            FeeCampaign = campaign
        };
        var legacyAllocation = new AccrualPaymentAllocation
        {
            FinancialOperation = legacyPayment,
            Accrual = principal,
            Amount = 100m
        };
        database.Context.AddRange(
            legacyFund,
            legacyIncome,
            campaign,
            principal,
            legacyPayment,
            legacyAllocation);
        await database.Context.SaveChangesAsync();

        var payment = await FinanceServiceTestFactory.Create(database.Context).CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                stableIncome.Id,
                new DateOnly(2026, 7, 15),
                new DateOnly(2026, 7, 1),
                100m,
                "FEE-NORMALIZE-ROUTE",
                null,
                FeeCampaignId: campaign.Id),
            null,
            CancellationToken.None);

        Assert.True(payment.Succeeded, payment.ErrorMessage);
        Assert.Equal(stableIncome.Id, principal.IncomeTypeId);
        Assert.False(legacyAllocation.IsActive);
        var activeAllocations = database.Context.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.AccrualId == principal.Id)
            .ToArray();
        Assert.Equal(2, activeAllocations.Length);
        Assert.Equal(200m, activeAllocations.Sum(item => item.Amount));
        var worksheet = await FinanceServiceTestFactory.Create(database.Context).GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.FeeCampaignId == campaign.Id);
        Assert.Equal(200m, row.IncomeAmount);
        Assert.Equal(300m, row.Debt);
        Assert.Equal(0m, worksheet.Value.AdvanceTotal);
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_InactivePaymentAllocationCarriesExistingObligationWithoutDuplicate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var otherIncome = AddOtherIncomeDestination(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор после отмены распределения оплаты",
            IncomeTypeId = otherIncome.Id,
            IncomeType = otherIncome,
            ContributionAmount = 500m,
            TargetAmount = 500m,
            StartsOn = new DateOnly(2026, 6, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = fixtures.Garage });
        database.Context.FeeCampaigns.Add(campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var june = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        Assert.True(june.Succeeded, june.ErrorMessage);
        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                otherIncome.Id,
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 6, 1),
                500m,
                "FEE-CANCELED",
                null),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);

        var inactiveAllocation = Assert.Single(
            database.Context.AccrualPaymentAllocations,
            item => item.IsActive && item.FinancialOperationId == payment.Value!.Id);
        inactiveAllocation.IsActive = false;
        await database.Context.SaveChangesAsync();
        Assert.Empty(database.Context.AccrualPaymentAllocations.Where(item => item.IsActive));

        var july = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 7, 1), null),
            null,
            CancellationToken.None);

        Assert.False(july.Succeeded);
        Assert.Equal("fee_campaign_accruals_empty", july.ErrorCode);
        Assert.Single(database.Context.Accruals, item => item.FeeCampaignId == campaign.Id && !item.IsCanceled);
        var option = Assert.Single(await new EfFeeCampaignRepository(database.Context)
            .GetPaymentOptionsForGarageAsync(
                fixtures.Garage.Id,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                CancellationToken.None));
        Assert.False(option.Campaign.ClosedAtUtc.HasValue);
        Assert.NotNull(option.Accrual);
        Assert.Equal(0m, option.PaidAmount);
        Assert.Equal(0m, option.CollectedAmount);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);
        var projection = Assert.Single(worksheet.Value!.Rows, row => row.FeeCampaignId == campaign.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), projection.AccountingMonth);
        Assert.Equal(0m, projection.AccrualAmount);
        Assert.Equal(500m, projection.Debt);
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_UsesSelectedParticipantGarages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        AddOtherIncomeDestination(database.Context);
        var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
        var selectedGarage = new Garage { Number = "22", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        var notSelectedGarage = new Garage { Number = "33", PeopleCount = 1, FloorCount = 1, Owner = secondOwner };
        var campaign = new FeeCampaign
        {
            Name = "Сбор на камеры",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            ContributionAmount = 700m,
            TargetAmount = 35000m,
            StartsOn = new DateOnly(2026, 5, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { FeeCampaign = campaign, Garage = selectedGarage });
        database.Context.AddRange(secondOwner, selectedGarage, notSelectedGarage, campaign);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(campaign.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = Assert.Single(result.Value!.CreatedAccruals);
        Assert.Equal(selectedGarage.Number, accrual.GarageNumber);
        Assert.Equal(700m, accrual.Amount);
        Assert.Equal("fee_campaign", accrual.Source);
        Assert.DoesNotContain(database.Context.Accruals, item => item.GarageId == fixtures.Garage.Id || item.GarageId == notSelectedGarage.Id);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_KeepsExistingAccrualAmountAfterTariffUpdate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "connection";
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var dictionaries = DictionaryServiceTestFactory.Create(database.Context);

        await finance.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        await dictionaries.UpdateTariffAsync(
            tariff.Id,
            new UpsertTariffRequest("Членский тариф", "fixed", 500m, new DateOnly(2026, 1, 1), "Новая ставка"),
            null,
            CancellationToken.None);

        var july = await finance.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 7, 1), null),
            null,
            CancellationToken.None);

        Assert.True(july.Succeeded);
        var accruals = await finance.GetAccrualsAsync(new AccrualListRequest(null, null, null), CancellationToken.None);
        Assert.Contains(accruals, item =>
            item.AccountingMonth == new DateOnly(2026, 6, 1) &&
            item.Amount == 300m &&
            item.Comment == "Автоначисление; тариф Членский тариф: ставка 300.00, действует с 01.01.2026.");
        Assert.Contains(accruals, item =>
            item.AccountingMonth == new DateOnly(2026, 7, 1) &&
            item.Amount == 500m &&
            item.Comment == "Автоначисление; тариф Членский тариф: ставка 500.00, действует с 01.01.2026.");
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CalculatesMeterAmountFromReading()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, null),
            null,
            CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(275m, result.Value!.TotalAmount);
        Assert.Equal(275m, result.Value.CreatedAccruals[0].Amount);
        Assert.Equal("meter_water", result.Value.CalculationBase);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_AppliesActiveWaterRateImmediatelyAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по действующей ставке",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Водоснабжение",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            UnitName = "м³"
        });
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Повторная настройка воды",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            UnitName = "м³"
        });
        await database.Context.SaveChangesAsync();
        var actorUserId = Guid.NewGuid();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, null),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(5.5m, result.Value!.Consumption);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(275m, accrual.Amount);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Equal(AccrualSources.Regular, accrual.Source);
        Assert.Contains("ставка 50.00", accrual.Comment, StringComparison.Ordinal);
        Assert.Contains("расход 5,5", accrual.Comment, StringComparison.Ordinal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.metered_accrual_created_from_reading");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("275.00", audit.Summary, StringComparison.Ordinal);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var waterRow = Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(275m, waterRow.AccrualAmount);
        Assert.Equal(275m, waterRow.PayableAmount);
        Assert.Equal(275m, waterRow.Debt);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_UsesIndependentMeterForArbitraryRegularService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "custom_metered_service";
        fixtures.IncomeType.Name = "Охрана по счётчику";
        var tariff = new Tariff
        {
            Name = "Тариф охраны по показанию",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 12.5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Охрана по счётчику",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            UnitName = "ед."
        };
        database.Context.ChargeServiceSettings.Add(setting);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                setting.MeterKind,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                8m,
                null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(setting.MeterKind, result.Value!.MeterKind);
        Assert.Equal(8m, result.Value.Consumption);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(100m, accrual.Amount);
        Assert.Equal(fixtures.IncomeType.Id, accrual.IncomeTypeId);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(setting.MeterKind, row.MeterKind);
        Assert.Equal(8m, row.MeterConsumption);
        Assert.Equal(100m, row.PayableAmount);
    }

    [Fact]
    public async Task SavePaymentFormMeterReadingAsync_AppliesActiveTieredElectricityRatesImmediately()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия по диапазонам",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Электроэнергия",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            HasTieredTariff = true,
            UnitName = "кВт·ч"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                230m,
                null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(130m, result.Value!.Consumption);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(650m, accrual.Amount);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Contains("пороговый тариф по текущему показанию", accrual.Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavePaymentFormMeterReadingAsync_AppliesActiveTieredWaterRatesImmediately()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        database.Context.ChargeServiceSettings.RemoveRange(database.Context.ChargeServiceSettings);
        var tariff = new Tariff
        {
            Name = "Вода по диапазонам",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 2m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Вода",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            HasTieredTariff = true,
            UnitName = "м³"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Water,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                140m,
                null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(130m, result.Value!.Consumption);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(650m, accrual.Amount);
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Contains("пороговый тариф по текущему показанию", accrual.Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavePaymentFormMeterReadingAsync_UsesFlatRateWhenTieredBillingIsDisabled()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия с доступными порогами",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Электроэнергия без порогов",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            HasTieredTariff = false,
            UnitName = "кВт·ч"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                230m,
                null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(130m, result.Value!.Consumption);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(260m, accrual.Amount);
        Assert.Contains("ставка 2.00", accrual.Comment, StringComparison.Ordinal);
        Assert.DoesNotContain("пороговый тариф", accrual.Comment, StringComparison.Ordinal);

        var updated = await service.UpdateMeterReadingAsync(
            result.Value.Id,
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 21),
                250m,
                null,
                result.Value.Version),
            null,
            CancellationToken.None);

        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal(300m, accrual.Amount);
        Assert.Contains("ставка 2.00", accrual.Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_CreatesPreviouslyMissingCurrentMonthMeteredAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var tariff = new Tariff
        {
            Name = "Подключенный тариф воды",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Водоснабжение",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            UnitName = "м³"
        });
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Water,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 21),
                18m,
                null,
                reading.Value.Version),
            null,
            CancellationToken.None);

        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(8m, result.Value!.Consumption);
        Assert.Equal(400m, Assert.Single(database.Context.Accruals).Amount);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_AllocatesExistingAdvanceToImmediateMeteredAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Water;
        var tariff = new Tariff
        {
            Name = "Вода",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Водоснабжение",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            UnitName = "м³"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var advance = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 10),
                new DateOnly(2026, 6, 1),
                200m,
                "PKO-water-advance",
                null),
            null,
            CancellationToken.None);
        Assert.True(advance.Succeeded, advance.ErrorMessage);
        Assert.Empty(database.Context.AccrualPaymentAllocations.Where(item => item.IsActive));

        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Water,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                15.5m,
                null),
            null,
            CancellationToken.None);

        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var allocation = Assert.Single(database.Context.AccrualPaymentAllocations, item => item.IsActive);
        Assert.Equal(200m, allocation.Amount);
        Assert.Equal(75m, Assert.Single(database.Context.Accruals).Amount - allocation.Amount);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public async Task CreateMeterReadingAsync_AppliesDatedMeteredConfigurationOutsideCurrentMonth(int monthNumber)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = MeterKinds.Water;
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Текущая вода",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = new Tariff
            {
                Name = "Текущий тариф воды",
                CalculationBase = TariffCalculationBases.MeterWater,
                Rate = 50m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            },
            IsMetered = true,
            UnitName = "м³"
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                MeterKinds.Water,
                new DateOnly(2026, monthNumber, 1),
                new DateOnly(2026, monthNumber, 20),
                15.5m,
                null,
                PeriodOverrideReason: "Ввод показания за выбранный период"),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(new DateOnly(2026, monthNumber, 1), accrual.AccountingMonth);
        Assert.Equal(275m, accrual.Amount);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.metered_accrual_created_from_reading");
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_RejectsTariffThatDoesNotMatchIncomeType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var tariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("regular_accrual_tariff_mismatch", result.ErrorCode);
        Assert.Empty(database.Context.Accruals);
        Assert.Empty(database.Context.AuditEvents.Where(item => item.Action == "finance.regular_accruals_generated"));
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CalculatesTieredElectricityAmountFromReading()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия",
            CalculationBase = "meter_electricity",
            Rate = 4m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 230m, null),
            null,
            CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(650m, result.Value!.TotalAmount);
        Assert.Equal(650m, result.Value.CreatedAccruals[0].Amount);
        Assert.Equal("meter_electricity", result.Value.CalculationBase);
        Assert.Contains("пороговый тариф по текущему показанию", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Contains("пороговый тариф по текущему показанию", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_UsesFlatElectricityRateWhenServiceDisablesTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Электроэнергия без порогов",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = fixtures.IncomeType,
            Tariff = tariff,
            IsMetered = true,
            HasTieredTariff = false,
            UnitName = "кВт·ч"
        });
        database.Context.MeterReadings.Add(new MeterReading
        {
            Garage = fixtures.Garage,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = new DateOnly(2026, 6, 1),
            ReadingDate = new DateOnly(2026, 6, 20),
            PreviousValue = 100m,
            CurrentValue = 230m,
            Consumption = 130m
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(260m, result.Value!.TotalAmount);
        Assert.Contains("ставка 2.00", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
        Assert.DoesNotContain("пороговый тариф", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Contains("ставка 2.00", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CalculatesVariableElectricityTiersFromPersistedConfiguration()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия",
            CalculationBase = "meter_electricity",
            Rate = 2m,
            ElectricityTiersJson = """
                [
                  {"id":"11111111-1111-1111-1111-111111111111","name":"До 50","upperBound":50,"rate":2,"isCustom":false},
                  {"id":"22222222-2222-2222-2222-222222222222","name":"До 100","upperBound":100,"rate":3,"isCustom":false},
                  {"id":"33333333-3333-3333-3333-333333333333","name":"До 150","upperBound":150,"rate":4,"isCustom":true},
                  {"id":"44444444-4444-4444-4444-444444444444","name":"Свыше 150","upperBound":null,"rate":5,"isCustom":false}
                ]
                """,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 230m, null),
            null,
            CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(650m, result.Value!.TotalAmount);
        Assert.Contains("до 150 кВт·ч по 4.00", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_UsesConstantSelectCountForManyGaragesAndMeterReadings()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Массовый тариф воды",
            CalculationBase = "meter_water",
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        var garages = new List<Garage> { fixtures.Garage };
        for (var index = 1; index < 200; index++)
        {
            var garage = new Garage
            {
                Number = $"M-{index:D3}",
                PeopleCount = 1,
                FloorCount = 1,
                Owner = fixtures.Garage.Owner
            };
            garages.Add(garage);
            database.Context.Garages.Add(garage);
        }
        database.Context.MeterReadings.AddRange(garages.Select(garage => new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new DateOnly(2026, 6, 1),
            ReadingDate = new DateOnly(2026, 6, 30),
            PreviousValue = 10m,
            CurrentValue = 12m,
            Consumption = 2m
        }));
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        commandCounter.Reset();
        var firstRun = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        var firstRunSelectCount = commandCounter.Count;

        commandCounter.Reset();
        var secondRun = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        var secondRunSelectCount = commandCounter.Count;

        Assert.True(firstRun.Succeeded, firstRun.ErrorMessage);
        Assert.Equal(200, firstRun.Value!.CreatedCount);
        Assert.Equal(20000m, firstRun.Value.TotalAmount);
        Assert.InRange(firstRunSelectCount, 1, 7);
        Assert.False(secondRun.Succeeded);
        Assert.Equal("regular_accruals_empty", secondRun.ErrorCode);
        Assert.InRange(secondRunSelectCount, 1, 5);
        Assert.DoesNotContain(commandCounter.Commands, command => command.Contains("JOIN \"owners\"", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(200, database.Context.Accruals.Count());
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_UsesReplacementMeterReadingAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var firstReading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, "Первичный замер"),
            null,
            CancellationToken.None);
        Assert.True(firstReading.Succeeded);
        var canceled = await service.CancelMeterReadingAsync(
            firstReading.Value!.Id,
            new CancelFinanceEntryRequest("Замер внесен ошибочно"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded);
        var replacement = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, "Повторный замер"),
            null,
            CancellationToken.None);
        Assert.True(replacement.Succeeded);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(400m, result.Value!.TotalAmount);
        Assert.Equal(400m, result.Value.CreatedAccruals[0].Amount);
        Assert.Equal(1, result.Value.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal("meter_water", result.Value.CalculationBase);
        Assert.Equal(2, await database.Context.MeterReadings.CountAsync());
        Assert.Equal(1, await database.Context.MeterReadings.CountAsync(reading => reading.IsCanceled));
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_RejectsSecondRunForSameMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null);
        await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("regular_accruals_empty", result.ErrorCode);
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CreatesRowsForGaragesAddedAfterTheFirstMonthlyRun()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null);
        await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        var laterGarage = new Garage
        {
            Number = "NEW-001",
            PeopleCount = 1,
            FloorCount = 1,
            Owner = fixtures.Garage.Owner
        };
        database.Context.Garages.Add(laterGarage);
        await database.Context.SaveChangesAsync();

        var result = await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Equal(laterGarage.Id, Assert.Single(result.Value.CreatedAccruals).GarageId);
        Assert.Equal(2, await database.Context.Accruals.CountAsync());
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccrualsAsync_CreatesSalaryForEveryActiveSupplierInGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var group = fixtures.Supplier.Group;
        var secondSupplier = new Supplier { Name = "Бухгалтер", GroupId = group.Id };
        var archivedSupplier = new Supplier { Name = "Архивный сотрудник", GroupId = group.Id, IsArchived = true };
        var otherGroup = new SupplierGroup { Name = "Юристы" };
        var otherSupplier = new Supplier { Name = "Юрист", Group = otherGroup };
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary", IsSystem = true };
        database.Context.AddRange(secondSupplier, archivedSupplier, otherSupplier, salaryType);
        await database.Context.SaveChangesAsync();

        var result = await service.GenerateSupplierGroupSalaryAccrualsAsync(
            new GenerateSupplierGroupSalaryAccrualsRequest(group.Id, new DateOnly(2026, 6, 20), 7000.005m, "PAY-06", "Июнь"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal(2, result.Value.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(14000.02m, result.Value.TotalAmount);
        Assert.All(result.Value.CreatedAccruals, accrual =>
        {
            Assert.Equal("Зарплата", accrual.ExpenseTypeName);
            Assert.Equal("regular", accrual.Source);
            Assert.Equal("PAY-06", accrual.DocumentNumber);
            Assert.Equal(7000.01m, accrual.Amount);
            Assert.Contains("Зарплата по группе Коммунальные услуги", accrual.Comment, StringComparison.Ordinal);
            Assert.Contains("Июнь", accrual.Comment, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(result.Value.CreatedAccruals, accrual => accrual.SupplierName == archivedSupplier.Name || accrual.SupplierName == otherSupplier.Name);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.supplier_group_salary_accruals_generated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано начислений зарплаты: 2", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("группа Коммунальные услуги", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccrualsAsync_UsesConstantSelectCountForManySuppliers()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var salaryType = new ExpenseType { Name = "Salary", Code = "salary", IsSystem = true };
        for (var index = 1; index < 200; index++)
        {
            database.Context.Suppliers.Add(new Supplier
            {
                Name = $"Employee {index:D3}",
                GroupId = fixtures.Supplier.GroupId
            });
        }

        database.Context.ExpenseTypes.Add(salaryType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var request = new GenerateSupplierGroupSalaryAccrualsRequest(
            fixtures.Supplier.GroupId,
            new DateOnly(2026, 6, 1),
            7000m,
            "PAY-06",
            null);

        commandCounter.Reset();
        var firstRun = await service.GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        var firstRunSelectCount = commandCounter.Count;

        commandCounter.Reset();
        var secondRun = await service.GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        var secondRunSelectCount = commandCounter.Count;

        Assert.True(firstRun.Succeeded, firstRun.ErrorMessage);
        Assert.Equal(200, firstRun.Value!.CreatedCount);
        Assert.Equal(1400000m, firstRun.Value.TotalAmount);
        Assert.InRange(firstRunSelectCount, 1, 4);
        Assert.False(secondRun.Succeeded);
        Assert.Equal("salary_accruals_empty", secondRun.ErrorCode);
        Assert.InRange(secondRunSelectCount, 1, 4);
        Assert.Equal(200, database.Context.SupplierAccruals.Count());
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccrualsAsync_RejectsSecondRunForSameMonthAndDocument()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary", IsSystem = true };
        database.Context.Add(salaryType);
        await database.Context.SaveChangesAsync();
        var request = new GenerateSupplierGroupSalaryAccrualsRequest(fixtures.Supplier.GroupId, new DateOnly(2026, 6, 1), 7000m, null, null);

        var first = await service.GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        var second = await service.GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("salary_accruals_empty", second.ErrorCode);
    }

    [Fact]
    public async Task GetAccrualsAsync_SearchesAndOrdersByMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 5, 1), 500m, "regular", null), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 600m, "manual", "Ежемесячная корректировка гаража %_"), null, CancellationToken.None);

        var result = await service.GetAccrualsAsync(new AccrualListRequest(null, null, "ежемесячная"), CancellationToken.None);
        var literalWildcard = await service.GetAccrualsAsync(new AccrualListRequest(null, null, "%_"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(result, literalWildcard);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(600m, result[0].Amount);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_AllowsAnotherAccountingMonthAndAuditsOverrideReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                fixtures.Garage.Id,
                "water",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 20),
                15.5m,
                null,
                PeriodOverrideReason: "Плановый ввод показания"),
            Guid.NewGuid(),
            CancellationToken.None);
        var paymentFormResult = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 15.5m, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value!.AccountingMonth);
        Assert.False(paymentFormResult.Succeeded);
        Assert.Equal("meter_reading_conflict", paymentFormResult.ErrorCode);
        Assert.Single(database.Context.MeterReadings);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created");
        Assert.Contains("Причина ввода вне текущего месяца: Плановый ввод показания", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_UsesInitialMeterValueAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, "Контроль"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(10m, result.Value!.PreviousValue);
        Assert.Equal(5.5m, result.Value.Consumption);
        Assert.False(result.Value.HasGapWarning);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Внесено показание water", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("дата 20.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("предыдущее 10", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("текущее 15,5", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("расход 5,5", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("без предупреждения", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Контроль", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeterReadingCommands_RequireAnExplicitManualWaterValue()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);

        var createWithoutValue = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 17), null, null),
            null,
            CancellationToken.None);
        var paymentFormWithoutValue = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 17), null, null),
            null,
            CancellationToken.None);
        var updateWithoutValue = await service.UpdateMeterReadingAsync(
            created.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), null, null, created.Value.Version),
            null,
            CancellationToken.None);
        var correctionWithoutValue = await service.CorrectHistoricalMeterReadingAsync(
            created.Value.Id,
            new CorrectHistoricalMeterReadingRequest(new DateOnly(2026, 6, 21), null, null, "Сверка с журналом", created.Value.Version),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.All(
            new[] { createWithoutValue, paymentFormWithoutValue, updateWithoutValue, correctionWithoutValue },
            result =>
            {
                Assert.False(result.Succeeded);
                Assert.Equal("meter_reading_value_required", result.ErrorCode);
            });
        var stored = Assert.Single(database.Context.MeterReadings);
        Assert.Equal(15m, stored.CurrentValue);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created");
    }

    [Fact]
    public async Task CreateMeterReadingAsync_DoesNotSubstituteZeroForMissingWaterBaseline()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.InitialWaterMeterValue = null;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var missingBaseline = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 15m, null),
            null,
            CancellationToken.None);

        Assert.False(missingBaseline.Succeeded);
        Assert.Equal("water_meter_reading_baseline_required", missingBaseline.ErrorCode);
        Assert.Empty(database.Context.MeterReadings);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created");

        fixtures.Garage.InitialWaterMeterValue = 10m;
        await database.Context.SaveChangesAsync();
        var first = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 15m, null),
            null,
            CancellationToken.None);
        fixtures.Garage.InitialWaterMeterValue = null;
        await database.Context.SaveChangesAsync();

        var next = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 18m, null),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.Equal(15m, next.Value!.PreviousValue);
        Assert.Equal(3m, next.Value.Consumption);
    }

    [Fact]
    public async Task SavePaymentFormMeterReadingAsync_CreatesAndUpdatesWithRotatedVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var actorUserId = Guid.NewGuid();
        var createRequest = new SavePaymentFormMeterReadingRequest(
            fixtures.Garage.Id,
            "water",
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 6, 20),
            15.5m,
            "Из формы оплаты");

        var created = await service.SavePaymentFormMeterReadingAsync(createRequest, actorUserId, CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.NotEqual(Guid.Empty, created.Value!.Version);
        Assert.Equal(new DateOnly(2026, 6, 1), created.Value.AccountingMonth);

        var updated = await service.SavePaymentFormMeterReadingAsync(
            createRequest with
            {
                MeterReadingId = created.Value.Id,
                ExpectedVersion = created.Value.Version,
                CurrentValue = 18m,
                Comment = "Исправлено из формы оплаты"
            },
            actorUserId,
            CancellationToken.None);

        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal(18m, updated.Value!.CurrentValue);
        Assert.Equal(8m, updated.Value.Consumption);
        Assert.NotEqual(created.Value.Version, updated.Value.Version);
        Assert.Single(database.Context.MeterReadings);
        Assert.Equal(2, database.Context.AuditEvents.Count(item =>
            item.Action == "finance.meter_reading_created" || item.Action == "finance.meter_reading_updated"));
    }

    [Fact]
    public async Task SavePaymentFormMeterReadingAsync_RejectsStaleOrMissingVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var created = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                15.5m,
                null),
            null,
            CancellationToken.None);
        var originalVersion = created.Value!.Version;
        var firstUpdate = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 21),
                17m,
                null,
                created.Value.Id,
                originalVersion),
            null,
            CancellationToken.None);

        var staleUpdate = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 22),
                19m,
                null,
                created.Value.Id,
                originalVersion),
            null,
            CancellationToken.None);
        var missingToken = await service.SavePaymentFormMeterReadingAsync(
            new SavePaymentFormMeterReadingRequest(
                fixtures.Garage.Id,
                "water",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 22),
                19m,
                null,
                created.Value.Id),
            null,
            CancellationToken.None);

        Assert.True(firstUpdate.Succeeded, firstUpdate.ErrorMessage);
        Assert.False(staleUpdate.Succeeded);
        Assert.Equal("meter_reading_conflict", staleUpdate.ErrorCode);
        Assert.False(missingToken.Succeeded);
        Assert.Equal("meter_reading_conflict", missingToken.ErrorCode);
        Assert.Equal(17m, database.Context.MeterReadings.Single().CurrentValue);
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_AllowsOnlyCurrentAccountingMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));
        var past = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var current = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 17), 20m, null),
            null,
            CancellationToken.None);
        var future = await FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero))).CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 25m, null),
            null,
            CancellationToken.None);

        var pastUpdate = await service.UpdateMeterReadingAsync(
            past.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 16m, null, past.Value.Version),
            null,
            CancellationToken.None);
        var currentUpdate = await service.UpdateMeterReadingAsync(
            current.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 18), 21m, null, current.Value.Version),
            null,
            CancellationToken.None);
        var rebuiltFutureVersion = database.Context.MeterReadings.Single(item => item.Id == future.Value!.Id).Version;
        var futureUpdate = await service.UpdateMeterReadingAsync(
            future.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 21), 26m, null, rebuiltFutureVersion),
            null,
            CancellationToken.None);

        Assert.False(pastUpdate.Succeeded);
        Assert.Equal("meter_reading_current_month_required", pastUpdate.ErrorCode);
        Assert.True(currentUpdate.Succeeded, currentUpdate.ErrorMessage);
        Assert.Equal(21m, currentUpdate.Value!.CurrentValue);
        Assert.False(futureUpdate.Succeeded);
        Assert.Equal("meter_reading_current_month_required", futureUpdate.ErrorCode);
        Assert.Equal(15m, database.Context.MeterReadings.Single(item => item.Id == past.Value.Id).CurrentValue);
        Assert.Equal(25m, database.Context.MeterReadings.Single(item => item.Id == future.Value.Id).CurrentValue);
        var updateAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_updated");
        Assert.Contains("за 07.2026", updateAudit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectHistoricalMeterReadingAsync_AllowsBlankReasonAndWritesAuditedCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, "До сверки"),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var corrected = await service.CorrectHistoricalMeterReadingAsync(
            created.Value!.Id,
            new CorrectHistoricalMeterReadingRequest(new DateOnly(2026, 6, 21), 18m, "После сверки", "   ", created.Value.Version),
            actorUserId,
            CancellationToken.None);

        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        Assert.Equal(18m, corrected.Value!.CurrentValue);
        Assert.NotEqual(created.Value.Version, corrected.Value.Version);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_historical_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("update", audit.ActionKind);
        Assert.Contains("Скорректировано показание другого периода", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Корректировка показания за другой месяц", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Корректировка показания за другой месяц.", metadata.RootElement.GetProperty("reason").GetString());
        Assert.Contains("Текущее показание", metadata.RootElement.GetProperty("changedFields").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectHistoricalMeterReadingAsync_RejectsCurrentAndAllowsAnotherMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)));
        var current = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 17), 110m, null),
            null,
            CancellationToken.None);
        var future = await FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero))).CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 17), 120m, null),
            null,
            CancellationToken.None);

        var currentResult = await service.CorrectHistoricalMeterReadingAsync(
            current.Value!.Id,
            new CorrectHistoricalMeterReadingRequest(new DateOnly(2026, 7, 18), 111m, null, "Причина", current.Value.Version),
            null,
            CancellationToken.None);
        var futureResult = await service.CorrectHistoricalMeterReadingAsync(
            future.Value!.Id,
            new CorrectHistoricalMeterReadingRequest(new DateOnly(2026, 8, 18), 121m, null, "Причина", future.Value.Version),
            null,
            CancellationToken.None);

        Assert.False(currentResult.Succeeded);
        Assert.Equal("meter_reading_historical_month_required", currentResult.ErrorCode);
        Assert.True(futureResult.Succeeded, futureResult.ErrorMessage);
        Assert.Equal(121m, futureResult.Value!.CurrentValue);
        Assert.Equal(110m, database.Context.MeterReadings.Single(item => item.Id == current.Value.Id).CurrentValue);
        Assert.Equal(121m, database.Context.MeterReadings.Single(item => item.Id == future.Value.Id).CurrentValue);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_historical_updated");
    }

    [Fact]
    public async Task CreateMeterReadingAsync_RoundsMeterValuesAndConsumptionAwayFromZero()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.InitialWaterMeterValue = 10.0005m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5555m, null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(15.556m, result.Value!.CurrentValue);
        Assert.Equal(10.001m, result.Value.PreviousValue);
        Assert.Equal(5.555m, result.Value.Consumption);
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_WritesChangedFieldsAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, "Первичное показание"),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateMeterReadingAsync(
            created.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 18m, "Исправили после сверки"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(18m, result.Value!.CurrentValue);
        Assert.Equal(8m, result.Value.Consumption);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("meter_reading", metadata.RootElement.GetProperty("financeEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Текущее показание", changedFields, StringComparison.Ordinal);
        Assert.Contains("Расход", changedFields, StringComparison.Ordinal);
        Assert.Contains("Комментарий", changedFields, StringComparison.Ordinal);
        Assert.Equal("3", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_RecalculatesLinkedUnpaidAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по счетчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var generation = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        var result = await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, null, reading.Value.Version),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(generation.Succeeded, generation.ErrorMessage);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(8m, result.Value!.Consumption);
        Assert.Equal(400m, Assert.Single(database.Context.Accruals).Amount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated_from_meter_reading");
        Assert.Equal("update", audit.ActionKind);
        Assert.Contains("было", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("250.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("400.00", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_RecalculatesLinkedTieredElectricityAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "electricity";
        var tariff = new Tariff
        {
            Name = "Электроэнергия по диапазонам",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 4m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 230m, null),
            null,
            CancellationToken.None);
        Assert.True((await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 250m, null, reading.Value.Version),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(150m, result.Value!.Consumption);
        Assert.Equal(750m, Assert.Single(database.Context.Accruals).Amount);
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_RejectsRecalculationWhenLinkedAccrualIsPartiallyPaid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по счетчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var generation = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);
        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 100m, "PKO-partial-meter", null),
            null,
            CancellationToken.None);

        var result = await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, null, reading.Value.Version),
            null,
            CancellationToken.None);

        Assert.True(generation.Succeeded, generation.ErrorMessage);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_accrual_paid", result.ErrorCode);
        Assert.Contains("частично оплачено", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(15m, database.Context.MeterReadings.Single().CurrentValue);
        Assert.Equal(reading.Value.Version, database.Context.MeterReadings.Single().Version);
        Assert.Equal(250m, database.Context.Accruals.Single().Amount);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated_from_meter_reading");
    }

    [Fact]
    public async Task UpdateMeterReadingAsync_RollsBackReadingWhenLinkedAccrualUpdateFails()
    {
        var failure = new MeteredAccrualUpdateFailureInterceptor();
        await using var database = await TestDatabase.CreateAsync(failure);
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по счетчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        Assert.True((await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);
        failure.Enabled = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, null, reading.Value.Version),
            null,
            CancellationToken.None));

        failure.Enabled = false;
        database.Context.ChangeTracker.Clear();
        Assert.Equal(15m, (await database.Context.MeterReadings.SingleAsync()).CurrentValue);
        Assert.Equal(250m, (await database.Context.Accruals.SingleAsync()).Amount);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated_from_meter_reading");
    }

    [Fact]
    public async Task CreateMeterReadingAsync_InsertingHistoricalReadingRebuildsFollowingChain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero)));
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 20), 15m, null),
            null,
            CancellationToken.None);
        var june = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 30m, null),
            null,
            CancellationToken.None);

        var may = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 20m, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(may.Succeeded, may.ErrorMessage);
        var rebuiltJune = database.Context.MeterReadings.Single(item => item.Id == june.Value!.Id);
        Assert.Equal(20m, rebuiltJune.PreviousValue);
        Assert.Equal(10m, rebuiltJune.Consumption);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_chain_rebuilt" && item.EntityId == rebuiltJune.Id.ToString());
    }

    [Fact]
    public async Task CorrectCancelAndRestoreHistoricalMeterReading_RebuildFollowingChain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero)));
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 20), 15m, null),
            null,
            CancellationToken.None);
        var may = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 20m, null),
            null,
            CancellationToken.None);
        var june = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 30m, null),
            null,
            CancellationToken.None);

        var corrected = await service.CorrectHistoricalMeterReadingAsync(
            may.Value!.Id,
            new CorrectHistoricalMeterReadingRequest(new DateOnly(2026, 5, 21), 22m, null, "Сверка", may.Value.Version),
            null,
            CancellationToken.None);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        var juneId = june.Value!.Id;
        Assert.Equal(8m, database.Context.MeterReadings.Single(item => item.Id == juneId).Consumption);

        var canceled = await service.CancelMeterReadingAsync(
            may.Value.Id,
            new CancelFinanceEntryRequest("Ошибочная запись"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        var afterCancel = database.Context.MeterReadings.Single(item => item.Id == juneId);
        Assert.Equal(15m, afterCancel.PreviousValue);
        Assert.Equal(15m, afterCancel.Consumption);

        var restored = await service.RestoreMeterReadingAsync(may.Value.Id, null, CancellationToken.None);
        Assert.True(restored.Succeeded, restored.ErrorMessage);
        var afterRestore = database.Context.MeterReadings.Single(item => item.Id == juneId);
        Assert.Equal(22m, afterRestore.PreviousValue);
        Assert.Equal(8m, afterRestore.Consumption);
    }

    [Fact]
    public async Task InsertHistoricalMeterReading_RecalculatesFollowingUnpaidAccrualAndRejectsPaidAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по счетчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero)));
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 20), 15m, null),
            null,
            CancellationToken.None);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 30m, null),
            null,
            CancellationToken.None);
        Assert.True((await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);

        var inserted = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 20m, null),
            null,
            CancellationToken.None);
        Assert.True(inserted.Succeeded, inserted.ErrorMessage);
        Assert.Equal(500m, Assert.Single(database.Context.Accruals).Amount);

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1), 100m, "PKO-chain", null),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        var rejected = await service.CancelMeterReadingAsync(
            inserted.Value!.Id,
            new CancelFinanceEntryRequest("Проверка оплаченного периода"),
            null,
            CancellationToken.None);

        Assert.False(rejected.Succeeded);
        Assert.Equal("meter_reading_accrual_paid", rejected.ErrorCode);
        Assert.False(database.Context.MeterReadings.Single(item => item.Id == inserted.Value.Id).IsCanceled);
        Assert.Equal(500m, Assert.Single(database.Context.Accruals).Amount);
    }

    [Fact]
    public async Task GetMeterDevicesAsync_ReturnsOnlyRequestedGarageAndKindInNewestFirstOrder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.MeterDevices.AddRange(
            new MeterDevice
            {
                GarageId = fixtures.Garage.Id,
                MeterKind = MeterKinds.Electricity,
                SerialNumber = "ЭЛ-СТАРЫЙ",
                InstalledOn = new DateOnly(2025, 1, 1),
                RemovedOn = new DateOnly(2026, 6, 30),
                InitialValue = 0m,
                FinalValue = 100m
            },
            new MeterDevice
            {
                GarageId = fixtures.Garage.Id,
                MeterKind = MeterKinds.Electricity,
                SerialNumber = "ЭЛ-НОВЫЙ",
                InstalledOn = new DateOnly(2026, 7, 1),
                InitialValue = 0m
            },
            new MeterDevice
            {
                GarageId = fixtures.Garage.Id,
                MeterKind = MeterKinds.Water,
                SerialNumber = "В-001",
                InstalledOn = new DateOnly(2026, 1, 1),
                InitialValue = 0m
            });
        await database.Context.SaveChangesAsync();

        var service = FinanceServiceTestFactory.Create(database.Context);
        var devices = await service.GetMeterDevicesAsync(fixtures.Garage.Id, MeterKinds.Electricity, CancellationToken.None);

        Assert.Collection(
            devices,
            device =>
            {
                Assert.Equal("ЭЛ-НОВЫЙ", device.SerialNumber);
                Assert.Null(device.RemovedOn);
            },
            device =>
            {
                Assert.Equal("ЭЛ-СТАРЫЙ", device.SerialNumber);
                Assert.Equal(new DateOnly(2026, 6, 30), device.RemovedOn);
            });
        Assert.Empty(await service.GetMeterDevicesAsync(fixtures.Garage.Id, "unknown", CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceMeterDeviceAsync_AllowsLowerReadingAndStartsNewPhysicalDeviceChain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)));
        var june = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 150m, null),
            null,
            CancellationToken.None);
        var july = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 20), 160m, null),
            null,
            CancellationToken.None);

        var replacement = await service.ReplaceMeterDeviceAsync(
            new ReplaceMeterDeviceRequest(
                fixtures.Garage.Id,
                MeterKinds.Electricity,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 20),
                "ЭЛ-2026-001",
                0m,
                5m,
                160m,
                "Плановая замена",
                july.Value!.Id,
                july.Value.Version),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        Assert.Equal("ЭЛ-2026-001", replacement.Value!.Device.SerialNumber);
        Assert.Equal(0m, replacement.Value.Device.InitialValue);
        Assert.Equal(5m, replacement.Value.Reading.CurrentValue);
        Assert.Equal(0m, replacement.Value.Reading.PreviousValue);
        Assert.Equal(10m, replacement.Value.Reading.PreviousDeviceConsumption);
        Assert.Equal(15m, replacement.Value.Reading.Consumption);
        Assert.True(replacement.Value.Reading.IsMeterReplacement);
        Assert.NotEqual(june.Value!.MeterDeviceId, replacement.Value.Reading.MeterDeviceId);
        var devices = database.Context.MeterDevices.OrderBy(item => item.InstalledOn).ToList();
        Assert.Collection(
            devices,
            oldDevice =>
            {
                Assert.Equal("Без номера", oldDevice.SerialNumber);
                Assert.Equal(new DateOnly(2026, 7, 19), oldDevice.RemovedOn);
                Assert.Equal(160m, oldDevice.FinalValue);
            },
            newDevice => Assert.Null(newDevice.RemovedOn));

        var august = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 15m, null),
            null,
            CancellationToken.None);
        Assert.True(august.Succeeded, august.ErrorMessage);
        Assert.Equal(replacement.Value.Device.Id, august.Value!.MeterDeviceId);
        Assert.Equal(5m, august.Value.PreviousValue);
        Assert.Equal(10m, august.Value.Consumption);
        var canceledReplacement = await service.CancelMeterReadingAsync(
            replacement.Value.Reading.Id,
            new CancelFinanceEntryRequest("Ошибочная замена"),
            null,
            CancellationToken.None);
        Assert.False(canceledReplacement.Succeeded);
        Assert.Equal("meter_device_replacement_reading_cancel_forbidden", canceledReplacement.ErrorCode);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.meter_device_replaced");
    }

    [Fact]
    public async Task ReplaceMeterDeviceAsync_RejectsInvalidNewDeviceValuesWithoutChangingHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);

        var result = await service.ReplaceMeterDeviceAsync(
            new ReplaceMeterDeviceRequest(
                fixtures.Garage.Id,
                MeterKinds.Water,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 21),
                "В-002",
                10m,
                5m,
                15m,
                "Замена",
                reading.Value!.Id,
                reading.Value.Version),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_device_current_below_initial", result.ErrorCode);
        Assert.Single(database.Context.MeterDevices);
        Assert.Null(database.Context.MeterDevices.Single().RemovedOn);
        Assert.Equal(15m, database.Context.MeterReadings.Single().CurrentValue);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.meter_device_replaced");
    }

    [Fact]
    public async Task ReplaceMeterDeviceAsync_RejectsDifferentAccountingMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.ReplaceMeterDeviceAsync(
            new ReplaceMeterDeviceRequest(
                fixtures.Garage.Id, MeterKinds.Electricity, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1),
                "ЭЛ-003", 0m, 1m, 100m, "Замена", null, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_device_replacement_date_month_mismatch", result.ErrorCode);
        Assert.Empty(database.Context.MeterDevices);
    }

    [Fact]
    public async Task ReplaceMeterDeviceAsync_RejectsPaidAccrualWithoutMutatingTrackedChain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff
        {
            Name = "Вода по счетчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(
            database.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero)));
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 20m, null),
            null,
            CancellationToken.None);
        var june = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 30m, null),
            null,
            CancellationToken.None);
        Assert.True((await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 1), 100m, "PKO-replace", null),
            null,
            CancellationToken.None)).Succeeded);
        var oldDevice = Assert.Single(database.Context.MeterDevices);

        var result = await service.ReplaceMeterDeviceAsync(
            new ReplaceMeterDeviceRequest(
                fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20),
                "В-003", 0m, 2m, 30m, "Проверка оплаты", june.Value!.Id, june.Value.Version),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_accrual_paid", result.ErrorCode);
        Assert.Single(database.Context.MeterDevices);
        Assert.Null(oldDevice.RemovedOn);
        var unchangedReading = database.Context.MeterReadings.Single(item => item.Id == june.Value.Id);
        Assert.Equal(oldDevice.Id, unchangedReading.MeterDeviceId);
        Assert.Equal(30m, unchangedReading.CurrentValue);
        Assert.Equal(10m, unchangedReading.Consumption);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.meter_device_replaced");
    }

    [Fact]
    public async Task CancelMeterReadingAsync_CancelsReadingAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, "Контроль"),
            null,
            CancellationToken.None);

        var result = await service.CancelMeterReadingAsync(created.Value!.Id, new CancelFinanceEntryRequest("Ошибочное показание"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCanceled);
        Assert.Contains("Отменено: Ошибочное показание", result.Value.Comment);
        Assert.Empty(await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, null, null), CancellationToken.None));
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None);
        Assert.Equal(0, summary.MeterReadingCount);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_canceled");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Отменено показание water", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("дата 20.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("расход 5,5", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Ошибочное показание", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreMeterReadingAsync_RestoresCanceledReadingAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.5m, "Контроль"),
            null,
            CancellationToken.None);
        await service.CancelMeterReadingAsync(created.Value!.Id, new CancelFinanceEntryRequest("Ошибочное показание"), null, CancellationToken.None);

        var result = await service.RestoreMeterReadingAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCanceled);
        Assert.Single(await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, null, null), CancellationToken.None));
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_restored");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Восстановлено показание water", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreMeterReadingAsync_RejectsDuplicateActiveReading()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        await service.CancelMeterReadingAsync(created.Value!.Id, new CancelFinanceEntryRequest("Ошибочное показание"), null, CancellationToken.None);
        Assert.True((await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 16m, null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.RestoreMeterReadingAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_duplicate", result.ErrorCode);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_restored");
    }

    [Fact]
    public async Task CreateMeterReadingAsync_WarnsWhenElectricityPreviousMonthIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 20), 110m, null),
            null,
            CancellationToken.None);

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 130m, null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.HasGapWarning);

        var readings = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, "electricity", null), CancellationToken.None);
        Assert.Contains(readings, reading => reading.AccountingMonth == new DateOnly(2026, 6, 1) && reading.HasGapWarning);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_RejectsDecreasedValue()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 5m, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_decreased", result.ErrorCode);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_AllowsSameValueWithZeroConsumption()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var first = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 15.125m, null),
            null,
            CancellationToken.None);
        var same = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15.125m, null),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(same.Succeeded, same.ErrorMessage);
        Assert.Equal(15.125m, same.Value!.PreviousValue);
        Assert.Equal(15.125m, same.Value.CurrentValue);
        Assert.Equal(0m, same.Value.Consumption);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_RejectsDuplicateGarageKindAndMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 110m, null), null, CancellationToken.None);

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 120m, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task GetMeterReadingsAsync_SearchesAndOrdersByMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 14m, null), null, CancellationToken.None);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 120m, "Ежемесячное электричество %_"), null, CancellationToken.None);

        var result = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, "electricity", "ежемесячное"), CancellationToken.None);
        var literalWildcard = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, "electricity", "%_"), CancellationToken.None);
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, "ежемесячное"), CancellationToken.None);

        var reading = Assert.Single(result);
        Assert.Equal(result, literalWildcard);
        Assert.Equal("electricity", reading.MeterKind);
        Assert.Equal(new DateOnly(2026, 6, 1), reading.AccountingMonth);
        Assert.Equal(20m, reading.Consumption);
        Assert.Equal(1, summary.MeterReadingCount);
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_ReturnsActiveGaragesWithoutReadingForMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
        var secondGarage = new Garage { Number = "13", PeopleCount = 2, FloorCount = 1, Owner = secondOwner };
        var archivedGarage = new Garage { Number = "14", PeopleCount = 1, FloorCount = 1, IsArchived = true };
        database.Context.AddRange(secondOwner, secondGarage, archivedGarage);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var canceledElectricity = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 120m, null),
            null,
            CancellationToken.None);
        await service.CancelMeterReadingAsync(canceledElectricity.Value!.Id, new CancelFinanceEntryRequest("Ошибочное показание"), null, CancellationToken.None);

        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 15), null, null),
            CancellationToken.None);
        var byOwner = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 15), "water", "петров"),
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, item => item.GarageNumber == "12" && item.MeterKind == "electricity" && item.AccountingMonth == new DateOnly(2026, 6, 1));
        Assert.Contains(result, item => item.GarageNumber == "13" && item.MeterKind == "water");
        Assert.Contains(result, item => item.GarageNumber == "13" && item.MeterKind == "electricity");
        Assert.DoesNotContain(result, item => item.GarageNumber == "14");
        Assert.Equal("13", Assert.Single(byOwner).GarageNumber);
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_FiltersByKindSearchAndLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 1), "water", "12", 1),
            CancellationToken.None);

        var missing = Assert.Single(result);
        Assert.Equal("12", missing.GarageNumber);
        Assert.Equal("water", missing.MeterKind);
        Assert.Equal("Иванов Иван", missing.OwnerName);
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_AppliesLimitAfterSkippingCompleteGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var secondGarage = new Garage { Number = "13", PeopleCount = 1, FloorCount = 1 };
        database.Context.Garages.Add(secondGarage);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", month, new DateOnly(2026, 6, 20), 10m, null),
            null,
            CancellationToken.None);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", month, new DateOnly(2026, 6, 20), 100m, null),
            null,
            CancellationToken.None);

        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(month, null, null, 1),
            CancellationToken.None);

        var missing = Assert.Single(result);
        Assert.Equal("13", missing.GarageNumber);
        Assert.Equal("water", missing.MeterKind);
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_UsesOneSelectForManyGaragesAndBothMeterKinds()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        for (var index = 1; index < 200; index++)
        {
            database.Context.Garages.Add(new Garage
            {
                Number = $"G-{index:D3}",
                PeopleCount = 1,
                FloorCount = 1,
                Owner = fixtures.Garage.Owner
            });
        }

        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        commandCounter.Reset();
        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 1), null, null, 100),
            CancellationToken.None);
        Assert.Equal(100, result.Count);
        Assert.Equal(1, commandCounter.Count);
        Assert.All(result, item => Assert.Contains(item.MeterKind, new[] { MeterKinds.Water, MeterKinds.Electricity }));
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 1), null, null),
            cancellation.Token));
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_ReturnsEmptyForUnknownMeterKind()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 1), "gas", null),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_BuildsRowsFromAccrualsPaymentsAndMeters()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var electricityType = new IncomeType { Name = "Электроэнергия", Code = "electricity" };
        database.Context.IncomeTypes.Add(electricityType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, electricityType.Id, new DateOnly(2026, 6, 1), 5674m, "regular", "Сверка начисления"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, electricityType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1000m, "PKO-electricity", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 500m, "PKO-membership-only", null),
            null,
            CancellationToken.None)).Succeeded);
        var createdReading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 118m, null),
            null,
            CancellationToken.None);
        Assert.True(createdReading.Succeeded);

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(fixtures.Garage.Id, result.Value!.GarageId);
        Assert.Equal(0m, result.Value.OpeningDebt);
        Assert.Equal(5674m, result.Value.AccrualTotal);
        Assert.Equal(1500m, result.Value.IncomeTotal);
        Assert.Equal(4174m, result.Value.DebtTotal);
        Assert.Equal(4174m, result.Value.ClosingDebt);
        Assert.Equal(2, result.Value.Rows.Count);

        var electricity = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == electricityType.Id);
        Assert.Equal("electricity", electricity.MeterKind);
        Assert.Equal(createdReading.Value!.Id, electricity.MeterReadingId);
        Assert.Equal(createdReading.Value.Version, electricity.MeterReadingVersion);
        Assert.Equal(new DateOnly(2026, 6, 21), electricity.MeterReadingDate);
        Assert.Equal(118m, electricity.MeterValue);
        Assert.Equal(18m, electricity.MeterConsumption);
        Assert.Equal(5674m, electricity.AccrualAmount);
        Assert.Equal(1000m, electricity.IncomeAmount);
        Assert.Equal(4674m, electricity.Debt);
        Assert.Equal("Сверка начисления", electricity.Reason);

        var membership = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(0m, membership.AccrualAmount);
        Assert.Equal(0m, membership.IncomeAmount);
        Assert.Equal(500m, membership.AdvanceAmount);
        Assert.Equal(0m, membership.Debt);
        Assert.Equal(500m, result.Value.AdvanceTotal);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_CapsAppliedPaymentAndCarriesExcessAsAdvance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var serviceType = new IncomeType { Name = "Охрана", Code = "security" };
        database.Context.IncomeTypes.Add(serviceType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, serviceType.Id, new DateOnly(2026, 1, 1), 100m, "regular", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, serviceType.Id, new DateOnly(2026, 2, 1), 100m, "regular", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, serviceType.Id, new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 1), 250m, null, null),
            null,
            CancellationToken.None)).Succeeded);

        var overpaidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)),
            CancellationToken.None);

        Assert.True(overpaidWorksheet.Succeeded, overpaidWorksheet.ErrorMessage);
        var january = Assert.Single(overpaidWorksheet.Value!.Rows, row =>
            row.IncomeTypeId == serviceType.Id && row.AccountingMonth == new DateOnly(2026, 1, 1));
        var february = Assert.Single(overpaidWorksheet.Value.Rows, row =>
            row.IncomeTypeId == serviceType.Id && row.AccountingMonth == new DateOnly(2026, 2, 1));
        Assert.Equal((100m, 0m, 0m), (january.IncomeAmount, january.AdvanceAmount, january.Debt));
        Assert.Equal((100m, 50m, 0m), (february.IncomeAmount, february.AdvanceAmount, february.Debt));
        Assert.Equal(50m, overpaidWorksheet.Value.AdvanceTotal);
        Assert.Equal(-50m, overpaidWorksheet.Value.ClosingBalance);
        Assert.All(overpaidWorksheet.Value.Rows, row => Assert.True(row.IncomeAmount <= row.PayableAmount));

        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, serviceType.Id, new DateOnly(2026, 3, 1), 40m, "regular", null),
            null,
            CancellationToken.None)).Succeeded);
        var appliedAdvanceWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        Assert.True(appliedAdvanceWorksheet.Succeeded, appliedAdvanceWorksheet.ErrorMessage);
        var march = Assert.Single(appliedAdvanceWorksheet.Value!.Rows, row =>
            row.IncomeTypeId == serviceType.Id && row.AccountingMonth == new DateOnly(2026, 3, 1));
        var februaryAfterAccrual = Assert.Single(appliedAdvanceWorksheet.Value.Rows, row =>
            row.IncomeTypeId == serviceType.Id && row.AccountingMonth == new DateOnly(2026, 2, 1));
        Assert.Equal((40m, 0m), (march.IncomeAmount, march.Debt));
        Assert.Equal(10m, februaryAfterAccrual.AdvanceAmount);
        Assert.Equal(10m, appliedAdvanceWorksheet.Value.AdvanceTotal);

        var marchOnlyWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        Assert.True(marchOnlyWorksheet.Succeeded, marchOnlyWorksheet.ErrorMessage);
        Assert.Equal(-50m, marchOnlyWorksheet.Value!.OpeningBalance);
        Assert.Equal(0m, marchOnlyWorksheet.Value!.OpeningDebt);
        Assert.Equal(-10m, marchOnlyWorksheet.Value.ClosingBalance);
        Assert.Equal(0m, marchOnlyWorksheet.Value.ClosingDebt);
        Assert.Equal(10m, marchOnlyWorksheet.Value.AdvanceTotal);
        var marchOnly = Assert.Single(marchOnlyWorksheet.Value.Rows, row => row.IncomeTypeId == serviceType.Id);
        Assert.Equal((40m, 40m, 0m), (marchOnly.PayableAmount, marchOnly.IncomeAmount, marchOnly.Debt));
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_IncludesMissingCurrentMeterRowsAndKeepsOtherAccruals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var currentMonth = MonthPeriod.CurrentLocalMonth();
        var waterType = new IncomeType { Name = "Водоснабжение", Code = MeterKinds.Water };
        var electricityType = new IncomeType { Name = "Электроэнергия", Code = MeterKinds.Electricity };
        var archivedMeterType = new IncomeType { Name = "Архивная вода", Code = MeterKinds.Water, IsArchived = true };
        database.Context.AddRange(
            waterType,
            electricityType,
            archivedMeterType,
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = currentMonth,
                Amount = 700m,
                Source = "regular"
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(currentMonth, currentMonth),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.Rows.Count);
        var membership = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(700m, membership.AccrualAmount);
        Assert.Null(membership.MeterKind);
        foreach (var meterType in new[] { waterType, electricityType })
        {
            var missingMeter = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == meterType.Id);
            Assert.Equal(meterType.Code, missingMeter.MeterKind);
            Assert.Null(missingMeter.MeterValue);
            Assert.Equal(0m, missingMeter.AccrualAmount);
            Assert.Equal(0m, missingMeter.IncomeAmount);
            Assert.Equal(0m, missingMeter.Debt);
        }
        Assert.DoesNotContain(result.Value.Rows, row => row.IncomeTypeId == archivedMeterType.Id);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ShowsAnnualObligationUntilFullPaymentAndRestoresItAfterCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var annualAccrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 1, 1), 700m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(annualAccrual.Succeeded, annualAccrual.ErrorMessage);
        var annualAccrualId = annualAccrual.Value!.Id;
        var partialPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1), 300m, null, null),
            null,
            CancellationToken.None);
        Assert.True(partialPayment.Succeeded, partialPayment.ErrorMessage);

        var partialWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 1)),
            CancellationToken.None);

        Assert.True(partialWorksheet.Succeeded, partialWorksheet.ErrorMessage);
        var partialRows = partialWorksheet.Value!.Rows
            .Where(row => row.AnnualAccrualId == annualAccrualId)
            .OrderBy(row => row.AccountingMonth)
            .ToList();
        Assert.Equal(5, partialRows.Count);
        Assert.Equal(700m, partialRows[0].AccrualAmount);
        Assert.Equal(700m, partialRows[0].PayableAmount);
        Assert.Equal(700m, partialRows[1].Debt);
        Assert.Equal(700m, partialRows[2].PayableAmount);
        Assert.Equal(300m, partialRows[2].IncomeAmount);
        Assert.Equal(400m, partialRows[2].Debt);
        Assert.Equal(400m, partialRows[4].PayableAmount);
        Assert.Equal(400m, partialRows[4].Debt);

        var fullPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 1), 500m, null, null),
            null,
            CancellationToken.None);
        Assert.True(fullPayment.Succeeded, fullPayment.ErrorMessage);
        var paidWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(paidWorksheet.Succeeded, paidWorksheet.ErrorMessage);
        var paidRows = paidWorksheet.Value!.Rows
            .Where(row => row.AnnualAccrualId == annualAccrualId)
            .OrderBy(row => row.AccountingMonth)
            .ToList();
        Assert.Equal(4, paidRows.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), paidRows[^1].AccountingMonth);
        Assert.Equal(400m, paidRows[^1].PayableAmount);
        Assert.Equal(400m, paidRows[^1].IncomeAmount);
        Assert.Equal(100m, paidRows[^1].AdvanceAmount);
        Assert.Equal(0m, paidRows[^1].Debt);
        Assert.Equal(700m, paidWorksheet.Value.AccrualTotal);
        Assert.Equal(800m, paidWorksheet.Value.IncomeTotal);
        Assert.Equal(100m, paidWorksheet.Value.AdvanceTotal);
        Assert.Equal(0m, paidWorksheet.Value.ClosingDebt);

        var canceledPayment = await service.CancelOperationAsync(
            fullPayment.Value!.Id,
            new CancelFinanceEntryRequest("Проверяем возврат годового остатка"),
            null,
            CancellationToken.None);
        Assert.True(canceledPayment.Succeeded, canceledPayment.ErrorMessage);
        var canceledWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 3, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(canceledWorksheet.Succeeded, canceledWorksheet.ErrorMessage);
        Assert.Equal(0m, canceledWorksheet.Value!.UnrepresentedOpeningDebt);
        var juneAfterCancellation = Assert.Single(canceledWorksheet.Value.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth == new DateOnly(2026, 6, 1));
        Assert.Equal(400m, juneAfterCancellation.Debt);

        Assert.True((await service.RestoreOperationAsync(fullPayment.Value.Id, null, CancellationToken.None)).Succeeded);
        var restoredWorksheet = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(restoredWorksheet.Succeeded, restoredWorksheet.ErrorMessage);
        Assert.DoesNotContain(restoredWorksheet.Value!.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth > new DateOnly(2026, 4, 1));
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_UsesTwoBoundedSelectsForCampaignsReasonsAndCombinedWorksheetData()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var electricityType = new IncomeType { Name = "Электроэнергия", Code = "electricity" };
        database.Context.IncomeTypes.Add(electricityType);
        database.Context.Accruals.AddRange(
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = new DateOnly(2026, 5, 1),
                Amount = 300m,
                Source = "manual"
            },
            new Accrual
            {
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = electricityType.Id,
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 500m,
                Source = "regular"
            });
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 5, 20),
                AccountingMonth = new DateOnly(2026, 5, 1),
                Amount = 100m,
                DocumentNumber = "PKO-old-combined",
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = fixtures.IncomeType.Id
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 20),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 125m,
                DocumentNumber = "PKO-current-combined",
                GarageId = fixtures.Garage.Id,
                IncomeTypeId = electricityType.Id
            });
        var meterReading = new MeterReading
        {
            GarageId = fixtures.Garage.Id,
            MeterKind = "electricity",
            AccountingMonth = new DateOnly(2026, 6, 1),
            ReadingDate = new DateOnly(2026, 6, 21),
            PreviousValue = 100m,
            CurrentValue = 118m,
            Consumption = 18m
        };
        database.Context.MeterReadings.Add(meterReading);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, commandCounter.Count);
        Assert.Equal(fixtures.Garage.Number, result.Value!.GarageNumber);
        Assert.Equal(fixtures.Garage.Owner?.FullName, result.Value.OwnerName);
        Assert.Equal(200m, result.Value!.OpeningDebt);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(electricityType.Id, row.IncomeTypeId);
        Assert.Equal(500m, row.AccrualAmount);
        Assert.Equal(0m, row.IncomeAmount);
        Assert.Equal(125m, row.AdvanceAmount);
        Assert.Equal(225m, result.Value.AdvanceTotal);
        Assert.Equal(meterReading.Id, row.MeterReadingId);
        Assert.Equal(meterReading.Version, row.MeterReadingVersion);
        Assert.Equal(meterReading.ReadingDate, row.MeterReadingDate);
        Assert.Equal(118m, row.MeterValue);
        Assert.Equal(18m, row.MeterConsumption);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsEmptyPeriodInTwoBoundedSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 0m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, commandCounter.Count);
        Assert.Equal(0m, result.Value!.OpeningDebt);
        Assert.Equal(0m, result.Value.AccrualTotal);
        Assert.Equal(0m, result.Value.IncomeTotal);
        Assert.Equal(0m, result.Value.ClosingDebt);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsFailureForMissingGarageInTwoBoundedSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            Guid.NewGuid(),
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_not_found", result.ErrorCode);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsFailureForArchivedGarageInTwoBoundedSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.Garage.IsArchived = true;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_not_found", result.ErrorCode);
        Assert.Equal(2, commandCounter.Count);
    }

    [Theory]
    [InlineData(2026, 7, 2026, 6, "income_worksheet_period_invalid")]
    [InlineData(1976, 6, 2026, 7, "income_worksheet_period_too_large")]
    public async Task GetGarageIncomeWorksheetAsync_RejectsInvalidPeriodBeforeDatabaseAccess(
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        string expectedErrorCode)
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            Guid.NewGuid(),
            new GarageIncomeWorksheetRequest(new DateOnly(fromYear, fromMonth, 1), new DateOnly(toYear, toMonth, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(0, commandCounter.Count);
    }

    [Fact]
    public async Task GarageIncomeWorksheetQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfGarageIncomeWorksheetQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetAsync(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            cancellationSource.Token));
    }

    [Fact]
    public async Task GetGarageBalanceHistoryAsync_ReturnsZeroMonthWhenThereAreNoFinancialRows()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 0m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageBalanceHistoryAsync(
            fixtures.Garage.Id,
            new GarageBalanceHistoryRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(0m, result.Value!.AccrualTotal);
        Assert.Equal(0m, result.Value.IncomeTotal);
        Assert.Equal(0m, result.Value.Debt);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(new DateOnly(2026, 8, 1), row.AccountingMonth);
        Assert.Equal(0m, row.OpeningDebt);
        Assert.Equal(0m, row.AccrualAmount);
        Assert.Equal(0m, row.IncomeAmount);
        Assert.Equal(0m, row.ClosingDebt);
    }

    [Theory]
    [InlineData(2026, 7, 2026, 6, "balance_history_period_invalid")]
    [InlineData(1976, 6, 2026, 7, "balance_history_period_too_large")]
    public async Task GetGarageBalanceHistoryAsync_RejectsInvalidPeriodBeforeDatabaseAccess(
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        string expectedErrorCode)
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageBalanceHistoryAsync(
            Guid.NewGuid(),
            new GarageBalanceHistoryRequest(new DateOnly(fromYear, fromMonth, 1), new DateOnly(toYear, toMonth, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(0, commandCounter.Count);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_CarriesOpeningDebtIntoPeriodTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        fixtures.Garage.StartingBalance = 200m;
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 5, 1), 1000m, "manual", "Старое начисление"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 1), 300m, "PKO-old", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "regular", "Текущее начисление"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 100m, "PKO-current", null),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(900m, result.Value!.OpeningBalance);
        Assert.Equal(900m, result.Value!.OpeningDebt);
        Assert.Equal(500m, result.Value.AccrualTotal);
        Assert.Equal(100m, result.Value.IncomeTotal);
        Assert.Equal(1300m, result.Value.DebtTotal);
        Assert.Equal(1300m, result.Value.ClosingBalance);
        Assert.Equal(1300m, result.Value.ClosingDebt);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_BuildsRowsFromSupplierAccrualsExpensesStaffAndCollections()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);
        var waterIncomeType = new IncomeType { Name = "Водоснабжение", Code = "water" };
        var unmatchedIncomeType = new IncomeType { Name = "Пожертвование" };
        var salaryExpenseType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        var expenseOnlyType = new ExpenseType { Name = "Ремонт", Code = "repair" };
        var repairService = new ChargeServiceSetting { Name = "Ремонт" };
        var repairSupplier = new Supplier { Name = "Ремонтная организация", GroupId = fixtures.Supplier.GroupId, ChargeServiceSetting = repairService, ExpenseType = expenseOnlyType, ExpenseFund = fixtures.ExpenseFund };
        var accrualOnlyType = new ExpenseType { Name = "Охрана", Code = "security" };
        var securityService = new ChargeServiceSetting { Name = "Охрана" };
        var securitySupplier = new Supplier { Name = "Охранная организация", GroupId = fixtures.Supplier.GroupId, ChargeServiceSetting = securityService, ExpenseType = accrualOnlyType, ExpenseFund = fixtures.ExpenseFund };
        var staffDepartment = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = staffDepartment,
            Rate = 40000m,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(
            waterIncomeType,
            unmatchedIncomeType,
            salaryExpenseType,
            expenseOnlyType,
            repairService,
            repairSupplier,
            accrualOnlyType,
            securityService,
            securitySupplier,
            staffDepartment,
            staffMember,
            OpeningCashBalance(SeededBankAmount + 15_000m));
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, month, 32000m, "manual", "INV-water", "Счет за воду"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(securitySupplier.Id, accrualOnlyType.Id, month, 75m, "manual", "INV-security", "Счет за охрану"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), month, 10000m, "RKO-water", "Частичная оплата воды"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, "bonus", 5000m, "PR-bonus", "Премия"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffSalaryAdjustmentAsync(
            new CreateStaffSalaryAdjustmentRequest(staffMember.Id, month, "penalty", 1000m, "PR-penalty", "Штраф"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, new DateOnly(2026, 6, 21), month, 15000m, "RKO-staff", "Частичная зарплата"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(repairSupplier.Id, expenseOnlyType.Id, new DateOnly(2026, 6, 22), month, 100m, "RKO-repair", "Оплата ремонта"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, waterIncomeType.Id, new DateOnly(2026, 6, 19), month, 29000m, "PKO-water", "Поступление за воду"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, unmatchedIncomeType.Id, new DateOnly(2026, 6, 23), month, 50m, "PKO-donation", "Пожертвование"),
            null,
            CancellationToken.None)).Succeeded);
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(month), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(month, result.Value!.AccountingMonth);
        Assert.Equal(76075m, result.Value.AccrualTotal);
        Assert.Equal(25100m, result.Value.ExpenseTotal);
        Assert.Equal(51075m, result.Value.BalanceTotal);
        Assert.Equal(0m, result.Value.OpeningDebtTotal);
        Assert.Equal(0m, result.Value.OpeningAdvanceTotal);
        Assert.Equal(51075m, result.Value.ClosingDebtTotal);
        Assert.Equal(100m, result.Value.ClosingAdvanceTotal);
        Assert.Equal(SeededBankAmount, result.Value.CollectedTotal);
        Assert.Equal(SeededBankAmount - 10100m, result.Value.DifferenceTotal);
        Assert.Equal(29050m, result.Value.CashAmount);
        Assert.Equal(SeededBankAmount - 10100m, result.Value.BankAmount);

        var supplierRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == fixtures.ExpenseType.Id);
        Assert.Equal(fixtures.Supplier.Id, supplierRow.SupplierId);
        Assert.Equal("Vodokanal", supplierRow.CounterpartyName);
        Assert.Equal(fixtures.ExpenseType.Id, supplierRow.ExpenseTypeId);
        Assert.Equal(32000m, supplierRow.AccrualAmount);
        Assert.Equal(10000m, supplierRow.ExpenseAmount);
        Assert.Equal(22000m, supplierRow.Balance);
        Assert.Equal(0m, supplierRow.OpeningDebt);
        Assert.Equal(0m, supplierRow.OpeningAdvance);
        Assert.Equal(22000m, supplierRow.ClosingDebt);
        Assert.Equal(0m, supplierRow.ClosingAdvance);
        Assert.Equal(SeededBankAmount - 100m, supplierRow.CollectedAmount);
        Assert.Equal(SeededBankAmount - 10100m, supplierRow.Difference);
        Assert.Equal(fixtures.ExpenseFund.Id, supplierRow.ExpenseFundId);
        Assert.Equal(fixtures.ExpenseFund.Name, supplierRow.ExpenseFundName);

        var expenseOnlyRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == expenseOnlyType.Id);
        Assert.Equal(0m, expenseOnlyRow.AccrualAmount);
        Assert.Equal(100m, expenseOnlyRow.ExpenseAmount);
        Assert.Equal(0m, expenseOnlyRow.Balance);
        Assert.Equal(0m, expenseOnlyRow.ClosingDebt);
        Assert.Equal(100m, expenseOnlyRow.ClosingAdvance);
        Assert.Equal(SeededBankAmount - 10000m, expenseOnlyRow.CollectedAmount);
        Assert.Equal(SeededBankAmount - 10100m, expenseOnlyRow.Difference);

        var accrualOnlyRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == accrualOnlyType.Id);
        Assert.Equal(75m, accrualOnlyRow.AccrualAmount);
        Assert.Equal(0m, accrualOnlyRow.ExpenseAmount);
        Assert.Equal(75m, accrualOnlyRow.Balance);

        var staffRow = Assert.Single(result.Value.Rows, row => row.RowKind == "staff");
        Assert.Equal(staffMember.Id, staffRow.StaffMemberId);
        Assert.Equal("Петрова Ольга", staffRow.CounterpartyName);
        Assert.Equal(salaryExpenseType.Id, staffRow.ExpenseTypeId);
        Assert.Equal("Зарплата", staffRow.ExpenseTypeName);
        Assert.Equal(44000m, staffRow.AccrualAmount);
        Assert.Equal(40000m, staffRow.BaseAccrualAmount);
        Assert.Equal(5000m, staffRow.BonusAmount);
        Assert.Equal(1000m, staffRow.PenaltyAmount);
        Assert.Equal(15000m, staffRow.ExpenseAmount);
        Assert.Equal(29000m, staffRow.Balance);
        Assert.Null(staffRow.CollectedAmount);
        Assert.Null(staffRow.Difference);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_AggregatesSelectedMonthRangeInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var june = new DateOnly(2026, 6, 1);
        var july = new DateOnly(2026, 7, 1);

        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, june, 100m, "manual", "RANGE-06", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, july, 250m, "manual", "RANGE-07", null),
            null,
            CancellationToken.None)).Succeeded);
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(null, june, july),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(june, result.Value!.MonthFrom);
        Assert.Equal(july, result.Value.MonthTo);
        Assert.Equal(350m, Assert.Single(result.Value.Rows, row => row.SupplierId == fixtures.Supplier.Id).AccrualAmount);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_RejectsReversedPeriodWithoutQuery()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(null, new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("expense_worksheet_period_invalid", result.ErrorCode);
        Assert.Equal(0, commandCounter.Count);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_CalculatesOpeningBalancesForEachSupplierAndStaffExpenseTypePair()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var supplierGroup = new SupplierGroup { Name = "Коммунальные услуги" };
        var firstSupplier = new Supplier { Name = "Первый поставщик", Group = supplierGroup };
        var secondSupplier = new Supplier { Name = "Второй поставщик", Group = supplierGroup };
        var waterType = new ExpenseType { Name = "Водоснабжение", Code = "water" };
        var repairType = new ExpenseType { Name = "Ремонт", Code = "repair" };
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary", IsSystem = true };
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = department,
            Rate = 100m,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.AddRange(
            firstSupplier,
            secondSupplier,
            supplierGroup,
            waterType,
            repairType,
            salaryType,
            department,
            staffMember,
            new SupplierAccrual
            {
                Supplier = firstSupplier,
                ExpenseType = waterType,
                AccountingMonth = new DateOnly(2026, 1, 1),
                Amount = 100m,
                Source = AccrualSources.Manual
            },
            new SupplierAccrual
            {
                Supplier = firstSupplier,
                ExpenseType = waterType,
                AccountingMonth = new DateOnly(2026, 2, 1),
                Amount = 100m,
                Source = AccrualSources.Manual
            },
            new SupplierAccrual
            {
                Supplier = firstSupplier,
                ExpenseType = repairType,
                AccountingMonth = new DateOnly(2026, 1, 1),
                Amount = 50m,
                Source = AccrualSources.Manual
            },
            new SupplierAccrual
            {
                Supplier = secondSupplier,
                ExpenseType = waterType,
                AccountingMonth = new DateOnly(2026, 1, 1),
                Amount = 300m,
                Source = AccrualSources.Manual
            },
            new SupplierAccrual
            {
                Supplier = firstSupplier,
                ExpenseType = waterType,
                AccountingMonth = new DateOnly(2026, 3, 1),
                Amount = 30m,
                Source = AccrualSources.Manual
            },
            new SupplierAccrual
            {
                Supplier = firstSupplier,
                ExpenseType = waterType,
                AccountingMonth = new DateOnly(2026, 4, 1),
                Amount = 999m,
                Source = AccrualSources.Manual
            },
            CreateHistoricalExpense(firstSupplier, null, waterType, new DateOnly(2026, 1, 1), 70m),
            CreateHistoricalExpense(firstSupplier, null, repairType, new DateOnly(2026, 2, 1), 60m),
            CreateHistoricalExpense(secondSupplier, null, waterType, new DateOnly(2026, 2, 1), 100m),
            CreateHistoricalExpense(firstSupplier, null, waterType, new DateOnly(2026, 3, 1), 10m),
            CreateHistoricalExpense(firstSupplier, null, waterType, new DateOnly(2026, 4, 1), 555m),
            CreateHistoricalExpense(null, staffMember, salaryType, new DateOnly(2026, 1, 1), 60m),
            CreateHistoricalExpense(null, staffMember, salaryType, new DateOnly(2026, 2, 1), 100m),
            new StaffSalaryAdjustment
            {
                StaffMember = staffMember,
                AccountingMonth = new DateOnly(2026, 1, 1),
                AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                Amount = 20m,
                Reason = "Премия"
            },
            new StaffSalaryAdjustment
            {
                StaffMember = staffMember,
                AccountingMonth = new DateOnly(2026, 2, 1),
                AdjustmentType = StaffSalaryAdjustmentTypes.Penalty,
                Amount = 5m,
                Reason = "Штраф"
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 3, 10),
                AccountingMonth = new DateOnly(2026, 3, 1),
                Amount = 777m,
                StaffMember = staffMember
            },
            CreateHistoricalExpense(firstSupplier, null, waterType, new DateOnly(2026, 2, 1), 999m, isCanceled: true),
            CreateHistoricalExpense(null, staffMember, salaryType, new DateOnly(2026, 2, 1), 999m, isCanceled: true));
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(375m, result.Value!.OpeningBalanceTotal);
        Assert.Equal(385m, result.Value.OpeningDebtTotal);
        Assert.Equal(10m, result.Value.OpeningAdvanceTotal);
        Assert.Equal(505m, result.Value.ClosingDebtTotal);
        Assert.Equal(10m, result.Value.ClosingAdvanceTotal);
        var firstSupplierWaterRow = Assert.Single(result.Value.Rows, row =>
            row.SupplierId == firstSupplier.Id && row.ExpenseTypeId == waterType.Id);
        Assert.Equal(130m, firstSupplierWaterRow.OpeningBalance);
        Assert.Equal(130m, firstSupplierWaterRow.OpeningDebt);
        Assert.Equal(0m, firstSupplierWaterRow.OpeningAdvance);
        Assert.Equal(150m, firstSupplierWaterRow.ClosingDebt);
        Assert.Equal(0m, firstSupplierWaterRow.ClosingAdvance);
        Assert.Equal(30m, firstSupplierWaterRow.AccrualAmount);
        Assert.Equal(10m, firstSupplierWaterRow.ExpenseAmount);
        var firstSupplierRepairRow = Assert.Single(result.Value.Rows, row =>
            row.SupplierId == firstSupplier.Id && row.ExpenseTypeId == repairType.Id);
        Assert.Equal(-10m, firstSupplierRepairRow.OpeningBalance);
        Assert.Equal(0m, firstSupplierRepairRow.OpeningDebt);
        Assert.Equal(10m, firstSupplierRepairRow.OpeningAdvance);
        Assert.Equal(0m, firstSupplierRepairRow.ClosingDebt);
        Assert.Equal(10m, firstSupplierRepairRow.ClosingAdvance);
        Assert.Equal(200m, Assert.Single(result.Value.Rows, row =>
            row.SupplierId == secondSupplier.Id && row.ExpenseTypeId == waterType.Id).OpeningBalance);
        var staffRow = Assert.Single(result.Value.Rows, row => row.StaffMemberId == staffMember.Id);
        Assert.Equal(salaryType.Id, staffRow.ExpenseTypeId);
        Assert.Equal(55m, staffRow.OpeningBalance);
        Assert.Equal(55m, staffRow.OpeningDebt);
        Assert.Equal(0m, staffRow.OpeningAdvance);
        Assert.Equal(155m, staffRow.ClosingDebt);
        Assert.Equal(0m, staffRow.ClosingAdvance);
    }

    private static FinancialOperation CreateHistoricalExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = accountingMonth.AddDays(15),
            AccountingMonth = accountingMonth,
            Amount = amount,
            Supplier = supplier,
            StaffMember = staffMember,
            ExpenseType = expenseType,
            IsCanceled = isCanceled
        };

    [Fact]
    public async Task GetExpenseWorksheetAsync_CarriesDebtAndAdvanceAcrossMonthsWithoutCreatingTransferRows()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var supplierGroup = new SupplierGroup { Name = "Последовательность выплат" };
        var supplier = new Supplier { Name = "Поставщик последовательности", Group = supplierGroup };
        var expenseType = new ExpenseType { Name = "Последовательная услуга", Code = "sequence_service" };
        database.Context.AddRange(
            supplierGroup,
            supplier,
            expenseType,
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2026, 1, 1), 100m),
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2026, 2, 1), 200m),
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2026, 3, 1), 100m),
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2026, 4, 1), 80m),
            CreateHistoricalExpense(supplier, null, expenseType, new DateOnly(2026, 1, 1), 100m),
            CreateHistoricalExpense(supplier, null, expenseType, new DateOnly(2026, 2, 1), 50m),
            CreateHistoricalExpense(supplier, null, expenseType, new DateOnly(2026, 3, 1), 300m));
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var january = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 1, 1)), CancellationToken.None);
        var february = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 2, 1)), CancellationToken.None);
        var march = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)), CancellationToken.None);
        var april = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);
        var repeatedApril = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);

        Assert.Equal(5, commandCounter.Count);
        AssertExpenseCarry(Assert.Single(january.Value!.Rows), 0m, 0m, 0m, 0m);
        AssertExpenseCarry(Assert.Single(february.Value!.Rows), 0m, 0m, 150m, 0m);
        AssertExpenseCarry(Assert.Single(march.Value!.Rows), 150m, 0m, 0m, 50m);
        AssertExpenseCarry(Assert.Single(april.Value!.Rows), 0m, 50m, 30m, 0m);
        AssertExpenseCarry(Assert.Single(repeatedApril.Value!.Rows), 0m, 50m, 30m, 0m);
        Assert.Equal(4, await database.Context.SupplierAccruals.CountAsync());
        Assert.Equal(3, await database.Context.FinancialOperations.CountAsync());
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_DoesNotInferFundsFromMatchingIncomeNamesWithoutConfiguredFund()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        var garage = new Garage { Number = "COLLECTION-CARRY", PeopleCount = 1, FloorCount = 1 };
        var supplierGroup = new SupplierGroup { Name = "Перенос собранных средств" };
        var supplier = new Supplier { Name = "Энергосбыт", Group = supplierGroup };
        var incomeType = new IncomeType { Name = "Электроэнергия", Code = "electricity_carry" };
        var unrelatedIncomeType = new IncomeType { Name = "Пожертвование", Code = "donation_carry" };
        var expenseType = new ExpenseType { Name = "Электроэнергия", Code = "electricity_carry" };
        var june = new DateOnly(2026, 6, 1);
        var july = new DateOnly(2026, 7, 1);
        var august = new DateOnly(2026, 8, 1);
        database.Context.AddRange(
            garage,
            supplierGroup,
            supplier,
            incomeType,
            unrelatedIncomeType,
            expenseType,
            CreateSupplierAccrual(supplier, expenseType, june, 12000m),
            CreateSupplierAccrual(supplier, expenseType, july, 2000m),
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = june.AddDays(10),
                AccountingMonth = june,
                Amount = 9243.81m,
                Garage = garage,
                IncomeType = incomeType
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = june.AddDays(11),
                AccountingMonth = june,
                Amount = 500m,
                Garage = garage,
                IncomeType = incomeType,
                IsCanceled = true
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = june.AddDays(12),
                AccountingMonth = june,
                Amount = 700m,
                Garage = garage,
                IncomeType = unrelatedIncomeType
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = june.AddDays(20),
                AccountingMonth = june,
                Amount = 900m,
                Supplier = supplier,
                ExpenseType = expenseType,
                IsCanceled = true
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = july.AddDays(10),
                AccountingMonth = july,
                Amount = 1000m,
                Garage = garage,
                IncomeType = incomeType
            },
            CreateHistoricalExpense(supplier, null, expenseType, july, 4000m));
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var juneWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(june), CancellationToken.None);
        var julyWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(july), CancellationToken.None);
        var augustWorksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(august), CancellationToken.None);

        Assert.Equal(3, commandCounter.Count);
        var juneRow = Assert.Single(juneWorksheet.Value!.Rows);
        Assert.Null(juneRow.CollectedAmount);
        Assert.Null(juneRow.Difference);
        var julyRow = Assert.Single(julyWorksheet.Value!.Rows);
        Assert.Null(julyRow.CollectedAmount);
        Assert.Null(julyRow.Difference);
        Assert.Equal(0m, julyWorksheet.Value.CollectedTotal);
        Assert.Equal(0m, julyWorksheet.Value.DifferenceTotal);
        var augustRow = Assert.Single(augustWorksheet.Value!.Rows);
        Assert.Null(augustRow.CollectedAmount);
        Assert.Null(augustRow.Difference);
        Assert.Equal(6, await database.Context.FinancialOperations.CountAsync());
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_RecalculatesEmptyMonthsAcrossYearAfterPreviousPaymentCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var supplierGroup = new SupplierGroup { Name = "Перерасчет на границе года" };
        var supplier = new Supplier { Name = "Поставщик перерасчета", Group = supplierGroup };
        var expenseType = new ExpenseType { Name = "Услуга перерасчета", Code = "year_boundary_recalculation" };
        var decemberPayment = CreateHistoricalExpense(supplier, null, expenseType, new DateOnly(2026, 12, 1), 40m);
        database.Context.AddRange(
            supplierGroup,
            supplier,
            expenseType,
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2026, 12, 1), 100m),
            CreateSupplierAccrual(supplier, expenseType, new DateOnly(2027, 2, 1), 50m),
            decemberPayment,
            CreateHistoricalExpense(supplier, null, expenseType, new DateOnly(2027, 2, 1), 30m));
        await database.Context.SaveChangesAsync();

        var december = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 12, 1)), CancellationToken.None);
        var emptyJanuary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 1, 1)), CancellationToken.None);
        var february = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 2, 1)), CancellationToken.None);
        var emptyMarch = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 3, 1)), CancellationToken.None);

        AssertExpenseCarry(Assert.Single(december.Value!.Rows), 0m, 0m, 60m, 0m);
        AssertEmptyExpenseMonth(Assert.Single(emptyJanuary.Value!.Rows), 60m);
        AssertExpenseCarry(Assert.Single(february.Value!.Rows), 60m, 0m, 80m, 0m);
        AssertEmptyExpenseMonth(Assert.Single(emptyMarch.Value!.Rows), 80m);

        var canceled = await service.CancelOperationAsync(
            decemberPayment.Id,
            new CancelFinanceEntryRequest("Отмена прошлогодней выплаты"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(canceled.Succeeded);
        var recalculatedJanuary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 1, 1)), CancellationToken.None);
        var recalculatedFebruary = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 2, 1)), CancellationToken.None);
        var recalculatedMarch = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2027, 3, 1)), CancellationToken.None);

        AssertEmptyExpenseMonth(Assert.Single(recalculatedJanuary.Value!.Rows), 100m);
        AssertExpenseCarry(Assert.Single(recalculatedFebruary.Value!.Rows), 100m, 0m, 120m, 0m);
        AssertEmptyExpenseMonth(Assert.Single(recalculatedMarch.Value!.Rows), 120m);
        Assert.Single(database.Context.AuditEvents, audit => audit.Action == "finance.operation_canceled");
        Assert.Equal(2, await database.Context.SupplierAccruals.CountAsync());
        Assert.Equal(2, await database.Context.FinancialOperations.CountAsync());
    }

    private static SupplierAccrual CreateSupplierAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = AccrualSources.Manual
        };

    private static void AssertExpenseCarry(
        ExpenseWorksheetRowDto row,
        decimal openingDebt,
        decimal openingAdvance,
        decimal closingDebt,
        decimal closingAdvance)
    {
        Assert.Equal(openingDebt, row.OpeningDebt);
        Assert.Equal(openingAdvance, row.OpeningAdvance);
        Assert.Equal(closingDebt, row.ClosingDebt);
        Assert.Equal(closingAdvance, row.ClosingAdvance);
    }

    private static void AssertEmptyExpenseMonth(ExpenseWorksheetRowDto row, decimal carriedDebt)
    {
        Assert.Equal(0m, row.AccrualAmount);
        Assert.Equal(0m, row.ExpenseAmount);
        AssertExpenseCarry(row, carriedDebt, 0m, carriedDebt, 0m);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_KeepsCashAndBankEqualCollectedFundsAfterMixedExpenses()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        var month = new DateOnly(2026, 6, 1);
        var waterIncomeType = new IncomeType { Name = "Вода", Code = "water" };
        var cashExpenseType = fixtures.ExpenseType;
        database.Context.AddRange(
            waterIncomeType,
            new CashBankTransfer
            {
                TransferDate = new DateOnly(2026, 6, 15),
                Amount = 400m,
                Comment = "Сдача кассы в банк",
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, waterIncomeType.Id, new DateOnly(2026, 6, 10), month, 1000m, "PKO-reconcile", null),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), month, 150m, "BANK-reconcile", "Оплата с банка"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, cashExpenseType.Id, new DateOnly(2026, 6, 21), month, 200m, "CASH-reconcile", "Выплата из кассы", ExpensePaymentTypes.WithoutReceipt),
            null,
            CancellationToken.None)).Succeeded);

        var result = await service.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(month), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(250m, result.Value!.BankAmount);
        Assert.Equal(400m, result.Value.CashAmount);
        Assert.Equal(350m, result.Value.ExpenseTotal);
        Assert.Equal(650m, result.Value.CashAmount + result.Value.BankAmount);
        Assert.Equal(1000m - result.Value.ExpenseTotal, result.Value.CashAmount + result.Value.BankAmount);
    }

    [Fact]
    public async Task CashAndBankInvariant_SurvivesPaymentsCancellationRestorationAndFundRedistribution()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.CashBankTransfers.RemoveRange(database.Context.CashBankTransfers);
        database.Context.Funds.RemoveRange(database.Context.Funds);
        var month = new DateOnly(2026, 6, 1);
        var incomeType = new IncomeType { Name = "Инвариант остатков", Code = "balance_invariant" };
        var cashExpenseType = fixtures.ExpenseType;
        var bankFund = new Fund { Name = "Банк инварианта", NormalizedName = "БАНК ИНВАРИАНТА", AllowOperations = true };
        var reserveFund = new Fund { Name = "Резерв инварианта", NormalizedName = "РЕЗЕРВ ИНВАРИАНТА", AllowOperations = true };
        fixtures.Supplier.ExpenseFundId = reserveFund.Id;
        fixtures.Supplier.ExpenseFund = reserveFund;
        database.Context.AddRange(incomeType, bankFund, reserveFund);
        await database.Context.SaveChangesAsync();
        var financeService = FinanceServiceTestFactory.Create(database.Context);
        var fundService = new FundService(
            new EfFundRepository(database.Context),
            new AuditEventWriter(database.Context));

        async Task AssertInvariantAsync(decimal expectedCash, decimal expectedBank)
        {
            var worksheet = await financeService.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(month), CancellationToken.None);
            var activeIncome = await database.Context.FinancialOperations
                .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
                .SumAsync(operation => operation.Amount);
            var activeExpense = await database.Context.FinancialOperations
                .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                .SumAsync(operation => operation.Amount);

            Assert.True(worksheet.Succeeded);
            Assert.Equal(expectedCash, worksheet.Value!.CashAmount);
            Assert.Equal(expectedBank, worksheet.Value.BankAmount);
            Assert.Equal(activeIncome - activeExpense, worksheet.Value.CashAmount + worksheet.Value.BankAmount);
        }

        var income = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.Garage.Id, incomeType.Id, new DateOnly(2026, 6, 10), month, 1000m, "INV-INCOME", null),
            null,
            CancellationToken.None);
        Assert.True(income.Succeeded);
        await AssertInvariantAsync(1000m, 0m);

        var bankTransfer = await financeService.CreateCashBankTransferAsync(
            new CreateCashBankTransferRequest(new DateOnly(2026, 6, 15), 400m, "Сдача кассы в банк"),
            null,
            CancellationToken.None);
        Assert.True(bankTransfer.Succeeded);
        Assert.Empty(database.Context.FundOperations);
        Assert.Equal(0m, (await database.Context.Funds.SingleAsync(fund => fund.Id == bankFund.Id)).Balance);
        await AssertInvariantAsync(600m, 400m);

        var allocation = await fundService.CreateOperationAsync(
            bankFund.Id,
            new CreateFundOperationRequest("deposit", 150m, "Первичное распределение"),
            null,
            CancellationToken.None);
        var withdrawal = await fundService.CreateOperationAsync(
            bankFund.Id,
            new CreateFundOperationRequest("withdraw", 150m, "Возврат в нераспределенные средства"),
            null,
            CancellationToken.None);
        var redistribution = await fundService.CreateOperationAsync(
            reserveFund.Id,
            new CreateFundOperationRequest("deposit", 300m, "Распределение в резерв"),
            null,
            CancellationToken.None);
        Assert.True(allocation.Succeeded);
        Assert.True(withdrawal.Succeeded);
        Assert.True(redistribution.Succeeded);
        await AssertInvariantAsync(600m, 400m);

        var bankExpense = await financeService.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), month, 100m, "INV-BANK", null),
            null,
            CancellationToken.None);
        Assert.True(bankExpense.Succeeded);
        await AssertInvariantAsync(600m, 300m);
        Assert.True((await financeService.CancelOperationAsync(bankExpense.Value!.Id, new CancelFinanceEntryRequest("Проверка отмены банка"), null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(600m, 400m);
        Assert.True((await financeService.RestoreOperationAsync(bankExpense.Value.Id, null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(600m, 300m);

        var cashExpense = await financeService.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, cashExpenseType.Id, new DateOnly(2026, 6, 21), month, 200m, "INV-CASH", null, ExpensePaymentTypes.WithoutReceipt),
            null,
            CancellationToken.None);
        Assert.True(cashExpense.Succeeded);
        await AssertInvariantAsync(400m, 300m);
        Assert.True((await financeService.CancelOperationAsync(cashExpense.Value!.Id, new CancelFinanceEntryRequest("Проверка отмены кассы"), null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(600m, 300m);
        Assert.True((await financeService.RestoreOperationAsync(cashExpense.Value.Id, null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(400m, 300m);

        var rejectedIncomeCancellation = await financeService.CancelOperationAsync(
            income.Value!.Id,
            new CancelFinanceEntryRequest("Нельзя отменить потраченные деньги"),
            null,
            CancellationToken.None);
        Assert.False(rejectedIncomeCancellation.Succeeded);
        Assert.Equal("cash_amount_insufficient", rejectedIncomeCancellation.ErrorCode);
        await AssertInvariantAsync(400m, 300m);

        Assert.True((await financeService.CancelOperationAsync(cashExpense.Value!.Id, new CancelFinanceEntryRequest("Возврат кассовой выплаты"), null, CancellationToken.None)).Succeeded);
        Assert.True((await financeService.CancelOperationAsync(bankExpense.Value!.Id, new CancelFinanceEntryRequest("Возврат банковской выплаты"), null, CancellationToken.None)).Succeeded);
        Assert.True((await fundService.CancelOperationAsync(redistribution.Value!.Id, new CancelFundOperationRequest("Отмена перераспределения"), null, CancellationToken.None)).Succeeded);
        Assert.True((await fundService.CancelOperationAsync(withdrawal.Value!.Id, new CancelFundOperationRequest("Отмена изъятия"), null, CancellationToken.None)).Succeeded);
        await AssertInvariantAsync(600m, 400m);
    }

    [Fact]
    public async Task ExpenseWorksheetQuery_ReturnsEmptyDataInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfExpenseWorksheetQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetAsync(
            new DateOnly(2026, 6, 1),
            ["no_receipt"],
            ["Выплата без чека"],
            CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Empty(result.SupplierAccruals);
        Assert.Empty(result.SupplierExpenses);
        Assert.Empty(result.StaffMembers);
        Assert.Empty(result.StaffExpenses);
        Assert.Empty(result.SupplierOpeningAccruals);
        Assert.Empty(result.SupplierOpeningExpenses);
        Assert.Empty(result.SupplierStartingBalances);
        Assert.Empty(result.StaffOpeningExpenses);
        Assert.Empty(result.Incomes);
        Assert.Equal(0m, result.AvailableBalance.IncomeTotal);
        Assert.Equal(0m, result.AvailableBalance.BankDepositTotal);
        Assert.Equal(0m, result.AvailableBalance.CashExpenseTotal);
        Assert.Equal(0m, result.AvailableBalance.BankExpenseTotal);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_IncludesSupplierStartingBalanceBeforeCurrentPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 2000m;
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var month = new DateOnly(2026, 6, 1);

        var payment = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                month,
                300m,
                "TEST-OPENING-DEBT",
                null),
            null,
            CancellationToken.None);
        var worksheet = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(month),
            CancellationToken.None);

        Assert.True(payment.Succeeded);
        Assert.True(worksheet.Succeeded);
        var row = Assert.Single(worksheet.Value!.Rows, item =>
            item.SupplierId == fixtures.Supplier.Id && item.ExpenseTypeId == fixtures.ExpenseType.Id);
        Assert.Equal(2000m, row.OpeningBalance);
        Assert.Equal(2000m, row.OpeningDebt);
        Assert.Equal(0m, row.OpeningAdvance);
        Assert.Equal(300m, row.ExpenseAmount);
        Assert.Equal(1700m, row.ClosingDebt);
        Assert.Equal(0m, row.ClosingAdvance);
        Assert.Equal(2000m, worksheet.Value.OpeningDebtTotal);
        Assert.Equal(1700m, worksheet.Value.ClosingDebtTotal);
    }

    [Fact]
    public async Task ExpenseWorksheetQuery_AutomaticallyIncludesActiveStaffSalaryFromConfiguredDay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember
        {
            FullName = "Петрова Ольга",
            Department = department,
            Rate = 40000m,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
        };
        database.Context.StaffMembers.Add(staffMember);
        database.Context.ExpenseTypes.Add(new ExpenseType { Name = "Зарплата", Code = "salary", IsSystem = true });
        database.Context.ApplicationSettings.Add(new ApplicationSetting
        {
            Key = ApplicationSettingsService.SalaryAccrualDayKey,
            IntegerValue = 15
        });
        await database.Context.SaveChangesAsync();

        var beforeDay = await new EfExpenseWorksheetQuery(
                database.Context,
                new TestBusinessDateProvider(new DateOnly(2026, 7, 14)))
            .GetAsync(new DateOnly(2026, 7, 1), ["no_receipt"], ["Выплата без чека"], CancellationToken.None);
        var fromDay = await new EfExpenseWorksheetQuery(
                database.Context,
                new TestBusinessDateProvider(new DateOnly(2026, 7, 15)))
            .GetAsync(new DateOnly(2026, 7, 1), ["no_receipt"], ["Выплата без чека"], CancellationToken.None);

        Assert.Equal(0m, Assert.Single(beforeDay.StaffMembers).Rate);
        Assert.Equal(40000m, Assert.Single(fromDay.StaffMembers).Rate);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_ReturnsEmptyWorksheetInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(0m, result.Value!.AccrualTotal);
        Assert.Equal(0m, result.Value.ExpenseTotal);
        Assert.Equal(0m, result.Value.BankAmount);
        Assert.Equal(0m, result.Value.CashAmount);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task GetExpenseWorksheetAsync_IncludesOpeningAndAdjustmentOperationsInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        database.Context.CashBankBalanceOperations.AddRange(
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 7, 1),
                Amount = 1000m,
                Reason = "Старт кассы"
            },
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Bank,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 7, 1),
                Amount = 5000m,
                Reason = "Старт счёта"
            },
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.Adjustment,
                Direction = CashBankBalanceDirections.Decrease,
                OperationDate = new DateOnly(2026, 7, 2),
                Amount = 125m,
                Reason = "Списание кассы"
            });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(875m, result.Value!.CashAmount);
        Assert.Equal(5000m, result.Value.BankAmount);
    }

    [Fact]
    public async Task ExpenseWorksheetQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfExpenseWorksheetQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            query.GetAsync(
                new DateOnly(2026, 6, 1),
                ["no_receipt"],
                ["Выплата без чека"],
                cancellationSource.Token));
    }

    [Fact]
    public async Task FinanceAvailableBalanceQuery_ReturnsZeroForEmptyDatabaseInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfFinanceAvailableBalanceQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetAsync(["no_receipt"], ["Без чека"], CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(0m, result.IncomeTotal);
        Assert.Equal(0m, result.BankDepositTotal);
        Assert.Equal(0m, result.CashExpenseTotal);
        Assert.Equal(0m, result.BankExpenseTotal);
        Assert.Equal(0m, result.CashAdjustmentTotal);
        Assert.Equal(0m, result.BankAdjustmentTotal);
    }

    [Fact]
    public async Task FinanceAvailableBalanceQuery_AggregatesCashAndBankAdjustmentsInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        database.Context.CashBankBalanceOperations.AddRange(
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 7, 1),
                Amount = 1000m,
                Reason = "Старт кассы"
            },
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.Adjustment,
                Direction = CashBankBalanceDirections.Decrease,
                OperationDate = new DateOnly(2026, 7, 2),
                Amount = 125m,
                Reason = "Списание кассы"
            },
            new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Bank,
                OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 7, 1),
                Amount = 5000m,
                Reason = "Старт счёта"
            });
        await database.Context.SaveChangesAsync();
        var query = new EfFinanceAvailableBalanceQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetAsync(["no_receipt"], ["Без чека"], CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(875m, result.CashAdjustmentTotal);
        Assert.Equal(5000m, result.BankAdjustmentTotal);
    }

    [Fact]
    public async Task FinanceAvailableBalanceQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfFinanceAvailableBalanceQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetAsync(
            ["no_receipt"],
            ["Без чека"],
            cancellationSource.Token));
    }

    private static async Task RemoveSeededBankTransferAsync(GarageBalanceDbContext context)
    {
        context.FundOperations.RemoveRange(context.FundOperations);
        context.CashBankTransfers.RemoveRange(context.CashBankTransfers);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task IncomeDestinationAssignment_FollowsCreateUpdateCancelAndRestoreLifecycle()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.ExpenseFundId = null;
        fixtures.Supplier.ExpenseFund = null;
        database.Context.FundOperations.RemoveRange(
            database.Context.FundOperations.Where(operation => operation.FundId == fixtures.ExpenseFund.Id));
        database.Context.Funds.Remove(fixtures.ExpenseFund);
        await RemoveSeededBankTransferAsync(database.Context);
        var firstIncomeType = AddOtherIncomeDestination(database.Context);
        var secondFund = new Fund
        {
            Name = "Целевой фонд",
            NormalizedName = "ЦЕЛЕВОЙ ФОНД"
        };
        var secondIncomeType = new IncomeType
        {
            Name = "Целевое поступление",
            Code = "target_income",
            DestinationFund = secondFund,
            DestinationFundId = secondFund.Id
        };
        database.Context.AddRange(secondFund, secondIncomeType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var fundService = new FundService(
            new EfFundRepository(database.Context),
            new AuditEventWriter(database.Context));
        var actorUserId = Guid.NewGuid();
        var request = new CreateIncomeOperationRequest(
            fixtures.Garage.Id,
            firstIncomeType.Id,
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 1),
            400m,
            "PKO-FUND-1",
            null);

        var created = await service.CreateIncomeAsync(request, actorUserId, CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        var createdOperationId = created.Value!.Id;
        var assignment = await database.Context.FundOperations
            .Include(operation => operation.Fund)
            .SingleAsync(operation => operation.SourceFinancialOperationId == createdOperationId);
        Assert.Equal(firstIncomeType.DestinationFundId, assignment.FundId);
        Assert.Equal(400m, assignment.Amount);
        Assert.False(assignment.IsCanceled);
        Assert.Equal(400m, assignment.Fund.Balance);
        Assert.All(
            await fundService.GetFundsAsync(CancellationToken.None),
            fund => Assert.Equal(0m, fund.AvailableToDistribute));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_created");
        Assert.DoesNotContain(
            await fundService.GetOperationsAsync(100, includeCanceled: true, CancellationToken.None),
            item => item.Id == assignment.Id);
        var workingHistoryPage = await fundService.GetOperationsPageAsync(
            offset: 0,
            limit: 25,
            includeCanceled: true,
            CancellationToken.None);
        Assert.Equal(0, workingHistoryPage.TotalCount);
        Assert.Empty(workingHistoryPage.Items);
        var manualUpdate = await fundService.UpdateOperationAsync(
            assignment.Id,
            new UpdateFundOperationRequest(350m, "Ручное изменение"),
            actorUserId,
            CancellationToken.None);
        var manualCancel = await fundService.CancelOperationAsync(
            assignment.Id,
            new CancelFundOperationRequest("Ручная отмена"),
            actorUserId,
            CancellationToken.None);
        Assert.Equal("fund_operation_managed_by_income", manualUpdate.ErrorCode);
        Assert.Equal("fund_operation_managed_by_income", manualCancel.ErrorCode);

        var reduced = await service.UpdateIncomeAsync(
            createdOperationId,
            request with { Amount = 250m },
            actorUserId,
            CancellationToken.None);

        Assert.True(reduced.Succeeded, reduced.ErrorMessage);
        Assert.Equal(250m, assignment.Amount);
        Assert.Equal(250m, assignment.Fund.Balance);
        Assert.All(
            await fundService.GetFundsAsync(CancellationToken.None),
            fund => Assert.Equal(0m, fund.AvailableToDistribute));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_updated");

        var moved = await service.UpdateIncomeAsync(
            createdOperationId,
            request with { IncomeTypeId = secondIncomeType.Id, Amount = 300m },
            actorUserId,
            CancellationToken.None);

        Assert.True(moved.Succeeded, moved.ErrorMessage);
        Assert.Equal(secondFund.Id, assignment.FundId);
        Assert.Equal(300m, assignment.Amount);
        Assert.Equal(0m, firstIncomeType.DestinationFund!.Balance);
        Assert.Equal(300m, secondFund.Balance);

        var removedDestination = await service.UpdateIncomeAsync(
            createdOperationId,
            request with { IncomeTypeId = fixtures.IncomeType.Id, Amount = 300m },
            actorUserId,
            CancellationToken.None);

        Assert.True(removedDestination.Succeeded, removedDestination.ErrorMessage);
        Assert.True(assignment.IsCanceled);
        Assert.Equal(0m, secondFund.Balance);

        var restoredDestination = await service.UpdateIncomeAsync(
            createdOperationId,
            request with { IncomeTypeId = firstIncomeType.Id, Amount = 275m },
            actorUserId,
            CancellationToken.None);

        Assert.True(restoredDestination.Succeeded, restoredDestination.ErrorMessage);
        Assert.False(assignment.IsCanceled);
        Assert.Equal(firstIncomeType.DestinationFundId, assignment.FundId);
        Assert.Equal(275m, firstIncomeType.DestinationFund!.Balance);

        var canceled = await service.CancelOperationAsync(
            createdOperationId,
            new CancelFinanceEntryRequest("Ошибочное поступление"),
            actorUserId,
            CancellationToken.None);

        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.True(assignment.IsCanceled);
        Assert.Equal(0m, firstIncomeType.DestinationFund.Balance);
        Assert.All(
            await fundService.GetFundsAsync(CancellationToken.None),
            fund => Assert.Equal(0m, fund.AvailableToDistribute));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_canceled");
        var manualRestore = await fundService.RestoreOperationAsync(assignment.Id, actorUserId, CancellationToken.None);
        Assert.Equal("fund_operation_managed_by_income", manualRestore.ErrorCode);

        var restored = await service.RestoreOperationAsync(createdOperationId, actorUserId, CancellationToken.None);

        Assert.True(restored.Succeeded, restored.ErrorMessage);
        Assert.False(assignment.IsCanceled);
        Assert.Equal(275m, firstIncomeType.DestinationFund.Balance);
        Assert.All(
            await fundService.GetFundsAsync(CancellationToken.None),
            fund => Assert.Equal(0m, fund.AvailableToDistribute));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_restored");
    }

    [Fact]
    public async Task CancelIncomeAsync_RejectsWhenAssignedFundWasAlreadySpent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.ExpenseFundId = null;
        fixtures.Supplier.ExpenseFund = null;
        database.Context.FundOperations.RemoveRange(
            database.Context.FundOperations.Where(operation => operation.FundId == fixtures.ExpenseFund.Id));
        database.Context.Funds.Remove(fixtures.ExpenseFund);
        await RemoveSeededBankTransferAsync(database.Context);
        var incomeType = AddOtherIncomeDestination(database.Context);
        incomeType.DestinationFund!.AllowOperations = true;
        await database.Context.SaveChangesAsync();
        var financeService = FinanceServiceTestFactory.Create(database.Context);
        var fundService = new FundService(
            new EfFundRepository(database.Context),
            new AuditEventWriter(database.Context));
        var created = await financeService.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                incomeType.Id,
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 1),
                400m,
                "PKO-FUND-SPENT",
                null),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.True((await fundService.CreateOperationAsync(
            incomeType.DestinationFund.Id,
            new CreateFundOperationRequest(FundOperationKinds.Withdraw, 300m, "Средства фонда уже использованы"),
            null,
            CancellationToken.None)).Succeeded);

        var result = await financeService.CancelOperationAsync(
            created.Value!.Id,
            new CancelFinanceEntryRequest("Отмена использованного поступления"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.False((await database.Context.FinancialOperations.SingleAsync(item => item.Id == created.Value.Id)).IsCanceled);
        Assert.False((await database.Context.FundOperations.SingleAsync(item => item.SourceFinancialOperationId == created.Value.Id)).IsCanceled);
        Assert.Equal(100m, incomeType.DestinationFund.Balance);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_canceled");
    }

    [Fact]
    public async Task RestoreIncomeAsync_CreatesMissingLegacyFundAssignment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var incomeType = AddOtherIncomeDestination(database.Context);
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 15),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 180m,
            DocumentNumber = "LEGACY-ROUTED-INCOME",
            Garage = fixtures.Garage,
            GarageId = fixtures.Garage.Id,
            IncomeType = incomeType,
            IncomeTypeId = incomeType.Id,
            IsCanceled = true
        };
        database.Context.FinancialOperations.Add(operation);
        await database.Context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(database.Context)
            .RestoreOperationAsync(operation.Id, null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var assignment = await database.Context.FundOperations
            .SingleAsync(item => item.SourceFinancialOperationId == operation.Id);
        Assert.False(assignment.IsCanceled);
        Assert.Equal(180m, assignment.Amount);
        Assert.Equal(incomeType.DestinationFundId, assignment.FundId);
        Assert.Equal(180m, incomeType.DestinationFund!.Balance);
    }

    [Fact]
    public async Task CreateIncomeAsync_RollsBackIncomeAssignmentAndAuditsWhenFundInsertFails()
    {
        var failure = new FundOperationInsertFailureInterceptor();
        await using var database = await TestDatabase.CreateAsync(failure);
        var fixtures = await database.SeedAsync();
        await RemoveSeededBankTransferAsync(database.Context);
        var incomeType = AddOtherIncomeDestination(database.Context);
        await database.Context.SaveChangesAsync();
        failure.Enabled = true;
        var service = FinanceServiceTestFactory.Create(database.Context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                incomeType.Id,
                new DateOnly(2026, 7, 12),
                new DateOnly(2026, 7, 1),
                220m,
                "PKO-FUND-ROLLBACK",
                null),
            null,
            CancellationToken.None));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        failure.Enabled = false;
        database.Context.ChangeTracker.Clear();
        Assert.DoesNotContain(database.Context.FinancialOperations, item => item.DocumentNumber == "PKO-FUND-ROLLBACK");
        Assert.DoesNotContain(database.Context.FundOperations, item => item.SourceFinancialOperationId != null);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.income_created");
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "fund.income_assignment_created");
    }

    private static IncomeType AddOtherIncomeDestination(GarageBalanceDbContext context)
    {
        var fund = new Fund
        {
            Name = "Прочее",
            NormalizedName = "ПРОЧЕЕ"
        };
        var incomeType = new IncomeType
        {
            Name = "Прочие доходы",
            Code = "other_income",
            IsSystem = true,
            DestinationFund = fund,
            DestinationFundId = fund.Id
        };
        context.AddRange(fund, incomeType);
        return incomeType;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection);
            if (interceptors.Length > 0)
            {
                optionsBuilder.AddInterceptors(interceptors);
            }

            var options = optionsBuilder.Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<Fixtures> SeedAsync()
        {
            var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
            var garage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = owner, InitialWaterMeterValue = 10m, InitialElectricityMeterValue = 100m };
            var group = new SupplierGroup { Name = "Коммунальные услуги" };
            var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
            var expenseType = new ExpenseType { Name = "Вода", Code = "water" };
            var expenseFund = new Fund
            {
                Name = "Коммунальные расходы",
                NormalizedName = "КОММУНАЛЬНЫЕ РАСХОДЫ",
                Balance = SeededBankAmount
            };
            var expenseFundOpening = new FundOperation
            {
                Fund = expenseFund,
                FundId = expenseFund.Id,
                OperationKind = FundOperationKinds.Deposit,
                Amount = SeededBankAmount,
                BalanceBefore = 0m,
                BalanceAfter = SeededBankAmount,
                Reason = "Тестовое наполнение фонда",
                CreatedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var chargeService = new ChargeServiceSetting
            {
                Name = "Вода",
                IsRegular = false
            };
            var supplier = new Supplier
            {
                Name = "Vodokanal",
                Group = group,
                ChargeServiceSettingId = chargeService.Id,
                ChargeServiceSetting = chargeService,
                ExpenseTypeId = expenseType.Id,
                ExpenseType = expenseType,
                ExpenseFundId = expenseFund.Id,
                ExpenseFund = expenseFund
            };
            var bankDeposit = new CashBankTransfer
            {
                TransferDate = new DateOnly(2000, 1, 1),
                Amount = SeededBankAmount,
                Comment = "Тестовая сумма на банковском счете",
                CreatedAtUtc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            Context.AddRange(owner, garage, group, supplier, incomeType, expenseType, expenseFund, expenseFundOpening, chargeService, bankDeposit);
            await Context.SaveChangesAsync();
            return new Fixtures(garage, supplier, incomeType, expenseType, expenseFund);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record Fixtures(Garage Garage, Supplier Supplier, IncomeType IncomeType, ExpenseType ExpenseType, Fund ExpenseFund);

    private static CashBankBalanceOperation OpeningCashBalance(decimal amount) => new()
    {
        Account = CashBankAccounts.Cash,
        OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
        Direction = CashBankBalanceDirections.Increase,
        OperationDate = new DateOnly(2026, 1, 1),
        Amount = amount,
        Reason = "Тестовый стартовый остаток кассы"
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public List<string> Commands { get; } = [];

        public void Reset()
        {
            Count = 0;
            Commands.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Count++;
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class SupplierAccrualInsertFailureInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSupplierAccrualInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSupplierAccrualInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void ThrowIfSupplierAccrualInsert(DbCommand command)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"supplier_accruals\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Имитирована ошибка второй записи атомарной выплаты.");
            }
        }
    }

    private sealed class MeteredAccrualUpdateFailureInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfAccrualUpdate(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfAccrualUpdate(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void ThrowIfAccrualUpdate(DbCommand command)
        {
            if (Enabled && command.CommandText.Contains("UPDATE \"accruals\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Имитирована ошибка пересчета связанного начисления.");
            }
        }
    }

    private sealed class FundOperationInsertFailureInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"fund_operations\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Имитирована ошибка сохранения автоматического назначения фонда.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
