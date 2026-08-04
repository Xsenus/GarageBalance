using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlTariffAndMeterPerformanceTests
{
    [PostgreSqlFact]
    public async Task ActiveMeteredServicesUseEffectiveTariffsAndIndexedPredicates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var incomeType = new IncomeType { Name = "Вода performance", Code = "water_performance" };
        var activeTariff = CreateTariff("Действующий", TariffCalculationBases.MeterWater, new DateOnly(2026, 1, 1));
        var futureTariff = CreateTariff("Будущий", TariffCalculationBases.MeterWater, new DateOnly(2027, 1, 1));
        var archivedTariff = CreateTariff("Архивный", TariffCalculationBases.MeterWater, new DateOnly(2025, 1, 1), true);
        var activeService = CreateService("Вода performance", incomeType, activeTariff);
        var futureService = CreateService("Вода performance будущая", incomeType, futureTariff);
        var archivedTariffService = CreateService("Вода performance архивная", incomeType, archivedTariff);

        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(incomeType, activeTariff, futureTariff, archivedTariff);
            seedContext.AddRange(activeService, futureService, archivedTariffService);
            for (var index = 0; index < 250; index++)
            {
                var tariff = CreateTariff(
                    $"Фиксированный {index}",
                    TariffCalculationBases.Fixed,
                    new DateOnly(2020 + index % 6, 1 + index % 12, 1));
                seedContext.Tariffs.Add(tariff);
                var service = CreateService($"Услуга {index}", incomeType, tariff);
                service.IsRegular = false;
                service.IsMetered = false;
                seedContext.ChargeServiceSettings.Add(service);
            }

            await seedContext.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var repository = new EfChargeServiceSettingRepository(context);
            var services = await repository.GetActiveRegularMeteredAsync(
                TariffCalculationBases.MeterWater,
                new DateOnly(2026, 7, 1),
                50,
                CancellationToken.None);

            var service = Assert.Single(services, item => item.Id == activeService.Id);
            Assert.Equal(activeTariff.Id, service.TariffId);
            Assert.DoesNotContain(services, item => item.Id == futureService.Id);
            Assert.DoesNotContain(services, item => item.Id == archivedTariffService.Id);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var indexes = await ReadIndexesAsync(connection);
        AssertIndex(indexes, "IX_tariffs_CalculationBase_EffectiveFrom", "\"IsArchived\" = false");
        AssertIndex(indexes, "IX_charge_service_settings_IsRegular_IsMetered_TariffId", "\"IsArchived\" = false");
        AssertIndex(indexes, "IX_meter_readings_MeterKind_AccountingMonth_GarageId", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_meter_readings_GarageId_MeterKind_AccountingMonth", "UNIQUE");
        AssertIndex(indexes, "IX_meter_readings_GarageId_MeterKind_AccountingMonth", "\"IsCanceled\" = false");

        Assert.Contains(
            "IX_tariffs_CalculationBase_EffectiveFrom",
            await ExplainAsync(
                connection,
                """
                SELECT "Id"
                FROM tariffs
                WHERE "IsArchived" = false
                  AND "CalculationBase" = 'meter_water'
                  AND "EffectiveFrom" <= DATE '2026-07-01';
                """),
            StringComparison.Ordinal);
        var servicePlan = await ExplainAsync(
            connection,
            """
            SELECT "Id"
            FROM charge_service_settings
            WHERE "IsArchived" = false
              AND "IsRegular" = true
              AND "IsMetered" = true
            ORDER BY "TariffId";
            """);
        Assert.True(
            servicePlan.Contains("IX_charge_service_settings_IsRegular_IsMetered_TariffId", StringComparison.Ordinal) ||
            servicePlan.Contains("IX_charge_service_settings_TariffId", StringComparison.Ordinal),
            $"Expected PostgreSQL to use the filtered predicate index or the ordered TariffId index.{Environment.NewLine}{servicePlan}");
    }

    [PostgreSqlFact]
    public async Task ActiveMeteredServiceSelectionHonorsCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var repository = new EfChargeServiceSettingRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetActiveRegularMeteredAsync(
                TariffCalculationBases.MeterElectricity,
                new DateOnly(2026, 7, 1),
                50,
                cancellation.Token));
    }

    private static Tariff CreateTariff(string name, string calculationBase, DateOnly effectiveFrom, bool isArchived = false) =>
        new()
        {
            Name = name,
            CalculationBase = calculationBase,
            Rate = 100m,
            EffectiveFrom = effectiveFrom,
            IsArchived = isArchived
        };

    private static ChargeServiceSetting CreateService(string name, IncomeType incomeType, Tariff tariff) =>
        new()
        {
            Name = name,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = tariff,
            IsMetered = true,
            UnitName = TariffCalculationBases.GetUnitName(tariff.CalculationBase)
        };

    private static async Task<Dictionary<string, string>> ReadIndexesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('tariffs', 'charge_service_settings', 'meter_readings');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(0)] = reader.GetString(1);
        }

        return indexes;
    }

    private static void AssertIndex(IReadOnlyDictionary<string, string> indexes, string name, string expectedDefinition)
    {
        Assert.True(indexes.TryGetValue(name, out var definition), $"Index {name} was not created.");
        Assert.Contains(expectedDefinition, definition, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ExplainAsync(NpgsqlConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET enable_seqscan = off; SET enable_bitmapscan = off; SET enable_sort = off; EXPLAIN (ANALYZE, BUFFERS) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
