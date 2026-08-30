using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMeterReadingYearGridMigrationTests
{
    private const string PreviousMigration = "20260829181340_OptimizeAppReleaseVersionLookup";

    [PostgreSqlFact]
    public async Task MigrationBackfillsHistoricalReplacementMarkersBeforeCompactQueryUsesThem()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var garage = new Garage { Number = "MIG-77", PeopleCount = 1, FloorCount = 1 };
        var oldDevice = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "MIG-OLD-77",
            InstalledOn = new DateOnly(2044, 1, 1),
            RemovedOn = new DateOnly(2045, 1, 31),
            InitialValue = 0m,
            FinalValue = 100m
        };
        var replacementDevice = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "MIG-NEW-77",
            InstalledOn = new DateOnly(2045, 2, 1),
            InitialValue = 0m
        };
        var historicalReplacement = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            MeterDevice = replacementDevice,
            AccountingMonth = new DateOnly(2045, 2, 1),
            ReadingDate = new DateOnly(2045, 2, 20),
            CurrentValue = 5m,
            IsMeterReplacement = false
        };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(garage, oldDevice, replacementDevice, historicalReplacement);
            await seedContext.SaveChangesAsync();
        }

        await using (var upgradeContext = database.CreateContext())
        {
            await upgradeContext.GetService<IMigrator>().MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.MeterReadings
            .AsNoTracking()
            .SingleAsync(reading => reading.Id == historicalReplacement.Id);
        Assert.True(persisted.IsMeterReplacement);
    }
}
