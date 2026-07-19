using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMissingMeterReadingQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetMissingAsync_AggregatesMonthlyReadingStatusOnceAndKeepsMissingKindsExact()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var month = new DateOnly(2026, 7, 1);
        var withoutReadings = CreateGarage("MISSING-01");
        var waterOnly = CreateGarage("MISSING-02");
        var complete = CreateGarage("MISSING-03");
        var canceledReadings = CreateGarage("MISSING-04");
        var archived = CreateGarage("MISSING-05", isArchived: true);
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Garages.AddRange(withoutReadings, waterOnly, complete, canceledReadings, archived);
            setupContext.MeterReadings.AddRange(
                CreateReading(waterOnly, month, MeterKinds.Water),
                CreateReading(complete, month, MeterKinds.Water),
                CreateReading(complete, month, MeterKinds.Electricity),
                CreateReading(canceledReadings, month, MeterKinds.Water, isCanceled: true),
                CreateReading(canceledReadings, month, MeterKinds.Electricity, isCanceled: true));
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);

        var result = await new EfMissingMeterReadingQuery(queryContext).GetMissingAsync(
            month,
            [MeterKinds.Water, MeterKinds.Electricity],
            null,
            10,
            CancellationToken.None);
        var filtered = await new EfMissingMeterReadingQuery(queryContext).GetMissingAsync(
            month,
            [MeterKinds.Water, MeterKinds.Electricity],
            "missing-04",
            1,
            CancellationToken.None);

        Assert.Collection(
            result,
            row => Assert.Equal(("MISSING-01", MeterKinds.Water), (row.GarageNumber, row.MeterKind)),
            row => Assert.Equal(("MISSING-01", MeterKinds.Electricity), (row.GarageNumber, row.MeterKind)),
            row => Assert.Equal(("MISSING-02", MeterKinds.Electricity), (row.GarageNumber, row.MeterKind)),
            row => Assert.Equal(("MISSING-04", MeterKinds.Water), (row.GarageNumber, row.MeterKind)),
            row => Assert.Equal(("MISSING-04", MeterKinds.Electricity), (row.GarageNumber, row.MeterKind)));
        var filteredRow = Assert.Single(filtered);
        Assert.Equal(("MISSING-04", MeterKinds.Water), (filteredRow.GarageNumber, filteredRow.MeterKind));

        Assert.Equal(2, capture.Commands.Count);
        Assert.All(capture.Commands, command =>
        {
            Assert.Equal(1, CountOccurrences(command, "FROM meter_readings"));
            Assert.Contains("LEFT JOIN", command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GROUP BY", command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain("missing-04", capture.Commands[1], StringComparison.OrdinalIgnoreCase);
    }

    private static Garage CreateGarage(string number, bool isArchived = false) =>
        new()
        {
            Number = number,
            PeopleCount = 1,
            FloorCount = 1,
            IsArchived = isArchived
        };

    private static MeterReading CreateReading(
        Garage garage,
        DateOnly month,
        string meterKind,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = month,
            CurrentValue = 10m,
            IsCanceled = isCanceled
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
