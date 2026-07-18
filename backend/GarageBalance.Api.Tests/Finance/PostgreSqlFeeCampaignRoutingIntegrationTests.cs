using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignRoutingIntegrationTests
{
    [PostgreSqlFact]
    public async Task MigratedDatabase_RoutesDifferentCampaignsToStableDestinationAndRejectsCampaignDuplicate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "FEE-PG-1");
        var legacyIncomeType = new IncomeType { Name = "Старое назначение сбора", Code = "legacy_fee" };
        var firstCampaign = CreateCampaign("Сбор на ворота", legacyIncomeType, 500m);
        var secondCampaign = CreateCampaign("Сбор на камеры", legacyIncomeType, 700m);

        Guid destinationId;
        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            destination.Name = "Переименованное назначение доходов";
            destinationId = destination.Id;
            setupContext.AddRange(garage, legacyIncomeType, firstCampaign, secondCampaign);
            await setupContext.SaveChangesAsync();
        }

        await using (var createContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(createContext);
            var first = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(firstCampaign.Id, new DateOnly(2026, 9, 1), null),
                null,
                CancellationToken.None);
            var second = await service.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(secondCampaign.Id, new DateOnly(2026, 9, 1), null),
                null,
                CancellationToken.None);

            Assert.True(first.Succeeded, first.ErrorMessage);
            Assert.True(second.Succeeded, second.ErrorMessage);
            Assert.Equal(destinationId, first.Value!.IncomeTypeId);
            Assert.Equal(destinationId, second.Value!.IncomeTypeId);
            Assert.Equal("Переименованное назначение доходов", first.Value.IncomeTypeName);
            Assert.Equal(firstCampaign.Id, Assert.Single(first.Value.CreatedAccruals).FeeCampaignId);
            Assert.Equal(secondCampaign.Id, Assert.Single(second.Value.CreatedAccruals).FeeCampaignId);
        }

        await using (var verificationContext = database.CreateContext())
        {
            var accruals = await verificationContext.Accruals
                .AsNoTracking()
                .Include(item => item.IncomeType)
                .Include(item => item.FeeCampaign)
                .OrderBy(item => item.Amount)
                .ToListAsync();
            Assert.Equal(2, accruals.Count);
            Assert.All(accruals, item =>
            {
                Assert.Equal("other_income", item.IncomeType.Code);
                Assert.NotNull(item.IncomeType.DestinationFundId);
                Assert.NotNull(item.FeeCampaign);
            });

            verificationContext.Accruals.Add(new Accrual
            {
                GarageId = garage.Id,
                IncomeTypeId = destinationId,
                FeeCampaignId = firstCampaign.Id,
                AccountingMonth = new DateOnly(2026, 9, 1),
                DueDate = new DateOnly(2026, 9, 30),
                OverdueFromDate = new DateOnly(2026, 10, 31),
                Amount = 500m,
                Source = AccrualSources.FeeCampaign
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
        }
    }

    private static FeeCampaign CreateCampaign(string name, IncomeType incomeType, decimal contributionAmount) =>
        new()
        {
            Name = name,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            ContributionAmount = contributionAmount,
            TargetAmount = contributionAmount * 10,
            StartsOn = new DateOnly(2026, 1, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
}
