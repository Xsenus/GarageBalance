using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageIncomeWorksheetIntegrationTests
{
    [PostgreSqlFact]
    public async Task Worksheet_ReturnsMeterIdentityAndVersionForInlineEditing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var electricityType = await context.IncomeTypes.SingleAsync(type => type.Code == MeterKinds.Electricity);
        var garage = new Garage
        {
            Number = "PG-INLINE-METER",
            PeopleCount = 1,
            FloorCount = 1,
            InitialElectricityMeterValue = 100m
        };
        var reading = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = new DateOnly(2026, 7, 1),
            ReadingDate = new DateOnly(2026, 7, 17),
            PreviousValue = 100m,
            CurrentValue = 118m,
            Consumption = 18m
        };
        context.AddRange(
            garage,
            reading,
            new Accrual
            {
                Garage = garage,
                IncomeType = electricityType,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 134.46m,
                Source = "regular"
            });
        await context.SaveChangesAsync();

        var service = FinanceServiceTestFactory.Create(context);
        var result = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows);
        Assert.Equal(MeterKinds.Electricity, row.MeterKind);
        Assert.Equal(reading.Id, row.MeterReadingId);
        Assert.Equal(reading.Version, row.MeterReadingVersion);
        Assert.Equal(reading.ReadingDate, row.MeterReadingDate);
        Assert.Equal(118m, row.MeterValue);
        Assert.Equal(18m, row.MeterConsumption);
    }
}
