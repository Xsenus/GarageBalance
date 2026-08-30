using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlChargeServiceListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task ListUsesSingleCompactQueryAndSelectsTariffForBusinessDate()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var businessDate = new DateOnly(2044, 8, 15);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid scheduledSettingId;
        Guid activeTariffId;
        Guid directSettingId;
        Guid directTariffId;
        Guid gapSettingId;
        await using (var seedContext = database.CreateContext())
        {
            var incomeType = new IncomeType { Name = $"Service income {suffix}" };
            var directTariff = new Tariff
            {
                Name = $"Direct tariff {suffix} {new string('n', 50)}",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 100m,
                EffectiveFrom = new DateOnly(2044, 1, 1),
                Comment = new string('c', 900),
                ElectricityTiersJson = $"{{\"padding\":\"{new string('d', 20_000)}\"}}"
            };
            var activeTariff = new Tariff
            {
                Name = $"Active tariff {suffix} {new string('a', 50)}",
                CalculationBase = TariffCalculationBases.MeterElectricity,
                Rate = 8m,
                ElectricityFirstRate = 5m,
                ElectricitySecondRate = 7m,
                ElectricityTiersJson = "[{\"upperBound\":1000,\"rate\":5}]",
                EffectiveFrom = new DateOnly(2044, 7, 1),
                Comment = new string('h', 900)
            };
            var scheduledSetting = new ChargeServiceSetting
            {
                Name = $"A scheduled service {suffix}",
                IsRegular = true,
                PeriodicityMonths = 12,
                AccrualStartMonth = 3,
                PaymentDueDay = 20,
                PaymentDueMonth = 1,
                OverdueGraceDays = 30,
                IncomeType = incomeType,
                Tariff = directTariff,
                IsMetered = false,
                MeterKind = "electricity-custom",
                HasTieredTariff = false,
                UnitName = "кВт"
            };
            var directOnlyTariff = new Tariff
            {
                Name = $"Direct only tariff {suffix}",
                CalculationBase = TariffCalculationBases.People,
                Rate = 250m,
                EffectiveFrom = new DateOnly(2044, 1, 1),
                Comment = new string('x', 900)
            };
            var directSetting = new ChargeServiceSetting
            {
                Name = $"B direct service {suffix}",
                IsRegular = true,
                PeriodicityMonths = 1,
                PaymentDueDay = 10,
                OverdueGraceDays = 15,
                Tariff = directOnlyTariff,
                UnitName = "чел."
            };
            var futureTariff = new Tariff
            {
                Name = $"Future tariff {suffix}",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 300m,
                EffectiveFrom = new DateOnly(2044, 9, 1)
            };
            var gapSetting = new ChargeServiceSetting
            {
                Name = $"C gap service {suffix}",
                IsRegular = true,
                PeriodicityMonths = 1,
                PaymentDueDay = 25,
                OverdueGraceDays = 30,
                Tariff = futureTariff,
                UnitName = "руб."
            };
            seedContext.AddRange(
                incomeType,
                directTariff,
                activeTariff,
                directOnlyTariff,
                futureTariff,
                scheduledSetting,
                directSetting,
                gapSetting);
            seedContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = scheduledSetting,
                    Tariff = activeTariff,
                    EffectiveFrom = activeTariff.EffectiveFrom
                },
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSetting = gapSetting,
                    Tariff = futureTariff,
                    EffectiveFrom = futureTariff.EffectiveFrom
                });
            await seedContext.SaveChangesAsync();
            scheduledSettingId = scheduledSetting.Id;
            activeTariffId = activeTariff.Id;
            directSettingId = directSetting.Id;
            directTariffId = directOnlyTariff.Id;
            gapSettingId = gapSetting.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfChargeServiceSettingRepository(context).GetListAsync(
            null,
            false,
            null,
            null,
            50,
            businessDate,
            CancellationToken.None);

        var scheduled = Assert.Single(result, setting => setting.Id == scheduledSettingId);
        Assert.Equal(activeTariffId, scheduled.TariffId);
        Assert.Equal(TariffCalculationBases.MeterElectricity, scheduled.Tariff!.CalculationBase);
        Assert.True(scheduled.IsMetered);
        Assert.True(scheduled.HasTieredTariff);
        Assert.Equal(12, scheduled.PeriodicityMonths);
        Assert.Equal(3, scheduled.AccrualStartMonth);
        Assert.Equal(20, scheduled.PaymentDueDay);
        Assert.Equal(1, scheduled.PaymentDueMonth);
        Assert.Equal(30, scheduled.OverdueGraceDays);
        Assert.Equal("electricity-custom", scheduled.MeterKind);
        Assert.Equal(activeTariffId, Assert.Single(scheduled.TariffVersions).TariffId);

        var direct = Assert.Single(result, setting => setting.Id == directSettingId);
        Assert.Equal(directTariffId, direct.TariffId);
        Assert.Equal(TariffCalculationBases.People, direct.Tariff!.CalculationBase);
        Assert.Empty(direct.TariffVersions);

        var gap = Assert.Single(result, setting => setting.Id == gapSettingId);
        Assert.Null(gap.TariffId);
        Assert.Null(gap.Tariff);
        Assert.Empty(gap.TariffVersions);
        Assert.Empty(context.ChangeTracker.Entries());

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LEFT JOIN LATERAL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT @limit", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing_version", command, StringComparison.Ordinal);
        Assert.DoesNotContain("direct_tariff.\"Name\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("direct_tariff.\"Rate\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("direct_tariff.\"Comment\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("tariff.\"Name\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("tariff.\"Rate\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("tariff.\"Comment\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("setting.\"CreatedAtUtc\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("setting.\"UpdatedAtUtc\"", command, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task ListAppliesLiteralSearchFiltersAndLimitInPostgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid expectedId;
        await using (var seedContext = database.CreateContext())
        {
            var expected = new ChargeServiceSetting
            {
                Name = "A literal %_ service",
                IsRegular = true,
                IsMetered = true,
                PaymentDueDay = 20,
                OverdueGraceDays = 30
            };
            seedContext.ChargeServiceSettings.AddRange(
                expected,
                new ChargeServiceSetting
                {
                    Name = "B literal percent service",
                    IsRegular = true,
                    IsMetered = true,
                    PaymentDueDay = 20,
                    OverdueGraceDays = 30
                },
                new ChargeServiceSetting
                {
                    Name = "C literal %_ irregular",
                    IsRegular = false,
                    IsMetered = true,
                    PaymentDueDay = 20,
                    OverdueGraceDays = 30
                },
                new ChargeServiceSetting
                {
                    Name = "D literal %_ unmetered",
                    IsRegular = true,
                    IsMetered = false,
                    PaymentDueDay = 20,
                    OverdueGraceDays = 30
                });
            await seedContext.SaveChangesAsync();
            expectedId = expected.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfChargeServiceSettingRepository(context).GetListAsync(
            "%_",
            false,
            true,
            true,
            1,
            new DateOnly(2044, 8, 15),
            CancellationToken.None);

        Assert.Equal(expectedId, Assert.Single(result).Id);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("ILIKE @search", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setting.\"IsRegular\" = @is_regular", command, StringComparison.Ordinal);
        Assert.Contains("setting.\"IsMetered\" = @is_metered", command, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", command, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlFact]
    public async Task ListPropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfChargeServiceSettingRepository(context).GetListAsync(
                null,
                false,
                null,
                null,
                50,
                new DateOnly(2044, 8, 15),
                cancellation.Token));

        Assert.Empty(capture.Commands);
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
