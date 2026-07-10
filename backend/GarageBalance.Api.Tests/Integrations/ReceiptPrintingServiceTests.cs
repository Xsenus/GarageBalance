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
        Assert.Equal("Повторная выдача владельцу", adapter.LastRequest!.Reason);
        var audit = Assert.Single(database.Context.AuditEvents);
        Assert.Equal("receipt.reprint_requested", audit.Action);
        Assert.Equal("generate", audit.ActionKind);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("reprint", metadata.RootElement.GetProperty("receiptAction").GetString());
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
        return new ReceiptPrintingService(context, new AuditEventWriter(context), adapter ?? new DisabledReceiptPrintingAdapter());
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
