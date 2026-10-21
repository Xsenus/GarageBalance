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

public sealed record ArchivedGarageFinancialPurgeCandidate(Guid GarageId, string GarageNumber, Guid GarageVersion,
    int AccrualCount, int OperationCount, int FundOperationCount, int MeterReadingCount, int MeterDeviceCount,
    decimal ActiveIncomeTotal, string ContentFingerprint);

public sealed record ArchivedGarageFinancialPurgePlan(int FormatVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, int TotalArchived, IReadOnlyList<ArchivedGarageFinancialPurgeCandidate> Garages,
    IReadOnlyList<string> BlockedGarageNumbers, string Fingerprint);

public sealed record ArchivedGarageFinancialPurgeResult(Guid PlanId, int DeletedGarages, int DeletedAccruals,
    int DeletedOperations, int RemovedFundOperations, decimal RemovedIncomeTotal, string Fingerprint);

public sealed record MeterHistoryCleanupPlan(int FormatVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, int GarageCount, int ReadingCount, int KeptStartReadingCount, int ReplacementDeviceCount,
    int ReactivatedDeviceCount, int MeteredAccrualCount, int PaidMeteredAccrualCount, decimal MeteredAccrualTotal,
    decimal PaidMeteredTotal, string ContentFingerprint, string Fingerprint);

public sealed record MeterHistoryCleanupResult(Guid PlanId, int DeletedReadings, int DeletedReplacementDevices,
    int ReactivatedDevices, int CanceledAccruals, int RebuiltAllocationKeys, string Fingerprint);

public sealed record ArchivedFundBalanceRepairCandidate(Guid FundId, string FundName, Guid FundVersion, decimal Balance,
    Guid RemainderOperationId, decimal RemainderAmount, decimal RepairedRemainderAmount);

public sealed record ArchivedFundBalanceRepairPlan(int FormatVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, IReadOnlyList<ArchivedFundBalanceRepairCandidate> Funds, IReadOnlyList<string> BlockedFundNames,
    string Fingerprint);

public sealed record ArchivedFundBalanceRepairResult(Guid PlanId, int RepairedFunds, string Fingerprint);
