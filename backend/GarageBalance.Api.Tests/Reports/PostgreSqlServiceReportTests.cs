using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlServiceReportTests
{
    [PostgreSqlFact]
    public async Task ManyPaymentDatesUseBoundedAggregateQueriesAndFullDayTotals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commands = 0;
        await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .LogTo(_ => commands++, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]).Options);
        var garage = new Garage { Number = "85" };
        var other = new Garage { Number = "86" };
        var date = new DateOnly(2046, 9, 30);
        for (var index = 0; index < 30; index++) db.AddRange(Payment(garage, null, date.AddDays(-index), 10), Payment(other, null, date.AddDays(-index), 20));
        await db.SaveChangesAsync();
        commands = 0;
        var report = await new EfServiceReportRepository(db).GetPaymentsAsync(new(DateTo: date, Limit: 49), default);
        Assert.Equal(60, report.RowCount);
        Assert.Equal(49, report.Rows.Count);
        Assert.Equal(25, report.Days.Count);
        Assert.All(report.Days, day => Assert.Equal(30, day.Total));
        Assert.Equal(900, report.Total);
        Assert.InRange(commands, 1, 9);
    }

    [PostgreSqlFact]
    public async Task MigrationRollsBackAndReappliesAndQueriesRespectOwnedTransactionsAndCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260929120116_RestrictChargeServicesToSelectedGarages");
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(1, await db.ApplicationSettings.CountAsync(setting => setting.Key == EfServiceReportRepository.SettingsKey));
        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new EfServiceReportRepository(db);
        Assert.Empty((await repository.GetPaymentsAsync(new(DateTo: new(2046, 9, 30)), default)).Rows);
        Assert.Empty((await repository.GetDebtAsync(new(DateTo: new(2046, 9, 30)), default)).Rows);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetPaymentsAsync(new(DateTo: new(2046, 9, 30)), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetDebtAsync(new(DateTo: new(2046, 9, 30)), cancellation.Token));
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task GenericOverAllocationFailsAndDoesNotLeaveATransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(); await using var db = database.CreateContext();
        var income = await db.IncomeTypes.SingleAsync(type => type.Code == "electricity"); var garage = new Garage { Number = "85" }; var date = new DateOnly(2046, 9, 30);
        db.Add(new AccrualPaymentAllocation { Accrual = Accrual(garage, income, date, 11), FinancialOperation = Payment(garage, null, date, 10), Amount = 11, IsActive = true }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfServiceReportRepository(db).GetPaymentsAsync(new(DateTo: date), default));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task ColumnsPersistAndRejectStaleVersionsWithoutFinancialChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var repository = new EfServiceReportRepository(db);
        var original = await repository.GetColumnsAsync(default);
        var income = await db.IncomeTypes.SingleAsync(type => type.Code == "electricity");
        var service = new ChargeServiceSetting { Name = "Колонка проверки", IncomeType = income, IsRegular = true };
        db.Add(service); await db.SaveChangesAsync();
        var columns = new[] { new ServiceReportColumn(Guid.NewGuid(), "Свет", [service.Id]) };
        await repository.SaveColumnsAsync(new(original.Version, columns), null, default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var current = await repository.GetColumnsAsync(default);
        Assert.Equal("Свет", Assert.Single(current.Columns).Name);
        Assert.NotEqual(original.Version, current.Version);
        Assert.Equal(service.Id, Assert.Single(current.Columns[0].ServiceIds));
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.SaveColumnsAsync(new(original.Version, columns), null, default));
        Assert.Empty(await db.FinancialOperations.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task PaymentsCoverAllHistoryRangeAllocationTotalsAndNumericOrder()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var income = await db.IncomeTypes.SingleAsync(type => type.Code == "electricity");
        var service = new ChargeServiceSetting { Name = "Электричество проверки", IncomeType = income, IsRegular = true };
        var first = new Garage { Number = "2" }; var second = new Garage { Number = "10" };
        var early = new DateOnly(2046, 8, 31); var late = new DateOnly(2046, 9, 30);
        var general = Payment(first, null, late, 100);
        var accrual = Accrual(first, income, early, 60);
        db.AddRange(service, Payment(first, income, early, 40), general, Payment(second, income, late, 20),
            new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = general, Amount = 60, IsActive = true },
            Payment(first, income, late.AddDays(1), 999), Payment(first, income, late, 999, true));
        await db.SaveChangesAsync();
        var repository = new EfServiceReportRepository(db);
        var config = await repository.GetColumnsAsync(default);
        await repository.SaveColumnsAsync(new(config.Version, [new(Guid.NewGuid(), "Свет", [service.Id])]), null, default); await db.SaveChangesAsync();
        var query = new ServiceReportRequest(DateTo: late, Limit: 1);
        var report = await repository.GetPaymentsAsync(query, default);
        Assert.Equal(3, report.RowCount); Assert.Equal("2", Assert.Single(report.Rows).GarageNumber);
        Assert.Equal(160, report.Total); Assert.Equal([120m, 40m], report.Totals);
        Assert.Equal(120, Assert.Single(report.Days).Amounts.Sum());
        var range = await repository.GetPaymentsAsync(query with { DateFrom = late, Limit = 50 }, default);
        Assert.Equal(120, range.Total); Assert.Equal(["2", "10"], range.Rows.Select(row => row.GarageNumber));
        var garage = await repository.GetPaymentsAsync(query with { GarageId = second.Id }, default);
        Assert.Equal(20, garage.Total); Assert.DoesNotContain(garage.Columns, column => column.Name == "Прочее");
        var empty = await repository.GetPaymentsAsync(query with { DateFrom = late.AddDays(-1), DateTo = late.AddDays(-1) }, default);
        Assert.Empty(empty.Rows); Assert.Equal(0, empty.Total);
    }

    [PostgreSqlFact]
    public async Task DebtAccountsForInitialBalanceCreditPaymentsCanceledAndFutureRecords()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var income = await db.IncomeTypes.SingleAsync(type => type.Code == "electricity");
        var service = new ChargeServiceSetting { Name = "Долг проверки", IncomeType = income, IsRegular = true };
        var day = new DateOnly(2046, 9, 30);
        var first = new Garage { Number = "2", StartingBalance = 50, StartingOverdueDebt = 50 };
        var second = new Garage { Number = "10", StartingBalance = -20 };
        var old = Accrual(first, income, day.AddMonths(-1), 100);
        var notDue = Accrual(first, income, day, 80); notDue.OverdueFromDate = day.AddDays(1);
        var canceled = Accrual(first, income, day, 999); canceled.IsCanceled = true;
        db.AddRange(service, old, notDue, canceled, Accrual(first, income, day.AddMonths(1), 999), Accrual(second, income, day.AddMonths(-1), 100),
            Payment(first, null, day.AddDays(-1), 30),
            new AccrualPaymentAllocation { Accrual = old, FinancialOperation = Payment(first, income, day, 40), Amount = 40, IsActive = true },
            Payment(first, income, day.AddDays(1), 1000));
        await db.SaveChangesAsync();
        var repository = new EfServiceReportRepository(db);
        foreach (var scope in new[] { "accrued", "overdue" })
        {
            var config = await repository.GetColumnsAsync(default, scope);
            await repository.SaveColumnsAsync(new(config.Version, [new(Guid.NewGuid(), "Свет", [service.Id])], scope), null, default);
        }
        await db.SaveChangesAsync();
        var report = await repository.GetDebtAsync(new(DateTo: day, Limit: 1), default);
        Assert.Equal(2, report.RowCount); Assert.Equal(240, report.Total); Assert.Equal(160, Assert.Single(report.Rows).Total);
        Assert.Equal([220m, 20m], report.Totals);
        var overdue = await repository.GetDebtAsync(new(DateTo: day, OverdueOnly: true), default);
        Assert.Equal(160, overdue.Total); Assert.Equal([140m, 20m], overdue.Totals);
        var garage = await repository.GetDebtAsync(new(DateTo: day, GarageId: second.Id), default);
        Assert.Equal(80, garage.Total); Assert.DoesNotContain(garage.Columns, column => column.Name == "Прочее");
    }

    [PostgreSqlFact]
    public async Task MigrationPreservesLegacyConfigurationAndReportScopesAreIndependent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260930080739_AddServiceReportColumns");
        var legacy = await db.ApplicationSettings.SingleAsync(setting => setting.Key == "reports.service_columns");
        var columnId = Guid.NewGuid();
        legacy.JsonValue = System.Text.Json.JsonSerializer.Serialize(new[] { new ServiceReportColumn(columnId, "Своя колонка", []) });
        await db.SaveChangesAsync();
        var legacyVersion = legacy.Version;
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        var repository = new EfServiceReportRepository(db);
        var configs = new List<ServiceReportColumnsDto>();
        foreach (var scope in new[] { "payments", "accrued", "overdue" })
        {
            var config = await repository.GetColumnsAsync(default, scope);
            Assert.Equal(scope, config.Report);
            Assert.Equal(columnId, Assert.Single(config.Columns).Id);
            Assert.Equal("Своя колонка", config.Columns[0].Name);
            configs.Add(config);
        }
        Assert.Equal(legacyVersion, configs[0].Version);
        Assert.Equal(3, configs.Select(config => config.Version).Distinct().Count());
        foreach (var config in configs)
        {
            await repository.SaveColumnsAsync(new(config.Version, [new(columnId, config.Report, [])], config.Report), null, default);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        Assert.Equal("payments", Assert.Single((await repository.GetPaymentsAsync(new(DateTo: new(2046, 9, 30)), default)).Columns).Name);
        Assert.Equal("accrued", Assert.Single((await repository.GetDebtAsync(new(DateTo: new(2046, 9, 30)), default)).Columns).Name);
        Assert.Equal("overdue", Assert.Single((await repository.GetDebtAsync(new(DateTo: new(2046, 9, 30), OverdueOnly: true), default)).Columns).Name);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => repository.SaveColumnsAsync(new(configs[1].Version, [new(columnId, "Старая версия", [])], "accrued"), null, default));
        await migrator.MigrateAsync("20260930080739_AddServiceReportColumns");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.All(await db.ApplicationSettings.Where(setting => setting.Key.StartsWith("reports.service_columns.")).ToArrayAsync(), setting => Assert.Contains("payments", setting.JsonValue!));
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.FinancialOperations.ToArrayAsync());
    }

    private static FinancialOperation Payment(Garage garage, IncomeType? income, DateOnly date, decimal amount, bool canceled = false) => new()
    { Garage = garage, IncomeType = income, OperationKind = "income", OperationDate = date, AccountingMonth = new(date.Year, date.Month, 1), Amount = amount, IsCanceled = canceled };
    private static Accrual Accrual(Garage garage, IncomeType income, DateOnly date, decimal amount) => new()
    { Garage = garage, IncomeType = income, AccountingMonth = new(date.Year, date.Month, 1), DueDate = date, OverdueFromDate = date, Amount = amount, Source = "manual" };
}
