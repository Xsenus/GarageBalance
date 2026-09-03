using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Finance;

public sealed class StaffSalaryTimelineTests
{
    [Fact]
    public void CalculateBaseAccrual_UsesHistoricalRatesAndEmploymentGaps()
    {
        var staffMemberId = Guid.NewGuid();
        var ratePeriods = new List<StaffSalaryRatePeriod>
        {
            new() { StaffMemberId = staffMemberId, EffectiveFrom = new DateOnly(2026, 1, 1), Rate = 100m },
            new() { StaffMemberId = staffMemberId, EffectiveFrom = new DateOnly(2026, 3, 1), Rate = 200m }
        };
        var employmentPeriods = new List<StaffEmploymentPeriod>
        {
            new() { StaffMemberId = staffMemberId, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 2, 1) },
            new() { StaffMemberId = staffMemberId, EffectiveFrom = new DateOnly(2026, 4, 1) }
        };

        var result = StaffSalaryTimeline.CalculateBaseAccrual(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 5, 1),
            200m,
            new DateOnly(2026, 1, 1),
            false,
            new DateOnly(2026, 5, 1),
            ratePeriods,
            employmentPeriods);

        Assert.Equal(600m, result);
    }

    [Fact]
    public void CalculateBaseAccrual_FallsBackToLegacyDatesWhenHistoryIsMissing()
    {
        var result = StaffSalaryTimeline.CalculateBaseAccrual(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 5, 1),
            100m,
            new DateOnly(2026, 2, 1),
            true,
            new DateOnly(2026, 4, 1),
            [],
            []);

        Assert.Equal(300m, result);
    }
}
