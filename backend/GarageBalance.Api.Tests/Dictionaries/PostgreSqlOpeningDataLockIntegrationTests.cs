using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlOpeningDataLockIntegrationTests
{
    [PostgreSqlFact]
    public async Task OpeningDataLocks_DetectFinancialAndMeterHistoryOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage
        {
            Number = "OPENING-LOCK-1",
            StartingBalance = 100m,
            InitialWaterMeterValue = 10m,
            InitialElectricityMeterValue = 20m
        };
        var group = new SupplierGroup { Name = "Opening lock suppliers" };
        var supplier = new Supplier { Name = "Opening lock supplier", Group = group, StartingBalance = 200m };
        var incomeType = new IncomeType { Name = "Opening lock income", Code = "other_income" };
        var expenseType = new ExpenseType { Name = "Opening lock expense", Code = "other_expense" };

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                garage,
                group,
                supplier,
                incomeType,
                expenseType,
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 7, 1),
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    Amount = 50m,
                    Garage = garage,
                    IncomeType = incomeType
                },
                new MeterReading
                {
                    Garage = garage,
                    MeterKind = MeterKinds.Water,
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    ReadingDate = new DateOnly(2026, 7, 20),
                    PreviousValue = 10m,
                    CurrentValue = 12m,
                    Consumption = 2m
                },
                new MeterReading
                {
                    Garage = garage,
                    MeterKind = MeterKinds.Electricity,
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    ReadingDate = new DateOnly(2026, 7, 20),
                    PreviousValue = 20m,
                    CurrentValue = 25m,
                    Consumption = 5m
                },
                new SupplierAccrual
                {
                    Supplier = supplier,
                    ExpenseType = expenseType,
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    Amount = 75m,
                    Source = "manual"
                });
            await setupContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var garageLock = await new EfGarageRepository(queryContext)
            .GetOpeningDataLockAsync(garage.Id, CancellationToken.None);
        var supplierLock = await new EfSupplierRepository(queryContext)
            .HasFinancialHistoryAsync(supplier.Id, CancellationToken.None);

        Assert.True(garageLock.HasFinancialHistory);
        Assert.True(garageLock.HasWaterMeterHistory);
        Assert.True(garageLock.HasElectricityMeterHistory);
        Assert.True(supplierLock);
    }

    [PostgreSqlFact]
    public async Task OpeningBalanceAdjustments_SerializeConcurrentGarageCorrections()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage { Number = "OPENING-ADJUST-RACE", StartingBalance = 100m };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.Garages.Add(garage);
            await setupContext.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = DictionaryServiceTestFactory.Create(firstContext);
        var secondService = DictionaryServiceTestFactory.Create(secondContext);

        var results = await Task.WhenAll(
            firstService.AdjustGarageOpeningBalanceAsync(
                garage.Id,
                new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 1), 120m, "Первая сверка"),
                null,
                CancellationToken.None),
            secondService.AdjustGarageOpeningBalanceAsync(
                garage.Id,
                new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 2), 140m, "Вторая сверка"),
                null,
                CancellationToken.None));

        Assert.All(results, result => Assert.True(result.Succeeded));
        await using var assertionContext = database.CreateContext();
        var documents = await assertionContext.OpeningBalanceAdjustments
            .Where(item => item.TargetId == garage.Id)
            .ToListAsync();
        Assert.Equal(2, documents.Count);
        var firstDocument = Assert.Single(documents, item => item.PreviousAmount == 100m);
        var secondDocument = Assert.Single(documents, item => item.PreviousAmount == firstDocument.NewAmount);
        var finalBalance = await assertionContext.Garages.Where(item => item.Id == garage.Id).Select(item => item.StartingBalance).SingleAsync();
        Assert.Equal(secondDocument.NewAmount, finalBalance);
    }
}
