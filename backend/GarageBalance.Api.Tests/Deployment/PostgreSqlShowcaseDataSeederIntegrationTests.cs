using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Tests.Common;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlShowcaseDataSeederIntegrationTests
{
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

        var seeder = new ShowcaseDataSeeder(context);
        var first = await seeder.PrepareAsync(CancellationToken.None);
        var second = await seeder.PrepareAsync(CancellationToken.None);

        Assert.True(first.IsReady);
        Assert.True(second.IsReady);
        Assert.Equal(8, second.GarageCount);
        Assert.Equal(57, second.AccrualCount);
        Assert.Equal(7, second.FinancialOperationCount);
        Assert.Equal(32, second.MeterReadingCount);
        Assert.Equal(2, second.FeeCampaignCount);
        Assert.Equal(1, second.SupplierCount);
        Assert.Equal(1, second.PreservedUserCount);
        Assert.True(second.HasNoDebt);
        Assert.True(second.HasDebt);
        Assert.True(second.HasAdvance);
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
        Assert.All(
            await context.Accruals.Where(item => item.Source == "regular").ToListAsync(),
            item => Assert.False(string.IsNullOrWhiteSpace(item.CalculationDetailsJson)));
    }
}
