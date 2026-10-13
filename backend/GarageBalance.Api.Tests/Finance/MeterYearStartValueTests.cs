using System.Data.Common;
using System.Text.Json;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterYearStartValueTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Fact]
    public async Task YearPageDto_ExposesResolvedValueAndDateWithoutInternalChainSnapshot()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var expected = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var result = await FinanceServiceTestFactory.Create(context, businessDate: September)
            .GetMeterReadingYearPageAsync(new(2026, MeterKinds.Electricity, 25, 0), CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(expected.Count, result.Value!.Garages.Count);
        foreach (var garage in result.Value.Garages)
        {
            Assert.Equal(expected[garage.Id].Value, garage.StartValue);
            Assert.Equal(expected[garage.Id].Date, garage.StartValueDate);
            Assert.Equal(expected[garage.Id].Source, garage.StartValueSource);
        }
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"startValueSource\":\"device\"", json, StringComparison.Ordinal);
        Assert.Contains("\"startValueDeviceSerial\":\"ORIGINAL\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("startSnapshotJson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionBaselineDeviceId", json, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task YearPage_ResolvesOpeningValuesFromActualChainsAndConnections()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var expected = await SeedAsync(database.Context);
        database.Context.ChangeTracker.Clear();
        await AssertValuesAsync(database.Context, expected);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_YearPageUsesSameResolverAndOneCombinedCommand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Dictionary<Guid, (decimal? Value, string Source, DateOnly? Date)> expected;
        await using (var context = database.CreateContext()) expected = await SeedAsync(context);
        var capture = new CommandCapture();
        await using var queryContext = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(capture).Options);
        await AssertValuesAsync(queryContext, expected);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("jsonb_build_object", command, StringComparison.Ordinal);
        Assert.DoesNotContain("date_trunc", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Comment\"", command, StringComparison.Ordinal);
    }

    private static async Task AssertValuesAsync(GarageBalanceDbContext context, Dictionary<Guid, (decimal? Value, string Source, DateOnly? Date)> expected)
    {
        var page = await new EfMeterReadingRepository(context).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, CancellationToken.None);
        Assert.Equal(expected.Count, page.TotalCount);
        Assert.Equal(expected.Count, page.Garages.Count);
        foreach (var garage in page.Garages)
        {
            var value = expected[garage.Id];
            Assert.Equal(value.Value, garage.StartValue);
            Assert.Equal(value.Source, garage.StartValueSource);
            Assert.Equal(value.Date, garage.StartValueDate);
            if (garage.Number == "synthetic-unknown-connection")
            {
                Assert.True(garage.RequiresStartValue);
                Assert.False(garage.RequiresConnectionDate);
            }
            if (garage.Number == "synthetic-chain")
            {
                Assert.Equal("ORIGINAL", garage.StartValueDeviceSerial);
                Assert.Equal(9m, garage.InitialReadingValue);
            }
        }
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static async Task<Dictionary<Guid, (decimal? Value, string Source, DateOnly? Date)>> SeedAsync(GarageBalanceDbContext context)
    {
        foreach (var setting in context.ChargeServiceSettings) setting.IsArchived = true;
        var expected = new Dictionary<Guid, (decimal? Value, string Source, DateOnly? Date)>();
        Garage Garage(string name, decimal? legacy, decimal? value, string source, DateOnly? date)
        {
            var garage = new Garage { Number = name, RegisteredOn = September, AccountingStartMonth = September, InitialMeterReadingMonth = September.AddMonths(-1), InitialElectricityMeterValue = legacy, PeopleCount = 1, FloorCount = 1 };
            context.Add(garage);
            expected.Add(garage.Id, (value, source, date));
            return garage;
        }
        MeterDevice Device(Garage garage, string serial, DateOnly installed, decimal value, DateOnly? removed = null) =>
            new() { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = serial, InstalledOn = installed, InitialValue = value, RemovedOn = removed };
        Garage("synthetic-zero", 0, 0, "legacy", September.AddMonths(-1));
        var chain = Garage("synthetic-chain", 9, 100, "device", September.AddDays(-1));
        var original = Device(chain, "ORIGINAL", September.AddDays(-1), 100, September.AddDays(3));
        var second = Device(chain, "SECOND", September.AddDays(4), 5, September.AddDays(8));
        second.ReplacedDeviceId = original.Id;
        var third = Device(chain, "THIRD", September.AddDays(9), 7);
        third.ReplacedDeviceId = second.Id;
        context.AddRange(original, second, third,
            new MeterReading { Garage = chain, MeterKind = MeterKinds.Electricity, MeterDevice = third, AccountingMonth = September, ReadingDate = September.AddDays(20), CurrentValue = 9, IsMeterReplacement = true });
        var connectionGarage = Garage("synthetic-connection", 999, 50, "connection", September.AddDays(10));
        var connectionDevice = Device(connectionGarage, "CONNECTED", September.AddDays(10), 50);
        context.AddRange(connectionDevice, new GarageMeterConnection { GarageId = connectionGarage.Id, MeterKind = MeterKinds.Electricity, ConnectedOn = connectionDevice.InstalledOn, BaselineMeterDeviceId = connectionDevice.Id });
        Garage("synthetic-unknown", null, null, "unknown", September.AddMonths(-1));
        var restart = Garage("synthetic-restart", 100, 25, "device", September.AddDays(9));
        var beforeRestart = Device(restart, "OLD", September.AddDays(-1), 100, September.AddDays(8));
        var boundary = Device(restart, "Начало учёта synthetic", September.AddDays(9), 25, September.AddDays(13));
        var afterRestart = Device(restart, "AFTER", September.AddDays(14), 0);
        afterRestart.ReplacedDeviceId = boundary.Id;
        context.AddRange(beforeRestart, boundary, afterRestart);
        var legacy = Garage("synthetic-legacy-reading", 7, 7, "legacy", September.AddMonths(-1));
        context.AddRange(Device(legacy, "UNREFERENCED", September.AddDays(-1), 999),
            new MeterReading { Garage = legacy, MeterKind = MeterKinds.Electricity, AccountingMonth = September.AddMonths(-12), ReadingDate = September.AddMonths(-12), CurrentValue = 20 });
        var canceled = Garage("synthetic-canceled", 70, 70, "legacy", September.AddMonths(-1));
        var canceledDevice = Device(canceled, "CANCELED", September.AddDays(-1), 999);
        canceledDevice.IsCanceled = true;
        context.Add(canceledDevice);
        var unknownConnection = Garage("synthetic-unknown-connection", 999, null, "unknown", September);
        context.Add(new GarageMeterConnection { GarageId = unknownConnection.Id, MeterKind = MeterKinds.Electricity, ConnectedOn = September });
        await context.SaveChangesAsync();
        return expected;
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
