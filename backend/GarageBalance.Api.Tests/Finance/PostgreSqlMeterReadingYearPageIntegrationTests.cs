using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

    private static MeterReading CreateReading(
        Garage garage,
        string meterKind,
        DateOnly accountingMonth,
        decimal currentValue,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = accountingMonth,
            ReadingDate = accountingMonth.AddDays(19),
            CurrentValue = currentValue,
            IsCanceled = isCanceled
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
