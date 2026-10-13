using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionPeriodDataQueryTests
{
    [Fact]
    public async Task SqliteCalculationInputsUseOneFilteredReadOnlyQuery()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var capture = new SelectCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(database.Context.Database.GetDbConnection()).AddInterceptors(capture).Options;
        await using var context = new GarageBalanceDbContext(options);
        await VerifyAsync(context, capture);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlCalculationInputsUseOneFilteredReadOnlyQuery()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(capture).Options;
        await using var context = new GarageBalanceDbContext(options);
        await VerifyAsync(context, capture);
    }

    private static async Task VerifyAsync(GarageBalanceDbContext context, SelectCapture capture)
    {
        var month = new DateOnly(2026, 9, 1);
        var kind = MeterKinds.ForService(Guid.NewGuid());
        var ready = new Garage { Number = "QUERY-READY" };
        var pending = new Garage { Number = "QUERY-PENDING" };
        var closed = new Garage { Number = "QUERY-CLOSED" };
        var excluded = new Garage { Number = "QUERY-EXCLUDED" };
        var device = new MeterDevice { Garage = ready, MeterKind = kind, SerialNumber = "query-baseline", InstalledOn = month, InitialValue = 0m };
        var connection = new GarageMeterConnection { Garage = ready, MeterKind = kind, ConnectedOn = month, BaselineMeterDevice = device };
        var pendingConnection = new GarageMeterConnection { Garage = pending, MeterKind = kind, ConnectedOn = month.AddMonths(2) };
        var closedConnection = new GarageMeterConnection { Garage = closed, MeterKind = kind, ConnectedOn = month.AddMonths(-2), ClosedOn = month.AddDays(5) };
        MeterReading Reading(Garage garage, DateOnly period, string? meterKind = null, bool canceled = false) => new()
        {
            Garage = garage,
            MeterKind = meterKind ?? kind,
            AccountingMonth = period,
            ReadingDate = period.AddDays(20),
            PreviousValue = 10.125m,
            CurrentValue = 23.5m,
            Consumption = 13.375m,
            IsCanceled = canceled
        };
        var first = Reading(ready, month);
        var next = Reading(ready, month.AddMonths(1));
        var historical = Reading(closed, month);
        context.AddRange(ready, pending, closed, excluded, device, connection, pendingConnection, closedConnection,
            new GarageMeterConnection { Garage = excluded, MeterKind = kind, ConnectedOn = month },
            first, next, historical, Reading(ready, month.AddMonths(-1)), Reading(ready, month.AddMonths(2)),
            Reading(ready, month, canceled: true), Reading(ready, month, MeterKinds.Water), Reading(excluded, month));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        capture.Commands.Clear();

        var repository = new EfMeterReadingRepository(context);
        var result = await repository.GetAccrualPeriodDataAsync([ready.Id, pending.Id, closed.Id], [kind], month, month.AddMonths(1), CancellationToken.None);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccountingMonth", command, StringComparison.Ordinal);
        Assert.Contains("GarageId", command, StringComparison.Ordinal);
        Assert.Contains("MeterKind", command, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN", command, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { first.Id, next.Id, historical.Id }.Order(), result.Readings.Select(item => item.Id).Order());
        Assert.All(result.Readings, reading =>
        {
            Assert.Equal(kind, reading.MeterKind);
            Assert.Equal((10.125m, 23.5m, 13.375m), (reading.PreviousValue, reading.CurrentValue, reading.Consumption));
            Assert.Equal(reading.AccountingMonth.AddDays(20), reading.ReadingDate);
        });
        Assert.Equal(new[] { connection.Id, pendingConnection.Id }.Order(), result.Connections.Select(item => item.Id).Order());
        Assert.Equal(device.Id, result.Connections.Single(item => item.GarageId == ready.Id).BaselineMeterDeviceId);
        var withoutReading = result.Connections.Single(item => item.GarageId == pending.Id);
        Assert.Equal(month.AddMonths(2), withoutReading.ConnectedOn);
        Assert.Null(withoutReading.BaselineMeterDeviceId);
        Assert.Empty(context.ChangeTracker.Entries());

        capture.Commands.Clear();
        foreach (var empty in new[]
        {
            await repository.GetAccrualPeriodDataAsync([], [kind], month, month, CancellationToken.None),
            await repository.GetAccrualPeriodDataAsync([ready.Id], [], month, month, CancellationToken.None),
            await repository.GetAccrualPeriodDataAsync([ready.Id], [kind], month.AddMonths(1), month, CancellationToken.None)
        })
        {
            Assert.Empty(empty.Readings);
            Assert.Empty(empty.Connections);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetAccrualPeriodDataAsync([ready.Id], [kind], month, month, cancellation.Token));
        Assert.Empty(capture.Commands);
    }

    private sealed class SelectCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
