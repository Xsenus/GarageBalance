using System.Data.Common;
using System.Text.Json;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
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
    public async Task CreateIncomeAsync_CreatesOperationAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
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
        Assert.Contains("Создано поступление 1 500.50", audit.Summary, StringComparison.Ordinal);
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
        await database.SeedAsync();
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember { FullName = "Петрова Ольга", Department = department, Rate = 40000m };
        var salaryType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        database.Context.AddRange(department, staffMember, salaryType);
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
    public async Task CreateStaffPaymentAsync_DoesNotCreateOperationWhenBankAmountIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
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
        Assert.Equal("bank_amount_insufficient", result.ErrorCode);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
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
        var service = FinanceServiceTestFactory.Create(database.Context);

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
        var service = FinanceServiceTestFactory.Create(database.Context);

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
        database.Context.MeterReadings.AddRange(
            new MeterReading { GarageId = secondGarage.Id, MeterKind = "electricity", AccountingMonth = new DateOnly(2026, 2, 1), ReadingDate = new DateOnly(2026, 2, 20), CurrentValue = 125m },
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
        var secondSupplier = new Supplier { Name = "Teploset", GroupId = fixtures.Supplier.GroupId };
        var department = new StaffDepartment { Name = "Бухгалтерия" };
        var firstStaff = new StaffMember { FullName = "Петрова Ольга", Department = department, Rate = 40000m };
        var secondStaff = new StaffMember { FullName = "Иванов Сергей", Department = department, Rate = 20000m };
        var salaryExpenseType = new ExpenseType { Name = "Зарплата", Code = "salary" };
        database.Context.AddRange(secondSupplier, department, firstStaff, secondStaff, salaryExpenseType);
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

        var result = await service.CreateGarageDebtPaymentAsync(
            new CreateGarageDebtPaymentRequest(fixtures.Garage.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 500m, "Оплата старого долга"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("income", result.Value!.OperationKind);
        Assert.Equal("Перенос задолженности", result.Value.IncomeTypeName);
        Assert.Equal(900m, result.Value.GarageDebtBefore);
        Assert.Equal(400m, result.Value.GarageDebtAfter);
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
    public async Task CreateIncomeAsync_AllocatesPaymentToOldestGarageDebts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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

        var persistedAllocations = await database.Context.AccrualPaymentAllocations
            .OrderBy(item => item.Accrual.DueDate)
            .ToListAsync();
        Assert.Equal([500m, 300m], persistedAllocations.Select(item => item.Amount));

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
        Assert.Equal([500m, 300m], (await ActiveAllocationsAsync()).Select(item => item.Amount));

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
        Assert.Equal(400m, result.Value.Total);
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
                Assert.Equal(200m, accrual.PaidAmount);
                Assert.Equal(300m, accrual.OutstandingAmount);
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

    [Fact]
    public async Task CancelOperationAsync_CancelsOperationAndRemovesItFromSummary()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
        await database.Context.SaveChangesAsync();

        var result = await service.RestoreOperationAsync(created.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("bank_amount_insufficient", result.ErrorCode);
        Assert.True(await database.Context.FinancialOperations.AnyAsync(operation => operation.Id == created.Value.Id && operation.IsCanceled));
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "finance.operation_restored");
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
        Assert.Contains("Создана выплата 400.75", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("поставщику Vodokanal", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("от 20.06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("вид Вода", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-20", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Оплата воды", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateExpenseAsync_DoesNotCreateOperationWhenBankAmountIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
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
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateExpenseAsync_AllowsCashExpenseWithoutBankWhenCashIsAvailable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        var cashExpenseType = new ExpenseType { Name = "Авансовые выплаты", Code = "advance" };
        database.Context.AddRange(
            cashExpenseType,
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
                "Аванс из кассы"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(300m, result.Value!.Amount);
        Assert.Equal("Авансовые выплаты", result.Value.ExpenseTypeName);
        var worksheet = await service.GetExpenseWorksheetAsync(new ExpenseWorksheetRequest(new DateOnly(2026, 6, 1)), CancellationToken.None);
        Assert.True(worksheet.Succeeded);
        Assert.Equal(0m, worksheet.Value!.BankAmount);
        Assert.Equal(200m, worksheet.Value.CashAmount);
    }

    [Fact]
    public async Task CreateExpenseAsync_DoesNotUseBankForCashExpenseWhenCashIsInsufficient()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var cashExpenseType = new ExpenseType { Name = "Выплата без чека", Code = "no_receipt" };
        database.Context.Add(cashExpenseType);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var result = await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                fixtures.Supplier.Id,
                cashExpenseType.Id,
                new DateOnly(2026, 6, 20),
                new DateOnly(2026, 6, 1),
                1m,
                "CASH-NO-RECEIPT",
                null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("cash_amount_insufficient", result.ErrorCode);
        Assert.DoesNotContain(database.Context.FinancialOperations, operation => operation.OperationKind == FinancialOperationKinds.Expense);
        Assert.Empty(database.Context.AuditEvents);
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
        Assert.Contains("было 250.00 поставщику Vodokanal от 20.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("документ RKO-old", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("стало 400.00 поставщику Vodokanal от 21.06.2026 за 06.2026", audit.Summary, StringComparison.Ordinal);
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
        database.Context.Funds.RemoveRange(database.Context.Funds);
        var bankFund = new Fund { Name = "Банк для проверки", NormalizedName = "БАНК ДЛЯ ПРОВЕРКИ", Balance = 300m };
        database.Context.AddRange(bankFund, new FundOperation
        {
            Fund = bankFund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = 300m,
            BalanceBefore = 0m,
            BalanceAfter = 300m,
            Reason = "Сумма на банковском счете",
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
        var cashExpenseType = new ExpenseType { Name = "Авансовые выплаты", Code = "advance" };
        database.Context.AddRange(
            cashExpenseType,
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
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, cashExpenseType.Id, new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 1), 200.01m, "RKO-bank-to-cash-new", "Сверх кассы"),
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
        var service = FinanceServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 15), 700m, "manual", "Целевой сбор"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.AccountingMonth);
        Assert.Equal("manual", result.Value.Source);
        Assert.Equal("12", result.Value.GarageNumber);
        Assert.Equal(new DateOnly(2026, 7, 31), result.Value.DueDate);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Value.OverdueFromDate);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.accrual_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Создано начисление 700.00", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("по гаражу 12", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("за 06.2026", audit.Summary, StringComparison.Ordinal);
        Assert.Contains($"вид {fixtures.IncomeType.Name}", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("источник manual", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("Комментарий: Целевой сбор", audit.Summary, StringComparison.Ordinal);
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
    public async Task CreateAccrualAsync_RequiresCommentForManualAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

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

        Assert.False(result.Succeeded);
        Assert.Equal("accrual_regular_edit_comment_required", result.ErrorCode);
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
    public async Task CreateSupplierAccrualAsync_RequiresCommentForManualAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

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
    public async Task UpdateSupplierAccrualAsync_RequiresCommentForRegularAccrualCorrection()
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

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_accrual_regular_edit_comment_required", result.ErrorCode);
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
        await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-06", "Ежемесячная корректировка поставщика"), null, CancellationToken.None);

        var result = await service.GetSupplierAccrualsAsync(new SupplierAccrualListRequest(null, null, "ежемесячная"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(1200m, result[0].Amount);
    }

    [Fact]
    public async Task GetSupplierAccrualsPageAsync_FiltersBySupplierId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var secondSupplier = new Supplier { Name = "Teploset", GroupId = fixtures.Supplier.GroupId };
        database.Context.Suppliers.Add(secondSupplier);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 900m, "manual", "INV-1", "Счет первого поставщика"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(secondSupplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 1200m, "manual", "INV-2", "Счет второго поставщика"), null, CancellationToken.None)).Succeeded);

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
        Assert.Equal(tariff.Id, accrual.TariffId);
        Assert.Equal(new DateOnly(2026, 6, 30), accrual.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), accrual.OverdueFromDate);
        Assert.Equal("Каталог услуг: Членский взнос; Июнь; тариф Членский тариф: ставка 300.00, действует с 01.01.2026.", accrual.Comment);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.regular_catalog_accruals_generated" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RegularAccrualAutomationRunner_CreatesCurrentBusinessMonthWithoutDuplicates()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "membership";
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
        await database.Context.SaveChangesAsync();

        var runner = new RegularAccrualAutomationRunner(
            FinanceServiceTestFactory.Create(database.Context),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 19, 30, 0, TimeSpan.Zero)),
            Options.Create(new RegularAccrualAutomationOptions { TimeZoneId = "Asia/Novosibirsk" }),
            NullLogger<RegularAccrualAutomationRunner>.Instance);

        await runner.RunCurrentMonthAsync(CancellationToken.None);
        await runner.RunCurrentMonthAsync(CancellationToken.None);

        var accrual = Assert.Single(database.Context.Accruals);
        Assert.Equal(new DateOnly(2026, 8, 1), accrual.AccountingMonth);
        Assert.Equal(500m, accrual.Amount);
        Assert.Contains("Автоматическое ежемесячное формирование", accrual.Comment, StringComparison.Ordinal);
        Assert.Contains(
            database.Context.AuditEvents,
            item => item.Action == "finance.regular_catalog_accruals_generated" && item.ActorUserId == null);
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
    public async Task GenerateFeeCampaignAccrualsAsync_CreatesAccrualsForActiveGaragesAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
        Assert.Equal(fixtures.IncomeType.Id, result.Value.IncomeTypeId);
        Assert.Equal(2, result.Value.CreatedCount);
        Assert.Equal(0, result.Value.SkippedCount);
        Assert.Equal(1000m, result.Value.TotalAmount);
        Assert.All(result.Value.CreatedAccruals, accrual =>
        {
            Assert.Equal(500m, accrual.Amount);
            Assert.Equal("fee_campaign", accrual.Source);
            Assert.Equal(fixtures.IncomeType.Id, accrual.IncomeTypeId);
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
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_UsesConstantSelectCountForManyGarages()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
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
        Assert.InRange(firstRunSelectCount, 1, 4);
        Assert.False(secondRun.Succeeded);
        Assert.Equal("fee_campaign_accruals_empty", secondRun.ErrorCode);
        Assert.InRange(secondRunSelectCount, 1, 3);
        Assert.Equal(200, database.Context.Accruals.Count());
    }

    [Fact]
    public async Task GenerateFeeCampaignAccrualsAsync_RejectsSecondRunForSameMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
    public async Task GenerateFeeCampaignAccrualsAsync_UsesSelectedParticipantGarages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
        Assert.Equal(400m, result.Value!.TotalAmount);
        Assert.Equal(400m, result.Value.CreatedAccruals[0].Amount);
        Assert.Equal("meter_electricity", result.Value.CalculationBase);
        Assert.Contains("пороги электроэнергии до 50 кВт по 2.00, до 100 кВт по 3.00, свыше по 5.00", result.Value.CreatedAccruals[0].Comment, StringComparison.Ordinal);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated");
        Assert.Contains("пороги электроэнергии до 50 кВт по 2.00, до 100 кВт по 3.00, свыше по 5.00", audit.Summary, StringComparison.Ordinal);
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
        await service.CreateAccrualAsync(new CreateAccrualRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 600m, "manual", "Ежемесячная корректировка гаража"), null, CancellationToken.None);

        var result = await service.GetAccrualsAsync(new AccrualListRequest(null, null, "ежемесячная"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].AccountingMonth);
        Assert.Equal(600m, result[0].Amount);
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
        var service = FinanceServiceTestFactory.Create(database.Context);
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
        await service.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 120m, "Ежемесячное электричество"), null, CancellationToken.None);

        var result = await service.GetMeterReadingsAsync(new MeterReadingListRequest(null, null, "electricity", "ежемесячное"), CancellationToken.None);
        var summary = await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, "ежемесячное"), CancellationToken.None);

        var reading = Assert.Single(result);
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
            new CreateAccrualRequest(fixtures.Garage.Id, electricityType.Id, new DateOnly(2026, 6, 1), 5674m, "regular", null),
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
        Assert.True((await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(fixtures.Garage.Id, "electricity", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 118m, null),
            null,
            CancellationToken.None)).Succeeded);

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
        Assert.Equal(118m, electricity.MeterValue);
        Assert.Equal(18m, electricity.MeterConsumption);
        Assert.Equal(5674m, electricity.AccrualAmount);
        Assert.Equal(1000m, electricity.IncomeAmount);
        Assert.Equal(4674m, electricity.Debt);

        var membership = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == fixtures.IncomeType.Id);
        Assert.Equal(0m, membership.AccrualAmount);
        Assert.Equal(500m, membership.IncomeAmount);
        Assert.Equal(0m, membership.Debt);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_UsesOneSelectForGarageAndCombinedWorksheetData()
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
        database.Context.MeterReadings.Add(new MeterReading
        {
            GarageId = fixtures.Garage.Id,
            MeterKind = "electricity",
            AccountingMonth = new DateOnly(2026, 6, 1),
            ReadingDate = new DateOnly(2026, 6, 21),
            PreviousValue = 100m,
            CurrentValue = 118m,
            Consumption = 18m
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        commandCounter.Reset();

        var result = await service.GetGarageIncomeWorksheetAsync(
            fixtures.Garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(fixtures.Garage.Number, result.Value!.GarageNumber);
        Assert.Equal(fixtures.Garage.Owner?.FullName, result.Value.OwnerName);
        Assert.Equal(200m, result.Value!.OpeningDebt);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(electricityType.Id, row.IncomeTypeId);
        Assert.Equal(500m, row.AccrualAmount);
        Assert.Equal(125m, row.IncomeAmount);
        Assert.Equal(118m, row.MeterValue);
        Assert.Equal(18m, row.MeterConsumption);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsEmptyPeriodInOneSelect()
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
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(0m, result.Value!.OpeningDebt);
        Assert.Equal(0m, result.Value.AccrualTotal);
        Assert.Equal(0m, result.Value.IncomeTotal);
        Assert.Equal(0m, result.Value.ClosingDebt);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsFailureForMissingGarageInOneSelect()
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
        Assert.Equal(1, commandCounter.Count);
    }

    [Fact]
    public async Task GetGarageIncomeWorksheetAsync_ReturnsFailureForArchivedGarageInOneSelect()
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
        Assert.Equal(1, commandCounter.Count);
    }

    [Theory]
    [InlineData(2026, 7, 2026, 6, "income_worksheet_period_invalid")]
    [InlineData(2021, 7, 2026, 7, "income_worksheet_period_too_large")]
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
    [InlineData(2021, 7, 2026, 7, "balance_history_period_too_large")]
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
        Assert.Equal(900m, result.Value!.OpeningDebt);
        Assert.Equal(500m, result.Value.AccrualTotal);
        Assert.Equal(100m, result.Value.IncomeTotal);
        Assert.Equal(1300m, result.Value.DebtTotal);
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
        var accrualOnlyType = new ExpenseType { Name = "Охрана", Code = "security" };
        var staffDepartment = new StaffDepartment { Name = "Бухгалтерия" };
        var staffMember = new StaffMember { FullName = "Петрова Ольга", Department = staffDepartment, Rate = 40000m };
        database.Context.AddRange(waterIncomeType, unmatchedIncomeType, salaryExpenseType, expenseOnlyType, accrualOnlyType, staffDepartment, staffMember);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, month, 32000m, "manual", "INV-water", "Счет за воду"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(fixtures.Supplier.Id, accrualOnlyType.Id, month, 75m, "manual", "INV-security", "Счет за охрану"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), month, 10000m, "RKO-water", "Частичная оплата воды"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, new DateOnly(2026, 6, 21), month, 15000m, "RKO-staff", "Частичная зарплата"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseAsync(
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, expenseOnlyType.Id, new DateOnly(2026, 6, 22), month, 100m, "RKO-repair", "Оплата ремонта"),
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
        Assert.Equal(72075m, result.Value.AccrualTotal);
        Assert.Equal(25100m, result.Value.ExpenseTotal);
        Assert.Equal(47075m, result.Value.BalanceTotal);
        Assert.Equal(29000m, result.Value.CollectedTotal);
        Assert.Equal(-43075m, result.Value.DifferenceTotal);
        Assert.Equal(0m, result.Value.CashAmount);
        Assert.Equal(SeededBankAmount - 25100m, result.Value.BankAmount);

        var supplierRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == fixtures.ExpenseType.Id);
        Assert.Equal(fixtures.Supplier.Id, supplierRow.SupplierId);
        Assert.Equal("Vodokanal", supplierRow.CounterpartyName);
        Assert.Equal(fixtures.ExpenseType.Id, supplierRow.ExpenseTypeId);
        Assert.Equal(32000m, supplierRow.AccrualAmount);
        Assert.Equal(10000m, supplierRow.ExpenseAmount);
        Assert.Equal(22000m, supplierRow.Balance);
        Assert.Equal(29000m, supplierRow.CollectedAmount);
        Assert.Equal(-3000m, supplierRow.Difference);

        var expenseOnlyRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == expenseOnlyType.Id);
        Assert.Equal(0m, expenseOnlyRow.AccrualAmount);
        Assert.Equal(100m, expenseOnlyRow.ExpenseAmount);
        Assert.Equal(0m, expenseOnlyRow.Balance);
        Assert.Null(expenseOnlyRow.CollectedAmount);

        var accrualOnlyRow = Assert.Single(result.Value.Rows, row => row.ExpenseTypeId == accrualOnlyType.Id);
        Assert.Equal(75m, accrualOnlyRow.AccrualAmount);
        Assert.Equal(0m, accrualOnlyRow.ExpenseAmount);
        Assert.Equal(75m, accrualOnlyRow.Balance);

        var staffRow = Assert.Single(result.Value.Rows, row => row.RowKind == "staff");
        Assert.Equal(staffMember.Id, staffRow.StaffMemberId);
        Assert.Equal("Петрова Ольга", staffRow.CounterpartyName);
        Assert.Equal(salaryExpenseType.Id, staffRow.ExpenseTypeId);
        Assert.Equal("Зарплата", staffRow.ExpenseTypeName);
        Assert.Equal(40000m, staffRow.AccrualAmount);
        Assert.Equal(15000m, staffRow.ExpenseAmount);
        Assert.Equal(25000m, staffRow.Balance);
        Assert.Null(staffRow.CollectedAmount);
        Assert.Null(staffRow.Difference);
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
        Assert.Equal(360m, result.Value!.OpeningBalanceTotal);
        var firstSupplierWaterRow = Assert.Single(result.Value.Rows, row =>
            row.SupplierId == firstSupplier.Id && row.ExpenseTypeId == waterType.Id);
        Assert.Equal(130m, firstSupplierWaterRow.OpeningBalance);
        Assert.Equal(30m, firstSupplierWaterRow.AccrualAmount);
        Assert.Equal(10m, firstSupplierWaterRow.ExpenseAmount);
        Assert.Equal(-10m, Assert.Single(result.Value.Rows, row =>
            row.SupplierId == firstSupplier.Id && row.ExpenseTypeId == repairType.Id).OpeningBalance);
        Assert.Equal(200m, Assert.Single(result.Value.Rows, row =>
            row.SupplierId == secondSupplier.Id && row.ExpenseTypeId == waterType.Id).OpeningBalance);
        var staffRow = Assert.Single(result.Value.Rows, row => row.StaffMemberId == staffMember.Id);
        Assert.Equal(salaryType.Id, staffRow.ExpenseTypeId);
        Assert.Equal(40m, staffRow.OpeningBalance);
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
    public async Task GetExpenseWorksheetAsync_KeepsCashAndBankEqualCollectedFundsAfterMixedExpenses()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.FundOperations.RemoveRange(database.Context.FundOperations);
        database.Context.Funds.RemoveRange(database.Context.Funds);
        var month = new DateOnly(2026, 6, 1);
        var waterIncomeType = new IncomeType { Name = "Вода", Code = "water" };
        var cashExpenseType = new ExpenseType { Name = "Выплата без чека", Code = "no_receipt" };
        var bankFund = new Fund { Name = "Банк", NormalizedName = "БАНК", Balance = 400m };
        database.Context.AddRange(
            waterIncomeType,
            cashExpenseType,
            bankFund,
            new FundOperation
            {
                Fund = bankFund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 400m,
                BalanceBefore = 0m,
                BalanceAfter = 400m,
                Reason = "Сдача кассы в банк",
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
            new CreateExpenseOperationRequest(fixtures.Supplier.Id, cashExpenseType.Id, new DateOnly(2026, 6, 21), month, 200m, "CASH-reconcile", "Выплата из кассы"),
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
        Assert.Empty(result.StaffOpeningExpenses);
        Assert.Empty(result.Incomes);
        Assert.Equal(0m, result.AvailableBalance.IncomeTotal);
        Assert.Equal(0m, result.AvailableBalance.BankDepositTotal);
        Assert.Equal(0m, result.AvailableBalance.CashExpenseTotal);
        Assert.Equal(0m, result.AvailableBalance.BankExpenseTotal);
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
            var supplier = new Supplier { Name = "Vodokanal", Group = group };
            var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
            var expenseType = new ExpenseType { Name = "Вода", Code = "water" };
            var bankFund = new Fund { Name = "Тестовый банк", NormalizedName = "ТЕСТОВЫЙ БАНК", Balance = SeededBankAmount };
            var bankDeposit = new FundOperation
            {
                Fund = bankFund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = SeededBankAmount,
                BalanceBefore = 0m,
                BalanceAfter = SeededBankAmount,
                Reason = "Тестовая сумма на банковском счете",
                CreatedAtUtc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            Context.AddRange(owner, garage, group, supplier, incomeType, expenseType, bankFund, bankDeposit);
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
}
