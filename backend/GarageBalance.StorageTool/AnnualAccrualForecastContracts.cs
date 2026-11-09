using GarageBalance.Api.Application.Maintenance;

namespace GarageBalance.StorageTool;

public sealed record AnnualAccrualForecastReason(string Code, int Count);

public sealed record AnnualAccrualServiceForecast(Guid ServiceId, Guid? IncomeTypeId,
    int CandidateGarageCount, int NewAccrualCount, decimal NewAccrualAmount,
    int ExistingAccrualCount, decimal ExistingAccrualAmount,
    IReadOnlyList<AnnualAccrualForecastReason> Exclusions,
    IReadOnlyList<AnnualAccrualForecastReason> Uncertainties);

// Only aggregate financial data. No garage numbers, owners, contacts or comments.
public sealed record AnnualAccrualForecast(int Version, int Year, DateOnly BusinessDate, string TimeZoneId,
    DateTimeOffset GeneratedAtUtc, AccountingCleanupSource Source, bool HoldActive,
    string Scope, int ActiveGarageCount, int NewAccrualCount, decimal NewAccrualAmount,
    IReadOnlyList<AnnualAccrualServiceForecast> Services,
    IReadOnlyList<AnnualAccrualForecastReason> Uncertainties,
    bool IsComplete, string InputFingerprint, string Fingerprint)
{
    // NewAccrualCount counts garage/service pairs; coverage counts each garage once.
    public int AffectedGarageCount { get; init; }
}
