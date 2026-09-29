using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed record GarageTariffAssignmentDto(Guid Id, Guid GarageId, string GarageNumber, Guid ServiceId,
    Guid TariffId, string CalculationBase, decimal Rate, IReadOnlyList<ElectricityTariffTierDto> Tiers,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? Comment, bool IsArchived, Guid Version);

public sealed record CreateGarageTariffAssignmentsRequest(
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<Guid> GarageIds,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    [Range(0.0001, 999999999)] decimal Rate,
    [MaxLength(20)] IReadOnlyList<UpsertElectricityTariffTierRequest>? Tiers,
    [MaxLength(2000)] string? Comment, Guid ServiceVersion,
    [ActionComment, MaxLength(1000)] string? Reason);

public sealed record UpdateGarageTariffAssignmentRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    [Range(0.0001, 999999999)] decimal Rate,
    [MaxLength(20)] IReadOnlyList<UpsertElectricityTariffTierRequest>? Tiers,
    [MaxLength(2000)] string? Comment, Guid Version, Guid ServiceVersion,
    [ActionComment, MaxLength(1000)] string? Reason);

public sealed record ArchiveGarageTariffAssignmentRequest(Guid Version, [ActionComment, MaxLength(1000)] string? Reason);

public interface IGarageTariffAssignmentService
{
    Task<DictionaryResult<PagedResult<GarageTariffAssignmentDto>>> GetPageAsync(Guid serviceId, Guid? garageId,
        bool includeArchived, int? offset, int? limit, CancellationToken cancellationToken);
    Task<DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>> CreateAsync(Guid serviceId, CreateGarageTariffAssignmentsRequest request, Guid? actor, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageTariffAssignmentDto>> UpdateAsync(Guid serviceId, Guid id, UpdateGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken);
    Task<DictionaryResult<GarageTariffAssignmentDto>> ArchiveAsync(Guid serviceId, Guid id, ArchiveGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken);
}
