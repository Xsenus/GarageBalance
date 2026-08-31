using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Reports;

public sealed record GarageReportQuickListGarageDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    bool IsArchived);

public sealed record GarageReportQuickListDto(
    Guid Id,
    string Name,
    IReadOnlyList<GarageReportQuickListGarageDto> Garages,
    DateTimeOffset UpdatedAtUtc,
    Guid? UpdatedByUserId);

public sealed record UpsertGarageReportQuickListRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<Guid> GarageIds);

public sealed record DeleteGarageReportQuickListRequest(
    [ActionComment, MaxLength(1000)] string Reason);
