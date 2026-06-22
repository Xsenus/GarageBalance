using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Import;

public sealed record AccessImportDryRunRequest(
    [property: Required] string FileName,
    [property: Required] Stream Content);

public sealed record AccessImportCheckDto(
    string Code,
    string Title,
    string Status,
    string Message);

public sealed record AccessImportRunDto(
    Guid Id,
    string Mode,
    string Status,
    string OriginalFileName,
    string FileExtension,
    long FileSizeBytes,
    string ContentSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary,
    IReadOnlyList<AccessImportCheckDto> Checks);
