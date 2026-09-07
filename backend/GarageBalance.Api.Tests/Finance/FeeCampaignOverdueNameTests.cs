using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class FeeCampaignOverdueNameTests
{
    [Fact]
    public async Task SqlitePreservesCampaignNamesAndSeparateRowsWithoutChangingMoney()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyNamesAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlPreservesCampaignNamesAndSeparateRowsWithoutChangingMoney()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyNamesAsync(context);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task VerifyNamesAsync(GarageBalanceDbContext context)
    {
        var month = new DateOnly(2026, 6, 1);
        var garage = new Garage { Number = "CAMPAIGN-NAMES", PeopleCount = 1 };
        var income = new IncomeType { Name = "Общий фонд контроль", Code = "campaign_names_test" };
        var first = new FeeCampaign { Name = "Переименованный сбор", IncomeType = income, StartsOn = month, IsArchived = true };
        var second = new FeeCampaign { Name = "Освещение территории", IncomeType = income, StartsOn = month };
        var third = new FeeCampaign { Name = "Ремонт дороги", IncomeType = income, StartsOn = month };
        var paidCampaign = new FeeCampaign { Name = "Оплаченный сбор", IncomeType = income, StartsOn = month };
        Accrual Charge(string? basis, FeeCampaign? campaign, string source, decimal amount = 100m) => new()
        {
            Garage = garage,
            IncomeType = income,
            Basis = basis,
            FeeCampaign = campaign,
            Source = source,
            Amount = amount,
            AccountingMonth = month,
            DueDate = month.AddDays(20),
            OverdueFromDate = month.AddDays(21)
        };
        var snapshot = Charge("Ремонт ворот", first, AccrualSources.FeeCampaign, 300m);
        var liveName = Charge(null, second, AccrualSources.FeeCampaign);
        var blankSnapshot = Charge("   ", third, AccrualSources.FeeCampaign);
        var legacy = Charge("Исторический сбор", null, AccrualSources.FeeCampaign);
        var unknownLegacy = Charge(null, null, AccrualSources.FeeCampaign);
        var regular = Charge("Основание обычного начисления", null, AccrualSources.Manual);
        var fullyPaid = Charge("Оплаченный сбор", paidCampaign, AccrualSources.FeeCampaign, 50m);
        context.AddRange(snapshot, liveName, blankSnapshot, legacy, unknownLegacy, regular, fullyPaid);
        context.AddRange(new AccrualPaymentAllocation
        {
            Accrual = snapshot,
            Amount = 100m,
            FinancialOperation = new FinancialOperation
            {
                Garage = garage,
                IncomeType = income,
                OperationKind = FinancialOperationKinds.Income,
                Amount = 100m,
                OperationDate = month.AddDays(22),
                AccountingMonth = month
            }
        }, new AccrualPaymentAllocation
        {
            Accrual = fullyPaid,
            Amount = 50m,
            FinancialOperation = new FinancialOperation
            {
                Garage = garage,
                IncomeType = income,
                OperationKind = FinancialOperationKinds.Income,
                Amount = 50m,
                OperationDate = month.AddDays(22),
                AccountingMonth = month
            }
        });
        await context.SaveChangesAsync();
        var auditIds = await context.AuditEvents.Select(item => item.Id).OrderBy(id => id).ToArrayAsync();
        var service = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)));
        var result = await service.GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(700m, result.Value!.Total);
        Assert.Equal(6, result.Value.Rows.Count);
        Assert.All(result.Value.Rows, row => Assert.Equal(income.Name, row.IncomeTypeName));
        var rows = result.Value.Rows.ToDictionary(row => row.AccrualId!.Value);
        Assert.Equal("Ремонт ворот", rows[snapshot.Id].ChargeName);
        Assert.Equal(300m, rows[snapshot.Id].OriginalAmount);
        Assert.Equal(100m, rows[snapshot.Id].PaidAmount);
        Assert.Equal(200m, rows[snapshot.Id].OutstandingAmount);
        Assert.Equal(second.Name, rows[liveName.Id].ChargeName);
        Assert.Equal(third.Name, rows[blankSnapshot.Id].ChargeName);
        Assert.Equal("Исторический сбор", rows[legacy.Id].ChargeName);
        Assert.Equal(income.Name, rows[unknownLegacy.Id].ChargeName);
        Assert.Equal(income.Name, rows[regular.Id].ChargeName);
        Assert.DoesNotContain(fullyPaid.Id, rows.Keys);
        Assert.Equal(300m, (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == snapshot.Id)).Amount);
        Assert.Equal(auditIds, await context.AuditEvents.Select(item => item.Id).OrderBy(id => id).ToArrayAsync());
        Assert.Empty(await new EfAccrualRepository(context).GetOverdueDebtDetailsAsync(garage.Id, month, CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfAccrualRepository(context).GetOverdueDebtDetailsAsync(garage.Id, month.AddMonths(2), canceled.Token));
    }
}
