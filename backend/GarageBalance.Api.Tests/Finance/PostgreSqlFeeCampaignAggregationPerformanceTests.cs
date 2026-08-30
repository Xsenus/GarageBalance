using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFeeCampaignAggregationPerformanceTests
{
    [PostgreSqlFact]
    public async Task FeeCampaignAmountsAndPaymentOptionsUseBoundedCombinedQueries()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var incomeType = new IncomeType
        {
            Name = "Performance fee income",
            Code = "performance_fee_income"
        };
        var garage = new Garage { Number = "PERF-FEE-001" };
        var firstCampaign = new FeeCampaign
        {
            Name = "Performance fee A",
            IncomeType = incomeType,
            ContributionAmount = 500m,
            TargetAmount = 5_000m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = false,
            OverdueGraceDays = 30
        };
        var secondCampaign = new FeeCampaign
        {
            Name = "Performance fee B",
            IncomeType = incomeType,
            ContributionAmount = 700m,
            TargetAmount = 7_000m,
            StartsOn = new DateOnly(2026, 8, 1),
            AppliesToAllGarages = true,
            OverdueGraceDays = 30
        };
        firstCampaign.ParticipantGarages.Add(new FeeCampaignGarage
        {
            FeeCampaign = firstCampaign,
            Garage = garage
        });
        var firstAccrual = CreateAccrual(garage, incomeType, firstCampaign, 500m);
        var secondAccrual = CreateAccrual(garage, incomeType, secondCampaign, 700m);
        var taggedFirst = CreateIncome(garage, incomeType, 100m, firstCampaign);
        var taggedSecond = CreateIncome(garage, incomeType, 80m, secondCampaign);
        var legacyIncome = CreateIncome(garage, incomeType, 40m, null);
        var canceledTagged = CreateIncome(garage, incomeType, 900m, firstCampaign);
        canceledTagged.IsCanceled = true;

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                incomeType,
                garage,
                firstCampaign,
                secondCampaign,
                firstAccrual,
                secondAccrual,
                taggedFirst,
                taggedSecond,
                legacyIncome,
                canceledTagged);
            setupContext.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
            {
                FinancialOperation = legacyIncome,
                Accrual = firstAccrual,
                Amount = 40m
            });
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfFeeCampaignRepository(context);

        Assert.Equal(140m, await repository.GetCollectedAmountAsync(firstCampaign.Id, CancellationToken.None));
        AssertSingleCombinedCommand(capture);

        var collected = await repository.GetCollectedAmountsAsync(
            [firstCampaign.Id, secondCampaign.Id],
            CancellationToken.None);
        Assert.Equal(140m, collected[firstCampaign.Id]);
        Assert.Equal(80m, collected[secondCampaign.Id]);
        AssertSingleCombinedCommand(capture);

        var paidByGarage = await repository.GetPaidAmountsByGarageAsync(firstCampaign.Id, CancellationToken.None);
        Assert.Equal(140m, paidByGarage[garage.Id]);
        AssertSingleCombinedCommand(capture);

        var paymentOptions = await repository.GetPaymentOptionsForGarageAsync(
            garage.Id,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            CancellationToken.None);
        var optionsByCampaign = paymentOptions.ToDictionary(option => option.Campaign.Id);
        Assert.Equal(2, optionsByCampaign.Count);
        var firstOption = optionsByCampaign[firstCampaign.Id];
        Assert.Equal(firstAccrual.Id, firstOption.Accrual?.Id);
        Assert.Equal(40m, firstOption.PaidAmount);
        Assert.Equal(140m, firstOption.CollectedAmount);
        Assert.Equal(EntityState.Unchanged, context.Entry(firstOption.Campaign).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(firstOption.Accrual!).State);
        Assert.Same(firstOption.Campaign, firstOption.Accrual!.FeeCampaign);
        Assert.Equal(incomeType.Id, firstOption.Campaign.IncomeType.Id);
        Assert.Equal(garage.Id, Assert.Single(firstOption.Campaign.ParticipantGarages).Garage.Id);
        var secondOption = optionsByCampaign[secondCampaign.Id];
        Assert.Equal(secondAccrual.Id, secondOption.Accrual?.Id);
        Assert.Equal(0m, secondOption.PaidAmount);
        Assert.Equal(80m, secondOption.CollectedAmount);
        var optionCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("FROM fee_campaigns", optionCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM accruals", optionCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM financial_operations", optionCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM accrual_payment_allocations", optionCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT count(", optionCommand, StringComparison.OrdinalIgnoreCase);

        var emptyOptions = await repository.GetPaymentOptionsForGarageAsync(
            garage.Id,
            new DateOnly(2025, 8, 1),
            new DateOnly(2025, 8, 1),
            CancellationToken.None);
        Assert.Empty(emptyOptions);
        var emptyOptionCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("FROM fee_campaigns", emptyOptionCommand, StringComparison.OrdinalIgnoreCase);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetPaymentOptionsForGarageAsync(
                garage.Id,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 1),
                cancellation.Token));
        Assert.Empty(capture.TakeCommandsAndClear());
    }

    private static Accrual CreateAccrual(
        Garage garage,
        IncomeType incomeType,
        FeeCampaign campaign,
        decimal amount) =>
        new()
        {
            Garage = garage,
            IncomeType = incomeType,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 8, 31),
            OverdueFromDate = new DateOnly(2026, 10, 1),
            Amount = amount,
            Source = AccrualSources.FeeCampaign,
            Basis = campaign.Name
        };

    private static FinancialOperation CreateIncome(
        Garage garage,
        IncomeType incomeType,
        decimal amount,
        FeeCampaign? campaign) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 8, 15),
            AccountingMonth = new DateOnly(2026, 8, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            FeeCampaign = campaign
        };

    private static void AssertSingleCombinedCommand(SelectCommandCapture capture)
    {
        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM", command, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(command.CommandText);
            }
        }
    }
}
