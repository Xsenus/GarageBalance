using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;

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
        Assert.Equal(2, result.Value.GarageRowCount);
        Assert.Equal(2, result.Value.GarageRows.Count);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "12" && row.Debt == 500m && row.MeterReadingCount == 1);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "21" && row.Debt == 1000m);
    }

    [Fact]
    public async Task GetConsolidatedReportAsync_AppliesGarageRowLimitWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, 1), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3000m, result.Value!.AccrualTotal);
        Assert.Equal(2, result.Value.GarageRowCount);
        var row = Assert.Single(result.Value.GarageRows);
        Assert.Equal("12", row.GarageNumber);
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
    public async Task GetConsolidatedReportAsync_NormalizesReportPeriodToMonthStarts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 23), 2000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 15), null), CancellationToken.None);

        Assert.True(result.Succeeded);
        var month = Assert.Single(result.Value!.MonthlyRows);
        Assert.Equal(new DateOnly(2026, 6, 1), month.AccountingMonth);
        Assert.Equal(2000m, month.AccrualTotal);
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
    public async Task ExportConsolidatedReportXlsxAsync_ReturnsWorkbookWithMonthlyAndGarageRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", "Оплата"), null, CancellationToken.None);

        var result = await service.ExportConsolidatedReportXlsxAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-consolidated-20260601-20260601.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "Месяцы");
        AssertWorkbookContains(result.Value.Content, "Гаражи");
        AssertWorkbookContains(result.Value.Content, "2026-06");
    }

    [Fact]
    public async Task ExportConsolidatedReportPdfAsync_ReturnsDocumentWithMonthlyAndGarageRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", "Оплата"), null, CancellationToken.None);

        var result = await service.ExportConsolidatedReportPdfAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-consolidated-20260601-20260601.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance consolidated report");
        AssertPdfContains(result.Value.Content, "2026-06");
    }

    [Fact]
    public async Task ExportConsolidatedReportXlsxAsync_AppliesGarageSearchFilter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var result = await service.ExportConsolidatedReportXlsxAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "21"), CancellationToken.None);

        Assert.True(result.Succeeded);
        AssertWorkbookContains(result.Value!.Content, "21");
        AssertWorkbookDoesNotContain(result.Value.Content, "12");
    }

    [Fact]
    public async Task ExportConsolidatedReportPdfAsync_AppliesGarageSearchFilter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var result = await service.ExportConsolidatedReportPdfAsync(new ConsolidatedReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "21"), CancellationToken.None);

        Assert.True(result.Succeeded);
        AssertPdfContains(result.Value!.Content, "21 |");
        AssertPdfDoesNotContain(result.Value.Content, "12 |");
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
        Assert.Contains(result.Value.Rows, row => row.RowType == "payments" && row.CreatedAtUtc is not null);
    }

    [Fact]
    public async Task GetIncomeReportAsync_AppliesRowLimitWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 700m, "PKO-2", null), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "payments", 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("PKO-1", row.DocumentNumber);
        Assert.Equal(2200m, result.Value.IncomeTotal);
        Assert.Equal(-2200m, result.Value.Debt);
    }

    [Fact]
    public async Task GetIncomeReportAsync_ReturnsDebtAfterEachPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        var accrual = await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "manual", "Начисление за июнь"), null, CancellationToken.None);
        Assert.True(accrual.Succeeded);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 700m, "PKO-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 300m, "PKO-2", null), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var rows = result.Value!.Rows.OrderBy(row => row.Date).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("PKO-1", rows[0].DocumentNumber);
        Assert.Equal(1300m, rows[0].DebtAfterPayment);
        Assert.Equal("PKO-2", rows[1].DocumentNumber);
        Assert.Equal(1000m, rows[1].DebtAfterPayment);
    }

    [Fact]
    public async Task GetIncomeReportAsync_IncludesGarageStartingBalanceAsDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.FirstGarage.StartingBalance = 750m;
        await database.Context.SaveChangesAsync();
        var service = new ReportService(database.Context);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "accruals"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(750m, result.Value!.AccrualTotal);
        Assert.Equal(750m, result.Value.Debt);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("starting_balance", row.RowType);
        Assert.Equal("Стартовый баланс", row.IncomeTypeName);
        Assert.Equal(750m, row.AccrualAmount);
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

    [Fact]
    public async Task GetExpenseReportAsync_ReturnsPaymentRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], "all"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0m, result.Value!.AccrualTotal);
        Assert.Equal(400m, result.Value.ExpenseTotal);
        Assert.Equal(-400m, result.Value.Difference);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("payments", row.RowType);
        Assert.Equal("Vodokanal", row.SupplierName);
        Assert.Equal("RKO-1", row.DocumentNumber);
        Assert.Equal(400m, row.ExpenseAmount);
    }

    [Fact]
    public async Task GetExpenseReportAsync_AppliesRowLimitWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 1), 300m, "RKO-2", null), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], "payments", 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("RKO-1", row.DocumentNumber);
        Assert.Equal(700m, result.Value.ExpenseTotal);
        Assert.Equal(-700m, result.Value.Difference);
    }

    [Fact]
    public async Task GetExpenseReportAsync_IncludesSupplierStartingBalanceAsObligation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 1200m;
        await database.Context.SaveChangesAsync();
        var service = new ReportService(database.Context);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], "accruals"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1200m, result.Value!.AccrualTotal);
        Assert.Equal(1200m, result.Value.Difference);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("starting_balance", row.RowType);
        Assert.Equal("Стартовый баланс", row.ExpenseTypeName);
        Assert.Equal(1200m, row.AccrualAmount);
    }

    [Fact]
    public async Task GetExpenseReportAsync_FiltersBySupplierExpenseTypeAndSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var secondSupplier = new Supplier { Name = "Siberia Online", GroupId = fixtures.Supplier.GroupId };
        var secondExpenseType = new ExpenseType { Name = "Связь", Code = "internet" };
        database.Context.Suppliers.Add(secondSupplier);
        database.Context.ExpenseTypes.Add(secondExpenseType);
        await database.Context.SaveChangesAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(secondSupplier.Id, secondExpenseType.Id, new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 1), 900m, "RKO-2", null), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "Online",
                [secondSupplier.Id],
                [secondExpenseType.Id],
                "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Rows);
        Assert.Equal("Siberia Online", row.SupplierName);
        Assert.Equal("Связь", row.ExpenseTypeName);
        Assert.Equal(900m, result.Value.ExpenseTotal);
        Assert.Equal(-900m, result.Value.Difference);
    }

    [Fact]
    public async Task GetExpenseReportAsync_ReturnsSupplierAccrualRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", null), null, CancellationToken.None);
        await finance.CreateSupplierAccrualAsync(new CreateSupplierAccrualRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 1), 650m, "manual", "INV-1", "Счет поставщика"), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], "accruals"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(650m, result.Value!.AccrualTotal);
        Assert.Equal(0m, result.Value.ExpenseTotal);
        Assert.Equal(650m, result.Value.Difference);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("accruals", row.RowType);
        Assert.Equal("Vodokanal", row.SupplierName);
        Assert.Equal("INV-1", row.DocumentNumber);
        Assert.Equal(650m, row.AccrualAmount);
    }

    [Fact]
    public async Task GetExpenseReportAsync_ReturnsErrorForInvalidPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], "all"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task GetFundChangeReportAsync_ReturnsFundOperationsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var operatorUser = new AppUser
        {
            Id = actorUserId,
            Email = "funds@example.test",
            NormalizedEmail = "FUNDS@EXAMPLE.TEST",
            DisplayName = "Администратор ГСК",
            PasswordHash = "hash"
        };
        var fund = new Fund { Name = "Электроэнергия", NormalizedName = "ЭЛЕКТРОЭНЕРГИЯ", SortOrder = 10 };
        database.Context.Users.Add(operatorUser);
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.AddRange(
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 1500m,
                BalanceBefore = 0m,
                BalanceAfter = 1500m,
                Reason = "Распределение средств",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Withdraw,
                Amount = 300m,
                BalanceBefore = 1500m,
                BalanceAfter = 1200m,
                Reason = "Оплата счета",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 500m,
                BalanceBefore = 1200m,
                BalanceAfter = 1700m,
                Reason = "Вне периода",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetFundChangeReportAsync(
            new FundChangeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "электро", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1500m, result.Value!.DepositTotal);
        Assert.Equal(300m, result.Value.WithdrawalTotal);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.Contains(result.Value.Rows, row => row.ChangeKind == FundOperationKinds.Deposit && row.ChangeName == "Пополнение" && row.ActorDisplayName == "Администратор ГСК");
        Assert.Contains(result.Value.Rows, row => row.ChangeKind == FundOperationKinds.Withdraw && row.ChangeName == "Изъятие" && row.BalanceAfter == 1200m);
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fund_changes_generated" &&
            auditEvent.EntityId == "fund_changes" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GetCashPaymentReportAsync_ReturnsExpenseRowsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), 800m, "RKO-2", "Вне периода"), null, CancellationToken.None);

        var result = await service.GetCashPaymentReportAsync(
            new CashPaymentReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "вод", 10, actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(400m, result.Value!.Total);
        Assert.Equal(1, result.Value.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(new DateOnly(2026, 6, 12), row.Date);
        Assert.Equal(400m, row.Amount);
        Assert.True(row.HasReceipt);
        Assert.Equal("Вода: Vodokanal", row.Purpose);
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.cash_payments_generated" &&
            auditEvent.EntityId == "cash_payments" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GetBankDepositReportAsync_ReturnsDepositRowsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var fund = new Fund { Name = "Прочее", NormalizedName = "ПРОЧЕЕ", SortOrder = 10 };
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.AddRange(
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 3000m,
                BalanceBefore = 0m,
                BalanceAfter = 3000m,
                Reason = "Сдача наличных в банк",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Withdraw,
                Amount = 1000m,
                BalanceBefore = 3000m,
                BalanceAfter = 2000m,
                Reason = "Не сдача",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 16, 9, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetBankDepositReportAsync(
            new BankDepositReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк", 10, actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3000m, result.Value!.Total);
        Assert.Equal(1, result.Value.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(new DateOnly(2026, 6, 15), row.Date);
        Assert.Equal("Сдача наличных в банк", row.Comment);
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.bank_deposits_generated" &&
            auditEvent.EntityId == "bank_deposits" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GetFeeReportAsync_ReturnsSummaryDebtorsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var firstAccrual = await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"), null, CancellationToken.None);
        var secondAccrual = await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"), null, CancellationToken.None);
        var payment = await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 200m, "PKO-1", "Частичная оплата"), null, CancellationToken.None);
        Assert.True(firstAccrual.Succeeded, firstAccrual.ErrorMessage);
        Assert.True(secondAccrual.Succeeded, secondAccrual.ErrorMessage);
        Assert.True(payment.Succeeded, payment.ErrorMessage);

        var result = await service.GetFeeReportAsync(
            new FeeReportRequest("член", 10, actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("член", result.Value!.Variation);
        Assert.Equal(1000m, result.Value.AccruedTotal);
        Assert.Equal(200m, result.Value.CollectedTotal);
        Assert.Equal(800m, result.Value.DebtTotal);
        var summary = Assert.Single(result.Value.SummaryRows);
        Assert.Equal("Членский взнос", summary.Name);
        Assert.Equal("Членский взнос", summary.Goal);
        Assert.Equal(2, result.Value.GarageRows.Count);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "12" && row.Accrued == 500m && row.Paid == 200m && row.Debt == 300m);
        Assert.Contains(result.Value.GarageRows, row => row.GarageNumber == "21" && row.Accrued == 500m && row.Paid == 0m && row.Debt == 500m);
        Assert.Equal(2, result.Value.DebtorRows.Count);
        Assert.Contains(result.Value.DebtorRows, row => row.GarageNumber == "12" && row.Paid == 200m && row.Debt == 300m);
        Assert.Contains(result.Value.DebtorRows, row => row.GarageNumber == "21" && row.Paid == 0m && row.Debt == 500m);
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fees_generated" &&
            auditEvent.EntityId == "fees" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", "Оплата"), null, CancellationToken.None);

        var result = await service.ExportIncomeReportXlsxAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "PKO-1", [], [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-income-20260601-20260630.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "PKO-1");
        AssertWorkbookContains(result.Value.Content, "Поступления");
    }

    [Fact]
    public async Task ExportExpenseReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);

        var result = await service.ExportExpenseReportXlsxAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "RKO-1", [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-expense-20260601-20260630.xlsx", result.Value!.FileName);
        AssertWorkbookContains(result.Value.Content, "RKO-1");
        AssertWorkbookContains(result.Value.Content, "Выплаты");
    }

    [Fact]
    public async Task ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", "Оплата"), null, CancellationToken.None);

        var result = await service.ExportIncomeReportPdfAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "PKO-1", [], [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-income-20260601-20260630.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance income report");
        AssertPdfContains(result.Value.Content, "PKO-1");
    }

    [Fact]
    public async Task ExportExpenseReportPdfAsync_ReturnsDocumentWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);

        var result = await service.ExportExpenseReportPdfAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "RKO-1", [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-expense-20260601-20260630.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance expense report");
        AssertPdfContains(result.Value.Content, "RKO-1");
    }

    [Fact]
    public async Task ExportCashPaymentReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), 800m, "RKO-2", "Вне периода"), null, CancellationToken.None);

        var result = await service.ExportCashPaymentReportXlsxAsync(
            new CashPaymentReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "RKO-1", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-cash-payments-20260601-20260630.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "Оплаты из кассы");
        AssertWorkbookContains(result.Value.Content, "RKO-1");
        AssertWorkbookContains(result.Value.Content, "Оплата воды");
        AssertWorkbookDoesNotContain(result.Value.Content, "RKO-2");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.cash_payments_exported" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.MetadataJson != null &&
            auditEvent.MetadataJson.Contains("xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportCashPaymentReportPdfAsync_ReturnsDocumentWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = new FinanceService(database.Context);
        var service = new ReportService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);

        var result = await service.ExportCashPaymentReportPdfAsync(
            new CashPaymentReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "RKO-1"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-cash-payments-20260601-20260630.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance cash payments report");
        AssertPdfContains(result.Value.Content, "RKO-1");
    }

    [Fact]
    public async Task ExportBankDepositReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var fund = new Fund { Name = "Прочее", NormalizedName = "ПРОЧЕЕ", SortOrder = 10 };
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.AddRange(
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 3000m,
                BalanceBefore = 0m,
                BalanceAfter = 3000m,
                Reason = "Сдача наличных в банк",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 500m,
                BalanceBefore = 3000m,
                BalanceAfter = 3500m,
                Reason = "Вне периода",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();

        var result = await service.ExportBankDepositReportXlsxAsync(
            new BankDepositReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-bank-deposits-20260601-20260630.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "Сдача кассы в банк");
        AssertWorkbookContains(result.Value.Content, "Сдача наличных в банк");
        AssertWorkbookDoesNotContain(result.Value.Content, "Вне периода");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.bank_deposits_exported" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.MetadataJson != null &&
            auditEvent.MetadataJson.Contains("xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportBankDepositReportPdfAsync_ReturnsDocumentWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var fund = new Fund { Name = "Прочее", NormalizedName = "ПРОЧЕЕ", SortOrder = 10 };
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.Add(new FundOperation
        {
            Fund = fund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = 3000m,
            BalanceBefore = 0m,
            BalanceAfter = 3000m,
            Reason = "Сдача наличных в банк",
            ActorUserId = actorUserId,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)
        });
        await database.Context.SaveChangesAsync();

        var result = await service.ExportBankDepositReportPdfAsync(
            new BankDepositReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-bank-deposits-20260601-20260630.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance bank deposits report");
        AssertPdfContains(result.Value.Content, "2026-06-15");
    }

    [Fact]
    public async Task ExportIncomeReportXlsxAsync_WritesGeneratedAndExportedAuditWithoutRawSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new ReportService(database.Context);
        var actorUserId = Guid.NewGuid();
        var rawSearch = "private@example.com";

        var result = await service.ExportIncomeReportXlsxAsync(
            new IncomeReportRequest(
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                rawSearch,
                [],
                [],
                [],
                "all",
                ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var auditEvents = await database.Context.AuditEvents
            .Where(auditEvent => auditEvent.Action.StartsWith("reports."))
            .ToListAsync();
        auditEvents = auditEvents.OrderBy(auditEvent => auditEvent.CreatedAtUtc).ToList();
        Assert.Collection(
            auditEvents,
            generated =>
            {
                Assert.Equal("reports.income_generated", generated.Action);
                Assert.Equal("reports", generated.Section);
                Assert.Equal("generate", generated.ActionKind);
                Assert.Equal(actorUserId, generated.ActorUserId);
                Assert.Equal("report", generated.EntityType);
                Assert.Equal("income", generated.EntityId);
                Assert.Equal("Отчет по поступлениям", generated.EntityDisplayName);
                Assert.DoesNotContain(rawSearch, generated.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(rawSearch, generated.MetadataJson, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("searchLength", generated.MetadataJson, StringComparison.Ordinal);
            },
            exported =>
            {
                Assert.Equal("reports.income_exported", exported.Action);
                Assert.Equal("export", exported.ActionKind);
                Assert.Equal(actorUserId, exported.ActorUserId);
                Assert.Contains("xlsx", exported.MetadataJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(rawSearch, exported.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(rawSearch, exported.MetadataJson, StringComparison.OrdinalIgnoreCase);
            });
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

    private static void AssertWorkbookContains(byte[] content, string expected)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        var text = string.Join('\n', archive.Entries.Select(ReadEntry));
        Assert.Contains(expected, text);
    }

    private static void AssertWorkbookDoesNotContain(byte[] content, string unexpected)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var text = string.Join('\n', archive.Entries.Select(ReadEntry));
        Assert.DoesNotContain(unexpected, text);
    }

    private static void AssertPdfContains(byte[] content, string expected)
    {
        var text = Encoding.ASCII.GetString(content);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains(expected, text);
        Assert.Contains("%%EOF", text);
    }

    private static void AssertPdfDoesNotContain(byte[] content, string unexpected)
    {
        var text = Encoding.ASCII.GetString(content);
        Assert.DoesNotContain(unexpected, text);
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
