using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.IO.Compression;
using System.Text;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportServiceTests
{
    private const decimal SeededBankAmount = 1000000m;

    [Fact]
    public async Task GetConsolidatedReportAsync_ReturnsMonthlyTotalsAndGarageRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var incomeBreakdown = Assert.Single(result.Value.IncomeBreakdown);
        Assert.Equal(fixtures.IncomeType.Id, incomeBreakdown.TypeId);
        Assert.Equal("Членский взнос", incomeBreakdown.Name);
        Assert.Equal(1500m, incomeBreakdown.Amount);
        var expenseBreakdown = Assert.Single(result.Value.ExpenseBreakdown);
        Assert.Equal(fixtures.ExpenseType.Id, expenseBreakdown.TypeId);
        Assert.Equal("Вода", expenseBreakdown.Name);
        Assert.Equal(400m, expenseBreakdown.Amount);
    }

    [Fact]
    public async Task GetConsolidatedReportAsync_ReturnsCompleteServerBreakdownsBeyondLegacyPageLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var month = new DateOnly(2026, 6, 1);
        var operations = Enumerable.Range(0, 600)
            .SelectMany(index => new[]
            {
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = month.AddDays(index % 28),
                    AccountingMonth = month,
                    Amount = 10m,
                    IncomeTypeId = fixtures.IncomeType.Id
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Expense,
                    OperationDate = month.AddDays(index % 28),
                    AccountingMonth = month,
                    Amount = 4m,
                    ExpenseTypeId = fixtures.ExpenseType.Id
                }
            })
            .ToList();
        database.Context.FinancialOperations.AddRange(operations);
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

        var result = await service.GetConsolidatedReportAsync(
            new ConsolidatedReportRequest(month, month, null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(6000m, result.Value!.IncomeTotal);
        Assert.Equal(2400m, result.Value.ExpenseTotal);
        Assert.Equal(1200, result.Value.OperationCount);
        Assert.Equal(6000m, Assert.Single(result.Value.IncomeBreakdown).Amount);
        Assert.Equal(2400m, Assert.Single(result.Value.ExpenseBreakdown).Amount);
    }

    [Fact]
    public async Task ConsolidatedMonthlyQuery_LoadsCompleteAggregatesInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.FirstGarage.StartingBalance = 125m;
        await database.Context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "IN-FAST", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 400m, "OUT-FAST", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1800m, "regular", null), null, CancellationToken.None);
        await finance.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.FirstGarage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null), null, CancellationToken.None);
        commandCounter.Reset();

        var result = await new EfConsolidatedMonthlyReportQuery(database.Context).GetMonthlyDataAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            CancellationToken.None);

        Assert.Equal(1500m, Assert.Single(result.IncomeBreakdown).Amount);
        Assert.Equal(400m, Assert.Single(result.ExpenseBreakdown).Amount);
        Assert.Equal(1800m, Assert.Single(result.AccrualByMonth).Amount);
        Assert.Equal(1, Assert.Single(result.MeterReadingsByMonth).Count);
        Assert.Equal(125m, result.GarageStartingBalanceTotal);
        Assert.Equal(1, commandCounter.Count);
    }

    [Fact]
    public async Task ConsolidatedMonthlyQuery_ReturnsEmptyDataInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfConsolidatedMonthlyReportQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetMonthlyDataAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Empty(result.IncomeByMonth);
        Assert.Empty(result.ExpenseByMonth);
        Assert.Empty(result.AccrualByMonth);
        Assert.Empty(result.MeterReadingsByMonth);
        Assert.Equal(0m, result.GarageStartingBalanceTotal);
        Assert.Empty(result.IncomeBreakdown);
        Assert.Empty(result.ExpenseBreakdown);
    }

    [Fact]
    public async Task ConsolidatedMonthlyQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfConsolidatedMonthlyReportQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetMonthlyDataAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            cancellationSource.Token));
    }

    [Fact]
    public async Task GetConsolidatedReportAsync_AppliesGarageRowLimitWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var service = CreateService(database.Context);

        var result = await service.GetConsolidatedReportAsync(new ConsolidatedReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task GetGarageReportAsync_AppliesPageAfterAggregationWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1200m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 800m, "manual", "Корректировка"), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "1", null), null, CancellationToken.None);
        var actorUserId = Guid.NewGuid();

        var result = await service.GetGarageReportAsync(
            new GarageReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, false, 1, 1, actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3000m, result.Value!.AccrualTotal);
        Assert.Equal(1500m, result.Value.IncomeTotal);
        Assert.Equal(1500m, result.Value.Difference);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("21", row.GarageNumber);
        Assert.Equal(1000m, row.AccrualAmount);
        Assert.Equal(0m, row.IncomeAmount);
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.garages_generated" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.RelatedAccountingMonth == "2026-06");
    }

    [Fact]
    public async Task GetGarageReportAsync_GroupsServicesBeforeCountingAndPaging()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.FirstGarage.StartingBalance = 100m;
        await database.Context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "1", null), null, CancellationToken.None);

        var result = await service.GetGarageReportAsync(
            new GarageReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), null, true, 25, 0),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("ИТОГО", row.IncomeTypeName);
        Assert.Null(row.IncomeTypeId);
        Assert.Equal(2100m, row.AccrualAmount);
        Assert.Equal(1500m, row.IncomeAmount);
        Assert.Equal(600m, row.Difference);
    }

    [Fact]
    public async Task GetGarageReportAsync_AppliesGarageSearchBeforeTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "regular", null), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 1000m, "regular", null), null, CancellationToken.None);

        var result = await service.GetGarageReportAsync(
            new GarageReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "Петров Петр", false, 25, 0),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1000m, result.Value!.AccrualTotal);
        Assert.Equal(1, result.Value.RowCount);
        Assert.Equal("21", Assert.Single(result.Value.Rows).GarageNumber);
    }

    [Fact]
    public async Task GetGarageReportAsync_ReturnsErrorForInvalidPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.GetGarageReportAsync(
            new GarageReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 1), null, false),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task GarageReportQuery_LoadsTotalsCountAndPageInTwoSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 900m, "regular", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 400m, "GARAGE-FAST", null), null, CancellationToken.None);
        commandCounter.Reset();

        var result = await new EfGarageReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            null,
            false,
            0,
            25,
            CancellationToken.None);

        Assert.Equal(900m, result.AccrualTotal);
        Assert.Equal(400m, result.IncomeTotal);
        Assert.NotEmpty(result.Rows);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task GarageReportQuery_ReturnsEmptyDataInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfGarageReportQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            null,
            false,
            0,
            25,
            CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(0m, result.AccrualTotal);
        Assert.Equal(0m, result.IncomeTotal);
        Assert.Equal(0, result.RowCount);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task GarageReportQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfGarageReportQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            null,
            false,
            0,
            25,
            cancellationSource.Token));
    }

    [Fact]
    public async Task ConsolidatedGarageSearch_LoadsIdentitiesAndAllAggregatesInTwoSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 700m, "regular", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 300m, "SEARCH-FAST", null), null, CancellationToken.None);
        await finance.CreateMeterReadingAsync(new CreateMeterReadingRequest(fixtures.SecondGarage.Id, "water", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 12m, null), null, CancellationToken.None);
        commandCounter.Reset();

        var result = await new EfConsolidatedGarageReportQuery(database.Context).GetGarageRowsAsync(
            fixtures.SecondGarage.Number,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            25,
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(300m, row.IncomeTotal);
        Assert.Equal(700m + fixtures.SecondGarage.StartingBalance, row.AccrualTotal);
        Assert.Equal(1, row.MeterReadingCount);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task GetGarageReportAsync_ReturnsEmptyPageForPeriodWithoutData()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.SeedAsync();
        var service = CreateService(database.Context);

        var result = await service.GetGarageReportAsync(
            new GarageReportRequest(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), null, false),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0m, result.Value!.AccrualTotal);
        Assert.Equal(0m, result.Value.IncomeTotal);
        Assert.Equal(0, result.Value.RowCount);
        Assert.Empty(result.Value.Rows);
        Assert.Equal(0, result.Value.Offset);
        Assert.Equal(25, result.Value.Limit);
    }

    [Fact]
    public async Task ExportConsolidatedReportXlsxAsync_ReturnsWorkbookWithMonthlyAndGarageRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
    public async Task GetIncomeReportAsync_AppliesPageWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "PKO-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 700m, "PKO-2", null), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "payments", 1, Offset: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("PKO-1", row.DocumentNumber);
        Assert.Equal(2200m, result.Value.IncomeTotal);
        Assert.Equal(-2200m, result.Value.Debt);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
    }

    [Fact]
    public async Task GetIncomeReportAsync_AppliesSearchBeforePageAndTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 1500m, "MATCH-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 700m, "MATCH-2", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 900m, "OTHER", null), null, CancellationToken.None);

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "MATCH", [], [], [], "payments", 1, Offset: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        Assert.Equal(2200m, result.Value.IncomeTotal);
        Assert.Equal("MATCH-1", Assert.Single(result.Value.Rows).DocumentNumber);
    }

    [Fact]
    public async Task GetIncomeReportAsync_ReturnsDebtAfterEachPaymentInThreeSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        var accrual = await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 2000m, "manual", "Начисление за июнь"), null, CancellationToken.None);
        Assert.True(accrual.Succeeded);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 700m, "PKO-1", null), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 300m, "PKO-2", null), null, CancellationToken.None);
        commandCounter.Reset();

        var result = await service.GetIncomeReportAsync(
            new IncomeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], [], "payments"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, commandCounter.Count);
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
        var service = CreateService(database.Context);

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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var service = CreateService(database.Context);

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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
    public async Task GetExpenseReportAsync_AppliesPageWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 1), 300m, "RKO-2", null), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, [], [], "payments", 1, 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("RKO-1", row.DocumentNumber);
        Assert.Equal(700m, result.Value.ExpenseTotal);
        Assert.Equal(-700m, result.Value.Difference);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
    }

    [Fact]
    public async Task GetExpenseReportAsync_AppliesSearchBeforePageAndTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "MATCH-1", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 1), 300m, "MATCH-2", null), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 14), new DateOnly(2026, 6, 1), 200m, "OTHER", null), null, CancellationToken.None);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "MATCH", [], [], "payments", 1, 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.RowCount);
        Assert.Equal(700m, result.Value.ExpenseTotal);
        Assert.Equal("MATCH-1", Assert.Single(result.Value.Rows).DocumentNumber);
    }

    [Fact]
    public async Task GetExpenseReportAsync_IncludesSupplierStartingBalanceAsObligation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 1200m;
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);

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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var service = CreateService(database.Context);

        var result = await service.GetExpenseReportAsync(
            new ExpenseReportRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), null, [], [], "all"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("period_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task IncomeAccrualQuery_LoadsTotalsAndPageInTwoSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = fixtures.FirstGarage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 125m,
            Source = "manual"
        });
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfIncomeReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "accruals",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { fixtures.IncomeType.Id },
            null,
            25,
            0,
            CancellationToken.None);

        Assert.Equal(125m, result.AccrualTotal);
        Assert.Equal(1, result.RowCount);
        Assert.Single(result.Rows);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task ConsolidatedGarageQuery_BoundsTwoHundredRowsBeforeMaterialization()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        database.Context.Garages.AddRange(Enumerable.Range(1, 200).Select(index => new Garage
        {
            Number = index.ToString("D4"),
            StartingBalance = index
        }));
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfConsolidatedGarageReportQuery(database.Context).GetGarageRowsAsync(
            null,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 1),
            25,
            CancellationToken.None);

        Assert.Equal(2, commandCounter.Count);
        Assert.Equal(200, result.RowCount);
        Assert.Equal(25, result.Rows.Count);
        Assert.Equal("0001", result.Rows[0].GarageNumber);
        Assert.Equal(1m, result.Rows[0].AccrualTotal);
        Assert.Equal("0025", result.Rows[^1].GarageNumber);
        Assert.Equal(25m, result.Rows[^1].AccrualTotal);
    }

    [Fact]
    public async Task IncomePaymentQuery_LoadsPageAndDebtsInThreeSelectsRegardlessOfRowCount()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var month = new DateOnly(2026, 6, 1);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = fixtures.FirstGarage.Id,
            IncomeTypeId = fixtures.IncomeType.Id,
            AccountingMonth = month,
            Amount = 5000m,
            Source = "manual"
        });
        for (var index = 0; index < 25; index++)
        {
            database.Context.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, index + 1),
                AccountingMonth = month,
                Amount = 100m,
                DocumentNumber = $"PKO-PERF-{index:00}",
                GarageId = fixtures.FirstGarage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                CreatedAtUtc = new DateTimeOffset(2026, 6, index + 1, 12, 0, 0, TimeSpan.Zero)
            });
        }

        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfIncomeReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            CancellationToken.None);

        Assert.Equal(3, commandCounter.Count);
        Assert.Equal(25, result.RowCount);
        Assert.Equal(25, result.Rows.Count);
        Assert.Equal(4900m, result.Rows[0].DebtAfterPayment);
        Assert.Equal(2500m, result.Rows[^1].DebtAfterPayment);
    }

    [Fact]
    public async Task IncomeAllRowsQuery_LoadsThreeSectionsDebtAndCombinedTotalsInFiveSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.FirstGarage.StartingBalance = 100m;
        for (var index = 0; index < 200; index++)
        {
            database.Context.Accruals.Add(new Accrual
            {
                GarageId = fixtures.FirstGarage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 50m,
                Source = $"performance-test-{index:000}"
            });
            database.Context.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 15),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 20m,
                DocumentNumber = $"PKO-ALL-{index:000}",
                GarageId = fixtures.FirstGarage.Id,
                IncomeTypeId = fixtures.IncomeType.Id,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(index)
            });
        }

        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfIncomeReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            CancellationToken.None);

        Assert.Equal(5, commandCounter.Count);
        Assert.Equal(10100m, result.AccrualTotal);
        Assert.Equal(4000m, result.IncomeTotal);
        Assert.Equal(401, result.RowCount);
        Assert.Equal(25, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(fixtures.FirstGarage.Id, row.GarageId));
    }

    [Fact]
    public async Task ExpenseAccrualQuery_LoadsTotalsAndPageInTwoSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        database.Context.SupplierAccruals.Add(new SupplierAccrual
        {
            SupplierId = fixtures.Supplier.Id,
            ExpenseTypeId = fixtures.ExpenseType.Id,
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 275m,
            Source = "manual"
        });
        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfExpenseReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "accruals",
            new HashSet<Guid> { fixtures.Supplier.Id },
            new HashSet<Guid> { fixtures.ExpenseType.Id },
            null,
            25,
            0,
            CancellationToken.None);

        Assert.Equal(275m, result.AccrualTotal);
        Assert.Equal(1, result.RowCount);
        Assert.Single(result.Rows);
        Assert.Equal(2, commandCounter.Count);
    }

    [Fact]
    public async Task ExpenseAllRowsQuery_LoadsThreeSectionsAndCombinedTotalsInFourSelects()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        fixtures.Supplier.StartingBalance = 100m;
        for (var index = 0; index < 200; index++)
        {
            database.Context.SupplierAccruals.Add(new SupplierAccrual
            {
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id,
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 50m,
                Source = "performance-test",
                DocumentNumber = $"INV-PERF-{index:000}"
            });
            database.Context.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 15),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 20m,
                DocumentNumber = $"RKO-PERF-{index:000}",
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id
            });
        }

        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await new EfExpenseReportQuery(database.Context).GetRowsAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            CancellationToken.None);

        Assert.Equal(4, commandCounter.Count);
        Assert.Equal(10100m, result.AccrualTotal);
        Assert.Equal(4000m, result.ExpenseTotal);
        Assert.Equal(401, result.RowCount);
        Assert.Equal(25, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(fixtures.Supplier.Id, row.SupplierId));
    }

    [Fact]
    public async Task GetFundChangeReportAsync_ReturnsPagedFundOperationsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
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
                OperationKind = FundOperationKinds.Deposit,
                Amount = 700m,
                BalanceBefore = 1500m,
                BalanceAfter = 2200m,
                Reason = "Canceled fund operation",
                ActorUserId = actorUserId,
                IsCanceled = true,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero)
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
            new FundChangeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "электро", Limit: 1, Offset: 1, ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1500m, result.Value!.DepositTotal);
        Assert.Equal(300m, result.Value.WithdrawalTotal);
        Assert.Equal(2, result.Value.RowCount);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(FundOperationKinds.Deposit, row.ChangeKind);
        Assert.Equal("Пополнение", row.ChangeName);
        Assert.Equal(1500m, row.BalanceAfter);
        Assert.Equal("Администратор ГСК", row.ActorDisplayName);
        Assert.DoesNotContain(result.Value.Rows, row => row.Reason == "Canceled fund operation");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fund_changes_generated" &&
            auditEvent.EntityId == "fund_changes" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task FundChangeQuery_LoadsPagedRowsAndActorsInOneSqliteSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var actor = new AppUser
        {
            Email = "fund-query@example.test",
            NormalizedEmail = "FUND-QUERY@EXAMPLE.TEST",
            DisplayName = "Fund operator",
            PasswordHash = "hash"
        };
        var fund = new Fund { Name = "Reserve fund", NormalizedName = "RESERVE FUND", SortOrder = 10 };
        database.Context.Users.Add(actor);
        database.Context.Funds.Add(fund);
        var createdAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        database.Context.FundOperations.AddRange(Enumerable.Range(0, 200).Select(index => new FundOperation
        {
            Fund = fund,
            OperationKind = index % 2 == 0 ? FundOperationKinds.Deposit : FundOperationKinds.Withdraw,
            Amount = 10m,
            BalanceBefore = index * 10m,
            BalanceAfter = (index + 1) * 10m,
            Reason = $"Operation {index}",
            ActorUserId = index == 0 ? null : actor.Id,
            CreatedAtUtc = createdAt.AddMinutes(index)
        }));
        await database.Context.SaveChangesAsync();

        commandCounter.Reset();
        var result = await new EfFundChangeReportQuery(database.Context).GetFundChangesAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            null,
            0,
            25,
            CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(200, result.RowCount);
        Assert.Equal(1000m, result.DepositTotal);
        Assert.Equal(1000m, result.WithdrawalTotal);
        Assert.Equal(25, result.Rows.Count);
        var rowWithoutActor = result.Rows[0];
        Assert.Null(rowWithoutActor.ActorUserId);
        Assert.Null(rowWithoutActor.ActorDisplayName);
        Assert.All(result.Rows.Skip(1), row =>
        {
            Assert.Equal(fund.Id, row.FundId);
            Assert.Equal(fund.Name, row.FundName);
            Assert.Equal(actor.Id, row.ActorUserId);
            Assert.Equal(actor.DisplayName, row.ActorDisplayName);
        });
    }

    [Fact]
    public async Task ExportFundChangeReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var operatorUser = new AppUser
        {
            Id = actorUserId,
            Email = "funds-export@example.test",
            NormalizedEmail = "FUNDS-EXPORT@EXAMPLE.TEST",
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

        var result = await service.ExportFundChangeReportXlsxAsync(
            new FundChangeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "электро", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-fund-changes-20260601-20260630.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "Изменение фондов");
        AssertWorkbookContains(result.Value.Content, "Пополнение");
        AssertWorkbookContains(result.Value.Content, "Изъятие");
        AssertWorkbookContains(result.Value.Content, "Распределение средств");
        AssertWorkbookDoesNotContain(result.Value.Content, "Вне периода");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fund_changes_exported" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.MetadataJson != null &&
            auditEvent.MetadataJson.Contains("xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportFundChangeReportPdfAsync_ReturnsDocumentWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        var fund = new Fund { Name = "Электроэнергия", NormalizedName = "ЭЛЕКТРОЭНЕРГИЯ", SortOrder = 10 };
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.Add(new FundOperation
        {
            Fund = fund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = 1500m,
            BalanceBefore = 0m,
            BalanceAfter = 1500m,
            Reason = "Распределение средств",
            ActorUserId = actorUserId,
            CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero)
        });
        await database.Context.SaveChangesAsync();

        var result = await service.ExportFundChangeReportPdfAsync(
            new FundChangeReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "электро", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-fund-changes-20260601-20260630.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance fund changes report");
        AssertPdfContains(result.Value.Content, "2026-06-10");
        AssertPdfContains(result.Value.Content, "1 500.00");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fund_changes_exported" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.MetadataJson != null &&
            auditEvent.MetadataJson.Contains("pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCashPaymentReportAsync_ReturnsExpenseRowsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 1), 400m, "RKO-1", "Оплата воды"), null, CancellationToken.None);
        await finance.CreateExpenseAsync(new CreateExpenseOperationRequest(fixtures.Supplier.Id, fixtures.ExpenseType.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), 800m, "RKO-2", "Вне периода"), null, CancellationToken.None);

        var result = await service.GetCashPaymentReportAsync(
            new CashPaymentReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "вод", 10, ActorUserId: actorUserId),
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
    public async Task GetCashPaymentReportAsync_AppliesPageWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = CreateService(database.Context);
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 10),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 100m,
                DocumentNumber = "RKO-LIMIT-1",
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 11),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 200m,
                DocumentNumber = "RKO-LIMIT-2",
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 12),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 300m,
                DocumentNumber = "RKO-LIMIT-3",
                SupplierId = fixtures.Supplier.Id,
                ExpenseTypeId = fixtures.ExpenseType.Id
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetCashPaymentReportAsync(
            new CashPaymentReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Search: null, Limit: 1, Offset: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(600m, result.Value!.Total);
        Assert.Equal(3, result.Value.RowCount);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal("RKO-LIMIT-2", row.DocumentNumber);
    }

    [Fact]
    public async Task GetBankDepositReportAsync_ReturnsDepositRowsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
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
                IsCashToBankTransfer = true,
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 900m,
                BalanceBefore = 3000m,
                BalanceAfter = 3900m,
                Reason = "Canceled bank deposit",
                IsCashToBankTransfer = true,
                ActorUserId = actorUserId,
                IsCanceled = true,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)
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
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 500m,
                BalanceBefore = 2000m,
                BalanceAfter = 2500m,
                Reason = "Обычное распределение фонда",
                ActorUserId = actorUserId,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetBankDepositReportAsync(
            new BankDepositReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), "банк", 10, ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3000m, result.Value!.Total);
        Assert.Equal(1, result.Value.RowCount);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(new DateOnly(2026, 6, 15), row.Date);
        Assert.Equal("Сдача наличных в банк", row.Comment);
        Assert.DoesNotContain(result.Value.Rows, item => item.Comment == "Canceled bank deposit");
        Assert.DoesNotContain(result.Value.Rows, item => item.Comment == "Обычное распределение фонда");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.bank_deposits_generated" &&
            auditEvent.EntityId == "bank_deposits" &&
            auditEvent.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GetBankDepositReportAsync_AppliesPageWithoutChangingTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var fund = new Fund { Name = "Банк", NormalizedName = "БАНК", SortOrder = 10 };
        database.Context.Funds.Add(fund);
        database.Context.FundOperations.AddRange(
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 100m,
                BalanceBefore = 0m,
                BalanceAfter = 100m,
                Reason = "Сдача 1",
                IsCashToBankTransfer = true,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 200m,
                BalanceBefore = 100m,
                BalanceAfter = 300m,
                Reason = "Сдача 2",
                IsCashToBankTransfer = true,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero)
            },
            new FundOperation
            {
                Fund = fund,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 300m,
                BalanceBefore = 300m,
                BalanceAfter = 600m,
                Reason = "Сдача 3",
                IsCashToBankTransfer = true,
                CreatedAtUtc = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero)
            });
        await database.Context.SaveChangesAsync();

        var result = await service.GetBankDepositReportAsync(
            new BankDepositReportRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), Search: null, Limit: 1, Offset: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(600m, result.Value!.Total);
        Assert.Equal(3, result.Value.RowCount);
        Assert.Equal(1, result.Value.Offset);
        Assert.Equal(1, result.Value.Limit);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(new DateOnly(2026, 6, 11), row.Date);
        Assert.Equal("Сдача 2", row.Comment);
    }

    [Fact]
    public async Task GetFeeReportAsync_ReturnsSummaryDebtorsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
    public async Task FeeReportQuery_LoadsAccrualsPaymentsAndGarageIdentitiesInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        Assert.True((await finance.CreateAccrualAsync(
            new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"),
            null,
            CancellationToken.None)).Succeeded);
        Assert.True((await finance.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 200m, "PKO-1", "Частичная оплата"),
            null,
            CancellationToken.None)).Succeeded);
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 25m,
            IncomeTypeId = fixtures.IncomeType.Id,
            DocumentNumber = "PKO-WITHOUT-GARAGE"
        });
        await database.Context.SaveChangesAsync();

        var query = new EfFeeReportQuery(database.Context);
        commandCounter.Reset();
        var sameGarageData = await query.GetFeeDataAsync([fixtures.IncomeType.Id], CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(500m, sameGarageData.AccrualTotals[fixtures.IncomeType.Id]);
        Assert.Equal(225m, sameGarageData.CollectedTotals[fixtures.IncomeType.Id]);
        Assert.Single(sameGarageData.PaymentsByGarage);
        Assert.Equal(fixtures.FirstGarage.Number, sameGarageData.GaragesById[fixtures.FirstGarage.Id].GarageNumber);

        Assert.True((await finance.CreateIncomeAsync(
            new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 75m, "PKO-2", "Оплата без начисления"),
            null,
            CancellationToken.None)).Succeeded);
        commandCounter.Reset();
        var paymentOnlyGarageData = await query.GetFeeDataAsync([fixtures.IncomeType.Id], CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(500m, paymentOnlyGarageData.AccrualTotals[fixtures.IncomeType.Id]);
        Assert.Equal(300m, paymentOnlyGarageData.CollectedTotals[fixtures.IncomeType.Id]);
        Assert.Equal(fixtures.SecondGarage.Number, paymentOnlyGarageData.GaragesById[fixtures.SecondGarage.Id].GarageNumber);
    }

    [Fact]
    public async Task FeeReportQuery_ReturnsEmptyDataInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var query = new EfFeeReportQuery(database.Context);
        commandCounter.Reset();

        var result = await query.GetFeeDataAsync([Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        Assert.Empty(result.AccrualTotals);
        Assert.Empty(result.CollectedTotals);
        Assert.Empty(result.AccrualsByGarage);
        Assert.Empty(result.PaymentsByGarage);
        Assert.Empty(result.GaragesById);
    }

    [Fact]
    public async Task FeeReportQuery_PropagatesCancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var query = new EfFeeReportQuery(database.Context);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            query.GetFeeDataAsync([Guid.NewGuid()], cancellationSource.Token));
    }

    [Fact]
    public async Task GetFeeReportAsync_UsesFeeCampaignGoalAndTargetAmountWhenCampaignExists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        var campaign = new FeeCampaign
        {
            Name = "Сбор на ворота",
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            Goal = "Замена ворот",
            ContributionAmount = 500m,
            TargetAmount = 33500m,
            StartsOn = new DateOnly(2026, 5, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        var accrual = new Accrual
        {
            GarageId = fixtures.FirstGarage.Id,
            Garage = fixtures.FirstGarage,
            IncomeTypeId = fixtures.IncomeType.Id,
            IncomeType = fixtures.IncomeType,
            FeeCampaignId = campaign.Id,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign
        };
        database.Context.AddRange(campaign, accrual);
        await database.Context.SaveChangesAsync();
        Assert.True((await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 200m, "PKO-1", "Частичная оплата"), null, CancellationToken.None)).Succeeded);

        var result = await service.GetFeeReportAsync(
            new FeeReportRequest("ворота", 10),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(33500m, result.Value!.AccruedTotal);
        Assert.Equal(200m, result.Value.CollectedTotal);
        var summary = Assert.Single(result.Value.SummaryRows);
        Assert.Equal("Сбор на ворота", summary.Name);
        Assert.Equal("Замена ворот", summary.Goal);
        Assert.Equal(33500m, summary.FeeAmount);
        var garageRow = Assert.Single(result.Value.GarageRows);
        Assert.Equal("Сбор на ворота", garageRow.FeeName);
        Assert.Equal(500m, garageRow.Accrued);
        Assert.Equal(200m, garageRow.Paid);
        Assert.Equal(300m, garageRow.Debt);
    }

    [Fact]
    public async Task GetFeeReportAsync_SeparatesCampaignsSharingOtherIncomeDestination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
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
        var firstAccrual = CreateFeeAccrual(firstCampaign, fixtures.FirstGarage, fixtures.IncomeType, 500m);
        var secondAccrual = CreateFeeAccrual(secondCampaign, fixtures.FirstGarage, fixtures.IncomeType, 700m);
        var firstPayment = CreateFeePayment(fixtures.FirstGarage, fixtures.IncomeType, 200m, "FEE-1");
        var secondPayment = CreateFeePayment(fixtures.FirstGarage, fixtures.IncomeType, 300m, "FEE-2");
        database.Context.AddRange(
            firstCampaign,
            secondCampaign,
            firstAccrual,
            secondAccrual,
            firstPayment,
            secondPayment,
            new AccrualPaymentAllocation { Accrual = firstAccrual, FinancialOperation = firstPayment, Amount = 200m },
            new AccrualPaymentAllocation { Accrual = secondAccrual, FinancialOperation = secondPayment, Amount = 300m });
        await database.Context.SaveChangesAsync();

        var result = await CreateService(database.Context).GetFeeReportAsync(
            new FeeReportRequest(null, 10),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.SummaryRows.Count);
        Assert.Equal(2, result.Value.GarageRows.Count);
        Assert.Contains(result.Value.SummaryRows, row => row.IncomeTypeId == firstCampaign.Id && row.Name == firstCampaign.Name && row.Collected == 200m);
        Assert.Contains(result.Value.SummaryRows, row => row.IncomeTypeId == secondCampaign.Id && row.Name == secondCampaign.Name && row.Collected == 300m);
        Assert.Contains(result.Value.GarageRows, row => row.IncomeTypeId == firstCampaign.Id && row.Paid == 200m && row.Debt == 300m);
        Assert.Contains(result.Value.GarageRows, row => row.IncomeTypeId == secondCampaign.Id && row.Paid == 300m && row.Debt == 400m);
    }

    private static Accrual CreateFeeAccrual(FeeCampaign campaign, Garage garage, IncomeType incomeType, decimal amount) =>
        new()
        {
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            FeeCampaignId = campaign.Id,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = amount,
            Source = AccrualSources.FeeCampaign
        };

    private static FinancialOperation CreateFeePayment(Garage garage, IncomeType incomeType, decimal amount, string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            OperationDate = new DateOnly(2026, 6, 10),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = amount,
            DocumentNumber = documentNumber
        };

    [Fact]
    public async Task ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
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
        var service = CreateService(database.Context);
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
                IsCashToBankTransfer = true,
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
                IsCashToBankTransfer = true,
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
        var service = CreateService(database.Context);
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
            IsCashToBankTransfer = true,
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
    public async Task ExportFeeReportXlsxAsync_ReturnsWorkbookWithSummaryGaragesAndDebtors()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"), null, CancellationToken.None);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 200m, "PKO-1", "Частичная оплата"), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.SecondGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 1), 500m, "PKO-2", "Полная оплата"), null, CancellationToken.None);

        var result = await service.ExportFeeReportXlsxAsync(
            new FeeReportRequest("член", ActorUserId: actorUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-fees.xlsx", result.Value!.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.ContentType);
        AssertWorkbookContains(result.Value.Content, "Сборы");
        AssertWorkbookContains(result.Value.Content, "Гаражи");
        AssertWorkbookContains(result.Value.Content, "Должники");
        AssertWorkbookContains(result.Value.Content, "Иванов Иван");
        AssertWorkbookContains(result.Value.Content, "Петров Петр");
        Assert.Contains(database.Context.AuditEvents, auditEvent =>
            auditEvent.Action == "reports.fees_exported" &&
            auditEvent.ActorUserId == actorUserId &&
            auditEvent.MetadataJson != null &&
            auditEvent.MetadataJson.Contains("xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportFeeReportPdfAsync_ReturnsDocumentWithTotals()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);
        var service = CreateService(database.Context);
        await finance.CreateAccrualAsync(new CreateAccrualRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 1), 500m, "manual", "Сбор"), null, CancellationToken.None);
        await finance.CreateIncomeAsync(new CreateIncomeOperationRequest(fixtures.FirstGarage.Id, fixtures.IncomeType.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 1), 200m, "PKO-1", "Частичная оплата"), null, CancellationToken.None);

        var result = await service.ExportFeeReportPdfAsync(
            new FeeReportRequest("член"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("garagebalance-fees.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        AssertPdfContains(result.Value.Content, "GarageBalance fees report");
        AssertPdfContains(result.Value.Content, "500.00");
        AssertPdfContains(result.Value.Content, "300.00");
    }

    [Fact]
    public async Task ExportIncomeReportXlsxAsync_WritesGeneratedAndExportedAuditWithoutRawSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
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

    private static ReportService CreateService(GarageBalanceDbContext context)
    {
        return new ReportService(
            new EfCashMovementReportQuery(context),
            new EfFundChangeReportQuery(context),
            new EfConsolidatedMonthlyReportQuery(context),
            new EfConsolidatedGarageReportQuery(context),
            new EfGarageReportQuery(context),
            new EfFeeReportQuery(context),
            new EfExpenseReportQuery(context),
            new EfIncomeReportQuery(context),
            new EfApplicationUnitOfWork(context),
            new AuditEventWriter(context));
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
            var firstOwner = new Owner { LastName = "Иванов", FirstName = "Иван" };
            var secondOwner = new Owner { LastName = "Петров", FirstName = "Петр" };
            var firstGarage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = firstOwner, InitialWaterMeterValue = 10m, InitialElectricityMeterValue = 100m };
            var secondGarage = new Garage { Number = "21", PeopleCount = 2, FloorCount = 1, Owner = secondOwner, InitialWaterMeterValue = 5m, InitialElectricityMeterValue = 50m };
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
                IsCashToBankTransfer = true,
                CreatedAtUtc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };

            Context.AddRange(firstOwner, secondOwner, firstGarage, secondGarage, group, supplier, incomeType, expenseType, bankFund, bankDeposit);
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

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Count++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

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
