using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlTariffContinuationTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    [PostgreSqlFact]
    public async Task ContinuationAndArchiveRetainHistoryGenerateOctoberOnceAndAnnualServicesOnlyInJanuary()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PERIOD", PeopleCount = 2, RegisteredOn = October.AddMonths(-12) };
        var income = new IncomeType { Name = "Месячная услуга" };
        var annualIncome = new IncomeType { Name = "Годовая услуга" };
        var old = new Tariff
        {
            Name = "Исторический",
            Rate = 120,
            CalculationBase = TariffCalculationBases.People,
            EffectiveFrom = October.AddMonths(-12)
        };
        var last = new Tariff
        {
            Name = "Последний",
            Rate = 125,
            CalculationBase = TariffCalculationBases.People,
            EffectiveFrom = October.AddMonths(-1)
        };
        var annualTariff = new Tariff
        {
            Name = "Годовой",
            Rate = 1000,
            CalculationBase = TariffCalculationBases.Fixed,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var service = new ChargeServiceSetting
        {
            Name = "Месячная услуга",
            IncomeType = income,
            Tariff = last,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20
        };
        var annual = new ChargeServiceSetting
        {
            Name = "Годовая услуга",
            IncomeType = annualIncome,
            Tariff = annualTariff,
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            PaymentDueMonth = 1
        };
        var historical = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            Tariff = old,
            AccountingMonth = October.AddMonths(-2),
            Amount = 240,
            Source = AccrualSources.Regular
        };
        context.AddRange(garage, income, annualIncome, old, last, annualTariff, service, annual, historical,
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = service,
                Tariff = old,
                EffectiveFrom = old.EffectiveFrom,
                EffectiveTo = October.AddMonths(-1).AddDays(-1)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSetting = service,
                Tariff = last,
                EffectiveFrom = last.EffectiveFrom,
                EffectiveTo = October.AddDays(-1)
            });
        await context.SaveChangesAsync();
        await Run(database.ConnectionString, service.Id, false);
        Assert.Equal(2, await context.ChargeServiceTariffVersions.CountAsync(row => row.ChargeServiceSettingId == service.Id));
        Assert.False(await context.ChargeServiceTariffVersions.AnyAsync(row => row.IsArchived));
        await Run(database.ConnectionString, service.Id, true);
        context.ChangeTracker.Clear();
        Assert.True((await context.ChargeServiceTariffVersions.SingleAsync(row => row.TariffId == old.Id)).IsArchived);
        Assert.False((await context.ChargeServiceTariffVersions.SingleAsync(row => row.TariffId == last.Id)).IsArchived);
        Assert.Equal(240, (await context.Accruals.SingleAsync(row => row.Id == historical.Id)).Amount);
        Assert.False((await context.Tariffs.SingleAsync(row => row.Id == old.Id)).IsArchived);
        var auditCount = await context.AuditEvents.CountAsync();
        await Run(database.ConnectionString, service.Id, true);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        var finance = FinanceServiceTestFactory.Create(context, new FixedClock());
        var generated = await finance.GenerateRegularCatalogAccrualsAsync(new(October, "Проверка октября"), null, CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.Equal(250, (await context.Accruals.SingleAsync(row => row.AccountingMonth == October && row.IncomeTypeId == income.Id)).Amount);
        Assert.False(await context.Accruals.AnyAsync(row => row.AccountingMonth == October && row.IncomeTypeId == annualIncome.Id));
        var again = await finance.GenerateRegularCatalogAccrualsAsync(new(October, "Повтор октября"), null, CancellationToken.None);
        Assert.Equal("regular_catalog_accruals_empty", again.ErrorCode);
        Assert.Equal(1, await context.Accruals.CountAsync(row => row.AccountingMonth == October && row.IncomeTypeId == income.Id));
        var january = new DateOnly(2027, 1, 1);
        var yearly = await finance.GenerateRegularCatalogAccrualsAsync(new(january, "Проверка января"), null, CancellationToken.None);
        Assert.True(yearly.Succeeded, yearly.ErrorMessage);
        Assert.Equal(1000, (await context.Accruals.SingleAsync(row => row.AccountingMonth == january && row.IncomeTypeId == annualIncome.Id)).Amount);
        var annualAgain = await finance.GenerateRegularCatalogAccrualsAsync(new(january, "Повтор января"), null, CancellationToken.None);
        Assert.Equal("regular_catalog_accruals_empty", annualAgain.ErrorCode);
        Assert.Equal(1, await context.Accruals.CountAsync(row => row.AccountingMonth == january && row.IncomeTypeId == annualIncome.Id));
    }

    [PostgreSqlFact]
    public async Task InvalidTargetRateAndOverlappingPeriodRollBackWithoutArchiving()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var tariff = new Tariff { Name = "Непрерывный", Rate = 125, CalculationBase = TariffCalculationBases.People };
        var service = new ChargeServiceSetting
        {
            Name = "Непрерывная услуга",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            Tariff = tariff
        };
        context.AddRange(tariff, service, new ChargeServiceTariffVersion
        {
            ChargeServiceSetting = service,
            Tariff = tariff,
            EffectiveFrom = October.AddMonths(-1)
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, service.Id, true));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, service.Id, true, rate: "0"));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, Guid.NewGuid(), true));
        await Assert.ThrowsAsync<PostgresException>(() => Run(database.ConnectionString, service.Id, true, expectedDatabase: "garagebalance_staging"));
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Action.Contains("continued")));
        Assert.Equal(1, await context.ChargeServiceTariffVersions.CountAsync(row => row.ChargeServiceSettingId == service.Id));
    }

    internal static async Task Run(string connectionString, Guid serviceId, bool execute, string rate = "125", string? expectedDatabase = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "infrastructure/scripts/continue-service-tariff.sql"));
        var body = script[script.IndexOf("DO $continue$", StringComparison.Ordinal)..script.IndexOf("\\if :execute", StringComparison.Ordinal)];
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("BEGIN; SELECT "
            + "set_config('garagebalance.tariff_database', @database, true), set_config('garagebalance.tariff_service', @service, true), "
            + "set_config('garagebalance.tariff_month', '2026-10-01', true), set_config('garagebalance.tariff_rate', @rate, true), "
            + "set_config('garagebalance.tariff_archive_before', '2026-09-01', true), "
            + "set_config('garagebalance.tariff_reason', 'Продолжение периода по решению заказчика', true); " + body + (execute ? "COMMIT;" : "ROLLBACK;"), connection);
        command.Parameters.AddWithValue("database", expectedDatabase ?? connection.Database);
        command.Parameters.AddWithValue("service", serviceId.ToString());
        command.Parameters.AddWithValue("rate", rate);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
