using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Finance;

public sealed class RegularAccrualAutomationOptions
{
    public const string SectionName = "Finance:RegularAccrualAutomation";

    public bool Enabled { get; set; } = true;

    [Range(5, 1440)]
    public int CheckIntervalMinutes { get; set; } = 360;

    [Required]
    public string TimeZoneId { get; set; } = "Asia/Novosibirsk";
}
