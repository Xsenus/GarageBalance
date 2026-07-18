using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageIncomeWorksheetIntegrationTests
{
    [PostgreSqlFact]
    public async Task Worksheet_ProjectsAnnualObligationUntilFullPaymentAndReopensItAfterCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var membershipType = await context.IncomeTypes.SingleAsync(type => type.Code == "membership");
        var garage = new Garage
        {
            Number = "PG-ANNUAL-OBLIGATION",
            PeopleCount = 1,
            FloorCount = 1
        };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);

        var accrual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, membershipType.Id, new DateOnly(2026, 1, 1), 700m, "regular", null),
            null,
            CancellationToken.None);
        Assert.True(accrual.Succeeded, accrual.ErrorMessage);
        var annualAccrualId = accrual.Value!.Id;
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, membershipType.Id, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1), 300m, null, null),
            null,
            CancellationToken.None)).Succeeded);

        var partial = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(partial.Succeeded, partial.ErrorMessage);
        Assert.Equal(6, partial.Value!.Rows.Count(row => row.AnnualAccrualId == annualAccrualId));
        Assert.Equal(400m, Assert.Single(partial.Value.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth == new DateOnly(2026, 6, 1)).Debt);

        var fullPayment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, membershipType.Id, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 1), 400m, null, null),
            null,
            CancellationToken.None);
        Assert.True(fullPayment.Succeeded, fullPayment.ErrorMessage);
        var paid = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        Assert.DoesNotContain(paid.Value!.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth > new DateOnly(2026, 4, 1));

        var canceled = await service.CancelOperationAsync(
            fullPayment.Value!.Id,
            new CancelFinanceEntryRequest("Проверка возврата годового остатка"),
            null,
            CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        var reopened = await service.GetGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        Assert.True(reopened.Succeeded, reopened.ErrorMessage);
        Assert.Equal(400m, Assert.Single(reopened.Value!.Rows, row =>
            row.AnnualAccrualId == annualAccrualId && row.AccountingMonth == new DateOnly(2026, 6, 1)).Debt);
    }

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
        var row = Assert.Single(result.Value!.Rows, item => item.IncomeTypeId == electricityType.Id);
        Assert.Equal(MeterKinds.Electricity, row.MeterKind);
        Assert.Equal(reading.Id, row.MeterReadingId);
        Assert.Equal(reading.Version, row.MeterReadingVersion);
        Assert.Equal(reading.ReadingDate, row.MeterReadingDate);
        Assert.Equal(118m, row.MeterValue);
        Assert.Equal(18m, row.MeterConsumption);
        var missingWater = Assert.Single(result.Value.Rows, item => item.MeterKind == MeterKinds.Water);
        Assert.Null(missingWater.MeterValue);
        Assert.Equal(0m, missingWater.AccrualAmount);
    }
}
