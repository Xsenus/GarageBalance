using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlRegularAccrualSnapshotBackfillMigrationTests
{
    private const string PreviousMigration = "20260903004259_PreserveStaffSalaryHistory";

    [PostgreSqlFact]
    public async Task MigrationBackfillsOnlyMissingRegularSnapshotsWithoutChangingAmountsAndCanRunAgain()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
        Guid regularAccrualId;
        Guid manualAccrualId;
        Guid existingSnapshotAccrualId;
        const string existingSnapshot = "{\"version\":4,\"totalAmount\":88.00,\"lines\":[]}";
        await using (var oldContext = database.CreateContext())
        {
            var owner = new Owner { LastName = "Тестов", FirstName = "Миграция" };
            var garage = new Garage { Number = $"M-{Guid.NewGuid():N}", Owner = owner, PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = $"Услуга {Guid.NewGuid():N}", Code = $"migration_{Guid.NewGuid():N}" };
            var month = new DateOnly(2026, 8, 1);
            var regular = new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = month,
                DueDate = month.AddMonths(1).AddDays(9),
                OverdueFromDate = month.AddMonths(1).AddDays(10),
                Amount = 1234.56m,
                Source = AccrualSources.Regular,
                CalculationDetailsJson = null
            };
            var manual = new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = month.AddMonths(1),
                DueDate = month.AddMonths(2).AddDays(9),
                OverdueFromDate = month.AddMonths(2).AddDays(10),
                Amount = 77m,
                Source = AccrualSources.Manual,
                CalculationDetailsJson = null
            };
            var regularWithSnapshot = new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = month.AddMonths(2),
                DueDate = month.AddMonths(3).AddDays(9),
                OverdueFromDate = month.AddMonths(3).AddDays(10),
                Amount = 88m,
                Source = AccrualSources.Regular,
                CalculationDetailsJson = existingSnapshot
            };
            regularAccrualId = regular.Id;
            manualAccrualId = manual.Id;
            existingSnapshotAccrualId = regularWithSnapshot.Id;
            oldContext.AddRange(owner, garage, incomeType, regular, manual, regularWithSnapshot);
            await oldContext.SaveChangesAsync();
            await oldContext.Database.MigrateAsync();
        }

        string firstSnapshot;
        await using (var migratedContext = database.CreateContext())
        {
            var regular = await migratedContext.Accruals.SingleAsync(item => item.Id == regularAccrualId);
            var manual = await migratedContext.Accruals.SingleAsync(item => item.Id == manualAccrualId);
            var regularWithSnapshot = await migratedContext.Accruals.SingleAsync(item => item.Id == existingSnapshotAccrualId);
            Assert.Equal(1234.56m, regular.Amount);
            Assert.Null(manual.CalculationDetailsJson);
            Assert.Equal(existingSnapshot, regularWithSnapshot.CalculationDetailsJson);
            var details = RegularAccrualCalculator.Deserialize(regular.CalculationDetailsJson);
            Assert.NotNull(details);
            Assert.Equal(0, details.Version);
            Assert.Equal(regular.AccountingMonth, details.AccountingMonth);
            Assert.Equal(regular.Amount, details.TotalAmount);
            Assert.Empty(details.Lines);
            firstSnapshot = regular.CalculationDetailsJson!;
            await migratedContext.Database.MigrateAsync();
        }

        await using var repeatedContext = database.CreateContext();
        var repeated = await repeatedContext.Accruals.SingleAsync(item => item.Id == regularAccrualId);
        Assert.Equal(1234.56m, repeated.Amount);
        Assert.Equal(firstSnapshot, repeated.CalculationDetailsJson);
        Assert.Equal(existingSnapshot, (await repeatedContext.Accruals.SingleAsync(item => item.Id == existingSnapshotAccrualId)).CalculationDetailsJson);

        await repeatedContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        repeatedContext.ChangeTracker.Clear();
        Assert.Null((await repeatedContext.Accruals.SingleAsync(item => item.Id == regularAccrualId)).CalculationDetailsJson);
        Assert.Equal(existingSnapshot, (await repeatedContext.Accruals.SingleAsync(item => item.Id == existingSnapshotAccrualId)).CalculationDetailsJson);
        await repeatedContext.Database.MigrateAsync();
        repeatedContext.ChangeTracker.Clear();
        Assert.NotNull((await repeatedContext.Accruals.SingleAsync(item => item.Id == regularAccrualId)).CalculationDetailsJson);
    }
}
