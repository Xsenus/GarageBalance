using System.Data.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlPeopleCountHistoryTests
{
    [PostgreSqlFact]
    public async Task MigrationCopiesLegacyCountsWithoutChangingFinancialSnapshotsAndIsRepeatable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20260905113309_AddGarageBusinessRegistrationDate");
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "MIGRATION-PEOPLE", PeopleCount = 2 };
        var archived = new Garage { Number = "MIGRATION-ZERO", PeopleCount = 0, IsArchived = true };
        var income = new IncomeType { Name = "История мусора", Code = "history_migration_test" };
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = income,
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 250,
            Source = AccrualSources.Regular,
            CalculationDetailsJson = "{\"version\":4,\"totalAmount\":250}"
        };
        context.AddRange(garage, archived, accrual);
        await context.SaveChangesAsync();
        var snapshotBeforeMigration = await context.Accruals.AsNoTracking().Where(item => item.Id == accrual.Id)
            .Select(item => item.CalculationDetailsJson).SingleAsync();
        var auditIdsBeforeMigration = await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        // Isolate the migration under test: later fund migrations intentionally add audit events.
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260905122505_AddGaragePeopleCountHistory");
        await migrator.MigrateAsync("20260905122505_AddGaragePeopleCountHistory");
        context.ChangeTracker.Clear();
        var periods = await context.GaragePeopleCountPeriods.OrderBy(period => period.PeopleCount).ToArrayAsync();
        Assert.Equal([0, 2], periods.Select(period => period.PeopleCount));
        Assert.All(periods, period => Assert.Equal(DateOnly.MinValue, period.EffectiveFrom));
        var preserved = await context.Accruals.SingleAsync(item => item.Id == accrual.Id);
        Assert.Equal(250m, preserved.Amount);
        Assert.Equal(snapshotBeforeMigration, preserved.CalculationDetailsJson);
        Assert.Equal(auditIdsBeforeMigration, await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task HistoryQueryLoadsOnlyRequestedGaragesPeriodAndOnePrecedingValueInSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var selected = new Garage { Number = "BOUNDED-PEOPLE", PeopleCount = 2 };
        var other = new Garage { Number = "OTHER-PEOPLE", PeopleCount = 5 };
        var month = new DateOnly(2026, 6, 1);
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(selected, other);
            seed.GaragePeopleCountPeriods.AddRange(Enumerable.Range(-300, 365).Select(day => new GaragePeopleCountPeriod
            {
                GarageId = selected.Id,
                EffectiveFrom = month.AddDays(day),
                PeopleCount = day % 2 == 0 ? 1 : 2
            }));
            seed.Add(new GaragePeopleCountPeriod { GarageId = other.Id, EffectiveFrom = month, PeopleCount = 5 });
            await seed.SaveChangesAsync();
        }
        var capture = new ReaderCapture();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(capture).Options);
        var repository = new EfGarageRepository(context);
        var periods = await repository.GetPeopleCountPeriodsAsync([selected.Id], month, month.AddDays(29), CancellationToken.None);
        Assert.Equal(30, periods.Count);
        Assert.All(periods, period => Assert.Equal(selected.Id, period.GarageId));
        Assert.Equal(month, periods[0].EffectiveFrom);
        Assert.Equal(month.AddDays(29), periods[^1].EffectiveFrom);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("NOT EXISTS", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", command, StringComparison.OrdinalIgnoreCase);
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Empty(await repository.GetPeopleCountPeriodsAsync([], month, month, CancellationToken.None));
        Assert.Empty(await repository.GetPeopleCountPeriodsAsync([selected.Id], month.AddDays(1), month, CancellationToken.None));
        Assert.Single(capture.Commands);
        var afterHistory = await repository.GetPeopleCountPeriodsAsync([selected.Id], month.AddDays(1000), month.AddDays(1001), CancellationToken.None);
        Assert.Equal(month.AddDays(64), Assert.Single(afterHistory).EffectiveFrom);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetPeopleCountPeriodsAsync([selected.Id], month, month, canceled.Token));
    }

    [PostgreSqlFact]
    public async Task BatchWorksheetAndTariffRecalculationUseSameDailyHistoryAndProtectPaidAmounts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2026, 6, 1);
        var income = new IncomeType { Name = "Мусор по дням", Code = "daily_people_test" };
        var tariff = new Tariff { Name = "Дневной контроль", CalculationBase = TariffCalculationBases.People, Rate = 130, EffectiveFrom = month };
        context.Add(new ChargeServiceSetting
        {
            Name = income.Name,
            IncomeType = income,
            Tariff = tariff,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            UnitName = "чел."
        });
        await context.SaveChangesAsync();
        var ids = new List<Guid>();
        foreach (var number in new[] { "DAILY-PAID", "DAILY-UNPAID" })
        {
            var request = new UpsertGarageRequest(number, 1, 1, null, 0, null, null, null);
            var created = await DictionaryServiceTestFactory.Create(context, month).CreateGarageAsync(request, null, CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            ids.Add(created.Value!.Id);
            var changed = await DictionaryServiceTestFactory.Create(context, month.AddDays(12))
                .UpdateGarageAsync(created.Value.Id, request with { PeopleCount = 2 }, null, CancellationToken.None);
            Assert.True(changed.Succeeded, changed.ErrorMessage);
        }
        var finance = FinanceServiceTestFactory.Create(context);
        var generated = await finance.GenerateRegularAccrualsAsync(new GenerateRegularAccrualsRequest(income.Id, tariff.Id, month, null), null, CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var accruals = await context.Accruals.Where(item => item.IncomeTypeId == income.Id).ToArrayAsync();
        Assert.Equal(2, accruals.Length);
        Assert.All(accruals, accrual => Assert.Equal(208m, accrual.Amount));
        var worksheet = await finance.CalculateGarageIncomeWorksheetAsync(ids[0], new GarageIncomeWorksheetRequest(month, month), null, CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.IncomeTypeId == income.Id);
        Assert.Equal(208m, row.AccrualAmount);
        Assert.Equal([1, 2], row.CalculationDetails!.Lines.Select(line => line.PeopleCount!.Value));

        var paidAccrual = accruals.Single(item => item.GarageId == ids[0]);
        var paidSnapshot = paidAccrual.CalculationDetailsJson;
        context.Add(new AccrualPaymentAllocation
        {
            Accrual = paidAccrual,
            Amount = 100,
            FinancialOperation = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = ids[0],
                IncomeType = income,
                AccountingMonth = month,
                OperationDate = month.AddDays(29),
                Amount = 100
            }
        });
        tariff.Rate = 200;
        await context.SaveChangesAsync();
        var preview = await finance.PreviewRegularAccrualRecalculationAsync(new PreviewRegularAccrualRecalculationRequest(income.Id, tariff.Id, month), CancellationToken.None);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        Assert.Equal(1, preview.Value!.ProtectedPaidCount);
        Assert.Equal(320m, Assert.Single(preview.Value.Rows, item => !item.IsPaid).ProposedAmount);
        var applied = await finance.ApplyRegularAccrualRecalculationAsync(new ApplyRegularAccrualRecalculationRequest(
            income.Id, tariff.Id, month, preview.Value.PreviewFingerprint, "Проверка истории людей"), null, CancellationToken.None);
        Assert.True(applied.Succeeded, applied.ErrorMessage);
        Assert.Equal(208m, paidAccrual.Amount);
        Assert.Equal(paidSnapshot, paidAccrual.CalculationDetailsJson);
        Assert.Equal(320m, accruals.Single(item => item.GarageId == ids[1]).Amount);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCardEditRollsBackHistoryTogetherWithGarageAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var month = new DateOnly(2026, 6, 1);
        await using var first = database.CreateContext();
        var request = new UpsertGarageRequest("CONCURRENT-PEOPLE", 1, 1, null, 0, null, null, null);
        var created = await DictionaryServiceTestFactory.Create(first, month).CreateGarageAsync(request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garageId = created.Value!.Id;
        await using var stale = database.CreateContext();
        await stale.Garages.SingleAsync(item => item.Id == garageId);
        var changed = await DictionaryServiceTestFactory.Create(first, month.AddDays(12))
            .UpdateGarageAsync(garageId, request with { PeopleCount = 2 }, null, CancellationToken.None);
        Assert.True(changed.Succeeded, changed.ErrorMessage);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => DictionaryServiceTestFactory.Create(stale, month.AddDays(12))
            .UpdateGarageAsync(garageId, request with { PeopleCount = 3 }, null, CancellationToken.None));
        await using var verification = database.CreateContext();
        Assert.Equal(2, (await verification.Garages.SingleAsync(item => item.Id == garageId)).PeopleCount);
        var periods = await verification.GaragePeopleCountPeriods.Where(item => item.GarageId == garageId)
            .OrderBy(item => item.EffectiveFrom).ToArrayAsync();
        Assert.Equal([1, 2], periods.Select(item => item.PeopleCount));
        Assert.Single(await verification.AuditEvents.Where(item => item.Action == "dictionary.garage_updated").ToArrayAsync());
    }

    private sealed class ReaderCapture : DbCommandInterceptor
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
