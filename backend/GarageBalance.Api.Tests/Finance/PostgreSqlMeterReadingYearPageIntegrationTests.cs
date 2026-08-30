using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMeterReadingYearPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task YearPageLoadsTotalGaragesAndReadingsInOneCommandForEveryPageShape()
    {
        var firstGarage = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 };
        var secondGarage = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 };
        var thirdGarage = new Garage { Number = "10", PeopleCount = 1, FloorCount = 1 };
        var archivedGarage = new Garage { Number = "3", PeopleCount = 1, FloorCount = 1, IsArchived = true };
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Garages.AddRange(firstGarage, secondGarage, thirdGarage, archivedGarage);
            seedContext.MeterReadings.AddRange(
                CreateReading(secondGarage, MeterKinds.Electricity, new DateOnly(2045, 1, 1), 110m),
                CreateReading(secondGarage, MeterKinds.Electricity, new DateOnly(2045, 2, 1), 125m),
                CreateReading(secondGarage, MeterKinds.Water, new DateOnly(2045, 2, 1), 25m),
                CreateReading(secondGarage, MeterKinds.Electricity, new DateOnly(2044, 12, 1), 100m),
                CreateReading(thirdGarage, MeterKinds.Electricity, new DateOnly(2045, 3, 1), 250m, isCanceled: true),
                CreateReading(archivedGarage, MeterKinds.Electricity, new DateOnly(2045, 3, 1), 500m));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfMeterReadingRepository(context);

        var page = await repository.GetYearPageAsync(2045, MeterKinds.Electricity, 1, 2, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["2", "10"], page.Garages.Select(garage => garage.Number));
        Assert.Equal(
            [new DateOnly(2045, 1, 1), new DateOnly(2045, 2, 1)],
            page.Readings.Select(reading => reading.AccountingMonth));
        Assert.All(page.Readings, reading => Assert.Equal(secondGarage.Id, reading.GarageId));
        Assert.Equal([110m, 125m], page.Readings.Select(reading => reading.CurrentValue));
        Assert.All(page.Readings, reading => Assert.NotEqual(Guid.Empty, reading.Version));
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var finalPage = await repository.GetYearPageAsync(2045, MeterKinds.Electricity, 2, 2, CancellationToken.None);

        Assert.Equal(3, finalPage.TotalCount);
        Assert.Equal("10", Assert.Single(finalPage.Garages).Number);
        Assert.Empty(finalPage.Readings);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await repository.GetYearPageAsync(2045, MeterKinds.Electricity, 20, 2, CancellationToken.None);

        Assert.Equal(3, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Garages);
        Assert.Empty(beyondEnd.Readings);
        AssertSingleCombinedCommand(capture);
    }

    [PostgreSqlFact]
    public async Task YearPageReadsStoredReplacementMarkerWithoutPerReadingDeviceHistoryProbe()
    {
        var garage = new Garage { Number = "77", PeopleCount = 1, FloorCount = 1 };
        var oldDevice = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "OLD-77",
            InstalledOn = new DateOnly(2044, 1, 1),
            RemovedOn = new DateOnly(2045, 1, 31),
            InitialValue = 0m,
            FinalValue = 100m
        };
        var replacementDevice = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "NEW-77",
            InstalledOn = new DateOnly(2045, 2, 1),
            InitialValue = 0m
        };

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(garage, oldDevice, replacementDevice);
            seedContext.MeterReadings.AddRange(
                CreateReading(garage, MeterKinds.Electricity, new DateOnly(2045, 1, 1), 100m, meterDevice: oldDevice),
                CreateReading(garage, MeterKinds.Electricity, new DateOnly(2045, 2, 1), 5m, meterDevice: replacementDevice, isMeterReplacement: true),
                CreateReading(garage, MeterKinds.Electricity, new DateOnly(2045, 3, 1), 15m, meterDevice: replacementDevice));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var page = await new EfMeterReadingRepository(context)
            .GetYearPageAsync(2045, MeterKinds.Electricity, 0, 25, CancellationToken.None);

        Assert.Equal(3, page.Readings.Count);
        Assert.False(page.Readings[0].IsMeterReplacement);
        Assert.Null(page.Readings[0].MeterDeviceSerialNumber);
        Assert.True(page.Readings[1].IsMeterReplacement);
        Assert.Equal("NEW-77", page.Readings[1].MeterDeviceSerialNumber);
        Assert.False(page.Readings[2].IsMeterReplacement);
        Assert.Null(page.Readings[2].MeterDeviceSerialNumber);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("reading.\"IsMeterReplacement\"", command, StringComparison.Ordinal);
        Assert.Contains("AND reading.\"IsMeterReplacement\" = TRUE", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM meter_devices AS other_device", command, StringComparison.Ordinal);
        Assert.DoesNotContain("date_trunc", command, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task NaturalGaragePageOrderUsesDedicatedActiveIndex()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Garages.AddRange(Enumerable.Range(1, 300).Select(index => new Garage
            {
                Number = index.ToString(),
                PeopleCount = 1,
                FloorCount = 1,
                IsArchived = index % 29 == 0
            }));
            await seedContext.SaveChangesAsync();
            await seedContext.Database.ExecuteSqlRawAsync("ANALYZE garages;");
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText =
                """
                SELECT indexdef
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'garages'
                  AND indexname = 'IX_garages_active_natural_number';
                """;
            var definition = Assert.IsType<string>(await indexCommand.ExecuteScalarAsync());
            Assert.Contains("length", definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Number\"", definition, StringComparison.Ordinal);
            Assert.Contains("\"IsArchived\" = false", definition, StringComparison.OrdinalIgnoreCase);
        }

        await using (var settingsCommand = connection.CreateCommand())
        {
            settingsCommand.CommandText = "SET enable_seqscan = off; SET enable_sort = off; SET jit = off;";
            await settingsCommand.ExecuteNonQueryAsync();
        }

        await using var explainCommand = connection.CreateCommand();
        explainCommand.CommandText =
            """
            EXPLAIN (FORMAT TEXT)
            SELECT "Id", "Number"
            FROM garages
            WHERE "IsArchived" = false
            ORDER BY length("Number"), "Number", "Id"
            LIMIT 25;
            """;
        await using var reader = await explainCommand.ExecuteReaderAsync();
        var planLines = new List<string>();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        var plan = string.Join(Environment.NewLine, planLines);
        Assert.Contains("IX_garages_active_natural_number", plan, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task YearPageHonorsCancellationBeforeDatabaseMaterialization()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfMeterReadingRepository(context).GetYearPageAsync(
                2045,
                MeterKinds.Electricity,
                0,
                25,
                cancellation.Token));
    }

    private static MeterReading CreateReading(
        Garage garage,
        string meterKind,
        DateOnly accountingMonth,
        decimal currentValue,
        bool isCanceled = false,
        MeterDevice? meterDevice = null,
        bool isMeterReplacement = false) =>
        new()
        {
            Garage = garage,
            MeterDevice = meterDevice,
            MeterKind = meterKind,
            AccountingMonth = accountingMonth,
            ReadingDate = accountingMonth.AddDays(19),
            CurrentValue = currentValue,
            IsCanceled = isCanceled,
            IsMeterReplacement = isMeterReplacement
        };

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("WITH paged_garages AS", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(*) OVER ()", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN meter_readings", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
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
