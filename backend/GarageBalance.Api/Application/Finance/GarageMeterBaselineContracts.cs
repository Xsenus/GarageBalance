using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Finance;

/// <summary>Start (baseline) value of one metered service shown in the garage card.</summary>
public sealed record GarageMeterStartValueDto(
    string MeterKind,
    string ServiceName,
    string? UnitName,
    decimal? Value,
    bool HasReadings);

public sealed record UpsertGarageMeterStartValueRequest(
    [Required, MaxLength(40)] string MeterKind,
    [Range(0, 999999999)] decimal? Value);

public sealed record GarageMeterStartValueChange(
    string MeterKind,
    string Label,
    decimal? OldValue,
    decimal? NewValue);

/// <summary>
/// Outcome of applying start values. Disposing it releases the meter chain locks that
/// protect the changes until the caller has saved the unit of work.
/// </summary>
public sealed class GarageMeterStartValueApplyResult(
    IReadOnlyList<GarageMeterStartValueChange> changes,
    IReadOnlyList<IAsyncDisposable> locks) : IAsyncDisposable
{
    public IReadOnlyList<GarageMeterStartValueChange> Changes { get; } = changes;

    public async ValueTask DisposeAsync()
    {
        foreach (var chainLock in locks.Reverse())
        {
            await chainLock.DisposeAsync();
        }
    }
}

public interface IGarageMeterBaselineService
{
    /// <summary>Lists the start values of metered services that apply to the garage.</summary>
    Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartValuesAsync(Garage garage, CancellationToken cancellationToken);

    /// <summary>Lists metered services that apply to every garage, offered when a new garage is created.</summary>
    Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartServicesForNewGarageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies start values to the tracked garage, its baseline meter devices and dependent
    /// readings/accruals without saving. The caller saves the unit of work and disposes the result.
    /// A null value never overwrites an existing start value once devices or readings exist.
    /// </summary>
    Task<FinanceResult<GarageMeterStartValueApplyResult>> ApplyStartValuesAsync(
        Garage garage,
        IReadOnlyList<UpsertGarageMeterStartValueRequest> requested,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
