namespace GarageBalance.Api.Application.Maintenance;

public sealed record AccountingCleanupSource(string ExecutionHost, string DatabaseHost, int DatabasePort,
    string DatabaseName, string DatabaseIdentifier, string SchemaFingerprint);

public sealed record ArchivedGarageCleanupCandidate(Guid GarageId, string GarageNumber, Guid GarageVersion,
    IReadOnlyList<string> ProtectionReasons, int MeterDeviceCount, int MeterConnectionCount,
    int PeoplePeriodCount, int QuickListLinkCount);

public sealed record ArchivedGarageCleanupPlan(int FormatVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, int TotalArchived, int TotalEligible, bool HasMoreEligible,
    IReadOnlyList<ArchivedGarageCleanupCandidate> EligibleGarages,
    IReadOnlyList<ArchivedGarageCleanupCandidate> ProtectedGarages,
    IReadOnlyDictionary<string, int> ProtectionCounts, string Fingerprint);

public sealed record AccountingCleanupBackupReceipt(int FormatVersion, AccountingCleanupSource Source,
    string BackupPath, string Sha256, DateTimeOffset BackupCreatedAtUtc, DateTimeOffset RestoreVerifiedAtUtc,
    string RestoredSchemaFingerprint, bool RestoreSucceeded);

// This context is supplied by the privileged host command, never from an HTTP request or plan file.
public sealed record AccountingCleanupExecutionIdentity(bool HasHostPrivilege, bool ApplicationStopped, string Operator);

public sealed record ArchivedGarageCleanupResult(Guid PlanId, int DeletedGarages, string Fingerprint);

public sealed class AccountingCleanupException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
