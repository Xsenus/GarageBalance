using System.Text.Json;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Tests.Common;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;

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
        Assert.Equal(65, second.AccrualCount);
        Assert.Equal(8, second.FinancialOperationCount);
        Assert.Equal(36, second.MeterReadingCount);
        Assert.Equal(2, second.FeeCampaignCount);
        Assert.Equal(1, second.SupplierCount);
        Assert.Equal(1, second.PreservedUserCount);
        Assert.True(second.HasNoDebt);
        Assert.True(second.HasDebt);
        Assert.True(second.HasAdvance);
        Assert.True(second.NewGarageHasNoCalculatedHistory);
        Assert.True(second.CampaignsHaveLockedParticipants);
        Assert.True(second.AnnualAccrualsAreUnique);
        Assert.True(second.OverdueScenarioIsCorrect);
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
        Assert.All(
            await context.FeeCampaigns.Where(item => item.Goal != null && item.Goal.Contains(ShowcaseDataSeeder.Marker)).ToListAsync(),
            campaign => Assert.Equal(9, context.FeeCampaignGarages.Count(item => item.FeeCampaignId == campaign.Id)));
        var overdueGarage = await context.Garages.SingleAsync(item => item.Number == "109-ПРОСРОЧКА");
        var overdueAccrual = await context.Accruals.SingleAsync(item =>
            item.GarageId == overdueGarage.Id && item.Basis == "Частично оплаченная просрочка");
        Assert.Equal(new DateOnly(2026, 8, 21), overdueAccrual.OverdueFromDate);
        Assert.Equal(400m, await context.AccrualPaymentAllocations
            .Where(item => item.AccrualId == overdueAccrual.Id)
            .SumAsync(item => item.Amount));
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
}
