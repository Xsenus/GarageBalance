using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageDiscountAllocatorTests
{
    [Fact]
    public void Discount_SettlesAnEarlierDueDebtBeforeAnAnnualChargeDueLater()
    {
        var annual = Row(100, 1) with { DueDate = new(2026, 12, 31) };
        var overdue = Row(100, 6);
        var credit = Row(-50, 7);
        var allocation = Assert.Single(GarageDiscountAllocator.Allocate([annual, overdue, credit], new Dictionary<Guid, decimal>()));
        Assert.Equal(overdue.Id, allocation.AccrualId);
        Assert.Equal(50m, allocation.Amount);
    }

    [Fact]
    public void Discount_SettlesOpeningDebtBeforeLaterAccruals()
    {
        var later = Row(100, 8);
        var credit = Row(-150, 7);
        var allocations = GarageDiscountAllocator.Allocate([later, credit], new Dictionary<Guid, decimal>(), 100m);
        Assert.Equal(50m, Assert.Single(allocations).Amount);
    }

    [Fact]
    public void Discount_SettlesOnlyUnpaidDebtAndLeavesExcessForFutureCharges()
    {
        var debt = Row(100, 6);
        var credit = Row(-150, 7);
        var paid = new Dictionary<Guid, decimal> { [debt.Id] = 25 };
        var first = GarageDiscountAllocator.Allocate([debt, credit], paid);
        Assert.Equal(75m, Assert.Single(first).Amount);
        var future = Row(100, 8);
        var second = GarageDiscountAllocator.Allocate([debt, credit, future], paid);
        Assert.Equal(75m, second.Single(item => item.AccrualId == future.Id).Amount);
        Assert.Equal(150m, second.Sum(item => item.Amount));
    }

    [Fact]
    public void Discount_IsNotAppliedAgainToAlreadyPaidAccruals()
    {
        var debt = Row(100, 6);
        var credit = Row(-50, 7);
        Assert.Empty(GarageDiscountAllocator.Allocate([debt, credit], new Dictionary<Guid, decimal> { [debt.Id] = 100 }));
        Assert.Empty(GarageDiscountAllocator.Allocate([debt], new Dictionary<Guid, decimal>()));
    }

    [Fact]
    public void Discounts_AreDeterministicAndDoNotExceedDebtOrCredit()
    {
        var first = Row(50, 6);
        var second = Row(60, 7);
        var credits = new[] { Row(-75, 8), Row(-100, 9) };
        var result = GarageDiscountAllocator.Allocate([second, .. credits.Reverse(), first], new Dictionary<Guid, decimal>());
        Assert.Equal(110m, result.Sum(item => item.Amount));
        Assert.Equal(50m, result.Where(item => item.AccrualId == first.Id).Sum(item => item.Amount));
        Assert.Equal(75m, result.Where(item => item.CreditAccrualId == credits[0].Id).Sum(item => item.Amount));
    }

    private static AccrualPaymentAllocationAccrual Row(decimal amount, int month) =>
        new(Guid.NewGuid(), new(2026, month, 20), new(2026, month, 1), amount, DateTimeOffset.UnixEpoch);
}
