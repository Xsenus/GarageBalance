using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AnnualAccrualForecastTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly November = new(2026, 11, 9);

    [PostgreSqlFact]
    public async Task ForecastIsReadOnlyPrivateStableAndMatchesSupportedGeneration()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        var peopleService = Service("people", TariffCalculationBases.People, 37.125m);
        db.Add(peopleService);
        await db.SaveChangesAsync();
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        var beforeAudit = await db.AuditEvents.CountAsync();
        var engine = Engine(db);
        var forecast = await engine.PreviewAsync(2026, default);
        var second = await engine.PreviewAsync(2026, default);
        Assert.True(forecast.HoldActive);
        Assert.True(forecast.IsComplete);
        Assert.Equal(2, forecast.NewAccrualCount);
        Assert.Equal(1, forecast.AffectedGarageCount);
        Assert.Equal(1274.25m, forecast.NewAccrualAmount);
        Assert.Equal(forecast.InputFingerprint, second.InputFingerprint);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Accruals.ToListAsync());
        Assert.Equal(beforeAudit, await db.AuditEvents.CountAsync());
        Assert.True((await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026))).BooleanValue);
        var serialized = JsonSerializer.Serialize(forecast);
        Assert.DoesNotContain("PRIVATE GARAGE", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE COMMENT", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(garage.Id.ToString(), serialized, StringComparison.Ordinal);
        // Only a disposable synthetic database is changed to compare to the real generator.
        await HoldAsync(db, false);
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(January, null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.Equal(forecast.NewAccrualCount, generated.Value!.CreatedCount);
        Assert.Equal(forecast.NewAccrualAmount, await db.Accruals.SumAsync(a => a.Amount));
        Assert.Contains(forecast.Services, s => s.ServiceId == service.Id);
    }

    [PostgreSqlFact]
    public async Task ManualCancellationUsesLatestLifecycleAndExistingRowsAreNotNewMoney()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        var canceledGarage = Garage("PRIVATE CANCELED");
        var existingGarage = Garage("PRIVATE EXISTING");
        var canceled = Accrual(canceledGarage, service, true);
        var existing = Accrual(existingGarage, service, false);
        db.AddRange(canceled, existing, Event(canceled, "finance.accrual_canceled", 1));
        await db.SaveChangesAsync();
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        var forecast = await Engine(db).PreviewAsync(2026, default);
        var row = Assert.Single(forecast.Services);
        Assert.Equal(1, row.NewAccrualCount);
        Assert.Equal(1, row.ExistingAccrualCount);
        Assert.Equal(1200m, row.ExistingAccrualAmount);
        Assert.Contains(row.Exclusions, r => r.Code == "manually_canceled" && r.Count == 1);
        db.Add(Event(canceled, "finance.accrual_restored", 2));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var updated = await Engine(db).PreviewAsync(2026, default);
        Assert.Equal(2, updated.NewAccrualCount);
        Assert.NotEqual(forecast.InputFingerprint, updated.InputFingerprint);
    }

    [PostgreSqlFact]
    public async Task ExternalSnapshotIsRespectedAndReadCommittedOrDirtyContextIsRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.Equal("annual_forecast_snapshot_required", (await Assert.ThrowsAsync<AccountingCleanupException>(() => Engine(db).PreviewAsync(2026, default))).Code);
            await tx.RollbackAsync();
        }
        AnnualAccrualForecast first;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            first = await Engine(db).PreviewAsync(2026, default);
            await using var writer = database.CreateContext();
            await writer.Tariffs.ExecuteUpdateAsync(s => s.SetProperty(t => t.Rate, t => t.Rate + 1m));
            var sameSnapshot = await Engine(db).PreviewAsync(2026, default);
            Assert.Equal(first.InputFingerprint, sameSnapshot.InputFingerprint);
            Assert.Same(tx, db.Database.CurrentTransaction);
            // Preview must not mark the caller's write transaction READ ONLY.
            await db.Database.ExecuteSqlRawAsync("UPDATE application_settings SET \"BooleanValue\"=\"BooleanValue\" WHERE false");
            await tx.RollbackAsync();
        }
        var after = await Engine(db).PreviewAsync(2026, default);
        Assert.NotEqual(first.InputFingerprint, after.InputFingerprint);
        db.Add(Garage("unsaved"));
        Assert.Equal("annual_forecast_pending_changes", (await Assert.ThrowsAsync<AccountingCleanupException>(() => Engine(db).PreviewAsync(2026, default))).Code);
    }

    [PostgreSqlFact]
    public async Task StoredDateAndTimeZoneControlScopeAndUnsupportedInputsFailClosed()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        service.Tariff!.CalculationBase = TariffCalculationBases.MeterWater;
        service.IsMetered = true;
        db.Add(new ApplicationSetting { Key = "system.business_date_override", DateValue = new(2026, 12, 1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new TestBusinessDateProvider(new(2026, 1, 1), "Europe/Moscow");
        var report = await new AnnualAccrualForecastEngine(db, provider, TimeProvider.System).PreviewAsync(2026, default);
        Assert.Equal(new DateOnly(2026, 12, 1), report.BusinessDate);
        Assert.Equal("Europe/Moscow", report.TimeZoneId);
        Assert.Null(provider.OverrideDate);
        Assert.False(report.IsComplete);
        Assert.Contains(Assert.Single(report.Services).Uncertainties, r => r.Code == "metered_or_tiered_annual_not_supported");
        Assert.Equal(0m, report.NewAccrualAmount);
        var tracked = await db.ChargeServiceSettings.Include(s => s.Tariff).SingleAsync(s => s.Id == service.Id);
        tracked.IsMetered = false;
        tracked.Tariff!.CalculationBase = TariffCalculationBases.Fixed;
        await db.Garages.Where(g => g.Id == garage.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.AccountingStartMonth, new DateOnly(2026, 9, 1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var shifted = await Engine(db).PreviewAsync(2026, default);
        Assert.True(shifted.IsComplete);
        Assert.Equal(1200m, shifted.NewAccrualAmount);
        Assert.NotEqual(report.InputFingerprint, shifted.InputFingerprint);
    }

    [Fact]
    public async Task InvalidYearAndUnsupportedProviderAreRejected()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var engine = Engine(database.Context);
        Assert.Equal("annual_forecast_year_invalid", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(9999, default))).Code);
        Assert.Equal("annual_forecast_postgresql_required", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(2026, default))).Code);
    }

    [PostgreSqlFact]
    public async Task ExclusionsUncertaintyAndPeopleHistoryAreExplicit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        db.ChangeTracker.Clear();
        async Task Check(string code, bool uncertain, Func<GarageBalanceDbContext, Task> change)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await change(db);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var forecast = await Engine(db).PreviewAsync(2026, default);
            var row = forecast.Services.Single(s => s.ServiceId == service.Id);
            Assert.Contains(uncertain ? row.Uncertainties : row.Exclusions, r => r.Code == code && r.Count == 1);
            if (uncertain) Assert.False(forecast.IsComplete);
            await tx.RollbackAsync();
            db.ChangeTracker.Clear();
        }
        await Check("scheduled_after_business_month", false, async c =>
            (await c.ChargeServiceSettings.SingleAsync(s => s.Id == service.Id)).AccrualStartMonth = 12);
        await Check("accounting_not_started", false, async c =>
            (await c.Garages.SingleAsync(g => g.Id == garage.Id)).AccountingStartMonth = new(2027, 1, 1));
        await Check("no_effective_tariff", false, async c =>
            (await c.ChargeServiceSettings.SingleAsync(s => s.Id == service.Id)).TariffId = null);
        await Check("zero_amount", false, async c =>
        {
            (await c.Garages.SingleAsync(g => g.Id == garage.Id)).PeopleCount = 0;
            (await c.Tariffs.SingleAsync(t => t.Id == service.TariffId)).CalculationBase = TariffCalculationBases.People;
        });
        await Check("annual_service_configuration_invalid", true, async c =>
            (await c.ChargeServiceSettings.SingleAsync(s => s.Id == service.Id)).AccrualStartMonth = null);
        await Check("income_type_has_multiple_regular_services", true, c =>
        {
            c.Add(new ChargeServiceSetting
            {
                Name = "Synthetic duplicate income",
                IsRegular = true,
                PeriodicityMonths = 12,
                AccrualStartMonth = 1,
                IncomeTypeId = service.IncomeTypeId,
                TariffId = service.TariffId
            });
            return Task.CompletedTask;
        });
        await Check("partial_or_changing_tariff_not_supported", true, c =>
        {
            c.Add(new ChargeServiceTariffVersion { ChargeServiceSettingId = service.Id, TariffId = service.TariffId!.Value, EffectiveFrom = January.AddDays(14) });
            return Task.CompletedTask;
        });
        await Check("individual_tariff_not_supported", true, c =>
        {
            c.Add(new GarageTariffAssignment
            {
                GarageId = garage.Id,
                ChargeServiceSettingId = service.Id,
                TariffId = service.TariffId!.Value,
                EffectiveFrom = January
            });
            return Task.CompletedTask;
        });
        await Check("duplicate_active_annual_accruals", true, c =>
        {
            foreach (var month in new[] { January, January.AddMonths(1) })
                c.Add(new Accrual
                {
                    GarageId = garage.Id,
                    IncomeTypeId = service.IncomeTypeId!.Value,
                    TariffId = service.TariffId,
                    AccountingMonth = month,
                    AccountingYear = 2026,
                    IsAnnualAccrual = true,
                    Amount = 1200m,
                    Source = AccrualSources.Regular,
                    DueDate = month,
                    OverdueFromDate = month.AddDays(1)
                });
            return Task.CompletedTask;
        });
        var trackedTariff = await db.Tariffs.SingleAsync(t => t.Id == service.TariffId);
        trackedTariff.CalculationBase = TariffCalculationBases.People;
        trackedTariff.Rate = 31m;
        db.AddRange(new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = January, PeopleCount = 1 },
            new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = January.AddDays(15), PeopleCount = 3 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var history = await Engine(db).PreviewAsync(2026, default);
        Assert.True(history.IsComplete);
        Assert.Equal(63m, history.NewAccrualAmount); // 15 days × 1 + 16 days × 3.
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(January, null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.Equal(history.NewAccrualAmount, await db.Accruals.SumAsync(a => a.Amount));
    }

    [PostgreSqlFact]
    public async Task AnnualCampaignFutureYearAndEmptyScopeAreNotMisrepresented()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        db.Add(new FeeCampaign
        {
            Name = "Synthetic annual campaign",
            IncomeTypeId = service.IncomeTypeId!.Value,
            StartsOn = January,
            ContributionAmount = 100
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var campaign = await Engine(db).PreviewAsync(2026, default);
        Assert.False(campaign.IsComplete);
        Assert.Contains(campaign.Uncertainties, r => r.Code == "annual_fee_campaign_not_supported" && r.Count == 1);
        await using (var classification = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await db.ChargeServiceSettings.Where(s => s.Id == service.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PeriodicityMonths, 1));
            var monthly = await Engine(db).PreviewAsync(2026, default);
            Assert.True(monthly.IsComplete);
            db.Add(new ChargeServiceSetting
            {
                Name = "Synthetic ambiguous monthly",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                IncomeTypeId = service.IncomeTypeId,
                TariffId = service.TariffId
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var ambiguous = await Engine(db).PreviewAsync(2026, default);
            Assert.False(ambiguous.IsComplete);
            Assert.Contains(ambiguous.Uncertainties, r => r.Code == "annual_fee_campaign_not_supported");
            await classification.RollbackAsync();
        }
        var future = await Engine(db).PreviewAsync(2027, default);
        Assert.False(future.IsComplete);
        Assert.Contains(future.Uncertainties, r => r.Code == "future_year_not_release_forecast");
        var historical = await Engine(db).PreviewAsync(2025, default);
        Assert.False(historical.IsComplete);
        Assert.Contains(historical.Uncertainties, r => r.Code == "historical_year_requires_explicit_catch_up");
        await db.FeeCampaigns.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsArchived, true));
        await db.Garages.ExecuteUpdateAsync(s => s.SetProperty(g => g.IsArchived, true));
        var empty = await Engine(db).PreviewAsync(2026, default);
        Assert.True(empty.IsComplete);
        Assert.Equal(0, empty.ActiveGarageCount);
        Assert.Equal(0, empty.NewAccrualCount);
        foreach (var invalidEpoch in new[] { "[]", "{\"2027\":true}" })
        {
            var epoch = await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch");
            epoch.JsonValue = invalidEpoch;
            // The migrated schema itself rejects corrupt guard metadata. Do not
            // disable its integrity trigger merely to manufacture a bad snapshot.
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var intact = await Engine(db).PreviewAsync(2026, default);
            Assert.True(intact.IsComplete);
            Assert.Equal(empty.InputFingerprint, intact.InputFingerprint);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(db).PreviewAsync(2026, canceled.Token));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task SelectedGaragesUseSharedServiceTariffAndIgnoreLegacyIndividualOverride()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        service.AppliesToSelectedGarages = true;
        service.GarageIds = [garage.Id];
        db.AddRange(Garage("PRIVATE NOT SELECTED"),
            new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = service.Tariff!, EffectiveFrom = January },
            new GarageTariffAssignment
            {
                Garage = garage,
                ChargeServiceSetting = service,
                EffectiveFrom = January,
                Tariff = new Tariff
                {
                    Name = "Synthetic ignored individual",
                    CalculationBase = TariffCalculationBases.Fixed,
                    Rate = 9999m,
                    EffectiveFrom = January,
                    IsIndividual = true
                }
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var forecast = await Engine(db).PreviewAsync(2026, default);
        Assert.True(forecast.IsComplete);
        Assert.Equal(2, forecast.ActiveGarageCount);
        Assert.Equal(1, Assert.Single(forecast.Services).CandidateGarageCount);
        Assert.Equal(1200m, forecast.NewAccrualAmount);
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(January, null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.Equal(1, generated.Value!.CreatedCount);
        Assert.Equal(forecast.NewAccrualAmount, await db.Accruals.SumAsync(a => a.Amount));
    }

    [PostgreSqlFact]
    public async Task ShiftedForecastMatchesCatalogCatchUpAndTracksEffectiveMonthTariffs()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (first, service) = await SeedAsync(db);
        var october = new DateOnly(2026, 10, 1);
        first.AccountingStartMonth = october;
        var registration = Garage("PRIVATE REGISTRATION FALLBACK");
        registration.AccountingStartMonth = null;
        registration.RegisteredOn = october;
        var creation = Garage("PRIVATE CREATION FALLBACK");
        creation.AccountingStartMonth = null;
        creation.RegisteredOn = null;
        creation.CreatedAtUtc = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var january = Garage("PRIVATE JANUARY");
        var future = Garage("PRIVATE FUTURE");
        future.AccountingStartMonth = new(2026, 12, 1);
        var futureRegistration = Garage("PRIVATE FUTURE REGISTRATION");
        futureRegistration.AccountingStartMonth = null;
        futureRegistration.RegisteredOn = new(2026, 12, 1);
        var futureCreation = Garage("PRIVATE FUTURE CREATION");
        futureCreation.AccountingStartMonth = null;
        futureCreation.RegisteredOn = null;
        futureCreation.CreatedAtUtc = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var dated = new Tariff { Name = "Synthetic October tariff", CalculationBase = TariffCalculationBases.Fixed, Rate = 1800m, EffectiveFrom = october };
        db.AddRange(registration, creation, january, future, futureRegistration, futureCreation,
            new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = service.Tariff!, EffectiveFrom = January, EffectiveTo = october.AddDays(-1) },
            new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = dated, EffectiveFrom = october });
        await db.SaveChangesAsync();
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        var forecast = await Engine(db).PreviewAsync(2026, default);
        Assert.True(forecast.IsComplete);
        Assert.Equal(4, forecast.NewAccrualCount);
        Assert.Equal(4, forecast.AffectedGarageCount);
        Assert.Equal(6600m, forecast.NewAccrualAmount);
        Assert.Contains(Assert.Single(forecast.Services).Exclusions, r => r.Code == "accounting_not_started" && r.Count == 3);
        Assert.Empty(await db.Accruals.ToArrayAsync());
        await db.Tariffs.Where(t => t.Id == dated.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Rate, 1900m));
        var changed = await Engine(db).PreviewAsync(2026, default);
        Assert.Equal(6900m, changed.NewAccrualAmount);
        Assert.NotEqual(forecast.InputFingerprint, changed.InputFingerprint);
        await db.Tariffs.Where(t => t.Id == dated.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Rate, 1800m));
        await HoldAsync(db, false);
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(new(2026, 11, 1), null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var rows = await db.Accruals.AsNoTracking().ToArrayAsync();
        Assert.Equal(forecast.NewAccrualCount, rows.Length);
        Assert.Equal(forecast.NewAccrualAmount, rows.Sum(a => a.Amount));
        Assert.Equal(3, rows.Count(a => a.AccountingMonth == october && a.Amount == 1800m));
        Assert.Equal(January, Assert.Single(rows, a => a.GarageId == january.Id).AccountingMonth);
    }

    [PostgreSqlFact]
    public async Task ShiftedFirstTariffAndGapMatchCatalogWithoutInventingJanuaryTerms()
    {
        foreach (var scenario in new[] { "direct", "version", "gap" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var db = database.CreateContext();
            var (garage, service) = await SeedAsync(db);
            var october = new DateOnly(2026, 10, 1);
            garage.AccountingStartMonth = october;
            if (scenario == "gap")
                db.Add(new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = service.Tariff!, EffectiveFrom = January, EffectiveTo = october.AddDays(-1) });
            else
            {
                service.Tariff!.EffectiveFrom = october;
                service.Tariff.Rate = 1800m;
                if (scenario == "version")
                    db.Add(new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = service.Tariff, EffectiveFrom = october });
            }
            await db.SaveChangesAsync();
            await HoldAsync(db, true);
            db.ChangeTracker.Clear();
            var forecast = await Engine(db).PreviewAsync(2026, default);
            Assert.True(forecast.IsComplete);
            Assert.Equal(scenario == "gap" ? 0 : 1, forecast.NewAccrualCount);
            Assert.Equal(scenario == "gap" ? 0m : 1800m, forecast.NewAccrualAmount);
            if (scenario == "gap") Assert.Contains(Assert.Single(forecast.Services).Exclusions, r => r.Code == "no_effective_tariff");
            await HoldAsync(db, false);
            var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
                .GenerateRegularCatalogAccrualsAsync(new(new(2026, 11, 1), null), null, default);
            Assert.Equal(scenario != "gap", generated.Succeeded);
            var rows = await db.Accruals.AsNoTracking().ToArrayAsync();
            Assert.Equal(forecast.NewAccrualCount, rows.Length);
            Assert.Equal(forecast.NewAccrualAmount, rows.Sum(a => a.Amount));
            Assert.All(rows, a => Assert.Equal(october, a.AccountingMonth));
        }
    }

    [PostgreSqlFact]
    public async Task ShiftedPeopleHistoryUsesActualMonthAndMatchesGenerator()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        var october = new DateOnly(2026, 10, 1);
        garage.AccountingStartMonth = october;
        service.Tariff!.CalculationBase = TariffCalculationBases.People;
        service.Tariff.Rate = 31m;
        db.AddRange(
            new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = service.Tariff, EffectiveFrom = January, EffectiveTo = october.AddDays(-1) },
            new ChargeServiceTariffVersion { ChargeServiceSetting = service, Tariff = new Tariff { Name = "Synthetic October people", CalculationBase = TariffCalculationBases.People, Rate = 62m, EffectiveFrom = october }, EffectiveFrom = october },
            new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = January, PeopleCount = 1 },
            new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = october, PeopleCount = 2 },
            new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = october.AddDays(15), PeopleCount = 4 });
        await db.SaveChangesAsync();
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        var forecast = await Engine(db).PreviewAsync(2026, default);
        Assert.True(forecast.IsComplete);
        Assert.Equal(188m, forecast.NewAccrualAmount);
        await HoldAsync(db, false);
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(new(2026, 11, 1), null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var row = await db.Accruals.SingleAsync();
        Assert.Equal(october, row.AccountingMonth);
        Assert.Equal(forecast.NewAccrualAmount, row.Amount);
    }

    [PostgreSqlFact]
    public async Task ShiftedCancellationsPreserveBothMonthsAndUnknownLegacyScopeBlocksRelease()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (garage, service) = await SeedAsync(db);
        var october = new DateOnly(2026, 10, 1);
        garage.AccountingStartMonth = october;
        var unknownIds = new List<Guid>();
        foreach (var scenario in new[] { "scheduled", "effective", "known_other", "unknown_other", "no_audit_other", "restored_other" })
        {
            var canceledGarage = Garage("PRIVATE " + scenario);
            canceledGarage.AccountingStartMonth = october;
            var row = Accrual(canceledGarage, service, true);
            row.AccountingMonth = scenario == "scheduled" ? January : scenario == "effective" ? october : new(2026, 3, 1);
            if (scenario != "known_other") { row.AccountingYear = null; row.IsAnnualAccrual = null; }
            db.Add(row);
            if (scenario != "no_audit_other") db.Add(Event(row, "finance.accrual_canceled", 1));
            if (scenario == "restored_other") db.Add(Event(row, "finance.accrual_restored", 2));
            if (scenario is "unknown_other" or "no_audit_other") unknownIds.Add(canceledGarage.Id);
        }
        await db.SaveChangesAsync();
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        var uncertain = await Engine(db).PreviewAsync(2026, default);
        Assert.False(uncertain.IsComplete);
        Assert.Contains(Assert.Single(uncertain.Services).Uncertainties, r => r.Code == "legacy_canceled_annual_scope_unknown" && r.Count == 2);
        Assert.Contains(Assert.Single(uncertain.Services).Exclusions, r => r.Code == "manually_canceled" && r.Count == 3);
        Assert.Equal(2, uncertain.NewAccrualCount);
        Assert.Equal(2400m, uncertain.NewAccrualAmount);
        // Isolate the supported cohort in this synthetic database for exact parity.
        await db.Garages.Where(g => unknownIds.Contains(g.Id)).ExecuteUpdateAsync(s => s.SetProperty(g => g.IsArchived, true));
        var complete = await Engine(db).PreviewAsync(2026, default);
        Assert.True(complete.IsComplete);
        await HoldAsync(db, false);
        var generated = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(new(2026, 11, 1), null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var rows = await db.Accruals.AsNoTracking().Where(a => !a.IsCanceled).ToArrayAsync();
        Assert.Equal(complete.NewAccrualCount, rows.Length);
        Assert.Equal(complete.NewAccrualAmount, rows.Sum(a => a.Amount));
        Assert.All(rows, a => Assert.Equal(october, a.AccountingMonth));
        Assert.Equal(6, await db.Accruals.CountAsync(a => a.IsCanceled));
    }

    [PostgreSqlFact]
    public async Task GuardYearRangeMatchesReleaseValidationWithoutChangingSource()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        await HoldAsync(db, true);
        db.ChangeTracker.Clear();
        Assert.True((await Engine(db).PreviewAsync(2026, default)).IsComplete);
        Guid? invalidId = null;
        foreach (var year in new[] { "0000", "1899" })
        {
            var hold = invalidId.HasValue
                ? await db.ApplicationSettings.SingleAsync(s => s.Id == invalidId)
                : new ApplicationSetting { BooleanValue = true };
            hold.Key = AnnualAccrualHoldPolicy.KeyPrefix + year;
            if (!invalidId.HasValue) { db.Add(hold); invalidId = hold.Id; }
            var epoch = await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch");
            epoch.JsonValue = JsonSerializer.Serialize(new Dictionary<string, bool> { ["2026"] = true, [year] = true });
            // Commit normally with every integrity trigger enabled. PostgreSQL
            // accepts matching four-digit maps; the release domain also requires >=1900.
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var before = await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Id == invalidId);
            var report = await Engine(db).PreviewAsync(2026, default);
            Assert.False(report.IsComplete);
            Assert.Contains(report.Uncertainties, r => r.Code == "annual_guard_state_invalid");
            var after = await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Id == invalidId);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(before.Key, after.Key);
            Assert.True(after.BooleanValue);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Empty(await db.Accruals.ToArrayAsync());
        }
    }

    private static AnnualAccrualForecastEngine Engine(GarageBalanceDbContext db) => new(db, new TestBusinessDateProvider(November), TimeProvider.System);
    private static Garage Garage(string number) => new() { Number = number, PeopleCount = 2, RegisteredOn = January, AccountingStartMonth = January, Comment = "PRIVATE COMMENT" };
    private static ChargeServiceSetting Service(string suffix, string calculationBase = TariffCalculationBases.Fixed, decimal rate = 1200m) => new()
    {
        Name = "Synthetic annual forecast " + suffix,
        IsRegular = true,
        PeriodicityMonths = 12,
        AccrualStartMonth = 1,
        IncomeType = new IncomeType { Name = "Synthetic forecast " + suffix, Code = "synthetic_forecast_" + suffix },
        Tariff = new Tariff { Name = "Synthetic forecast " + suffix, CalculationBase = calculationBase, Rate = rate, EffectiveFrom = January }
    };
    private static async Task<(Garage, ChargeServiceSetting)> SeedAsync(GarageBalanceDbContext db)
    {
        // Migrations seed example catalogue services. Isolate this fixture's scope.
        await db.ChargeServiceSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, true));
        var garage = Garage("PRIVATE GARAGE");
        var service = Service("fixed");
        db.AddRange(garage, service);
        await db.SaveChangesAsync();
        return (garage, service);
    }
    private static async Task HoldAsync(GarageBalanceDbContext db, bool held)
    {
        var setting = await db.ApplicationSettings.SingleOrDefaultAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026));
        if (setting is null) db.Add(setting = new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026) });
        setting.BooleanValue = held;
        (await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch")).JsonValue = held ? "{\"2026\":true}" : "{}";
        await db.SaveChangesAsync();
    }
    private static Accrual Accrual(Garage garage, ChargeServiceSetting service, bool canceled) => new()
    {
        Garage = garage,
        IncomeType = service.IncomeType!,
        Tariff = service.Tariff,
        AccountingMonth = January,
        AccountingYear = 2026,
        IsAnnualAccrual = true,
        Amount = 1200,
        Source = AccrualSources.Regular,
        IsCanceled = canceled,
        DueDate = January,
        OverdueFromDate = January.AddDays(1)
    };
    private static AuditEvent Event(Accrual accrual, string action, int day) => new()
    {
        EntityType = "accrual",
        EntityId = accrual.Id.ToString(),
        Action = action,
        Summary = "PRIVATE AUDIT",
        CreatedAtUtc = new(2026, 1, day, 0, 0, 0, TimeSpan.Zero)
    };
}
