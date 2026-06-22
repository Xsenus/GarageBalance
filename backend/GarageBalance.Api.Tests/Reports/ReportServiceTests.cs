using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task GetConsolidatedReportAsync_ReturnsMonthlyTotalsAndGarageRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "1", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 400m, "2", null), null, CancellationToken.None);
        await finance.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.FirstGarage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null), null, CancellationToken.None);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1500m, result.Value!.IncomeTotal);
        Assert.Equal(400m, result.Value.ExpenseTotal);
        Assert.Equal(3000m, result.Value.AccrualTotal);
        Assert.Equal(1100m, result.Value.Balance);
        Assert.Equal(1500m, result.Value.Debt);
        var month = Assert.Single(result.Value.MonthlyRows);
        Assert.Equal(2, month.OperationCount);
        Assert.Equal(2, month.AccrualCount);
        Assert.Equal(1, month.MeterReadingCount);
        Assert.Equal(2, result.Value.GarageRows.Count);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "12" && row.Debt == 500m && row.MeterReadingCount == 1);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "21" && row.Debt == 1000m);
    }

    [Fact]
    public async Task GetConsolidatedReportAsync_FiltersGarageRowsByOwnerOrGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "Петров"), CancellationToken.None);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.GarageRows);
        Assert.Equal("21", row.GarageNumber);
        Assert.Equal(1000m, row.AccrualTotal);
    }

    [Fact]
    public async Task GetConsolidatedReportAsync_ReturnsErrorForInvalidPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task GetIncomeReportAsync_ReturnsAccrualAndPaymentRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "manual", "Начисление за июнь"), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", "Оплата за июнь"), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2000m, result.Value!.AccrualTotal);
        Assert.Equal(1500m, result.Value.IncomeTotal);
        Assert.Equal(500m, result.Value.Debt);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Contains(result.Value.Rows, row => row.RowType == "accruals" && row.AccrualAmount == 2000m && row.GarageNumber == "12");
        Assert.Contains(result.Value.Rows, row => row.RowType == "payments" && row.IncomeAmount == 1500m && row.DocumentNumber == "PKO-1");
    }

    [Fact]
    public async Task GetIncomeReportAsync_FiltersByOwnerIncomeTypeAndRowMode()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var targetIncomeType = new IncomeType { Name = "Целевой взнос", Code = "target" };
        database.Context.IncomeTypes.Add(targetIncomeType);
        await database.Context.SaveChangesAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, targetIncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 700m, "PKO-2", null), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Петров",
                [],
                [fixtures.SecondGarage.OwnerId!.Value],
                [targetIncomeType.Id],
                "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Rows);
        Assert.Equal("payments", row.RowType);
        Assert.Equal("21", row.GarageNumber);
        Assert.Equal("Целевой взнос", row.IncomeTypeName);
        Assert.Equal(700m, result.Value.IncomeTotal);
        Assert.Equal(-700m, result.Value.Debt);
    }

    [Fact]
    public async Task GetIncomeReportAsync_ReturnsErrorForInvalidPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], [], "all"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
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
            var firstOwner = new Owner { LastName = "Иванов", FirstName = "Иван" };
            var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
            var firstGarage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = firstOwner, InitialWaterMeterValue = 10m, InitialElectricityMeterValue = 100m };
            var secondGarage = new Garage { Number = "21", PeopleCount = 2, FloorCount = 1, Owner = secondOwner, InitialWaterMeterValue = 5m, InitialElectricityMeterValue = 50m };
            var group = new SupplierGroup { Name = "Коммунальные услуги" };
            var supplier = new Supplier { Name = "Vodokanal", Group = group };
            var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
            var expenseType = new ExpenseType { Name = "Вода", Code = "water" };

            Context.AddRange(firstOwner, secondOwner, firstGarage, secondGarage, group, supplier, incomeType, expenseType);
            await Context.SaveChangesAsync();
            return new Fixtures(firstGarage, secondGarage, supplier, incomeType, expenseType);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record Fixtures(Garage FirstGarage, Garage SecondGarage, Supplier Supplier, IncomeType IncomeType, ExpenseType ExpenseType);
}
