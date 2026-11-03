using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AnnualAccrualHoldTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public async Task AnnualCustomCampaignDestinationCannotGenerateOrCreatePaymentsWhileHeld()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var fund = new Fund { Name = "Synthetic annual fund", NormalizedName = "SYNTHETIC ANNUAL FUND" };
        annual.IncomeType!.DestinationFund = fund;
        var campaign = new FeeCampaign
        {
            Name = "Synthetic custom annual campaign",
            IncomeType = annual.IncomeType,
            StartsOn = September,
            EndsOn = September.AddMonths(1).AddDays(-1),
            ContributionAmount = 100m,
            TargetAmount = 1000m,
            AppliesToAllGarages = true
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { Garage = garage });
        db.AddRange(fund, campaign, new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await db.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: November);
        var generated = await service.GenerateFeeCampaignAccrualsAsync(new(campaign.Id, September, null), null, default);
        Assert.Equal("annual_accrual_generation_held", generated.ErrorCode);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(September, September), default);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var direct = await service.CreateIncomeAsync(new(garage.Id, annual.IncomeTypeId!.Value, September.AddDays(10), September, 100, null, null, null, campaign.Id), null, default);
        Assert.Equal("annual_accrual_generation_held", direct.ErrorCode);
        var full = await service.CreateFullGaragePaymentAsync(new(garage.Id, September.AddDays(10),
            [new(annual.IncomeTypeId.Value, September, 100, null, FeeCampaignId: campaign.Id)]), null, default);
        Assert.Equal("annual_accrual_generation_held", full.ErrorCode);
        Assert.Empty(await db.Accruals.ToArrayAsync());
        Assert.Empty(await db.FinancialOperations.ToArrayAsync());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(12, false)]
    public async Task FeeCampaignYearAndProvenanceFollowItsActiveService(int periodicity, bool legacyCode)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db, September);
        garage.RegisteredOn = September;
        setting.PeriodicityMonths = periodicity;
        setting.AccrualStartMonth = 9;
        if (legacyCode)
        {
            var membership = new IncomeType { Name = "Synthetic legacy membership", Code = "membership", IsSystem = true };
            db.Add(membership);
            setting.IncomeType = membership;
            setting.IncomeTypeId = membership.Id;
        }
        var fund = new Fund { Name = "Synthetic fee fund", NormalizedName = "SYNTHETIC FEE FUND" };
        setting.IncomeType!.DestinationFund = fund;
        var campaign = new FeeCampaign
        {
            Name = "Synthetic typed fee",
            IncomeType = setting.IncomeType,
            StartsOn = September,
            EndsOn = September.AddMonths(1).AddDays(-1),
            ContributionAmount = 100m,
            TargetAmount = 1000m,
            AppliesToAllGarages = false
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { Garage = garage });
        db.AddRange(fund, campaign);
        await db.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: November);
        var result = await service.GenerateFeeCampaignAccrualsAsync(new(campaign.Id, September, null), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = await db.Accruals.SingleAsync(item => item.FeeCampaignId == campaign.Id);
        Assert.Equal(periodicity >= 12, accrual.IsAnnualAccrual);
        Assert.Equal(periodicity >= 12 ? 2026 : null, accrual.AccountingYear);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FeeCampaignYearMatchesWhenCreatedByPaymentOrWorksheet(int route)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        garage.RegisteredOn = September;
        annual.AccrualStartMonth = 9;
        var fund = new Fund { Name = "Synthetic route fund", NormalizedName = "SYNTHETIC ROUTE FUND" };
        annual.IncomeType!.DestinationFund = fund;
        var campaign = new FeeCampaign
        {
            Name = "Synthetic route campaign",
            IncomeType = annual.IncomeType,
            StartsOn = September,
            EndsOn = September.AddMonths(1).AddDays(-1),
            ContributionAmount = 100m,
            TargetAmount = 1000m,
            AppliesToAllGarages = false
        };
        campaign.ParticipantGarages.Add(new FeeCampaignGarage { Garage = garage });
        db.AddRange(fund, campaign);
        await db.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: November);
        if (route == 1)
        {
            var result = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(September, September), default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        else if (route == 2)
        {
            var result = await service.CreateFullGaragePaymentAsync(new(garage.Id, September.AddDays(10),
                [new(annual.IncomeTypeId!.Value, September, 100, null, FeeCampaignId: campaign.Id)]), null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        else
        {
            var result = await service.CreateIncomeAsync(new(garage.Id, annual.IncomeTypeId!.Value,
                September.AddDays(10), September, 100, null, null, null, campaign.Id), null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        var accrual = await db.Accruals.SingleAsync(item => item.FeeCampaignId == campaign.Id);
        Assert.True(accrual.IsAnnualAccrual);
        Assert.Equal(2026, accrual.AccountingYear);
    }

    [Fact]
    public async Task RestoreHonorsHistoricalYearEvenIfStoredClassificationIsInconsistent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = annual.IncomeType!,
            AccountingMonth = September,
            AccountingYear = 2026,
            IsAnnualAccrual = false,
            Amount = 100m,
            Source = AccrualSources.Regular,
            DueDate = September.AddDays(10),
            OverdueFromDate = September.AddDays(11),
            IsCanceled = true
        };
        db.AddRange(accrual, new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await db.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(db, businessDate: November).RestoreAccrualAsync(accrual.Id, null, default);
        Assert.Equal("annual_accrual_generation_held", result.ErrorCode);
        Assert.True(accrual.IsCanceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorksheetKeepsUnknownHistoricalTariffAndReportsReviewWithoutBlockingOtherRows(bool sameTariffWhileHeld)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db, September);
        garage.RegisteredOn = September;
        setting.PeriodicityMonths = 1;
        setting.AccrualStartMonth = 9;
        var historicalTariff = new Tariff
        {
            Name = "Synthetic historical tariff",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 50m,
            EffectiveFrom = September
        };
        var selectedHistoricalTariff = sameTariffWhileHeld ? setting.Tariff! : historicalTariff;
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            Tariff = selectedHistoricalTariff,
            AccountingMonth = September,
            Amount = 50m,
            Source = AccrualSources.Regular,
            DueDate = September.AddDays(10),
            OverdueFromDate = September.AddDays(11)
        };
        db.AddRange(historicalTariff, old);
        if (sameTariffWhileHeld) db.Add(new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await db.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(result.Value!.Rows, row => row.IncomeTypeId == setting.IncomeTypeId && row.CalculationIssue != null);
        db.ChangeTracker.Clear();
        var preserved = await db.Accruals.SingleAsync(item => item.Id == old.Id);
        Assert.Null(preserved.IsAnnualAccrual);
        Assert.Equal(selectedHistoricalTariff.Id, preserved.TariffId);
        Assert.Equal(50m, preserved.Amount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WorksheetPreservesUnknownHistoryDuringHoldForAnnualOrPreStartMonth(bool beforeStart, bool knownAnnual)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db, September);
        garage.RegisteredOn = September;
        setting.AccrualStartMonth = 9;
        var month = beforeStart ? September.AddMonths(-1) : September;
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            Tariff = setting.Tariff,
            AccountingMonth = month,
            Amount = 50m,
            Source = AccrualSources.Regular,
            DueDate = month.AddDays(10),
            OverdueFromDate = month.AddDays(11)
        };
        old.IsAnnualAccrual = knownAnnual ? true : null;
        db.AddRange(old, new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await db.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(db, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(month, month), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        if (!beforeStart) Assert.Contains(result.Value!.Rows, row => row.IncomeTypeId == setting.IncomeTypeId && row.CalculationIssue != null);
        db.ChangeTracker.Clear();
        var preserved = await db.Accruals.SingleAsync(item => item.Id == old.Id);
        Assert.False(preserved.IsCanceled);
        Assert.Equal(50m, preserved.Amount);
        Assert.Equal(knownAnnual ? true : null, preserved.IsAnnualAccrual);
    }

    [Fact]
    public async Task HoldBlocksAnnualReadCalculationManualRegularAndWorkerButMonthlyDatesSurvive()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyLifecycleAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualHoldAndMonthlyLifecycle()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await VerifyLifecycleAsync(db);
    }

    private static async Task VerifyLifecycleAsync(GarageBalanceDbContext db)
    {
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var monthly = new ChargeServiceSetting
        {
            Name = "Synthetic monthly unchanged start",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = new IncomeType { Name = "Synthetic monthly", Code = "synthetic_monthly_hold" },
            Tariff = new Tariff { Name = "Synthetic monthly", CalculationBase = TariffCalculationBases.Fixed, Rate = 125, EffectiveFrom = new(2026, 1, 1) }
        };
        db.AddRange(monthly, new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        if (db.Database.IsNpgsql())
            (await db.ApplicationSettings.SingleAsync(item => item.Key == "finance.annual_accrual_guard_epoch")).JsonValue = "{\"2026\":true}";
        await db.SaveChangesAsync();
        annual.AccrualStartMonth = 9;
        await db.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: November);
        var workerPreview = await service.PreviewRegularAccrualAutomationAsync(September, default);
        Assert.Contains(workerPreview.Warnings, warning => warning.Contains("2026", StringComparison.Ordinal));
        await service.GenerateRegularCatalogAccrualsAsync(new(September, null), null, default);
        Assert.DoesNotContain(await db.Accruals.ToArrayAsync(), accrual => accrual.IncomeTypeId == annual.IncomeTypeId);
        var read = await service.GetGarageAnnualPaymentsAsync(garage.Id, 2026, default);
        Assert.True(read.Succeeded, read.ErrorMessage);
        Assert.True(read.Value!.GenerationHeld);
        Assert.Empty(read.Value.Items);
        var calculated = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default);
        Assert.True(calculated.Succeeded, calculated.ErrorMessage);
        Assert.True(calculated.Value!.GenerationHeld);
        foreach (var source in new[] { AccrualSources.Manual, AccrualSources.Regular })
        {
            var denied = await service.CreateAccrualAsync(new(garage.Id, annual.IncomeTypeId!.Value, September, 1200, source, null), null, default);
            Assert.Equal("annual_accrual_generation_held", denied.ErrorCode);
        }
        var generated = await service.GenerateRegularAccrualsAsync(new(annual.IncomeTypeId!.Value, annual.TariffId!.Value, September, null), null, default);
        Assert.Equal("annual_accrual_generation_held", generated.ErrorCode);
        await service.GenerateRegularCatalogAccrualsAsync(new(November, null), null, default);
        Assert.DoesNotContain(await db.Accruals.ToArrayAsync(), accrual => accrual.IncomeTypeId == annual.IncomeTypeId);
        var worksheet = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September.AddMonths(-1), November), null, default);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(new[] { 2026 }, worksheet.Value!.HeldAnnualYears);
        Assert.Equal(new[] { September, September.AddMonths(1), November }, await db.Accruals.Where(a => a.GarageId == garage.Id && a.IncomeTypeId == monthly.IncomeTypeId).OrderBy(a => a.AccountingMonth).Select(a => a.AccountingMonth).ToArrayAsync());
        Assert.Equal(September, (await db.Garages.SingleAsync(g => g.Id == garage.Id)).AccountingStartMonth);
        Assert.DoesNotContain(await db.Accruals.ToArrayAsync(), accrual => accrual.AccountingYear == 2026);
        var future = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2027, null, default);
        Assert.True(future.Succeeded, future.ErrorMessage);
        Assert.False(future.Value!.GenerationHeld);
        Assert.NotEmpty(future.Value.Items);
        Assert.DoesNotContain(await db.Accruals.ToArrayAsync(), accrual => accrual.AccountingYear == 2027);
    }

    [Fact]
    public async Task HoldPreservesExistingAnnualAndBlocksRestoreUpdateAndTariffRecalculation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(db, September);
        var active = new Accrual { Garage = garage, IncomeType = annual.IncomeType!, Tariff = annual.Tariff, AccountingMonth = September, AccountingYear = 2026, Amount = 777, Source = AccrualSources.Regular };
        var canceled = new Accrual { Garage = garage, IncomeType = annual.IncomeType!, AccountingMonth = September, AccountingYear = 2026, Amount = 888, Source = AccrualSources.Manual, IsCanceled = true };
        db.AddRange(active, canceled, new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await db.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: November);
        Assert.Equal("annual_accrual_generation_held", (await service.RestoreAccrualAsync(canceled.Id, null, default)).ErrorCode);
        Assert.Equal("annual_accrual_generation_held", (await service.UpdateAccrualAsync(active.Id, new(garage.Id, annual.IncomeTypeId!.Value, September, 999, AccrualSources.Regular, null), null, default)).ErrorCode);
        var preview = await service.PreviewRegularAccrualRecalculationAsync(new(annual.IncomeTypeId!.Value, annual.TariffId!.Value, September), default);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        Assert.All(preview.Value!.Rows, row => Assert.Equal("unchanged", row.Action));
        var applied = await service.ApplyRegularAccrualRecalculationAsync(new(annual.IncomeTypeId.Value, annual.TariffId.Value, September, preview.Value.PreviewFingerprint, "Synthetic hold"), null, default);
        Assert.True(applied.Succeeded, applied.ErrorMessage);
        await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, November), null, default);
        db.ChangeTracker.Clear();
        Assert.Equal(777m, (await db.Accruals.SingleAsync(a => a.Id == active.Id)).Amount);
        Assert.True((await db.Accruals.SingleAsync(a => a.Id == canceled.Id)).IsCanceled);
        Assert.Equal(2, await db.Accruals.CountAsync());
    }

    [Fact]
    public async Task PolicyIsYearScopedAndNewScopeObservesExplicitRelease()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var setting = new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true };
        db.Add(setting);
        await db.SaveChangesAsync();
        var policy = new AnnualAccrualHoldPolicy(new EfApplicationSettingRepository(db));
        Assert.True(await policy.IsHeldAsync(2026, default));
        foreach (var year in new int?[] { null, 1, 1899, 2025, 2027, 10000 }) Assert.False(await policy.IsHeldAsync(year, default));
        setting.BooleanValue = false;
        await db.SaveChangesAsync();
        Assert.True(await policy.IsHeldAsync(2026, default));
        Assert.False(await new AnnualAccrualHoldPolicy(new EfApplicationSettingRepository(db)).IsHeldAsync(2026, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnnualAccrualHoldPolicy.Key(0));
    }
}
