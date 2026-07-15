using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Backups;

public sealed class DatabaseBackupOptions
{
    public const string SectionName = "DatabaseBackup";

    public bool Enabled { get; init; }
    public bool AutomaticEnabled { get; init; }

    [Required]
    public string Directory { get; init; } = "/backups";

    [Range(1, 168)]
    public int IntervalHours { get; init; } = 24;

    [Range(1, 365)]
    public int RetentionCount { get; init; } = 30;

    [Required]
    public string PgDumpPath { get; init; } = "pg_dump";

    [Required]
    public string PgRestorePath { get; init; } = "pg_restore";
}

public enum DatabaseBackupKind
{
    Manual,
    Automatic,
    PreUpdate
}

public sealed record DatabaseBackupFileDto(
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    string Kind);

public sealed record DatabaseBackupStatusDto(
    bool Enabled,
    bool AutomaticEnabled,
    int IntervalHours,
    int RetentionCount,
    string Directory,
    bool IsRunning,
    DateTimeOffset? LastSuccessfulBackupAtUtc,
    string? LastError,
    IReadOnlyList<DatabaseBackupFileDto> Backups);

public sealed record DatabaseBackupResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static DatabaseBackupResult<T> Success(T value) => new(true, value, null, null);
    public static DatabaseBackupResult<T> Failure(string code, string message) => new(false, default, code, message);
}

public interface IDatabaseBackupService
{
    Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken);
    Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(
        DatabaseBackupKind kind,
        string? reason,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
