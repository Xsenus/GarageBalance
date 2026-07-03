using System.Text.Json;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FinanceServiceTests
{
    [Fact]
    public async Task CreateIncomeAsync_CreatesOperationAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                fixtures.Garage.Id,
                fixtures.IncomeType.Id,
                new DateOnly(2026, 6, 19),
                new DateOnly(2026, 6, 15),
                1500.50m,
                "PKO-19",
                "Авансовый платеж"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("income", result.Value!.OperationKind);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.AccountingMonth);
        Assert.Equal("12", result.Value.GarageNumber);
        Assert.Equal("Членский взнос", result.Value.IncomeTypeName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.income_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано поступление 1500,50", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 19.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("вид Членский взнос", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-19", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Авансовый платеж", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationAudit_UsesWriterStructuredFieldsAndCancelReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
    public async Task UpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);

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
        var service = new FinanceService(database.Context);

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
        var service = new FinanceService(database.Context);

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
    public async Task CreateIncomeAsync_ReturnsGarageDebtBeforeAndAfterPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 200m;
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);
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
    public async Task CreateIncomeAsync_AllocatesPaymentToOldestGarageDebts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
                Assert.Equal(new DateOnly(2026, 6, 1), first.AccountingMonth);
                Assert.Equal(500m, first.DebtBefore);
                Assert.Equal(500m, first.PaidAmount);
                Assert.Equal(0m, first.DebtAfter);
            },
            second =>
            {
                Assert.Equal("month", second.AllocationKind);
                Assert.Equal(new DateOnly(2026, 7, 1), second.AccountingMonth);
                Assert.Equal(700m, second.DebtBefore);
                Assert.Equal(300m, second.PaidAmount);
                Assert.Equal(400m, second.DebtAfter);
            });
    }

    [Fact]
    public async Task GetGarageBalanceHistoryAsync_ReturnsMonthlyRunningDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.StartingBalance = 100m;
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);
        Assert.True((await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "regular", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 200m, "PKO-history-1", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 700m, "regular", null), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 1), 300m, "PKO-history-2", null), null, CancellationToken.None)).Succeeded);

        var result = await service.GetGarageBalanceHistoryAsync(
            fixtures.Garage.Id,
            new GarageBalanceHistoryRequest(new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 31)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("12", result.Value!.GarageNumber);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.MonthFrom);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Value.MonthTo);
        Assert.Equal(100m, result.Value.StartingBalance);
        Assert.Equal(1200m, result.Value.AccrualTotal);
        Assert.Equal(500m, result.Value.IncomeTotal);
        Assert.Equal(800m, result.Value.Debt);
        Assert.Collection(
            result.Value.Rows,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 6, 1), first.AccountingMonth);
                Assert.Equal(100m, first.OpeningDebt);
                Assert.Equal(500m, first.AccrualAmount);
                Assert.Equal(200m, first.IncomeAmount);
                Assert.Equal(400m, first.ClosingDebt);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 7, 1), second.AccountingMonth);
                Assert.Equal(400m, second.OpeningDebt);
                Assert.Equal(700m, second.AccrualAmount);
                Assert.Equal(300m, second.IncomeAmount);
                Assert.Equal(800m, second.ClosingDebt);
            });
    }

    [Fact]
    public async Task GetGarageBalanceHistoryAsync_ReturnsFailureForMissingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = new FinanceService(database.Context);

        var result = await service.GetGarageBalanceHistoryAsync(Guid.NewGuid(), new GarageBalanceHistoryRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllocatesPaymentToOldestSupplierDebts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        Assert.Contains("было 300,00 по гаражу 12 от 19.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-old", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 450,00 по гаражу 12 от 20.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ PKO-new", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Исправлена сумма", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelOperationAsync_CancelsOperationAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        Assert.Contains("Отменено поступление 400,00", audit.Summary, StringComparison.Ordinal);
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
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
    public async Task CreateExpenseAsync_ReturnsNotFoundForMissingSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);

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
        var service = new FinanceService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                fixtures.ExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 15),
                400.75m,
                "RKO-20",
                "Оплата воды"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("expense", result.Value!.OperationKind);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.AccountingMonth);
        Assert.Equal("Vodokanal", result.Value.SupplierName);
        Assert.Equal("Вода", result.Value.ExpenseTypeName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.expense_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создана выплата 400,75", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 20.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("вид Вода", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-20", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Оплата воды", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        Assert.Contains("было 250,00 поставщику Vodokanal от 20.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-old", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 400,00 поставщику Vodokanal от 21.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-new", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Исправлена выплата", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateExpenseAsync_ReturnsSupplierDebtBeforeAndAfterPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 300m;
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        Assert.Equal(1, result.AccrualCount);
        Assert.Equal(0, result.MeterReadingCount);
    }

    [Fact]
    public async Task GetOperationsAsync_SearchesByGarageSupplierDocumentAndComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "DOC-12", "Оплата по квитанции"), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "DOC-20", "Компенсация поставщику"), null, CancellationToken.None);

        var garageResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "12"), CancellationToken.None);
        var supplierResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "vodokanal"), CancellationToken.None);
        var commentResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "квитанции"), CancellationToken.None);

        Assert.Single(garageResult);
        Assert.Equal("income", garageResult[0].OperationKind);
        Assert.Single(supplierResult);
        Assert.Equal("expense", supplierResult[0].OperationKind);
        Assert.Single(commentResult);
        Assert.Equal("Оплата по квитанции", commentResult[0].Comment);
    }

    [Fact]
    public async Task CreateAccrualAsync_CreatesManualAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 15), 700m, "manual", "Целевой сбор"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal("manual", result.Value.Source);
        Assert.Equal("12", result.Value.GarageNumber);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано начисление 700,00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Целевой сбор", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAccrualAsync_RequiresCommentForManualAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_comment_required", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAccrualAsync_RequiresCommentForRegularAccrualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null),
            null,
            CancellationToken.None);

        var result = await service.UpdateAccrualAsync(
            created.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 750m, "regular", " "),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_regular_edit_comment_required", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "manual", "Исходная ручная сумма"),
            null,
            CancellationToken.None);

        var result = await service.UpdateAccrualAsync(
            created.Value!.Id,
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 7, 1), 750m, "manual", "Исправили после сверки"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("было 700,00 по гаражу 12 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; комментарий Исходная ручная сумма", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 750,00 по гаражу 12 за 07.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; комментарий Исправили после сверки", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAccrualAsync_RejectsDuplicateGarageTypeMonthAndSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var request = new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null);
        await service.CreateAccrualAsync(request, null, CancellationToken.None);

        var result = await service.CreateAccrualAsync(request with { Amount = 800m }, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAccrualAsync_AllowsReplacementAfterCancel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        Assert.Contains("Отменено начисление 700,00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Начислено не тому гаражу", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_CreatesManualAccrualAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        Assert.Contains("Создано начисление 1200,00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ INV-1", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Счет за воду", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RequiresCommentForManualAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);

        var result = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-no-comment", "   "),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_comment_required", result.ErrorCode);
        Assert.Empty(database.Context.SupplierAccruals);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsUnsupportedSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);

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
    public async Task UpdateSupplierAccrualAsync_RequiresCommentForRegularAccrualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        var created = await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", "INV-regular", null),
            null,
            CancellationToken.None);

        var result = await service.UpdateSupplierAccrualAsync(
            created.Value!.Id,
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1250m, "regular", "INV-regular", " "),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_regular_edit_comment_required", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateSupplierAccrualAsync_WritesBeforeAndAfterAuditForManualCorrection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        Assert.Contains("было 1200,00 поставщику Vodokanal за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; документ INV-old; комментарий Исходный счет", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 1250,00 поставщику Vodokanal за 07.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual; документ INV-new; комментарий Уточненный счет", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSupplierAccrualAsync_RejectsDuplicateSupplierTypeMonthSourceAndDocument()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
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
        Assert.Contains("Отменено начисление 1200,00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.ExpenseType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ INV-cancel", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Причина: Счет заменен", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSupplierAccrualsAsync_SearchesAndOrdersByMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 5, 1), 900m, "regular", "INV-05", null), null, CancellationToken.None);
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-06", "supplier adjustment"), null, CancellationToken.None);

        var result = await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, "adjustment"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(1200m, result[0].Amount);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CreatesFixedAccrualsForActiveGarages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);
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
        Assert.Equal("Июнь; тариф Членский тариф: ставка 300, действует с 01.01.2026.", accrual.Comment);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано регулярных начислений: 1", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("на сумму 300,00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("тариф Членский тариф, база fixed, ставка 300", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("пропущено 0", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_KeepsExistingAccrualAmountAfterTariffUpdate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var finance = new FinanceService(database.Context);
        var dictionaries = new DictionaryService(database.Context);

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
            item.Comment == "Автоначисление; тариф Членский тариф: ставка 300, действует с 01.01.2026.");
        Assert.Contains(accruals, item =>
            item.AccountingMonth == new DateOnly(2026, 7, 1) &&
            item.Amount == 500m &&
            item.Comment == "Автоначисление; тариф Членский тариф: ставка 500, действует с 01.01.2026.");
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
        var service = new FinanceService(database.Context);
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
    public async Task GenerateRegularAccrualsAsync_RejectsTariffThatDoesNotMatchIncomeType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
        var tariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);

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
        var service = new FinanceService(database.Context);
        await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 230m, null),
            null,
            CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(400m, result.Value!.TotalAmount);
        Assert.Equal(400m, result.Value.CreatedAccruals[0].Amount);
        Assert.Equal("meter_electricity", result.Value.CalculationBase);
        Assert.Contains("пороги электроэнергии до 50 кВт по 2, до 100 кВт по 3, свыше по 5", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Contains("пороги электроэнергии до 50 кВт по 2, до 100 кВт по 3, свыше по 5", audit.Summary, StringComparison.Ordinal);
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
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
        var request = new GenerateRegularAccrualsRequest(fixtures.IncomeType.Id, tariff.Id, new DateOnly(2026, 6, 1), null);
        await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        var result = await service.GenerateRegularAccrualsAsync(request, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("regular_accruals_empty", result.ErrorCode);
        Assert.Single(database.Context.Accruals);
    }

    [Fact]
    public async Task GenerateSupplierGroupSalaryAccrualsAsync_CreatesSalaryForEveryActiveSupplierInGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
    public async Task GenerateSupplierGroupSalaryAccrualsAsync_RejectsSecondRunForSameMonthAndDocument()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 5, 1), 500m, "regular", null), null, CancellationToken.None);
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 600m, "manual", "garage monthly adjustment"), null, CancellationToken.None);

        var result = await service.GetAccrualsAsync(new AccrualListRequest(null, null, "garage"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(600m, result[0].Amount);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_UsesInitialMeterValueAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
    public async Task CreateMeterReadingAsync_RoundsMeterValuesAndConsumptionAwayFromZero()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.InitialWaterMeterValue = 10.0005m;
        await database.Context.SaveChangesAsync();
        var service = new FinanceService(database.Context);

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
    public async Task CancelMeterReadingAsync_CancelsReadingAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
    public async Task CreateMeterReadingAsync_WarnsWhenElectricityPreviousMonthIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);

        var result = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 5m, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_decreased", result.ErrorCode);
    }

    [Fact]
    public async Task CreateMeterReadingAsync_RejectsDuplicateGarageKindAndMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
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
        var service = new FinanceService(database.Context);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "water", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 20), 14m, null), null, CancellationToken.None);
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 120m, "monthly electricity"), null, CancellationToken.None);

        var result = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, "electricity", "monthly"), CancellationToken.None);

        var reading = Assert.Single(result);
        Assert.Equal("electricity", reading.MeterKind);
        Assert.Equal(new DateOnly(2026, 6, 1), reading.AccountingMonth);
        Assert.Equal(20m, reading.Consumption);
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
        var service = new FinanceService(database.Context);
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

        Assert.Equal(3, result.Count);
        Assert.Contains(result, item => item.GarageNumber == "12" && item.MeterKind == "electricity" && item.AccountingMonth == new DateOnly(2026, 6, 1));
        Assert.Contains(result, item => item.GarageNumber == "13" && item.MeterKind == "water");
        Assert.Contains(result, item => item.GarageNumber == "13" && item.MeterKind == "electricity");
        Assert.DoesNotContain(result, item => item.GarageNumber == "14");
    }

    [Fact]
    public async Task GetMissingMeterReadingsAsync_FiltersByKindSearchAndLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = new FinanceService(database.Context);

        var result = await service.GetMissingMeterReadingsAsync(
            new MissingMeterReadingListRequest(new DateOnly(2026, 6, 1), "water", "12", 1),
            CancellationToken.None);

        var missing = Assert.Single(result);
        Assert.Equal("12", missing.GarageNumber);
        Assert.Equal("water", missing.MeterKind);
        Assert.Equal("Иванов Иван", missing.OwnerName);
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

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<Fixtures> SeedAsync()
        {
            var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
            var garage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = owner, InitialWaterMeterValue = 10m, InitialElectricityMeterValue = 100m };
            var group = new SupplierGroup { Name = "Коммунальные услуги" };
            var supplier = new Supplier { Name = "Vodokanal", Group = group };
            var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
            var expenseType = new ExpenseType { Name = "Вода", Code = "water" };

            Context.AddRange(owner, garage, group, supplier, incomeType, expenseType);
            await Context.SaveChangesAsync();
            return new Fixtures(garage, supplier, incomeType, expenseType);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record Fixtures(Garage Garage, Supplier Supplier, IncomeType IncomeType, ExpenseType ExpenseType);
}
