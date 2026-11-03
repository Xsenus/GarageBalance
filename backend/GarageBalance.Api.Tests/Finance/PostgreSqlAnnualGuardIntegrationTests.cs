using System.Data;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAnnualGuardIntegrationTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private const string EpochKey = "finance.annual_accrual_guard_epoch";

    [PostgreSqlFact]
    public async Task HeldYearBlocksCustomAnnualAndLegacyButAllowsMonthlyFutureDebtAndIrregular()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var monthly = new ChargeServiceSetting
        {
            Name = "Synthetic monthly",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = new IncomeType { Name = "Synthetic monthly", Code = "synthetic_monthly_guard" },
            Tariff = new Tariff { Name = "Synthetic monthly", CalculationBase = TariffCalculationBases.Fixed, Rate = 100, EffectiveFrom = September }
        };
        var debt = new IncomeType { Name = "Synthetic transfer", Code = "debt_transfer", IsSystem = true };
        db.AddRange(monthly, debt);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        db.Add(AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, 2026, September, AccrualSources.Regular));
        var held = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_accrual_held", Assert.IsType<PostgresException>(held.InnerException).ConstraintName);
        db.ChangeTracker.Clear();
        var legacy = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September, AccrualSources.Regular);
        legacy.Basis = "Legacy regular annual basis";
        db.Add(legacy);
        var legacyHeld = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_ambiguous", Assert.IsType<PostgresException>(legacyHeld.InnerException).ConstraintName);
        db.ChangeTracker.Clear();
        var inconsistentIrregular = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September, AccrualSources.Regular);
        inconsistentIrregular.IrregularPaymentId = Guid.NewGuid();
        db.Add(inconsistentIrregular);
        var irregularError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_ambiguous", FindPostgresException(irregularError).ConstraintName);
        db.ChangeTracker.Clear();
        db.Add(AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September, AccrualSources.DebtTransfer));
        var transferError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_ambiguous", FindPostgresException(transferError).ConstraintName);
        db.ChangeTracker.Clear();
        var disguisedAnnual = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September, AccrualSources.Manual);
        disguisedAnnual.Basis = "Synthetic irregular";
        db.Add(disguisedAnnual);
        var disguisedError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_ambiguous", FindPostgresException(disguisedError).ConstraintName);
        db.ChangeTracker.Clear();
        db.Add(AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, 2027, September, AccrualSources.Regular));
        var mismatchError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_accrual_held", FindPostgresException(mismatchError).ConstraintName);
        db.ChangeTracker.Clear();
        var monthlyAccrual = AccrualFor(garage.Id, monthly.IncomeTypeId!.Value, monthly.TariffId, null, September, AccrualSources.Regular);
        monthlyAccrual.IsAnnualAccrual = false;
        var debtAccrual = AccrualFor(garage.Id, debt.Id, null, null, September, AccrualSources.DebtTransfer);
        debtAccrual.IsAnnualAccrual = false;
        db.AddRange(monthlyAccrual,
            AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, 2027, new(2027, 1, 1), AccrualSources.Regular),
            debtAccrual);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.Accruals.CountAsync());
        Assert.Equal(September, (await db.Garages.SingleAsync(item => item.Id == garage.Id)).AccountingStartMonth);
    }

    [PostgreSqlFact]
    public async Task HeldCanceledAnnualCannotBeRestoredWhileChangingYearOrType()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var old = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, 2026, September, AccrualSources.Regular);
        old.IsCanceled = true;
        db.Add(old);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == old.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.IsCanceled, false).SetProperty(a => a.AccountingYear, 2027)));
        Assert.Equal("gb_annual_accrual_held", error.ConstraintName);
        Assert.True((await db.Accruals.SingleAsync(a => a.Id == old.Id)).IsCanceled);
    }

    [PostgreSqlFact]
    public async Task MissingEpochFailsClosedWithoutSavingAccrual()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.ApplicationSettings.Where(s => s.Key == EpochKey).ExecuteDeleteAsync());
        Assert.Equal("gb_annual_guard_invalid", error.ConstraintName);
        Assert.Single(await db.ApplicationSettings.Where(s => s.Key == EpochKey).ToArrayAsync());
        Assert.Empty(await db.Accruals.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task InconsistentHoldAndEpochAreRejectedAndRolledBack()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.ApplicationSettings
            .Where(s => s.Key == EpochKey).ExecuteUpdateAsync(u => u.SetProperty(s => s.JsonValue, "{\"2026\":true}")));
        Assert.Equal("gb_annual_guard_invalid", error.ConstraintName);
        Assert.Equal("{}", (await db.ApplicationSettings.SingleAsync(s => s.Key == EpochKey)).JsonValue);
    }

    [PostgreSqlFact]
    public async Task WaitingInsertFailsAfterHoldCommitsAtEveryIsolationLevel()
    {
        foreach (var level in new[] { IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead, IsolationLevel.Serializable })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var seed = database.CreateContext();
            var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(seed, September);
            seed.Add(new ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false });
            await seed.SaveChangesAsync();
            seed.ChangeTracker.Clear();
            await using var writer = database.CreateContext();
            await using var reader = database.CreateContext();
            await using var writerTx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE");
            await writer.ApplicationSettings.Where(s => s.Key == EpochKey).ExecuteUpdateAsync(u => u.SetProperty(s => s.JsonValue, "{\"2026\":true}"));
            await writer.ApplicationSettings.Where(s => s.Key == "finance.annual_accrual_hold.2026").ExecuteUpdateAsync(u => u.SetProperty(s => s.BooleanValue, true));
            await using var readerTx = await reader.Database.BeginTransactionAsync(level);
            await reader.Database.ExecuteSqlRawAsync("SELECT 1");
            reader.Add(AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, 2026, September, AccrualSources.Regular));
            var waiting = reader.SaveChangesAsync();
            await Task.Delay(250);
            Assert.False(waiting.IsCompleted);
            await writerTx.CommitAsync();
            var failure = await Record.ExceptionAsync(() => waiting);
            Assert.NotNull(failure);
            var postgres = FindPostgresException(failure);
            Assert.Equal(level == IsolationLevel.ReadCommitted ? "23514" : "40001", postgres.SqlState);
            await readerTx.RollbackAsync();
            await using var verify = database.CreateContext();
            Assert.Empty(await verify.Accruals.ToArrayAsync());
        }
    }

    [PostgreSqlFact]
    public async Task LegacyCampaignWithoutYearOrTariffCannotBypassHoldViaOtherIncome()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var income = await db.IncomeTypes.SingleAsync(i => i.Code == "other_income");
        db.Add(new ChargeServiceSetting
        {
            Name = "Synthetic archived annual campaign",
            IsRegular = true,
            IsArchived = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 9,
            IncomeTypeId = income.Id,
            TariffId = annual.TariffId
        });
        var campaign = new FeeCampaign
        {
            Name = "Synthetic ordinary campaign after archive",
            IncomeType = income,
            StartsOn = September,
            EndsOn = September.AddMonths(1).AddDays(-1),
            ContributionAmount = 100m,
            TargetAmount = 1000m,
            AppliesToAllGarages = true
        };
        db.Add(campaign);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var campaignAccrual = AccrualFor(garage.Id, income.Id, null, null, September, AccrualSources.FeeCampaign);
        campaignAccrual.FeeCampaignId = campaign.Id;
        db.Add(campaignAccrual);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_ambiguous", FindPostgresException(error).ConstraintName);
        db.ChangeTracker.Clear();
        var ordinary = AccrualFor(garage.Id, income.Id, null, null, September, AccrualSources.FeeCampaign);
        ordinary.FeeCampaignId = campaign.Id;
        ordinary.IsAnnualAccrual = false;
        db.Add(ordinary);
        await db.SaveChangesAsync();
        Assert.Single(await db.Accruals.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task PolicyAndEpochMismatchBlocksInsertInsideSameTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        db.Add(new ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false });
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.ApplicationSettings.Where(s => s.Key == "finance.annual_accrual_hold.2026")
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.BooleanValue, true));
        db.Add(AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, 2026, September, AccrualSources.Regular));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_invalid", FindPostgresException(error).ConstraintName);
        await tx.RollbackAsync();
        await using var verify = database.CreateContext();
        Assert.False((await verify.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_hold.2026")).BooleanValue);
        Assert.Empty(await verify.Accruals.ToArrayAsync());
        await using var reverse = database.CreateContext();
        await using var reverseTx = await reverse.Database.BeginTransactionAsync();
        await reverse.ApplicationSettings.Where(s => s.Key == EpochKey)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.JsonValue, "{\"2026\":true}"));
        reverse.Add(AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, 2026, September, AccrualSources.Regular));
        var reverseError = await Assert.ThrowsAsync<DbUpdateException>(() => reverse.SaveChangesAsync());
        Assert.Equal("gb_annual_guard_invalid", FindPostgresException(reverseError).ConstraintName);
        await reverseTx.RollbackAsync();
        await using var verifyReverse = database.CreateContext();
        Assert.Equal("{}", (await verifyReverse.ApplicationSettings.SingleAsync(s => s.Key == EpochKey)).JsonValue);
        Assert.Empty(await verifyReverse.Accruals.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task OrdinaryOtherIncomeCampaignIsAllowedDuringAnnualHold()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, _) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var income = await db.IncomeTypes.SingleAsync(i => i.Code == "other_income");
        var campaign = new FeeCampaign
        {
            Name = "Synthetic ordinary campaign",
            IncomeType = income,
            StartsOn = September,
            EndsOn = September.AddMonths(1).AddDays(-1),
            ContributionAmount = 100m,
            TargetAmount = 1000m,
            AppliesToAllGarages = true
        };
        db.Add(campaign);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var accrual = AccrualFor(garage.Id, income.Id, null, null, September, AccrualSources.FeeCampaign);
        accrual.FeeCampaignId = campaign.Id;
        accrual.IsAnnualAccrual = false;
        db.Add(accrual);
        await db.SaveChangesAsync();
        Assert.Single(await db.Accruals.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task AmbiguousMixedMonthlyAndAnnualSettingsFailClosedEvenWhenArchived()
    {
        foreach (var archived in new[] { false, true })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var db = database.CreateContext();
            var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
            annual.IsArchived = archived;
            db.Add(new ChargeServiceSetting
            {
                Name = "Synthetic mixed monthly",
                IsRegular = true,
                IsArchived = archived,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                IncomeTypeId = annual.IncomeTypeId,
                TariffId = annual.TariffId
            });
            await db.SaveChangesAsync();
            await EnableHoldAsync(db, 2026);
            db.ChangeTracker.Clear();
            db.Add(AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("gb_annual_guard_ambiguous", FindPostgresException(error).ConstraintName);
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Accruals.ToArrayAsync());
        }
    }

    [PostgreSqlFact]
    public async Task PersistedClassificationAllowsMonthlyButBlocksAnnualAcrossMixedSettingsAndRestores()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        db.Add(new ChargeServiceSetting
        {
            Name = "Synthetic mixed monthly",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeTypeId = annual.IncomeTypeId,
            TariffId = annual.TariffId
        });
        var historical = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular);
        historical.IsAnnualAccrual = false;
        historical.IsCanceled = true;
        db.Add(historical);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var monthly = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September.AddMonths(1), AccrualSources.Regular);
        monthly.IsAnnualAccrual = false;
        var future = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, 2027, new(2027, 1, 1), AccrualSources.Regular);
        future.IsAnnualAccrual = true;
        db.AddRange(monthly, future);
        await db.SaveChangesAsync();
        var restore = await db.Accruals.Where(a => a.Id == historical.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsCanceled, false));
        Assert.Equal(1, restore);
        Assert.Equal(1, await db.Accruals.Where(a => a.Id == monthly.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsCanceled, true)));
        Assert.Equal(1, await db.Accruals.Where(a => a.Id == future.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Amount, 101m)));
        db.ChangeTracker.Clear();
        var blocked = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September.AddMonths(2), AccrualSources.Regular);
        blocked.IsAnnualAccrual = true;
        db.Add(blocked);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("gb_annual_accrual_held", FindPostgresException(error).ConstraintName);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Accruals.CountAsync());
    }

    [PostgreSqlFact]
    public async Task RouteChangesCannotConvertEitherDirectionWhenAnnualYearIsHeld()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var monthly = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Manual);
        monthly.IsAnnualAccrual = false;
        var yearly = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null, September.AddMonths(1), AccrualSources.Manual);
        yearly.IsAnnualAccrual = true;
        db.AddRange(monthly, yearly);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var toAnnual = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == monthly.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsAnnualAccrual, true)));
        Assert.Equal("gb_annual_accrual_held", toAnnual.ConstraintName);
        var toMonthly = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == yearly.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsAnnualAccrual, false)));
        Assert.Equal("gb_annual_accrual_held", toMonthly.ConstraintName);
        Assert.False((await db.Accruals.SingleAsync(a => a.Id == monthly.Id)).IsAnnualAccrual);
        Assert.True((await db.Accruals.SingleAsync(a => a.Id == yearly.Id)).IsAnnualAccrual);
    }

    [PostgreSqlFact]
    public async Task ClassificationMigrationPreservesUnknownHistoryAndRejectsLossyRollback()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var historical = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular);
        db.Add(historical);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync("20261102085829_AnnualAccrualTransactionGuard");
        await db.Database.MigrateAsync();
        var restored = await db.Accruals.SingleAsync(a => a.Id == historical.Id);
        Assert.Null(restored.IsAnnualAccrual);
        Assert.Equal(historical.Amount, restored.Amount);
        var newMonthly = AccrualFor(garage.Id, annual.IncomeTypeId.Value, annual.TariffId, null,
            September.AddMonths(1), AccrualSources.Manual);
        newMonthly.IsAnnualAccrual = false;
        db.Add(newMonthly);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync("20261102085829_AnnualAccrualTransactionGuard"));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        Assert.False((await db.Accruals.SingleAsync(a => a.Id == newMonthly.Id)).IsAnnualAccrual);
        Assert.Null((await db.Accruals.SingleAsync(a => a.Id == historical.Id)).IsAnnualAccrual);
    }

    [PostgreSqlFact]
    public async Task UnknownHistoricalProvenanceCannotBeDiscardedByReroutingWithoutHold()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var other = new IncomeType { Name = "Synthetic new route", Code = "synthetic_new_route" };
        var newTariff = new Tariff
        {
            Name = "Synthetic new tariff",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 50m,
            EffectiveFrom = September
        };
        var legacy = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Manual);
        db.AddRange(other, newTariff, legacy);
        await db.SaveChangesAsync();
        var firstStep = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsAnnualAccrual, false)));
        Assert.Equal("gb_annual_guard_ambiguous", firstStep.ConstraintName);
        var tariffChange = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.TariffId, newTariff.Id)));
        Assert.Equal("gb_annual_guard_ambiguous", tariffChange.ConstraintName);
        var monthChange = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.AccountingMonth, September.AddMonths(1))));
        Assert.Equal("gb_annual_guard_ambiguous", monthChange.ConstraintName);
        Assert.Equal(1, await db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Amount, 101m)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IncomeTypeId, other.Id).SetProperty(a => a.IsAnnualAccrual, false)));
        Assert.Equal("gb_annual_guard_ambiguous", error.ConstraintName);
        db.ChangeTracker.Clear();
        var unchanged = await db.Accruals.SingleAsync(a => a.Id == legacy.Id);
        Assert.Equal(annual.IncomeTypeId, unchanged.IncomeTypeId);
        Assert.Equal(101m, unchanged.Amount);
        Assert.Null(unchanged.IsAnnualAccrual);
    }

    [PostgreSqlFact]
    public async Task UnknownHistoricalAccrualCannotBeCanceledDuringHoldEvenWithMixedSettings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        db.Add(new ChargeServiceSetting
        {
            Name = "Synthetic mixed monthly for cancellation",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeTypeId = annual.IncomeTypeId,
            TariffId = annual.TariffId
        });
        var legacy = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular);
        db.Add(legacy);
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsCanceled, true)));
        Assert.Equal("gb_annual_guard_ambiguous", error.ConstraintName);
        Assert.False((await db.Accruals.SingleAsync(a => a.Id == legacy.Id)).IsCanceled);
    }

    [PostgreSqlFact]
    public async Task ReadCommittedStaleAutomaticCancellationCannotCrossHoldCommit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var seed = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(seed, September);
        var legacy = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular);
        seed.AddRange(legacy, new ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false });
        await seed.SaveChangesAsync();
        await using var writer = database.CreateContext();
        await using var reader = database.CreateContext();
        await using var tx = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE");
        await writer.ApplicationSettings.Where(s => s.Key == EpochKey).ExecuteUpdateAsync(u => u.SetProperty(s => s.JsonValue, "{\"2026\":true}"));
        await writer.ApplicationSettings.Where(s => s.Key == "finance.annual_accrual_hold.2026").ExecuteUpdateAsync(u => u.SetProperty(s => s.BooleanValue, true));
        var pending = reader.Accruals.Where(a => a.Id == legacy.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsCanceled, true));
        await Task.Delay(250);
        Assert.False(pending.IsCompleted);
        await tx.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => pending);
        Assert.Equal("gb_annual_guard_ambiguous", error.ConstraintName);
        await using var verify = database.CreateContext();
        Assert.False((await verify.Accruals.SingleAsync(a => a.Id == legacy.Id)).IsCanceled);
    }

    [PostgreSqlFact]
    public async Task UnknownHistoricalAccrualCannotBecomeMonthlyByChangingServiceSettings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var legacy = AccrualFor(garage.Id, annual.IncomeTypeId!.Value, annual.TariffId, null, September, AccrualSources.Regular);
        db.Add(legacy);
        await db.SaveChangesAsync();
        annual.PeriodicityMonths = 1;
        await db.SaveChangesAsync();
        await EnableHoldAsync(db, 2026);
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Accruals.Where(a => a.Id == legacy.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Amount, 101m)));
        Assert.Equal("gb_annual_guard_ambiguous", error.ConstraintName);
        Assert.Equal(100m, (await db.Accruals.SingleAsync(a => a.Id == legacy.Id)).Amount);
    }

    [PostgreSqlFact]
    public async Task StaleSnapshotPaymentServiceRollsBackFinancialChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId, incomeId, campaignId, fundId;
        Guid[] originalAuditIds;
        await using (var seed = database.CreateContext())
        {
            var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(seed, September);
            var fund = new Fund { Name = "Synthetic annual fund", NormalizedName = "SYNTHETIC ANNUAL FUND" };
            annual.IncomeType!.DestinationFund = fund;
            var campaign = new FeeCampaign
            {
                Name = "Synthetic annual campaign",
                IncomeType = annual.IncomeType,
                StartsOn = September,
                EndsOn = September.AddMonths(1).AddDays(-1),
                ContributionAmount = 100m,
                TargetAmount = 1000m,
                AppliesToAllGarages = true
            };
            campaign.ParticipantGarages.Add(new FeeCampaignGarage { Garage = garage });
            seed.AddRange(fund, campaign, new ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false });
            await seed.SaveChangesAsync();
            garageId = garage.Id; incomeId = annual.IncomeTypeId!.Value; campaignId = campaign.Id; fundId = fund.Id;
            originalAuditIds = await seed.AuditEvents.OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync();
        }
        await using var reader = database.CreateContext();
        await using var snapshot = await reader.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        Assert.False((await reader.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == "finance.annual_accrual_hold.2026")).BooleanValue);
        await using (var writer = database.CreateContext())
        await using (var tx = await writer.Database.BeginTransactionAsync())
        {
            await writer.ApplicationSettings.Where(s => s.Key == EpochKey).ExecuteUpdateAsync(u => u.SetProperty(s => s.JsonValue, "{\"2026\":true}"));
            await writer.ApplicationSettings.Where(s => s.Key == "finance.annual_accrual_hold.2026").ExecuteUpdateAsync(u => u.SetProperty(s => s.BooleanValue, true));
            await tx.CommitAsync();
        }
        var service = FinanceServiceTestFactory.Create(reader, businessDate: new(2026, 11, 1));
        var error = await Record.ExceptionAsync(() => service.CreateIncomeAsync(
            new(garageId, incomeId, September.AddDays(10), September, 100, null, null, null, campaignId), null, default));
        Assert.NotNull(error);
        Assert.Equal("40001", FindPostgresException(error).SqlState);
        await snapshot.RollbackAsync();
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Accruals.ToArrayAsync());
        Assert.Empty(await verify.FinancialOperations.ToArrayAsync());
        Assert.Empty(await verify.AccrualPaymentAllocations.ToArrayAsync());
        Assert.Empty(await verify.FundOperations.ToArrayAsync());
        Assert.Equal(originalAuditIds, await verify.AuditEvents.OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync());
        Assert.Equal(0m, (await verify.Funds.SingleAsync(f => f.Id == fundId)).Balance);
    }

    private static PostgresException FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres) return postgres;
        }
        throw new Xunit.Sdk.XunitException("Expected PostgreSQL exception in failure chain");
    }

    private static Accrual AccrualFor(Guid garage, Guid income, Guid? tariff, int? year, DateOnly month, string source) => new()
    {
        GarageId = garage,
        IncomeTypeId = income,
        TariffId = tariff,
        AccountingMonth = month,
        AccountingYear = year,
        DueDate = month.AddDays(10),
        OverdueFromDate = month.AddDays(11),
        Amount = 100,
        Source = source
    };

    private static async Task EnableHoldAsync(GarageBalanceDbContext db, int year)
    {
        var epoch = await db.ApplicationSettings.SingleAsync(s => s.Key == EpochKey);
        epoch.JsonValue = $"{{\"{year}\":true}}";
        db.Add(new ApplicationSetting { Key = $"finance.annual_accrual_hold.{year}", BooleanValue = true });
        await db.SaveChangesAsync();
    }
}
