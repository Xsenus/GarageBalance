using System.Text.Json;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlShowcaseDataSeederIntegrationTests
{
    [Fact]
    public void RepresentativeElectricityTiers_AreReadableByTheApplicationJsonContract()
    {
        var electricityTiers = JsonSerializer.Deserialize<ShowcaseElectricityTier[]>(
            ShowcaseDataSeeder.CreateRepresentativeElectricityTiersJson());

        Assert.Collection(
            Assert.IsType<ShowcaseElectricityTier[]>(electricityTiers),
            tier => AssertTier(tier, 1100m, 7.5m),
            tier => AssertTier(tier, 1700m, 10m),
            tier => AssertTier(tier, null, 15m));
    }

    [PostgreSqlFact]
    public async Task Prepare_IsIdempotentPreservesUsersAndCreatesAllDemonstrationStates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.Users.Add(new AppUser
        {
            Email = "showcase-admin@example.test",
            NormalizedEmail = "SHOWCASE-ADMIN@EXAMPLE.TEST",
            DisplayName = "Showcase administrator",
            PasswordHash = "not-a-real-password-hash"
        });
        context.Owners.Add(new Owner { LastName = "Old", FirstName = "Business data" });
        await context.SaveChangesAsync();

        var existingMembership = await context.ChargeServiceSettings
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .SingleAsync(item => item.IncomeType!.Code == "membership");
        existingMembership.Tariff!.EffectiveFrom = new DateOnly(2026, 8, 1);
        await context.SaveChangesAsync();
        context.Tariffs.Add(new Tariff
        {
            Name = existingMembership.Tariff.Name,
            CalculationBase = existingMembership.Tariff.CalculationBase,
            Rate = existingMembership.Tariff.Rate,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Comment = "Existing staging tariff history"
        });
        await context.SaveChangesAsync();

        var seeder = new ShowcaseDataSeeder(context);
        var first = await seeder.PrepareAsync(CancellationToken.None);
        var seededElectricity = await context.ChargeServiceSettings
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .SingleAsync(item => item.IncomeType!.Code == "electricity");
        seededElectricity.Tariff!.ElectricityTiersJson = """
            [
              {"id":"11111111-1111-1111-1111-111111111111","name":"0-1100","upperBound":1100,"rate":7.5,"isCustom":false},
              {"id":"22222222-2222-2222-2222-222222222222","name":"1101-1700","upperBound":1700,"rate":10,"isCustom":false},
              {"id":"33333333-3333-3333-3333-333333333333","name":"1701+","upperBound":null,"rate":15,"isCustom":false}
            ]
            """;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.True((await seeder.AuditAsync(CancellationToken.None)).IsReady);
        var second = await seeder.PrepareAsync(CancellationToken.None);

        Assert.True(first.IsReady);
        Assert.True(second.IsReady);
        Assert.Equal(10, second.GarageCount);
        Assert.Equal(67, second.AccrualCount);
        Assert.Equal(11, second.FinancialOperationCount);
        Assert.Equal(36, second.MeterReadingCount);
        Assert.Equal(3, second.FeeCampaignCount);
        Assert.Equal(3, second.SupplierCount);
        Assert.Equal(3, second.StaffMemberCount);
        Assert.Equal(1, second.PreservedUserCount);
        Assert.True(second.HasNoDebt);
        Assert.True(second.HasDebt);
        Assert.True(second.HasAdvance);
        Assert.True(second.NewGarageHasNoCalculatedHistory);
        Assert.True(second.CampaignsHaveLockedParticipants);
        Assert.True(second.AnnualAccrualsAreUnique);
        Assert.True(second.OverdueScenarioIsCorrect);
        Assert.True(second.StaffScenariosAreComplete);
        Assert.True(second.SupplierScenariosAreComplete);
        Assert.True(second.FundBalancesReconcile);
        Assert.True(second.BusinessDateIsPinned);
        Assert.Equal(
            ShowcaseDataSeeder.BusinessDate,
            await context.ApplicationSettings
                .Where(item => item.Key == ApplicationSettingsService.BusinessDateOverrideKey)
                .Select(item => item.DateValue)
                .SingleAsync());
        Assert.Single(await context.Users.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(await context.Owners.AsNoTracking().ToListAsync(), item => item.LastName == "Old");
        Assert.Equal(2, await context.FundOperations.CountAsync(item => item.Reason.Contains(ShowcaseDataSeeder.Marker)));
        Assert.Equal(20000m, await context.Funds
            .Where(item => item.SortOrder == context.Funds.Min(fund => fund.SortOrder))
            .Select(item => item.Balance)
            .SingleAsync());

        var settings = await context.ChargeServiceSettings
            .AsNoTracking()
            .Include(item => item.IncomeType)
            .Include(item => item.Tariff)
            .Where(item => !item.IsArchived)
            .ToDictionaryAsync(item => item.IncomeType!.Code!);
        Assert.Equal(TariffCalculationBases.MeterWater, settings["water"].Tariff!.CalculationBase);
        Assert.Equal(TariffCalculationBases.People, settings["trash"].Tariff!.CalculationBase);
        Assert.Equal(TariffCalculationBases.Fixed, settings["membership"].Tariff!.CalculationBase);
        Assert.True(settings["electricity"].IsMetered);
        Assert.True(settings["electricity"].HasTieredTariff);
        Assert.Equal(2, await context.ChargeServiceTariffVersions
            .CountAsync(item => item.ChargeServiceSettingId == settings["membership"].Id));
        Assert.Empty(await context.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal(1, await context.Tariffs
            .Where(item => item.Id == settings["electricity"].TariffId)
            .CountAsync(item => item.ElectricityFirstRate != null
                && item.ElectricitySecondRate != null
                && item.ElectricityThirdRate != null));
        var electricityTiers = JsonSerializer.Deserialize<ShowcaseElectricityTier[]>(
            settings["electricity"].Tariff!.ElectricityTiersJson!);
        Assert.Collection(
            Assert.IsType<ShowcaseElectricityTier[]>(electricityTiers),
            tier => AssertTier(tier, 1100m, 7.5m),
            tier => AssertTier(tier, 1700m, 10m),
            tier => AssertTier(tier, null, 15m));
        Assert.All(
            await context.Accruals.Where(item => item.Source == "regular").ToListAsync(),
            item => Assert.False(string.IsNullOrWhiteSpace(item.CalculationDetailsJson)));
        var newGarage = await context.Garages.SingleAsync(item => item.Number == "110-НОВЫЙ");
        Assert.Empty(await context.Accruals.Where(item => item.GarageId == newGarage.Id).ToListAsync());
        Assert.Empty(await context.FeeCampaignGarages.Where(item => item.GarageId == newGarage.Id).ToListAsync());
        var seededCampaigns = await context.FeeCampaigns
            .Where(item => item.Goal != null && item.Goal.Contains(ShowcaseDataSeeder.Marker))
            .OrderBy(item => item.Name)
            .ToListAsync();
        Assert.Equal(2, seededCampaigns.Count(item => item.AppliesToAllGarages));
        Assert.All(
            seededCampaigns.Where(item => item.AppliesToAllGarages),
            campaign => Assert.Equal(9, context.FeeCampaignGarages.Count(item => item.FeeCampaignId == campaign.Id)));
        var selectedCampaign = Assert.Single(seededCampaigns, item => !item.AppliesToAllGarages);
        Assert.Equal(2, context.FeeCampaignGarages.Count(item => item.FeeCampaignId == selectedCampaign.Id));
        Assert.Equal(3, await context.StaffMembers.CountAsync(item => item.FullName.Contains("Демонстрационный сотрудник")));
        Assert.Equal(2, await context.StaffSalaryAdjustments.CountAsync(item => !item.IsCanceled));
        Assert.Single(await context.FinancialOperations
            .Where(item => item.StaffMemberId != null && !item.IsCanceled)
            .ToListAsync());
        var expenseWorksheet = await FinanceServiceTestFactory.Create(context).GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(ShowcaseDataSeeder.AccountingMonth),
            CancellationToken.None);
        Assert.True(expenseWorksheet.Succeeded, expenseWorksheet.ErrorMessage);
        var activeStaff = Assert.Single(
            expenseWorksheet.Value!.Rows,
            item => item.CounterpartyName == "Демонстрационный сотрудник — полный месяц");
        Assert.Equal(45000m, activeStaff.BaseAccrualAmount);
        Assert.Equal(5000m, activeStaff.BonusAmount);
        Assert.Equal(2000m, activeStaff.PenaltyAmount);
        Assert.Equal(48000m, activeStaff.AccrualAmount);
        Assert.Equal(30000m, activeStaff.ExpenseAmount);
        Assert.Contains(
            expenseWorksheet.Value.Rows,
            item => item.CounterpartyName == "Демонстрационный сотрудник — принят в августе"
                && item.BaseAccrualAmount == 30000m);
        var dismissedStaff = Assert.Single(
            expenseWorksheet.Value.Rows,
            item => item.CounterpartyName == "Демонстрационный сотрудник — уволен");
        Assert.Equal(0m, dismissedStaff.BaseAccrualAmount);
        Assert.Equal(0m, dismissedStaff.AccrualAmount);
        Assert.Equal(0m, dismissedStaff.ExpenseAmount);
        var supplierRows = expenseWorksheet.Value.Rows.Where(item => item.SupplierId.HasValue).ToArray();
        Assert.Equal(3, supplierRows.Length);
        Assert.Contains(supplierRows, item =>
            item.CounterpartyName == "ДЕМО Энергосбыт — задолженность"
            && item.ClosingDebt == 8000m
            && item.ClosingAdvance == 0m);
        Assert.Contains(supplierRows, item =>
            item.CounterpartyName == "ДЕМО Вывоз — расчёт закрыт"
            && item.ClosingDebt == 0m
            && item.ClosingAdvance == 0m);
        Assert.Contains(supplierRows, item =>
            item.CounterpartyName == "ДЕМО Водоканал — аванс"
            && item.ClosingDebt == 0m
            && item.ClosingAdvance == 2000m);
        var overdueGarage = await context.Garages.SingleAsync(item => item.Number == "109-ПРОСРОЧКА");
        var overdueAccrual = await context.Accruals.SingleAsync(item =>
            item.GarageId == overdueGarage.Id && item.Basis == "Частично оплаченная просрочка");
        Assert.Equal(new DateOnly(2026, 8, 21), overdueAccrual.OverdueFromDate);
        Assert.Equal(400m, await context.AccrualPaymentAllocations
            .Where(item => item.AccrualId == overdueAccrual.Id)
            .SumAsync(item => item.Amount));
    }

    [PostgreSqlFact]
    public async Task Prepare_PinsBusinessDateSoStartupAutomationKeepsControlScenariosStable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var seeder = new ShowcaseDataSeeder(context);
        var prepared = await seeder.PrepareAsync(CancellationToken.None);
        Assert.True(prepared.IsReady);

        var pinnedDate = await context.ApplicationSettings
            .Where(item => item.Key == ApplicationSettingsService.BusinessDateOverrideKey)
            .Select(item => item.DateValue)
            .SingleAsync();
        Assert.Equal(ShowcaseDataSeeder.BusinessDate, pinnedDate);

        var businessDateProvider = new TestBusinessDateProvider(new DateOnly(2026, 9, 4));
        businessDateProvider.SetOverride(pinnedDate);
        var startupAutomation = new RegularAccrualAutomationRunner(
            FinanceServiceTestFactory.Create(
                context,
                new FixedTimeProvider(new DateTimeOffset(2026, 9, 4, 4, 0, 0, TimeSpan.Zero))),
            businessDateProvider,
            new EfRegularAccrualAutomationLock(context),
            NullLogger<RegularAccrualAutomationRunner>.Instance);

        var automationResult = await startupAutomation.RunCurrentMonthAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
        var audit = await seeder.AuditAsync(CancellationToken.None);

        Assert.True(automationResult.Succeeded, automationResult.Message);
        Assert.Equal(0, automationResult.CreatedCount);
        Assert.True(audit.IsReady);
        Assert.True(audit.HasNoDebt);
        Assert.True(audit.NewGarageHasNoCalculatedHistory);
        Assert.True(audit.BusinessDateIsPinned);
        Assert.Equal(67, await context.Accruals.CountAsync(item => item.Comment == ShowcaseDataSeeder.Marker));
        Assert.DoesNotContain(
            await context.Accruals.AsNoTracking().ToListAsync(),
            item => item.AccountingMonth > ShowcaseDataSeeder.AccountingMonth);
    }

    private static void AssertTier(ShowcaseElectricityTier tier, decimal? upperBound, decimal rate)
    {
        Assert.NotEqual(Guid.Empty, tier.Id);
        Assert.False(string.IsNullOrWhiteSpace(tier.Name));
        Assert.Equal(upperBound, tier.UpperBound);
        Assert.Equal(rate, tier.Rate);
        Assert.False(tier.IsCustom);
    }

    private sealed record ShowcaseElectricityTier(
        Guid Id,
        string Name,
        decimal? UpperBound,
        decimal Rate,
        bool IsCustom);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
