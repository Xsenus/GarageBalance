using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Finance;

public sealed class RegularAccrualAutomationOptions
{
    public const string SectionName = "Finance:RegularAccrualAutomation";

    public bool Enabled { get; set; } = true;

    [Range(5, 1440)]
    public int CheckIntervalMinutes { get; set; } = 360;

    [Range(1, 60)]
    public int FailureRetryMinutes { get; set; } = 5;

    [Required]
    public string TimeZoneId { get; set; } = "Asia/Novosibirsk";

    public TimeSpan GetDelayAfterRun(bool failed) =>
        TimeSpan.FromMinutes(failed ? FailureRetryMinutes : CheckIntervalMinutes);
}
