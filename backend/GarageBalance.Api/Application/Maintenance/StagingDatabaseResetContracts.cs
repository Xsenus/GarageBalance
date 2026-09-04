namespace GarageBalance.Api.Application.Maintenance;

public sealed class StagingDatabaseResetOptions
{
    public const string SectionName = "StagingDatabaseReset";
    public bool Enabled { get; init; }
    public string Password { get; init; } = string.Empty;
}

public sealed record StagingDatabaseResetRequest(string Password, string Confirmation, string Reason);

public sealed record StagingDatabaseResetDto(
    string BackupFileName,
    long ClearedRowCount,
    long PreservedUsers,
    long PreservedTariffs,
    long PreservedIrregularPayments,
    long PreservedFunds,
    decimal FundBalance,
    decimal GeneralPoolBalance);

public sealed record StagingDatabaseResetResult(
    bool Succeeded,
    StagingDatabaseResetDto? Value,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static StagingDatabaseResetResult Success(StagingDatabaseResetDto value) => new(true, value, null, null);
    public static StagingDatabaseResetResult Failure(string code, string message) => new(false, null, code, message);
}

public interface IStagingDatabaseResetService
{
    Task<StagingDatabaseResetResult> ResetAsync(
        StagingDatabaseResetRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
