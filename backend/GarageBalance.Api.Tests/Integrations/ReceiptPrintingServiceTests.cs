using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class ReceiptPrintingServiceTests
{
    [Theory]
    [InlineData(1, "1 позиция")]
    [InlineData(2, "2 позиции")]
    [InlineData(5, "5 позиций")]
    [InlineData(11, "11 позиций")]
    [InlineData(21, "21 позиция")]
    public void FormatReceiptLineCount_UsesRussianPluralForms(int count, string expected)
    {
        Assert.Equal(expected, ReceiptPrintingService.FormatReceiptLineCount(count));
    }

    [Fact]
    public async Task RegisterActionAsync_PrintCreatesAuditEventForIncomeOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        var actorUserId = Guid.NewGuid();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("print", null),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("pending_adapter", result.Value!.Status);
        Assert.Equal("print", result.Value.Action);
        Assert.False(result.Value.IsCopy);
        Assert.Null(result.Value.CopyMark);
        Assert.Equal(operation.Id, result.Value.FinancialOperationId);
        Assert.Equal("PKO-1", result.Value.DocumentNumber);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal(result.Value.AuditEventId, audit.Id);
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("receipt.print_requested", audit.Action);
        Assert.Equal("generate", audit.ActionKind);
        Assert.Equal("integrations", audit.Section);
        Assert.Equal("receipt_printing", audit.EntityType);
        Assert.Equal(operation.Id.ToString(), audit.EntityId);
        Assert.Equal(operation.Id.ToString(), audit.RelatedDocumentId);
        Assert.Equal("PKO-1", audit.RelatedDocumentNumber);
        Assert.Equal("1", audit.RelatedGarageNumber);
        Assert.Equal("Иванов Иван", audit.RelatedCounterpartyName);
        Assert.Contains("pending_adapter", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("adapterMessage", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterActionAsync_PrintBuildsOneReceiptForPaymentBatchAndItsAllocations()
    {
        await using var database = await TestDatabase.CreateAsync();
        var batchId = Guid.NewGuid();
        var firstOperation = await SeedIncomeOperationAsync(database.Context);
        firstOperation.ReceiptBatchId = batchId;
        var waterIncomeType = new IncomeType { Name = "Водоснабжение", Code = "water" };
        var secondOperation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = firstOperation.OperationDate,
            AccountingMonth = firstOperation.AccountingMonth,
            Amount = 500m,
            ReceiptBatchId = batchId,
            GarageId = firstOperation.GarageId,
            Garage = firstOperation.Garage,
            IncomeType = waterIncomeType
        };
        var memberFeeAccrual = new Accrual
        {
            GarageId = firstOperation.GarageId!.Value,
            Garage = firstOperation.Garage!,
            IncomeTypeId = firstOperation.IncomeTypeId!.Value,
            IncomeType = firstOperation.IncomeType!,
            AccountingMonth = firstOperation.AccountingMonth,
            DueDate = new DateOnly(2026, 8, 10),
            OverdueFromDate = new DateOnly(2026, 8, 11),
            Amount = 1500m,
            Source = "regular"
        };
        var waterAccrual = new Accrual
        {
            GarageId = firstOperation.GarageId.Value,
            Garage = firstOperation.Garage!,
            IncomeType = waterIncomeType,
            AccountingMonth = secondOperation.AccountingMonth,
            DueDate = new DateOnly(2026, 8, 10),
            OverdueFromDate = new DateOnly(2026, 8, 11),
            Amount = 500m,
            Source = "meter"
        };
        database.Context.AddRange(secondOperation, memberFeeAccrual, waterAccrual);
        database.Context.AccrualPaymentAllocations.AddRange(
            new AccrualPaymentAllocation
            {
                FinancialOperation = firstOperation,
                Accrual = memberFeeAccrual,
                Amount = 1500m
            },
            new AccrualPaymentAllocation
            {
                FinancialOperation = secondOperation,
                Accrual = waterAccrual,
                Amount = 500m
            });
        await database.Context.SaveChangesAsync();
        var adapter = new FakeReceiptPrintingAdapter(ReceiptPrintingAdapterResult.Printed("Единая квитанция отправлена на печать."));
        var service = CreateService(database.Context, adapter);

        var result = await service.RegisterActionAsync(
            secondOperation.Id,
            new ReceiptPrintingActionRequest("print", null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(batchId, result.Value!.ReceiptBatchId);
        Assert.Equal(2000m, result.Value.TotalAmount);
        Assert.Equal(2, result.Value.LineCount);
        Assert.Equal($"ПАКЕТ-{batchId:N}", result.Value.DocumentNumber);
        Assert.Equal(batchId, adapter.LastRequest!.ReceiptBatchId);
        Assert.Equal(2000m, adapter.LastRequest.Amount);
        Assert.Equal("Несколько услуг", adapter.LastRequest.IncomeTypeName);
        Assert.Collection(
            adapter.LastRequest.Lines!.OrderBy(item => item.IncomeTypeName),
            line =>
            {
                Assert.Equal("Водоснабжение", line.IncomeTypeName);
                Assert.Equal(500m, line.Amount);
                Assert.Equal(500m, Assert.Single(line.Allocations).Amount);
            },
            line =>
            {
                Assert.Equal("Членский взнос", line.IncomeTypeName);
                Assert.Equal(1500m, line.Amount);
                Assert.Equal(1500m, Assert.Single(line.Allocations).Amount);
            });
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal(batchId.ToString(), audit.EntityId);
        Assert.Equal(batchId.ToString(), audit.RelatedDocumentId);
        Assert.Contains("единая квитанция", audit.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 позиции", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("2 000.00", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(batchId.ToString(), metadata.RootElement.GetProperty("receiptBatchId").GetString());
        Assert.Equal("2", metadata.RootElement.GetProperty("lineCount").GetString());
        Assert.Equal("2", metadata.RootElement.GetProperty("allocationCount").GetString());
    }

    [Fact]
    public async Task RegisterActionAsync_CancelCreatesAuditEventWithReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("cancel", "Квитанция испорчена"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("cancel", result.Value!.Action);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal("receipt.print_canceled", audit.Action);
        Assert.Equal("cancel", audit.ActionKind);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Квитанция испорчена", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RegisterActionAsync_ReprintCreatesAuditEventWithReasonAndExternalReceiptId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        var adapter = new FakeReceiptPrintingAdapter(ReceiptPrintingAdapterResult.Printed(
            "Копия квитанции отправлена на печать.",
            deviceResponseCode: "OK",
            externalReceiptId: "receipt-copy-42"));
        var service = CreateService(database.Context, adapter);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("reprint", "Повторная выдача владельцу"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("reprint", result.Value!.Action);
        Assert.Equal("printed", result.Value.Status);
        Assert.True(result.Value.IsCopy);
        Assert.Equal("КОПИЯ", result.Value.CopyMark);
        Assert.Equal("Повторная выдача владельцу", adapter.LastRequest!.Reason);
        Assert.True(adapter.LastRequest.IsCopy);
        Assert.Equal("КОПИЯ", adapter.LastRequest.CopyMark);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal("receipt.reprint_requested", audit.Action);
        Assert.Equal("generate", audit.ActionKind);
        Assert.Equal("Копия квитанции PKO-1", audit.EntityDisplayName);
        Assert.Contains("Повторная печать копии", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("reprint", metadata.RootElement.GetProperty("receiptAction").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("isCopy").GetString());
        Assert.Equal("КОПИЯ", metadata.RootElement.GetProperty("copyMark").GetString());
        Assert.Equal("printed", metadata.RootElement.GetProperty("adapterStatus").GetString());
        Assert.Equal("OK", metadata.RootElement.GetProperty("deviceResponseCode").GetString());
        Assert.Equal("receipt-copy-42", metadata.RootElement.GetProperty("externalReceiptId").GetString());
        Assert.Equal("Повторная выдача владельцу", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RegisterActionAsync_WritesAdapterStatusAndSafeErrorDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        var adapter = new FakeReceiptPrintingAdapter(new ReceiptPrintingAdapterResult(
            "device_error",
            "Печатающее устройство недоступно.",
            DeviceResponseCode: "NO_CONNECTION"));
        var service = CreateService(database.Context, adapter);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("print", null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("device_error", result.Value!.Status);
        Assert.Equal("Печатающее устройство недоступно.", result.Value.StatusMessage);
        Assert.Equal("print", adapter.LastRequest!.Action);
        Assert.Equal(operation.Id, adapter.LastRequest.FinancialOperationId);
        Assert.Equal("PKO-1", adapter.LastRequest.DocumentNumber);
        Assert.Equal(1500m, adapter.LastRequest.Amount);
        var audit = Assert.Single(database.Context.AuditEvents);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("device_error", metadata.RootElement.GetProperty("adapterStatus").GetString());
        Assert.Equal("NO_CONNECTION", metadata.RootElement.GetProperty("deviceResponseCode").GetString());
        Assert.Equal("Печатающее устройство недоступно.", metadata.RootElement.GetProperty("adapterMessage").GetString());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("reprint")]
    public async Task RegisterActionAsync_CancelAndReprintRequireReason(string action)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest(action, " "),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("receipt_print_reason_required", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RegisterActionAsync_RejectsExpenseOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 7, 9),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 500m
        };
        database.Context.FinancialOperations.Add(operation);
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("print", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("receipt_print_income_required", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RegisterActionAsync_RejectsMixedReceiptBatch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var batchId = Guid.NewGuid();
        var incomeOperation = await SeedIncomeOperationAsync(database.Context);
        incomeOperation.ReceiptBatchId = batchId;
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = incomeOperation.OperationDate,
            AccountingMonth = incomeOperation.AccountingMonth,
            Amount = 100m,
            ReceiptBatchId = batchId
        });
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            incomeOperation.Id,
            new ReceiptPrintingActionRequest("print", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("receipt_print_batch_invalid", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RegisterActionAsync_RejectsReceiptBatchAboveBoundedLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var batchId = Guid.NewGuid();
        var anchor = await SeedIncomeOperationAsync(database.Context);
        anchor.ReceiptBatchId = batchId;
        database.Context.FinancialOperations.AddRange(Enumerable.Range(0, ReceiptPrintingLimits.MaximumLineCount).Select(index => new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = anchor.OperationDate,
            AccountingMonth = anchor.AccountingMonth,
            Amount = 1m,
            ReceiptBatchId = batchId,
            GarageId = anchor.GarageId,
            Garage = anchor.Garage,
            IncomeTypeId = anchor.IncomeTypeId,
            IncomeType = anchor.IncomeType,
            Comment = $"Позиция {index + 2}"
        }));
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            anchor.Id,
            new ReceiptPrintingActionRequest("print", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("receipt_print_batch_too_large", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RegisterActionAsync_RejectsPrintForCanceledIncomeOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = await SeedIncomeOperationAsync(database.Context);
        operation.IsCanceled = true;
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            operation.Id,
            new ReceiptPrintingActionRequest("print", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("receipt_print_operation_canceled", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task RegisterActionAsync_RejectsUnknownOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.RegisterActionAsync(
            Guid.NewGuid(),
            new ReceiptPrintingActionRequest("print", null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("financial_operation_not_found", result.ErrorCode);
    }

    private static ReceiptPrintingService CreateService(GarageBalanceDbContext context, IReceiptPrintingAdapter? adapter = null)
    {
        return new ReceiptPrintingService(
            new EfReceiptPrintingRepository(context),
            new AuditEventWriter(context),
            adapter ?? new DisabledReceiptPrintingAdapter());
    }

    private static async Task<FinancialOperation> SeedIncomeOperationAsync(GarageBalanceDbContext context)
    {
        var owner = new Owner
        {
            LastName = "Иванов",
            FirstName = "Иван"
        };
        var garage = new Garage
        {
            Number = "1",
            PeopleCount = 3,
            FloorCount = 1,
            Owner = owner
        };
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "member_fee" };
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 9),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 1500m,
            DocumentNumber = "PKO-1",
            Garage = garage,
            IncomeType = incomeType
        };
        context.FinancialOperations.Add(operation);
        await context.SaveChangesAsync();
        return operation;
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

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeReceiptPrintingAdapter(ReceiptPrintingAdapterResult result) : IReceiptPrintingAdapter
    {
        public ReceiptPrintingAdapterRequest? LastRequest { get; private set; }

        public Task<ReceiptPrintingAdapterResult> ProcessAsync(ReceiptPrintingAdapterRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
