using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Diagnostics;

public sealed class DiagnosticLoggingOptions
{
    public const string SectionName = "DiagnosticLogging";

    public bool Enabled { get; init; }

    [Required]
    public string Directory { get; init; } = "/logs";

    [Range(1, 90)]
    public int RetentionDays { get; init; } = 14;

    [Range(1, 100)]
    public int MaxFileSizeMb { get; init; } = 10;

    [Range(1, 30)]
    public int PackageDays { get; init; } = 7;

    [Range(1, 100)]
    public int PackageMaxSizeMb { get; init; } = 20;
}

public sealed record DiagnosticLogStatusDto(
    bool Enabled,
    int RetentionDays,
    int PackageDays,
    int PackageMaxSizeMb,
    int FileCount,
    long TotalSizeBytes,
    DateTimeOffset? LastEntryAtUtc,
    string? LastWriteError);

public sealed record DiagnosticPackage(string FileName, byte[] Content);

public interface IDiagnosticLogStore
{
    DiagnosticLogStatusDto GetStatus();
    Task<DiagnosticPackage?> CreatePackageAsync(CancellationToken cancellationToken);
}
