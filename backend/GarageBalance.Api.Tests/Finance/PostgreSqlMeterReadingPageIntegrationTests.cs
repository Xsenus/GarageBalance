using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMeterReadingPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task ReadingPageLoadsCountRowsGarageAndOwnerInOneCommandForEveryPageShape()
    {
        var owner = new Owner { LastName = "Петров", FirstName = "Петр", MiddleName = "Петрович" };
        var firstGarage = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 };
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Garages.AddRange(firstGarage, secondGarage);
            seedContext.MeterReadings.AddRange(
                CreateReading(secondGarage, MeterKinds.Electricity, new DateOnly(2046, 3, 1), 130m, "Target latest"),
                CreateReading(firstGarage, MeterKinds.Electricity, new DateOnly(2046, 2, 1), 120m, "Target owner row", previousValue: 100m, hasGapWarning: true),
                CreateReading(firstGarage, MeterKinds.Electricity, new DateOnly(2046, 1, 1), 100m, "Ordinary row"),
                CreateReading(firstGarage, MeterKinds.Water, new DateOnly(2046, 2, 1), 20m, "Target wrong kind"),
                CreateReading(firstGarage, MeterKinds.Electricity, new DateOnly(2045, 12, 1), 90m, "Target outside period"),
                CreateReading(secondGarage, MeterKinds.Electricity, new DateOnly(2046, 4, 1), 150m, "Target canceled", isCanceled: true));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfMeterReadingRepository(context);

        var page = await repository.GetPageAsync(
            new DateOnly(2046, 1, 1),
            new DateOnly(2046, 3, 1),
            MeterKinds.Electricity,
            "target",
            1,
            1,
            CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        var reading = Assert.Single(page.Items);
        Assert.Equal(firstGarage.Id, reading.GarageId);
        Assert.Equal(firstGarage.Number, reading.Garage.Number);
        Assert.Equal(owner.FullName, reading.Garage.Owner!.FullName);
        Assert.Equal(new DateOnly(2046, 2, 1), reading.AccountingMonth);
        Assert.Equal(new DateOnly(2046, 2, 20), reading.ReadingDate);
        Assert.Equal(120m, reading.CurrentValue);
        Assert.Equal(100m, reading.PreviousValue);
        Assert.Equal(20m, reading.Consumption);
        Assert.True(reading.HasGapWarning);
        Assert.Equal("Target owner row", reading.Comment);
        Assert.False(reading.IsCanceled);
        Assert.NotEqual(Guid.Empty, reading.Version);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await repository.GetPageAsync(
            new DateOnly(2046, 1, 1),
            new DateOnly(2046, 3, 1),
            MeterKinds.Electricity,
            "target",
            20,
            5,
            CancellationToken.None);

        Assert.Equal(2, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var empty = await repository.GetPageAsync(null, null, null, "missing", 0, 5, CancellationToken.None);

        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        AssertSingleCombinedCommand(capture);
    }

    private static MeterReading CreateReading(
        Garage garage,
        string meterKind,
        DateOnly accountingMonth,
        decimal currentValue,
        string comment,
        decimal previousValue = 0m,
        bool hasGapWarning = false,
        bool isCanceled = false) =>
        new()
        {
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = accountingMonth,
            ReadingDate = accountingMonth.AddDays(19),
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            Consumption = currentValue - previousValue,
            HasGapWarning = hasGapWarning,
            Comment = comment,
            IsCanceled = isCanceled
        };

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meter_readings", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garages", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owners", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
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
