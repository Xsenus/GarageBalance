using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed class GarageMeterConnectionService(
    IGarageRepository garageRepository,
    IMeterReadingRepository meterReadingRepository,
    IGarageMeterBaselineService baselineService,
    IApplicationUnitOfWork unitOfWork) : IGarageMeterConnectionService
{
    public async Task<FinanceResult<GarageMeterConnectionBatchDto>> ConnectBatchAsync(
        ConnectGarageMetersBatchRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Items is null || request.Items.Count is < 1 or > 100 ||
            request.Items.Any(item => item is null || item.GarageId == Guid.Empty) ||
            request.Items.Select(item => item.GarageId).Distinct().Count() != request.Items.Count)
        {
            return FinanceResult<GarageMeterConnectionBatchDto>.Failure(
                "meter_connection_batch_invalid", "Выберите от 1 до 100 разных гаражей.");
        }
        if (!MeterKinds.IsValid(request.MeterKind?.Trim()) || request.ConnectedOn.Year < 1900 ||
            request.Reason?.Trim().Length > 1000 || request.Items.Any(item => item.Value is < 0 or > 999999999))
        {
            return FinanceResult<GarageMeterConnectionBatchDto>.Failure(
                "meter_connection_batch_invalid", "Проверьте услугу, дату подключения, причину и стартовые показания.");
        }

        var leases = new List<GarageMeterStartValueApplyResult>();
        var scope = await meterReadingRepository.BeginConnectionWriteAsync(cancellationToken);
        try
        {
            var kind = request.MeterKind!.Trim();
            var orderedItems = request.Items.OrderBy(item => item.GarageId).ToArray();
            var garages = (await garageRepository.GetActiveByIdsAsync(
                orderedItems.Select(item => item.GarageId).ToArray(), cancellationToken)).ToDictionary(garage => garage.Id);
            if (garages.Count != orderedItems.Length)
            {
                return FinanceResult<GarageMeterConnectionBatchDto>.Failure("garage_not_found", "Один из выбранных гаражей не найден или находится в архиве.");
            }
            var rows = new List<GarageMeterConnectionBatchItemDto>();
            foreach (var item in orderedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var garage = garages[item.GarageId];
                if (await meterReadingRepository.ExistsConnectionAsync(garage.Id, kind, cancellationToken))
                {
                    return FinanceResult<GarageMeterConnectionBatchDto>.Failure(
                        "meter_connection_already_exists", $"Услуга уже подключена к гаражу {garage.Number}. Обновите список; изменения не сохранены.");
                }
                var applied = await baselineService.ApplyStartValuesAsync(garage,
                    [new UpsertGarageMeterStartValueRequest(kind, item.Value, request.ConnectedOn, Reason: request.Reason)],
                    actorUserId, cancellationToken);
                if (!applied.Succeeded)
                {
                    return FinanceResult<GarageMeterConnectionBatchDto>.Failure(applied.ErrorCode!, applied.ErrorMessage!);
                }
                leases.Add(applied.Value!);
                rows.Add(new GarageMeterConnectionBatchItemDto(garage.Id, garage.Number, applied.Value!.Changes));
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return FinanceResult<GarageMeterConnectionBatchDto>.Success(new GarageMeterConnectionBatchDto(rows));
        }
        catch (Exception exception) when (exception is OptimisticConcurrencyException or ApplicationConcurrencyException or ApplicationPersistenceConflictException)
        {
            return FinanceResult<GarageMeterConnectionBatchDto>.Failure(
                "meter_connection_changed", "Подключения изменились другим пользователем. Обновите список и повторите действие; изменения не сохранены.");
        }
        finally
        {
            await DisposeScopeAndLeasesAsync(scope, leases);
        }
    }

    public async Task<FinanceResult<GarageMeterConnectionPreviewDto>> PreviewAsync(
        PreviewGarageMeterConnectionsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GarageId == Guid.Empty || request.GarageVersion == Guid.Empty ||
            request.Values is null || request.Values.Count is < 1 or > 500 || request.Values.Any(item => item is null))
        {
            return FinanceResult<GarageMeterConnectionPreviewDto>.Failure(
                "meter_connection_preview_invalid", "Обновите карточку гаража и передайте изменения услуг.");
        }
        var scope = await meterReadingRepository.BeginConnectionWriteAsync(cancellationToken);
        var leases = new List<GarageMeterStartValueApplyResult>();
        try
        {
            var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
            if (garage is null)
            {
                return FinanceResult<GarageMeterConnectionPreviewDto>.Failure("garage_not_found", "Гараж не найден.");
            }
            OptimisticConcurrencyGuard.EnsureCurrent(request.GarageVersion, garage);
            var applied = await baselineService.ApplyStartValuesAsync(garage, request.Values, actorUserId, cancellationToken);
            if (!applied.Succeeded)
            {
                return FinanceResult<GarageMeterConnectionPreviewDto>.Failure(applied.ErrorCode!, applied.ErrorMessage!);
            }
            leases.Add(applied.Value!);
            cancellationToken.ThrowIfCancellationRequested();
            return FinanceResult<GarageMeterConnectionPreviewDto>.Success(new GarageMeterConnectionPreviewDto(
                garage.Id, garage.Number, applied.Value!.Changes,
                meterReadingRepository.GetPendingConnectionAccrualChanges(garage.Id)));
        }
        catch (OptimisticConcurrencyException)
        {
            return FinanceResult<GarageMeterConnectionPreviewDto>.Failure(
                "meter_connection_changed", "Карточка гаража или подключение изменились. Обновите данные перед просмотром изменений.");
        }
        finally
        {
            await DisposeScopeAndLeasesAsync(scope, leases);
        }
    }

    private static async Task DisposeScopeAndLeasesAsync(IMeterConnectionWriteScope scope, List<GarageMeterStartValueApplyResult> leases)
    {
        try
        {
            // A PostgreSQL transaction may be aborted after a persistence conflict.
            // Roll it back before running advisory-unlock commands on the same session.
            await scope.RollbackAsync();
            foreach (var lease in Enumerable.Reverse(leases)) await lease.DisposeAsync();
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }
}
