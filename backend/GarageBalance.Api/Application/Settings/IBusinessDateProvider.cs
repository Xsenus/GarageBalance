using Microsoft.Extensions.Options;
using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Application.Settings;

public interface IBusinessDateProvider
{
    DateOnly SystemDate { get; }
    DateOnly Today { get; }
    DateOnly? OverrideDate { get; }
    void SetOverride(DateOnly? value);
}

public sealed class BusinessDateProvider(
    TimeProvider timeProvider,
    IOptions<RegularAccrualAutomationOptions> options) : IBusinessDateProvider
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
    private int _overrideDayNumber = -1;

    public DateOnly SystemDate
    {
        get
        {
            var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone);
            return DateOnly.FromDateTime(localNow.DateTime);
        }
    }

    public DateOnly Today => OverrideDate ?? SystemDate;

    public DateOnly? OverrideDate
    {
        get
        {
            var dayNumber = Volatile.Read(ref _overrideDayNumber);
            return dayNumber < 0 ? null : DateOnly.FromDayNumber(dayNumber);
        }
    }

    public void SetOverride(DateOnly? value) =>
        Volatile.Write(ref _overrideDayNumber, value?.DayNumber ?? -1);
}
