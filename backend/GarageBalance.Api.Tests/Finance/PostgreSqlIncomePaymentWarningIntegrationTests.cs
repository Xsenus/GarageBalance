using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIncomePaymentWarningIntegrationTests
{
    [PostgreSqlFact]
    public async Task WarningQuery_UsesPreviousActiveElectricityPaymentAndCalendarDayBoundary()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = "PG-ELECTRICITY-WARNING", PeopleCount = 1, FloorCount = 1 };
            var electricity = await seedContext.IncomeTypes.SingleAsync(item => item.Code == MeterKinds.Electricity);
            seedContext.Garages.Add(garage);
            seedContext.FinancialOperations.AddRange(
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 6, 1),
                    AccountingMonth = new DateOnly(2026, 6, 1),
                    Amount = 500m,
                    DocumentNumber = "PG-ELECTRICITY-FIRST",
                    Garage = garage,
                    IncomeType = electricity
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 6, 20),
                    AccountingMonth = new DateOnly(2026, 6, 1),
                    Amount = 500m,
                    DocumentNumber = "PG-ELECTRICITY-CANCELED",
                    Garage = garage,
                    IncomeType = electricity,
                    IsCanceled = true
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 7, 20),
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    Amount = 500m,
                    DocumentNumber = "PG-ELECTRICITY-FUTURE",
                    Garage = garage,
                    IncomeType = electricity
                });
            await seedContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = electricity.Id;
        }

        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);
        var day29 = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(garageId, incomeTypeId, new DateOnly(2026, 6, 30)),
            CancellationToken.None);
        var day30 = await service.GetIncomePaymentWarningAsync(
            new IncomePaymentWarningRequest(garageId, incomeTypeId, new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        Assert.True(day29.Succeeded, day29.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 6, 1), day29.Value!.PreviousPaymentDate);
        Assert.Equal(29, day29.Value.DaysSincePreviousPayment);
        Assert.True(day29.Value.RequiresConfirmation);
        Assert.True(day30.Succeeded, day30.ErrorMessage);
        Assert.Equal(30, day30.Value!.DaysSincePreviousPayment);
        Assert.False(day30.Value.RequiresConfirmation);
    }
}
