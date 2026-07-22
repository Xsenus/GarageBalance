using GarageBalance.Api.Application.Dictionaries;
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
    public async Task Campaigns_GenerateForAllOrUpdatedSelectionAndLockHistoricalParticipants()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var firstGarage = new Garage { Number = "FEE-ALL-1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = "FEE-ALL-2", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var thirdGarage = new Garage { Number = "FEE-ALL-3", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var archivedGarage = new Garage { Number = "FEE-ARCHIVED", PeopleCount = 1, FloorCount = 1, Owner = owner, IsArchived = true };
        Guid otherIncomeId;
        Guid allCampaignId;
        Guid selectedCampaignId;

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(owner, firstGarage, secondGarage, thirdGarage, archivedGarage);
            await setupContext.SaveChangesAsync();
            otherIncomeId = await setupContext.IncomeTypes
                .Where(item => item.Code == "other_income")
                .Select(item => item.Id)
                .SingleAsync();
            var dictionaries = DictionaryServiceTestFactory.Create(setupContext);
            var allCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest("Сбор для всех", otherIncomeId, true, []),
                null,
                CancellationToken.None);
            var selectedCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [firstGarage.Id]),
                null,
                CancellationToken.None);
            Assert.True(allCampaign.Succeeded, allCampaign.ErrorMessage);
            Assert.True(selectedCampaign.Succeeded, selectedCampaign.ErrorMessage);
            allCampaignId = allCampaign.Value!.Id;
            selectedCampaignId = selectedCampaign.Value!.Id;

            var changedBeforeGeneration = await dictionaries.UpdateFeeCampaignAsync(
                selectedCampaignId,
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [firstGarage.Id, secondGarage.Id]),
                null,
                CancellationToken.None);
            Assert.True(changedBeforeGeneration.Succeeded, changedBeforeGeneration.ErrorMessage);
            Assert.Equal([firstGarage.Id, secondGarage.Id], changedBeforeGeneration.Value!.ParticipantGarageIds);
        }

        await using (var generationContext = database.CreateContext())
        {
            var finance = FinanceServiceTestFactory.Create(generationContext);
            var allResult = await finance.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(allCampaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);
            var selectedResult = await finance.GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(selectedCampaignId, new DateOnly(2026, 6, 1), null),
                null,
                CancellationToken.None);

            Assert.True(allResult.Succeeded, allResult.ErrorMessage);
            Assert.Equal(3, allResult.Value!.CreatedCount);
            Assert.DoesNotContain(allResult.Value.CreatedAccruals, item => item.GarageId == archivedGarage.Id);
            Assert.True(selectedResult.Succeeded, selectedResult.ErrorMessage);
            Assert.Equal(2, selectedResult.Value!.CreatedCount);
            Assert.Equal(
                new[] { firstGarage.Id, secondGarage.Id }.Order().ToArray(),
                selectedResult.Value.CreatedAccruals.Select(item => item.GarageId).Order().ToArray());
        }

        await using (var updateContext = database.CreateContext())
        {
            var dictionaries = DictionaryServiceTestFactory.Create(updateContext);
            var rejected = await dictionaries.UpdateFeeCampaignAsync(
                selectedCampaignId,
                CampaignRequest("Выборочный сбор", otherIncomeId, false, [thirdGarage.Id]),
                null,
                CancellationToken.None);

            Assert.False(rejected.Succeeded);
            Assert.Equal("fee_campaign_participants_locked", rejected.ErrorCode);
            var storedParticipants = await updateContext.FeeCampaignGarages
                .Where(item => item.FeeCampaignId == selectedCampaignId)
                .Select(item => item.GarageId)
                .Order()
                .ToArrayAsync();
            Assert.Equal(new[] { firstGarage.Id, secondGarage.Id }.Order().ToArray(), storedParticipants);
            Assert.Equal(2, await updateContext.Accruals.CountAsync(item => item.FeeCampaignId == selectedCampaignId));
            Assert.Single(updateContext.AuditEvents, item =>
                item.Action == "dictionary.fee_campaign_updated" && item.EntityId == selectedCampaignId.ToString());
        }
    }

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

    [PostgreSqlFact]
    public async Task ActiveCampaignAutomation_UsesPostgreSqlMonthWindowAndRemainsIdempotent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var garage = builder.BuildGarage(number: "FEE-AUTO-PG-1");
        Guid dueCampaignId;

        await using (var setupContext = database.CreateContext())
        {
            var destination = await setupContext.IncomeTypes.SingleAsync(item => item.Code == "other_income");
            var dueCampaign = CreateCampaign("Автоматический сбор PostgreSQL", destination, 650m);
            dueCampaign.StartsOn = new DateOnly(2026, 8, 20);
            dueCampaign.EndsOn = new DateOnly(2026, 8, 31);
            var futureCampaign = CreateCampaign("Будущий сбор PostgreSQL", destination, 700m);
            futureCampaign.StartsOn = new DateOnly(2026, 9, 1);
            setupContext.AddRange(garage, dueCampaign, futureCampaign);
            await setupContext.SaveChangesAsync();
            dueCampaignId = dueCampaign.Id;
        }

        await using (var generationContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(generationContext);
            var request = new GenerateActiveFeeCampaignAccrualsRequest(new DateOnly(2026, 8, 1), "Автоматический запуск PostgreSQL");

            var first = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);
            var second = await service.GenerateActiveFeeCampaignAccrualsAsync(request, null, CancellationToken.None);

            Assert.True(first.Succeeded, first.ErrorMessage);
            Assert.Equal(1, first.Value!.CreatedCount);
            Assert.Equal(dueCampaignId, Assert.Single(first.Value.CampaignResults).FeeCampaignId);
            Assert.True(second.Succeeded, second.ErrorMessage);
            Assert.Equal(0, second.Value!.CreatedCount);
            Assert.Single(second.Value.SkippedCampaigns);
        }

        await using var verificationContext = database.CreateContext();
        var accrual = await verificationContext.Accruals.AsNoTracking().SingleAsync();
        Assert.Equal(dueCampaignId, accrual.FeeCampaignId);
        Assert.Equal(new DateOnly(2026, 8, 1), accrual.AccountingMonth);
        Assert.Contains("Автоматический запуск PostgreSQL", accrual.Comment, StringComparison.Ordinal);
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

    private static UpsertFeeCampaignRequest CampaignRequest(
        string name,
        Guid incomeTypeId,
        bool appliesToAllGarages,
        IReadOnlyList<Guid> participantGarageIds) =>
        new(
            name,
            incomeTypeId,
            null,
            500m,
            5000m,
            new DateOnly(2026, 5, 1),
            null,
            appliesToAllGarages,
            30,
            participantGarageIds);
}
