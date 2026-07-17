using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AccrualDueDatesTests
{
    [Fact]
    public void ForChargeService_MonthlyChargeBecomesOverdueAfterFollowingPaymentMonthAndGracePeriod()
    {
        var setting = CreateSetting(periodicityMonths: 1, dueDay: 30, dueMonth: 1, graceDays: 30);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2026, 5, 1), setting);

        Assert.Equal(new DateOnly(2026, 6, 30), result.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.OverdueFromDate);
    }

    [Fact]
    public void ForChargeService_AnnualChargeUsesConfiguredMonthAndGracePeriod()
    {
        var setting = CreateSetting(periodicityMonths: 12, dueDay: 30, dueMonth: 6, graceDays: 30);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2026, 1, 1), setting);

        Assert.Equal(new DateOnly(2026, 6, 30), result.DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.OverdueFromDate);
    }

    [Fact]
    public void ForChargeService_OutdoorLightingBecomesOverdueOnFirstDayOfNextYear()
    {
        var setting = CreateSetting(periodicityMonths: 12, dueDay: 31, dueMonth: 12, graceDays: 0);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2026, 1, 1), setting);

        Assert.Equal(new DateOnly(2026, 12, 31), result.DueDate);
        Assert.Equal(new DateOnly(2027, 1, 1), result.OverdueFromDate);
    }

    [Fact]
    public void ForChargeService_ClampsConfiguredDayToEndOfShortMonth()
    {
        var setting = CreateSetting(periodicityMonths: 1, dueDay: 31, dueMonth: 1, graceDays: 0);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2027, 1, 1), setting);

        Assert.Equal(new DateOnly(2027, 2, 28), result.DueDate);
        Assert.Equal(new DateOnly(2027, 3, 1), result.OverdueFromDate);
    }

    [Fact]
    public void ForChargeService_ClampsConfiguredDayToLeapYearFebruaryEnd()
    {
        var setting = CreateSetting(periodicityMonths: 1, dueDay: 31, dueMonth: 1, graceDays: 0);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2024, 1, 1), setting);

        Assert.Equal(new DateOnly(2024, 2, 29), result.DueDate);
        Assert.Equal(new DateOnly(2024, 3, 1), result.OverdueFromDate);
    }

    [Fact]
    public void ForChargeService_DecemberChargeMovesPaymentDateToNextYear()
    {
        var setting = CreateSetting(periodicityMonths: 1, dueDay: 31, dueMonth: 1, graceDays: 0);

        var result = AccrualDueDates.ForChargeService(new DateOnly(2026, 12, 1), setting);

        Assert.Equal(new DateOnly(2027, 1, 31), result.DueDate);
        Assert.Equal(new DateOnly(2027, 2, 1), result.OverdueFromDate);
    }

    [Fact]
    public void ForFeeCampaign_WithoutEndDateUsesLeapYearMonthEnd()
    {
        var result = AccrualDueDates.ForFeeCampaign(new DateOnly(2024, 2, 1), null, overdueGraceDays: 30);

        Assert.Equal(new DateOnly(2024, 2, 29), result.DueDate);
        Assert.Equal(new DateOnly(2024, 3, 31), result.OverdueFromDate);
    }

    [Fact]
    public void FromDueDate_RejectsNegativeGracePeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccrualDueDates.FromDueDate(new DateOnly(2026, 1, 1), -1));
    }

    private static ChargeServiceSetting CreateSetting(int periodicityMonths, int dueDay, int dueMonth, int graceDays)
    {
        return new ChargeServiceSetting
        {
            Name = "Услуга",
            IsRegular = true,
            PeriodicityMonths = periodicityMonths,
            PaymentDueDay = dueDay,
            PaymentDueMonth = dueMonth,
            OverdueGraceDays = graceDays
        };
    }
}
