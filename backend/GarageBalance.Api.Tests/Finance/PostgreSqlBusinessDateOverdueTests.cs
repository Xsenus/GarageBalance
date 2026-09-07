using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlBusinessDateOverdueTests
{
    [PostgreSqlFact]
    public async Task NovemberFifthAndReverseDateRespectServiceAndCampaignDeadlinesWithoutChangingMoney()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2026, 9, 1);
        var garage = new Garage { Number = "DATE-05-NOV", PeopleCount = 1 };
        var water = new IncomeType { Name = "Вода контроль даты", Code = "date_water_test" };
        var trash = new IncomeType { Name = "Мусор контроль даты", Code = "date_trash_test" };
        var other = new IncomeType { Name = "Прочее контроль даты", Code = "date_other_test" };
        var campaign = new FeeCampaign { Name = "Ремонт контроль даты", IncomeType = other, StartsOn = month, EndsOn = new DateOnly(2026, 10, 20), OverdueGraceDays = 15 };
        var futureCampaign = new FeeCampaign { Name = "Будущий сбор контроль даты", IncomeType = other, StartsOn = month, EndsOn = new DateOnly(2026, 11, 30), OverdueGraceDays = 0 };
        Accrual Charge(IncomeType income, decimal amount, AccrualDueDates dates, FeeCampaign? fee = null) => new()
        {
            Garage = garage,
            IncomeType = income,
            Amount = amount,
            AccountingMonth = month,
            DueDate = dates.DueDate,
            OverdueFromDate = dates.OverdueFromDate,
            FeeCampaign = fee,
            Basis = fee?.Name,
            Source = fee is null ? AccrualSources.Regular : AccrualSources.FeeCampaign
        };
        var waterCharge = Charge(water, 300m, AccrualDueDates.ForChargeService(month,
            new ChargeServiceSetting { Name = water.Name, PaymentDueDay = 20, OverdueGraceDays = 15 }));
        var trashCharge = Charge(trash, 100m, AccrualDueDates.ForChargeService(month,
            new ChargeServiceSetting { Name = trash.Name, PaymentDueDay = 31, OverdueGraceDays = 0 }));
        var campaignCharge = Charge(other, 500m, AccrualDueDates.ForFeeCampaign(month, campaign.EndsOn, campaign.OverdueGraceDays), campaign);
        var futureCharge = Charge(other, 1000m, AccrualDueDates.ForFeeCampaign(month, futureCampaign.EndsOn, 0), futureCampaign);
        Assert.Equal(new DateOnly(2026, 11, 5), waterCharge.OverdueFromDate);
        Assert.Equal(waterCharge.OverdueFromDate, campaignCharge.OverdueFromDate);
        context.AddRange(waterCharge, trashCharge, campaignCharge, futureCharge);
        context.Add(new AccrualPaymentAllocation
        {
            Accrual = waterCharge,
            Amount = 100m,
            FinancialOperation = new FinancialOperation
            {
                Garage = garage,
                IncomeType = water,
                Amount = 100m,
                OperationKind = FinancialOperationKinds.Income,
                AccountingMonth = month,
                OperationDate = new DateOnly(2026, 10, 20)
            }
        });
        await context.SaveChangesAsync();
        var auditIds = await context.AuditEvents.Select(item => item.Id).OrderBy(id => id).ToArrayAsync();
        foreach (var (date, expected) in new[]
        {
            (new DateOnly(2026, 9, 5), 0m), (new DateOnly(2026, 11, 4), 100m),
            (new DateOnly(2026, 11, 5), 800m), (new DateOnly(2026, 11, 6), 800m),
            (new DateOnly(2026, 11, 4), 100m), (new DateOnly(2026, 9, 5), 0m)
        })
        {
            var clock = new FixedTimeProvider(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            var result = await FinanceServiceTestFactory.Create(context, clock).GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(date, result.Value!.AsOfDate);
            Assert.Equal(expected, result.Value.Total);
            Assert.DoesNotContain(result.Value.Rows, row => row.AccrualId == futureCharge.Id);
            var totals = await new EfGarageRepository(context, new TestBusinessDateProvider(date))
                .GetBalanceTotalsAsync([garage.Id], CancellationToken.None);
            Assert.Equal(expected, totals.OverdueAccrualTotals.GetValueOrDefault(garage.Id));
            Assert.Equal(1900m, totals.AccrualTotals[garage.Id]);
            Assert.Equal(100m, totals.IncomeTotals[garage.Id]);
            if (expected == 800m)
            {
                Assert.Equal(200m, Assert.Single(result.Value.Rows, row => row.AccrualId == waterCharge.Id).OutstandingAmount);
                Assert.Equal(500m, Assert.Single(result.Value.Rows, row => row.AccrualId == campaignCharge.Id).OutstandingAmount);
            }
        }
        Assert.Equal(auditIds, await context.AuditEvents.Select(item => item.Id).OrderBy(id => id).ToArrayAsync());
        Assert.Equal(4, await context.Accruals.CountAsync(item => item.GarageId == garage.Id));
        Assert.Equal(100m, await context.FinancialOperations.Where(item => item.GarageId == garage.Id).SumAsync(item => item.Amount));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
