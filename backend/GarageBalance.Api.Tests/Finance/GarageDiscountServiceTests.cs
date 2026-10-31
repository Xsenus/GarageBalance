using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceServiceTests
{
    [Fact]
    public async Task Discount_CarriesExcessAcrossServicesAndCancellationRestoresDebtWithoutCash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var penalty = new IncomeType { Name = "Штраф", Code = "penalty", IsSystem = true };
        database.Context.IncomeTypes.Add(penalty);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var debt = await service.CreateAccrualAsync(new(fixtures.Garage.Id, fixtures.IncomeType.Id, new(2026, 6, 1), 100m, "manual", "Долг"), null, default);
        Assert.True(debt.Succeeded, debt.ErrorMessage);
        var credit = await service.CreateAccrualAsync(new(fixtures.Garage.Id, penalty.Id, new(2026, 7, 1), -150m, "manual", "Скидка"), null, default);
        Assert.True(credit.Succeeded, credit.ErrorMessage);
        var first = await database.Context.AccrualPaymentAllocations.Where(item => item.IsActive).ToArrayAsync();
        Assert.Equal(100m, Assert.Single(first).Amount);
        Assert.Null(first[0].FinancialOperationId);
        Assert.Equal(credit.Value!.Id, first[0].CreditAccrualId);
        Assert.Equal(debt.Value!.Id, first[0].AccrualId);
        Assert.Empty(await database.Context.FinancialOperations.ToArrayAsync());

        var future = await service.CreateAccrualAsync(new(fixtures.Garage.Id, fixtures.IncomeType.Id, new(2026, 8, 1), 80m, "manual", "Будущее начисление"), null, default);
        Assert.True(future.Succeeded, future.ErrorMessage);
        var after = await database.Context.AccrualPaymentAllocations.Where(item => item.IsActive).ToArrayAsync();
        Assert.Equal(150m, after.Sum(item => item.Amount));
        Assert.Equal(50m, after.Single(item => item.AccrualId == future.Value!.Id).Amount);

        var canceled = await service.CancelAccrualAsync(credit.Value.Id, new("Отмена скидки"), null, default);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.Empty(await database.Context.AccrualPaymentAllocations.Where(item => item.IsActive).ToArrayAsync());
        Assert.Empty(await database.Context.FinancialOperations.ToArrayAsync());
        Assert.Contains(await database.Context.AuditEvents.ToArrayAsync(), item => item.Action == "finance.accrual_canceled");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.001)]
    [InlineData(-10)]
    public async Task Discount_RejectsZeroAndNegativeOrdinaryAccruals(decimal amount)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var result = await service.CreateAccrualAsync(new(fixtures.Garage.Id, fixtures.IncomeType.Id, new(2026, 6, 1), amount, "manual", null), null, default);
        Assert.Equal("accrual_amount_invalid", result.ErrorCode);
        Assert.Empty(await database.Context.Accruals.ToArrayAsync());
    }
}
