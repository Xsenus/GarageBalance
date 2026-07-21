using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Tests.Common;

internal sealed class TestBusinessDateProvider(DateOnly today) : IBusinessDateProvider
{
    public DateOnly SystemDate { get; } = today;
    public DateOnly Today => OverrideDate ?? SystemDate;
    public DateOnly? OverrideDate { get; private set; }
    public void SetOverride(DateOnly? value) => OverrideDate = value;

    public static TestBusinessDateProvider From(TimeProvider? timeProvider) =>
        new(DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime));
}
