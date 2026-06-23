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
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.income_created" && item.ActorUserId == actorUserId);
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
    public async Task GetOperationsAsync_SearchesByGarageSupplierAndDocument()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = new FinanceService(database.Context);
        await service.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.Garage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), 1500m, "DOC-12", null), null, CancellationToken.None);
        await service.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 1), 400m, "DOC-20", null), null, CancellationToken.None);

        var garageResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "12"), CancellationToken.None);
        var supplierResult = await service.GetOperationsAsync(new FinancialOperationListRequest(null, null, null, "vodokanal"), CancellationToken.None);

        Assert.Single(garageResult);
        Assert.Equal("income", garageResult[0].OperationKind);
        Assert.Single(supplierResult);
        Assert.Equal("expense", supplierResult[0].OperationKind);
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
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.accrual_created" && item.ActorUserId == actorUserId);
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
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.supplier_accrual_created" && item.ActorUserId == actorUserId);
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
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.regular_accruals_generated" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GenerateRegularAccrualsAsync_CalculatesMeterAmountFromReading()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.meter_reading_created" && item.ActorUserId == actorUserId);
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
