using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlMeterAccountingRestartTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    [PostgreSqlFact]
    public async Task RestartPreservesPaidHistoryAndNewOctoberInputStartsFromZeroExactlyOnce()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage
        {
            Number = "RESTART",
            InitialElectricityMeterValue = 9,
            InitialWaterMeterValue = 3,
            InitialMeterReadingMonth = October.AddMonths(-2),
            StartingBalance = 123,
            StartingOverdueDebt = 12
        };
        var income = new IncomeType { Name = "Учёт счётчика", Code = "restart_meter" };
        var tariff = new Tariff { Name = "Учёт счётчика", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 10 };
        var device = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "Без номера",
            InstalledOn = October.AddMonths(-1),
            InitialValue = 9
        };
        var reading = new MeterReading
        {
            Garage = garage,
            MeterDevice = device,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = October.AddMonths(-1),
            ReadingDate = October.AddDays(-1),
            CurrentValue = 19,
            PreviousValue = 9,
            Consumption = 10
        };
        var previous = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            Tariff = tariff,
            Amount = 100,
            AccountingMonth = October.AddMonths(-1),
            Source = AccrualSources.Regular,
            RequiresMeterReading = true,
            CalculationMeterKind = MeterKinds.Electricity,
            CalculationDetailsJson = "{\"historical\":true}"
        };
        var current = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            Tariff = tariff,
            Amount = 200,
            AccountingMonth = October,
            Source = AccrualSources.Regular,
            RequiresMeterReading = true,
            CalculationMeterKind = MeterKinds.Electricity
        };
        var payment = new FinancialOperation
        {
            Garage = garage,
            IncomeType = income,
            Amount = 100,
            AccountingMonth = October.AddMonths(-1),
            OperationDate = October.AddDays(-1),
            OperationKind = FinancialOperationKinds.Income
        };
        context.AddRange(garage, income, tariff, device, reading, previous, current, payment,
            new AccrualPaymentAllocation { Accrual = previous, FinancialOperation = payment, Amount = 100, IsActive = true },
            new ChargeServiceSetting
            {
                Name = "Учёт счётчика",
                IncomeType = income,
                Tariff = tariff,
                IsRegular = true,
                IsMetered = true,
                MeterKind = MeterKinds.Electricity,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1
            });
        await context.SaveChangesAsync();
        var historicalSnapshot = await context.Accruals.AsNoTracking().Where(row => row.Id == previous.Id)
            .Select(row => row.CalculationDetailsJson).SingleAsync();
        await Run(database.ConnectionString, true);
        context.ChangeTracker.Clear();
        Assert.True((await context.MeterReadings.SingleAsync()).IsCanceled);
        var history = await context.Accruals.SingleAsync(row => row.Id == previous.Id);
        Assert.False(history.IsCanceled);
        Assert.Equal(100, history.Amount);
        Assert.Equal(historicalSnapshot, history.CalculationDetailsJson);
        Assert.True((await context.Accruals.SingleAsync(row => row.Id == current.Id)).IsCanceled);
        Assert.True((await context.AccrualPaymentAllocations.SingleAsync()).IsActive);
        Assert.Equal(100, (await context.FinancialOperations.SingleAsync()).Amount);
        var restarted = await context.Garages.SingleAsync(row => row.Id == garage.Id);
        Assert.Equal(123, restarted.StartingBalance);
        Assert.Equal(12, restarted.StartingOverdueDebt);
        Assert.Equal(October.AddMonths(-1), restarted.InitialMeterReadingMonth);
        Assert.Equal(0, restarted.InitialElectricityMeterValue);
        Assert.Equal(October.AddDays(-1), (await context.MeterDevices.SingleAsync(row => row.Id == device.Id)).RemovedOn);
        var auditCount = await context.AuditEvents.CountAsync();
        await Run(database.ConnectionString, true);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        var service = FinanceServiceTestFactory.Create(context, new FixedClock());
        var created = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity, October, October.AddDays(1), 5, null), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(0, created.Value!.PreviousValue);
        Assert.Equal(5, created.Value.Consumption);
        Assert.Equal(50, (await context.Accruals.SingleAsync(row => row.AccountingMonth == October && row.IncomeTypeId == income.Id && !row.IsCanceled)).Amount);
        Assert.Equal(100, history.Amount);
    }

    [PostgreSqlFact]
    public async Task PaidOctoberAndLaterReadingsRejectWholeTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PROTECTED", InitialElectricityMeterValue = 123 };
        var income = new IncomeType { Name = "Защищённый" };
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            Amount = 50,
            Source = AccrualSources.Regular,
            AccountingMonth = October,
            RequiresMeterReading = true
        };
        var payment = new FinancialOperation
        {
            Garage = garage,
            IncomeType = income,
            Amount = 50,
            OperationKind = FinancialOperationKinds.Income,
            AccountingMonth = October,
            OperationDate = October
        };
        var allocation = new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = 50, IsActive = true };
        context.AddRange(garage, income, accrual, payment, allocation);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, true));
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Action.Contains("restart")));
        Assert.False((await context.Accruals.AsNoTracking().SingleAsync()).IsCanceled);
        allocation.IsActive = false;
        context.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = October.AddMonths(1),
            ReadingDate = October.AddMonths(1),
            CurrentValue = 4
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, true));
        Assert.Equal(123, (await context.Garages.AsNoTracking().SingleAsync(row => row.Id == garage.Id)).InitialElectricityMeterValue);
    }

    [PostgreSqlFact]
    public async Task DryRunAndInvalidTargetDoNotWrite()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PREVIEW", InitialElectricityMeterValue = 45 };
        context.Add(garage);
        await context.SaveChangesAsync();
        await Run(database.ConnectionString, false);
        Assert.Equal(45, (await context.Garages.AsNoTracking().SingleAsync(row => row.Id == garage.Id)).InitialElectricityMeterValue);
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Action.Contains("restart")));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, true, "garagebalance_staging"));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, true, month: "2026-10-02"));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, true, reason: ""));
    }

    internal static async Task Run(string connectionString, bool execute, string? expectedDatabase = null,
        string month = "2026-10-01", string reason = "Новый учёт с сохранением финансовой истории")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "infrastructure/scripts/restart-meter-accounting.sql"));
        var body = script[script.IndexOf("DO $restart$", StringComparison.Ordinal)..script.IndexOf("\\if :execute", StringComparison.Ordinal)];
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("BEGIN; SET LOCAL lock_timeout='5s'; SELECT "
            + "set_config('garagebalance.restart_database', @database, true), "
            + "set_config('garagebalance.restart_month', @month, true), "
            + "set_config('garagebalance.restart_reason', @reason, true); " + body + (execute ? "COMMIT;" : "ROLLBACK;"), connection);
        command.Parameters.AddWithValue("database", expectedDatabase ?? connection.Database);
        command.Parameters.AddWithValue("month", month);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
