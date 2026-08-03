using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

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
}
