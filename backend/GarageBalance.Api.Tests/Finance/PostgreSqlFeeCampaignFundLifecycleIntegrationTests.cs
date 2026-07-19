using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignFundLifecycleIntegrationTests
{
    [PostgreSqlFact]
    public async Task FeeCampaignAndFundLifecycle_CoversAllSelectedIncomeDistributionWithdrawalCancellationAndRestoration()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var month = new DateOnly(2026, 8, 1);
        Guid firstGarageId;
        Guid secondGarageId;
        Guid thirdGarageId;
        Guid archivedGarageId;
        Guid allCampaignId;
        Guid selectedCampaignId;
        Guid campaignIncomeTypeId;
        Guid freeIncomeTypeId;
        Guid campaignFundId;

        await using (var setupContext = database.CreateContext())
        {
            var owner = new Owner { LastName = "Проверка", FirstName = "Фондов" };
            var firstGarage = CreateGarage("FUND-LIFECYCLE-1", owner);
            var secondGarage = CreateGarage("FUND-LIFECYCLE-2", owner);
            var thirdGarage = CreateGarage("FUND-LIFECYCLE-3", owner);
            var archivedGarage = CreateGarage("FUND-LIFECYCLE-ARCHIVED", owner, isArchived: true);
            setupContext.AddRange(owner, firstGarage, secondGarage, thirdGarage, archivedGarage);
            await setupContext.SaveChangesAsync();
            firstGarageId = firstGarage.Id;
            secondGarageId = secondGarage.Id;
            thirdGarageId = thirdGarage.Id;
            archivedGarageId = archivedGarage.Id;

            var dictionaries = DictionaryServiceTestFactory.Create(setupContext);
            var freeIncomeType = await dictionaries.CreateIncomeTypeAsync(
                new UpsertAccountingTypeRequest("Свободное поступление сценария фондов PG", "pg_fund_lifecycle_income"),
                actorUserId,
                CancellationToken.None);
            var campaignIncomeType = await setupContext.IncomeTypes
                .AsNoTracking()
                .SingleAsync(item => item.Code == "other_income");
            Assert.True(freeIncomeType.Succeeded, freeIncomeType.ErrorMessage);
            Assert.NotNull(campaignIncomeType.DestinationFundId);
            freeIncomeTypeId = freeIncomeType.Value!.Id;
            campaignIncomeTypeId = campaignIncomeType.Id;
            campaignFundId = campaignIncomeType.DestinationFundId!.Value;

            var allCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest(
                    "Сбор для всех гаражей PG",
                    campaignIncomeTypeId,
                    contributionAmount: 100m,
                    targetAmount: 300m,
                    appliesToAllGarages: true,
                    participantGarageIds: []),
                actorUserId,
                CancellationToken.None);
            var selectedCampaign = await dictionaries.CreateFeeCampaignAsync(
                CampaignRequest(
                    "Выборочный сбор PG",
                    campaignIncomeTypeId,
                    contributionAmount: 50m,
                    targetAmount: 100m,
                    appliesToAllGarages: false,
                    participantGarageIds: [firstGarageId, thirdGarageId]),
                actorUserId,
                CancellationToken.None);
            Assert.True(allCampaign.Succeeded, allCampaign.ErrorMessage);
            Assert.True(selectedCampaign.Succeeded, selectedCampaign.ErrorMessage);
            allCampaignId = allCampaign.Value!.Id;
            selectedCampaignId = selectedCampaign.Value!.Id;
        }

        await using var context = database.CreateContext();
        var finance = FinanceServiceTestFactory.Create(context);
        var funds = CreateFundService(context);
        var allAccruals = await finance.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(allCampaignId, month, "Начисление всем гаражам PG"),
            actorUserId,
            CancellationToken.None);
        var selectedAccruals = await finance.GenerateFeeCampaignAccrualsAsync(
            new GenerateFeeCampaignAccrualsRequest(selectedCampaignId, month, "Начисление выбранным гаражам PG"),
            actorUserId,
            CancellationToken.None);

        Assert.True(allAccruals.Succeeded, allAccruals.ErrorMessage);
        Assert.Equal(3, allAccruals.Value!.CreatedCount);
        Assert.Equal(300m, allAccruals.Value.TotalAmount);
        Assert.DoesNotContain(allAccruals.Value.CreatedAccruals, item => item.GarageId == archivedGarageId);
        Assert.True(selectedAccruals.Succeeded, selectedAccruals.ErrorMessage);
        Assert.Equal(2, selectedAccruals.Value!.CreatedCount);
        Assert.Equal(100m, selectedAccruals.Value.TotalAmount);
        Assert.Equal(
            new[] { firstGarageId, thirdGarageId }.Order().ToArray(),
            selectedAccruals.Value.CreatedAccruals.Select(item => item.GarageId).Order().ToArray());

        var campaignIncome = await finance.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                firstGarageId,
                campaignIncomeTypeId,
                new DateOnly(2026, 8, 10),
                month,
                150m,
                "PG-FUND-CAMPAIGN-INCOME",
                "Полная оплата двух сборов PG"),
            actorUserId,
            CancellationToken.None);
        var freeIncome = await finance.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                secondGarageId,
                freeIncomeTypeId,
                new DateOnly(2026, 8, 11),
                month,
                300m,
                "PG-FUND-FREE-INCOME",
                "Поступление для ручного распределения PG"),
            actorUserId,
            CancellationToken.None);

        Assert.True(campaignIncome.Succeeded, campaignIncome.ErrorMessage);
        Assert.Equal(150m, campaignIncome.Value!.GarageDebtBefore);
        Assert.Equal(0m, campaignIncome.Value.GarageDebtAfter);
        Assert.True(freeIncome.Succeeded, freeIncome.ErrorMessage);

        var operationalFund = Assert.Single(
            (await funds.GetFundsAsync(CancellationToken.None))
                .Where(item => item.AllowOperations && item.Id != campaignFundId)
                .Take(1));
        var distribution = await funds.CreateOperationAsync(
            operationalFund.Id,
            new CreateFundOperationRequest("deposit", 200m, "Распределение поступления в фонд PG"),
            actorUserId,
            CancellationToken.None);
        var withdrawal = await funds.CreateOperationAsync(
            operationalFund.Id,
            new CreateFundOperationRequest("withdraw", 50m, "Изъятие из фонда PG"),
            actorUserId,
            CancellationToken.None);

        Assert.True(distribution.Succeeded, distribution.ErrorMessage);
        Assert.Equal(0m, distribution.Value!.BalanceBefore);
        Assert.Equal(200m, distribution.Value.BalanceAfter);
        Assert.True(withdrawal.Succeeded, withdrawal.ErrorMessage);
        Assert.Equal(200m, withdrawal.Value!.BalanceBefore);
        Assert.Equal(150m, withdrawal.Value.BalanceAfter);

        var canceledWithdrawal = await funds.CancelOperationAsync(
            withdrawal.Value.Id,
            new CancelFundOperationRequest("Проверка отмены изъятия PG"),
            actorUserId,
            CancellationToken.None);
        Assert.True(canceledWithdrawal.Succeeded, canceledWithdrawal.ErrorMessage);
        Assert.True(canceledWithdrawal.Value!.IsCanceled);
        context.ChangeTracker.Clear();
        Assert.Equal(
            200m,
            Assert.Single(await funds.GetFundsAsync(CancellationToken.None), item => item.Id == operationalFund.Id).Balance);

        var canceledDistribution = await funds.CancelOperationAsync(
            distribution.Value.Id,
            new CancelFundOperationRequest("Проверка отмены распределения PG"),
            actorUserId,
            CancellationToken.None);
        Assert.True(canceledDistribution.Succeeded, canceledDistribution.ErrorMessage);
        Assert.True(canceledDistribution.Value!.IsCanceled);
        context.ChangeTracker.Clear();
        Assert.Equal(
            0m,
            Assert.Single(await funds.GetFundsAsync(CancellationToken.None), item => item.Id == operationalFund.Id).Balance);

        var restoredDistribution = await funds.RestoreOperationAsync(
            distribution.Value.Id,
            actorUserId,
            CancellationToken.None);
        Assert.True(restoredDistribution.Succeeded, restoredDistribution.ErrorMessage);
        Assert.False(restoredDistribution.Value!.IsCanceled);
        context.ChangeTracker.Clear();
        Assert.Equal(
            200m,
            Assert.Single(await funds.GetFundsAsync(CancellationToken.None), item => item.Id == operationalFund.Id).Balance);

        var restoredWithdrawal = await funds.RestoreOperationAsync(
            withdrawal.Value.Id,
            actorUserId,
            CancellationToken.None);
        Assert.True(restoredWithdrawal.Succeeded, restoredWithdrawal.ErrorMessage);
        Assert.False(restoredWithdrawal.Value!.IsCanceled);
        context.ChangeTracker.Clear();
        Assert.Equal(
            150m,
            Assert.Single(await funds.GetFundsAsync(CancellationToken.None), item => item.Id == operationalFund.Id).Balance);

        context.ChangeTracker.Clear();
        var finalFunds = await funds.GetFundsAsync(CancellationToken.None);
        Assert.Equal(150m, Assert.Single(finalFunds, item => item.Id == campaignFundId).Balance);
        var finalOperationalFund = Assert.Single(finalFunds, item => item.Id == operationalFund.Id);
        Assert.Equal(150m, finalOperationalFund.Balance);
        Assert.Equal(150m, finalOperationalFund.AvailableToDistribute);
        Assert.Equal(5, await context.Accruals.CountAsync(item =>
            item.FeeCampaignId == allCampaignId || item.FeeCampaignId == selectedCampaignId));
        Assert.Equal(2, await context.AccrualPaymentAllocations.CountAsync(item =>
            item.FinancialOperationId == campaignIncome.Value.Id));
        Assert.Equal(150m, await context.AccrualPaymentAllocations
            .Where(item => item.FinancialOperationId == campaignIncome.Value.Id)
            .SumAsync(item => item.Amount));
        Assert.Equal(3, await context.FundOperations.CountAsync(item =>
            item.FundId == campaignFundId || item.FundId == operationalFund.Id));
        Assert.All(
            await context.FundOperations
                .Where(item => item.FundId == campaignFundId || item.FundId == operationalFund.Id)
                .ToListAsync(),
            item => Assert.False(item.IsCanceled));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "dictionary.fee_campaign_created"));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "finance.fee_campaign_accruals_generated"));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "finance.income_created"));
        Assert.Equal(1, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "fund.income_assignment_created"));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "fund.operation_canceled"));
        Assert.Equal(2, await context.AuditEvents.CountAsync(item =>
            item.ActorUserId == actorUserId && item.Action == "fund.operation_restored"));
    }

    private static Garage CreateGarage(string number, Owner owner, bool isArchived = false) =>
        new()
        {
            Number = number,
            PeopleCount = 1,
            FloorCount = 1,
            Owner = owner,
            IsArchived = isArchived
        };

    private static UpsertFeeCampaignRequest CampaignRequest(
        string name,
        Guid incomeTypeId,
        decimal contributionAmount,
        decimal targetAmount,
        bool appliesToAllGarages,
        IReadOnlyList<Guid> participantGarageIds) =>
        new(
            name,
            incomeTypeId,
            "Сквозная проверка сборов и фондов PG",
            contributionAmount,
            targetAmount,
            new DateOnly(2026, 8, 1),
            null,
            appliesToAllGarages,
            30,
            participantGarageIds);

    private static FundService CreateFundService(GarageBalanceDbContext context) =>
        new(
            new EfFundRepository(context),
            new EfFinanceAvailableBalanceQuery(context),
            new AuditEventWriter(context));
}
