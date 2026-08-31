using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AccrualPaymentAllocatorTests
{
    [Fact]
    public void Allocate_UsesOldestDueDateAndLeavesRemainderAsOverpayment()
    {
        var older = Accrual(new DateOnly(2026, 6, 30), 500m, 1);
        var newer = Accrual(new DateOnly(2026, 7, 31), 700m, 2);
        var payment = Payment(new DateOnly(2026, 7, 20), 1500m, 3) with
        {
            AccountingMonth = new DateOnly(2026, 8, 1)
        };

        var result = AccrualPaymentAllocator.Allocate([newer, older], [payment]);

        Assert.Collection(
            result,
            item => Assert.Equal((payment.Id, older.Id, 500m), (item.FinancialOperationId, item.AccrualId, item.Amount)),
            item => Assert.Equal((payment.Id, newer.Id, 700m), (item.FinancialOperationId, item.AccrualId, item.Amount)));
        Assert.Equal(300m, payment.Amount - result.Sum(item => item.Amount));
    }

    [Fact]
    public void Allocate_PartialAnnualPaymentDoesNotCloseAccrual()
    {
        var annual = Accrual(new DateOnly(2026, 6, 30), 1200m, 1);
        var payment = Payment(new DateOnly(2026, 6, 20), 1199m, 2);

        var allocation = Assert.Single(AccrualPaymentAllocator.Allocate([annual], [payment]));

        Assert.Equal(1199m, allocation.Amount);
        Assert.Equal(1m, annual.Amount - allocation.Amount);
    }

    [Fact]
    public void Allocate_UsesOldestPaymentsFirst()
    {
        var accrual = Accrual(new DateOnly(2026, 6, 30), 700m, 1);
        var olderPayment = Payment(new DateOnly(2026, 6, 10), 400m, 2);
        var newerPayment = Payment(new DateOnly(2026, 6, 20), 400m, 3);

        var result = AccrualPaymentAllocator.Allocate([accrual], [newerPayment, olderPayment]);

        Assert.Collection(
            result,
            item => Assert.Equal((olderPayment.Id, 400m), (item.FinancialOperationId, item.Amount)),
            item => Assert.Equal((newerPayment.Id, 300m), (item.FinancialOperationId, item.Amount)));
    }

    [Fact]
    public void Allocate_TargetedFeePaymentDoesNotPayAnotherCampaignWithSameIncomeType()
    {
        var firstCampaignId = Guid.NewGuid();
        var secondCampaignId = Guid.NewGuid();
        var first = Accrual(new DateOnly(2026, 8, 10), 10m, 1) with { FeeCampaignId = firstCampaignId };
        var second = Accrual(new DateOnly(2026, 8, 20), 10m, 2) with { FeeCampaignId = secondCampaignId };
        var payment = Payment(new DateOnly(2026, 8, 11), 10m, 3) with { FeeCampaignId = secondCampaignId };

        var allocation = Assert.Single(AccrualPaymentAllocator.Allocate([first, second], [payment]));

        Assert.Equal(second.Id, allocation.AccrualId);
        Assert.Equal(10m, allocation.Amount);
    }

    [Fact]
    public void Allocate_TargetedIrregularPaymentDoesNotPayAnotherIrregularAccrualWithSameIncomeType()
    {
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        var first = Accrual(new DateOnly(2026, 8, 10), 500m, 1) with { IrregularPaymentId = firstPaymentId };
        var second = Accrual(new DateOnly(2026, 8, 20), 700m, 2) with { IrregularPaymentId = secondPaymentId };
        var payment = Payment(new DateOnly(2026, 8, 11), 400m, 3) with { IrregularPaymentId = secondPaymentId };

        var allocation = Assert.Single(AccrualPaymentAllocator.Allocate([first, second], [payment]));

        Assert.Equal(second.Id, allocation.AccrualId);
        Assert.Equal(400m, allocation.Amount);
    }

    [Fact]
    public void Allocate_UntargetedPeriodPaymentPaysRegularAccrualBeforeOlderTargetedDebt()
    {
        var feeCampaign = Accrual(new DateOnly(2026, 7, 31), 300m, 1) with { FeeCampaignId = Guid.NewGuid() };
        var regularTrash = Accrual(new DateOnly(2026, 8, 31), 300m, 2);
        var periodPayment = Payment(new DateOnly(2026, 8, 20), 300m, 3);

        var allocation = Assert.Single(AccrualPaymentAllocator.Allocate([feeCampaign, regularTrash], [periodPayment]));

        Assert.Equal(regularTrash.Id, allocation.AccrualId);
        Assert.Equal(300m, allocation.Amount);
    }

    [Fact]
    public void Allocate_UntargetedPeriodPaymentPaysSelectedMonthBeforeOlderOrdinaryDebt()
    {
        var january = Accrual(new DateOnly(2026, 1, 31), 300m, 1);
        var february = Accrual(new DateOnly(2026, 2, 28), 300m, 2);
        var februaryPayment = Payment(new DateOnly(2026, 2, 20), 100m, 3) with
        {
            AccountingMonth = new DateOnly(2026, 2, 1)
        };

        var allocation = Assert.Single(AccrualPaymentAllocator.Allocate([january, february], [februaryPayment]));

        Assert.Equal(february.Id, allocation.AccrualId);
        Assert.Equal(100m, allocation.Amount);
    }

    [Fact]
    public void Allocate_LegacyUntargetedPaymentUsesRemainingAmountForIrregularAccrual()
    {
        var regular = Accrual(new DateOnly(2026, 8, 31), 500m, 1);
        var irregular = Accrual(new DateOnly(2026, 7, 31), 3_000m, 2) with { IrregularPaymentId = Guid.NewGuid() };
        var legacyPayment = Payment(new DateOnly(2026, 8, 20), 3_500m, 3);

        var result = AccrualPaymentAllocator.Allocate([irregular, regular], [legacyPayment]);

        Assert.Collection(
            result,
            item => Assert.Equal((regular.Id, 500m), (item.AccrualId, item.Amount)),
            item => Assert.Equal((irregular.Id, 3_000m), (item.AccrualId, item.Amount)));
    }

    private static AccrualPaymentAllocationAccrual Accrual(DateOnly dueDate, decimal amount, byte id) =>
        new(new Guid(id, 0, 0, new byte[8]), dueDate, new DateOnly(dueDate.Year, dueDate.Month, 1), amount, DateTimeOffset.UnixEpoch);

    private static AccrualPaymentAllocationPayment Payment(DateOnly operationDate, decimal amount, byte id) =>
        new(
            new Guid(id, 0, 0, new byte[8]),
            operationDate,
            new DateOnly(operationDate.Year, operationDate.Month, 1),
            amount,
            DateTimeOffset.UnixEpoch);
}
