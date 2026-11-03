using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlPreservedBaselineMigrationTests
{
    [PostgreSqlFact]
    public async Task Migration_PreservesValuesAndRowsAndRefusesToDropAnInUseBaseline()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync("20261030165821_AddGarageDiscountAllocations");
        await db.Database.MigrateAsync();
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        Assert.Equal(980m, (await db.MeterDevices.SingleAsync(device => device.Id == seed.Original.Id)).InitialValue);
        await db.MeterDevices.Where(device => device.Id == seed.Original.Id).ExecuteUpdateAsync(setters => setters.SetProperty(device => device.IsAccountingBaseline, true));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.MeterDevices.Where(device => device.Id == seed.Current.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.IsAccountingBaseline, true)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync("20261030165821_AddGarageDiscountAllocations"));
        Assert.Equal(PostgresErrorCodes.RaiseException, rollback.SqlState);
        Assert.True(await db.MeterDevices.AnyAsync(device => device.Id == seed.Original.Id && device.IsAccountingBaseline));
    }
}
