using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AccrualPaymentAllocatorTests
{
    [Fact]
    public void Allocate_UsesOldestDueDateAndLeavesRemainderAsOverpayment()
    {
        var older = Accrual(new DateOnly(2026, 6, 30), 500m, 1);
        var newer = Accrual(new DateOnly(2026, 7, 31), 700m, 2);
        var payment = Payment(new DateOnly(2026, 7, 20), 1500m, 3);

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

    private static AccrualPaymentAllocationAccrual Accrual(DateOnly dueDate, decimal amount, byte id) =>
        new(new Guid(id, 0, 0, new byte[8]), dueDate, new DateOnly(dueDate.Year, dueDate.Month, 1), amount, DateTimeOffset.UnixEpoch);

    private static AccrualPaymentAllocationPayment Payment(DateOnly operationDate, decimal amount, byte id) =>
        new(new Guid(id, 0, 0, new byte[8]), operationDate, amount, DateTimeOffset.UnixEpoch);
}
