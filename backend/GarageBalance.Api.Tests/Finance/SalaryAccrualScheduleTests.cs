using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class SalaryAccrualScheduleTests
{
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(28, true)]
    public void CurrentMonth_IsAccruedOnlyFromConfiguredDay(int businessDay, bool expected)
    {
        var result = SalaryAccrualSchedule.IsAccrued(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, businessDay),
            15);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void PastMonthRemainsAccruedAndFutureMonthIsNotAccrued()
    {
        var businessDate = new DateOnly(2026, 7, 20);

        Assert.True(SalaryAccrualSchedule.IsAccrued(new DateOnly(2026, 6, 1), businessDate, 28));
        Assert.False(SalaryAccrualSchedule.IsAccrued(new DateOnly(2026, 8, 1), businessDate, 1));
    }
}
