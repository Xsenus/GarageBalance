using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Backups;

public sealed class DatabaseBackupOptions
{
    public const string SectionName = "DatabaseBackup";

    public bool Enabled { get; init; } = true;
    public bool AutomaticEnabled { get; init; } = true;

    [Required]
    public string Directory { get; init; } = "auto";

    [Range(1, 168)]
    public int IntervalHours { get; init; } = 24;

    public bool CatchUpEnabled { get; init; } = true;

    [Range(1, 168)]
    public int FreshnessGraceHours { get; init; } = 6;

    [Range(0, 23)]
    public int AutomaticWindowStartHour { get; init; } = 2;

    [Range(1, 24)]
    public int AutomaticWindowEndHour { get; init; } = 5;

    [Required]
    public string AutomaticWindowTimeZoneId { get; init; } = "Europe/Moscow";

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
    string Kind,
    string? Sha256 = null,
    string ProtectionState = "local_only",
    DateTimeOffset? LastVerifiedAtUtc = null,
    int RequiredCopies = 1,
    int AvailableCopies = 0,
    int DesiredCopies = 1,
    int RequiredOffsiteCopies = 0,
    int AvailableOffsiteCopies = 0,
    long ProtectionLagSeconds = 0,
    IReadOnlyList<DatabaseBackupReplicaDto>? Replicas = null)
{
    public string ProtectionLabel => ProtectionState switch
    {
        "protected" => "Защищена",
        "protection_degraded" => "Защита ослаблена",
        "protection_pending" => "Ожидает копирования",
        "failed" => "Требует внимания",
        "manifest_missing" => "Нет манифеста",
        "local_verified" => "Проверена локально",
        "deleting" => "Удаляется",
        "deleted" => "Удалена",
        "local_only" => "Только локально",
        _ => "Требует проверки"
    };

    public string ProtectionTone => ProtectionState switch
    {
        "protected" or "local_verified" => "active",
        "protection_degraded" or "manifest_missing" => "warning",
        "failed" => "danger",
        _ => "archived"
    };
}

public sealed record DatabaseBackupReplicaDto(
    string DestinationId,
    string Location,
    string State,
    DateTimeOffset? LastVerifiedAtUtc,
    string? Error)
{
    public string StateLabel => State switch
    {
        "available" => "Проверена",
        "pending" => "В очереди",
        "uploading" => "Копируется",
        "unknown" => "Уточняется",
        "verificationpending" => "Ожидает проверки",
        "missing" => "Не найдена",
        "corrupted" => "Повреждена",
        "stale" => "Устарела",
        "failed" => "Ошибка",
        "deleting" => "Удаляется",
        "deleted" => "Удалена",
        "disabled" => "Отключена",
        _ => "Требует проверки"
    };
}

public sealed record DatabaseBackupManifest(
    int SchemaVersion,
    Guid BackupId,
    long Generation,
    string FileName,
    long SizeBytes,
    string Sha256,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    string ApplicationVersion,
    string PolicyId,
    int PolicyRevision);

public sealed record DatabaseBackupDownloadDto(
    string FileName,
    long SizeBytes,
    Stream Content);

public sealed record DatabaseBackupStatusDto(
    bool Enabled,
    bool AutomaticEnabled,
    int IntervalHours,
    int RetentionCount,
    string Directory,
    bool IsRunning,
    DateTimeOffset? LastSuccessfulBackupAtUtc,
    string? LastError,
    IReadOnlyList<DatabaseBackupFileDto> Backups,
    bool IsStale = false,
    int FreshnessThresholdHours = 48,
    string StorageLocation = "Локальное хранилище",
    DatabaseRestoreVerificationDto? RestoreVerification = null,
    bool ReconciliationPaused = false);

public sealed record DatabaseRestoreVerificationDto(
    string State,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? BackupCreatedAtUtc,
    double? RtoSeconds,
    int MaximumAgeHours)
{
    public string Message => State switch
    {
        "not_configured" => "Проверка восстановления не настроена",
        "not_run" => "Проверка восстановления ещё не запускалась",
        "running" => "Выполняется проверка восстановления",
        "stale" => "Проверку восстановления пора повторить",
        "failed" => "Проверка восстановления завершилась ошибкой",
        "verified" => "Восстановление проверено",
        _ => "Результат проверки восстановления недоступен"
    };
}

public sealed record DatabaseBackupResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static DatabaseBackupResult<T> Success(T value) => new(true, value, null, null);
    public static DatabaseBackupResult<T> Failure(string code, string message) => new(false, default, code, message);
}

public interface IDatabaseBackupService
{
    Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken cancellationToken);
    Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(
        DatabaseBackupKind kind,
        string? reason,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(
        string fileName,
        string? reason,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<DatabaseBackupResult<DatabaseBackupFileDto>> RetryProtectionAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(DatabaseBackupResult<DatabaseBackupFileDto>.Failure("not_supported", "Not supported."));
    Task<DatabaseBackupResult<DatabaseBackupFileDto>> VerifyProtectionAsync(
        string fileName,
        Guid? actorUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(DatabaseBackupResult<DatabaseBackupFileDto>.Failure("not_supported", "Not supported."));
}
