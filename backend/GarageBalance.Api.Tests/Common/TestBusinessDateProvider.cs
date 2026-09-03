using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Tests.Common;

internal sealed class TestBusinessDateProvider(DateOnly today, string timeZoneId = "UTC") : IBusinessDateProvider
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    public DateOnly SystemDate { get; } = today;
    public DateOnly Today => OverrideDate ?? SystemDate;
    public DateOnly? OverrideDate { get; private set; }
    public string TimeZoneId => _timeZone.Id;
    public DateOnly ToBusinessDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, _timeZone).DateTime);
    public void SetOverride(DateOnly? value) => OverrideDate = value;

    public static TestBusinessDateProvider From(TimeProvider? timeProvider) =>
        new(timeProvider is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
}
