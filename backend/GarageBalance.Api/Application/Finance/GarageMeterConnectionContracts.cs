using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Finance;

public sealed record ConnectGarageMetersBatchRequest(
    [Required, MaxLength(40)] string MeterKind,
    DateOnly ConnectedOn,
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<ConnectGarageMeterItemRequest> Items,
    [MaxLength(1000)] string? Reason = null);

public sealed record ConnectGarageMeterItemRequest(Guid GarageId, [Range(0, 999999999)] decimal? Value);

public sealed record GarageMeterConnectionBatchItemDto(
    Guid GarageId, string GarageNumber, IReadOnlyList<GarageMeterStartValueChange> Changes);

public sealed record GarageMeterConnectionBatchDto(IReadOnlyList<GarageMeterConnectionBatchItemDto> Items);

public sealed record PreviewGarageMeterConnectionsRequest(
    Guid GarageId,
    Guid GarageVersion,
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<UpsertGarageMeterStartValueRequest> Values);

public sealed record GarageMeterConnectionAccrualChange(
    Guid AccrualId,
    DateOnly AccountingMonth,
    string? MeterKind,
    decimal OldAmount,
    decimal NewAmount,
    bool OldRequiresMeterReading,
    bool NewRequiresMeterReading);

public sealed record GarageMeterConnectionPreviewDto(
    Guid GarageId,
    string GarageNumber,
    IReadOnlyList<GarageMeterStartValueChange> Changes,
    IReadOnlyList<GarageMeterConnectionAccrualChange> AccrualChanges);

public interface IGarageMeterConnectionService
{
    Task<FinanceResult<GarageMeterConnectionBatchDto>> ConnectBatchAsync(
        ConnectGarageMetersBatchRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<FinanceResult<GarageMeterConnectionPreviewDto>> PreviewAsync(
        PreviewGarageMeterConnectionsRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}

public interface IMeterConnectionWriteScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync();
}
