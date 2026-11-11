using System.Text.Json;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageIncomeWorksheetRefreshTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public async Task RefreshAfterCatchUpDoesNotInsertOrNormalizeConcurrentCampaignChanges()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyRefreshAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RefreshAfterCatchUpDoesNotInsertOrNormalizeConcurrentCampaignChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyRefreshAsync(context);
    }

    private static async Task VerifyRefreshAsync(GarageBalanceDbContext context)
    {
        var (garage, _) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var incomeType = new IncomeType
        {
            Name = "Synthetic refresh campaign income",
            Code = "synthetic_refresh_campaign",
            DestinationFund = new Fund { Name = "Synthetic refresh fund", NormalizedName = "SYNTHETIC REFRESH FUND" }
        };
        FeeCampaign Campaign(string name, DateOnly startsOn) => new()
        {
            Name = name,
            IncomeType = incomeType,
            StartsOn = startsOn,
            ContributionAmount = 100,
            TargetAmount = 1000,
            ParticipantGarages = [new FeeCampaignGarage { Garage = garage }]
        };
        var existing = Campaign("Synthetic existing campaign", October);
        context.Add(existing);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        var initialPeriod = await service.GetFinancialReportPeriodAsync(new(garage.Id, null, null), default);
        Assert.Equal(November, initialPeriod.Value!.DefaultMonthFrom);
        Assert.Empty(await context.Accruals.ToArrayAsync());
        var calculated = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default);
        Assert.True(calculated.Succeeded, calculated.ErrorMessage);
        var existingAccrual = await context.Accruals.SingleAsync(row => row.FeeCampaignId == existing.Id);
        Assert.Equal(100m, existingAccrual.Amount);

        // Simulate updates committed after the first calculation, before its UI refresh.
        // A normal worksheet GET would insert the new campaign and normalize the old one.
        existing.Name = "Synthetic changed campaign";
        existing.ContributionAmount = 200;
        var missing = Campaign("Synthetic newly eligible campaign", October);
        var closed = Campaign("Synthetic closed campaign", October);
        closed.ClosedAtUtc = DateTimeOffset.UtcNow;
        var future = Campaign("Synthetic future campaign", November.AddMonths(1));
        var payment = new FinancialOperation
        {
            Garage = garage,
            IncomeType = incomeType,
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = October.AddDays(10),
            AccountingMonth = October,
            Amount = 50
        };
        context.AddRange(missing, closed, future, payment,
            new AccrualPaymentAllocation { Accrual = existingAccrual, FinancialOperation = payment, Amount = 50 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = await SnapshotAsync(context);
        var refreshedPeriod = await service.GetFinancialReportPeriodAsync(new(garage.Id, null, null), default);
        Assert.Equal(October, refreshedPeriod.Value!.DefaultMonthFrom);
        Assert.Equal(November, refreshedPeriod.Value.DefaultMonthTo);
        var refresh = await service.GetGarageIncomeWorksheetAsync(garage.Id,
            new(refreshedPeriod.Value.DefaultMonthFrom, refreshedPeriod.Value.DefaultMonthTo, RefreshOnly: true), default);
        Assert.True(refresh.Succeeded, refresh.ErrorMessage);
        Assert.Equal(before, await SnapshotAsync(context));
        Assert.False(context.ChangeTracker.HasChanges());
        var annual = Assert.Single(refresh.Value!.Rows, row => row.AnnualAccrualId.HasValue);
        Assert.Equal(1200m, annual.AccrualAmount);
        Assert.Equal(1200m, annual.PayableAmount);
        var preservedFee = Assert.Single(refresh.Value.Rows, row => row.FeeCampaignId == existing.Id);
        Assert.Equal(100m, preservedFee.AccrualAmount);
        Assert.Equal(50m, preservedFee.Debt);
        Assert.DoesNotContain(refresh.Value.Rows, row => row.FeeCampaignId == missing.Id);

        // The default GET contract is unchanged, including its established fee workflow.
        var normal = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(October, November), default);
        Assert.True(normal.Succeeded, normal.ErrorMessage);
        Assert.Equal(200m, (await context.Accruals.SingleAsync(row => row.Id == existingAccrual.Id)).Amount);
        Assert.Equal(existing.Name, (await context.Accruals.SingleAsync(row => row.Id == existingAccrual.Id)).Basis);
        Assert.Single(await context.Accruals.Where(row => row.FeeCampaignId == missing.Id).ToArrayAsync());
        Assert.Empty(await context.Accruals.Where(row => row.FeeCampaignId == closed.Id || row.FeeCampaignId == future.Id).ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitCalculationDoesNotBecomeReadOnlyBecauseOfRefreshFlag()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, _) = await AnnualAccountingStartTests.SeedAsync(database.Context, October);
        var result = await FinanceServiceTestFactory.Create(database.Context, businessDate: November)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November, RefreshOnly: true), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Single(await database.Context.Accruals.ToArrayAsync());
    }

    private static async Task<string> SnapshotAsync(GarageBalanceDbContext context)
    {
        async Task<string> RowsAsync<TEntity>(DbSet<TEntity> set) where TEntity : class
        {
            var rows = await set.AsNoTracking().ToArrayAsync();
            return string.Join("\n", rows.Select(row => JsonSerializer.Serialize(context.Entry(row).Properties
                    .OrderBy(property => property.Metadata.Name)
                    .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue)))
                .Order(StringComparer.Ordinal));
        }

        return string.Join("\n---\n",
            await RowsAsync(context.Accruals),
            await RowsAsync(context.FeeCampaigns),
            await RowsAsync(context.AccrualPaymentAllocations),
            await RowsAsync(context.FinancialOperations),
            await RowsAsync(context.AuditEvents));
    }
}
