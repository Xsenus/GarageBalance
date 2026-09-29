using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class GarageTariffAssignmentService(
    IGarageTariffAssignmentRepository repository,
    IFundRepository funds,
    IGarageRepository garages,
    IChargeServiceSettingRepository services,
    ITariffRepository tariffs,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter audit,
    IFinanceService finance,
    IBusinessDateProvider businessDate,
    TimeProvider clock) : IGarageTariffAssignmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DictionaryResult<PagedResult<GarageTariffAssignmentDto>>> GetPageAsync(Guid serviceId,
        Guid? garageId, bool includeArchived, int? offset, int? limit, CancellationToken cancellationToken)
    {
        if (garageId == Guid.Empty || offset < 0)
            return DictionaryResult<PagedResult<GarageTariffAssignmentDto>>.Failure("garage_tariff_filter_invalid", "Проверьте гараж и номер страницы.");
        var service = await services.FindActiveAsync(serviceId, cancellationToken)
            ?? (includeArchived ? await services.FindArchivedAsync(serviceId, cancellationToken) : null);
        if (service is null)
            return DictionaryResult<PagedResult<GarageTariffAssignmentDto>>.Failure("charge_service_not_found", "Услуга не найдена.");
        var size = QueryLimits.NormalizePageSize(limit ?? QueryLimits.DefaultPageSize);
        var page = await repository.GetPageAsync(serviceId, garageId, includeArchived, offset ?? 0, size, cancellationToken);
        return DictionaryResult<PagedResult<GarageTariffAssignmentDto>>.Success(new(page.Items.Select(Map).ToArray(), page.TotalCount, offset ?? 0, size));
    }

    public async Task<DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>> CreateAsync(Guid serviceId,
        CreateGarageTariffAssignmentsRequest request, Guid? actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GarageIds is null || request.GarageIds.Count is < 1 or > 100
            || request.GarageIds.Contains(Guid.Empty) || request.GarageIds.Distinct().Count() != request.GarageIds.Count)
            return DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>.Failure("garage_tariff_garages_invalid", "Выберите от 1 до 100 различных гаражей.");
        // Match general-service writes: allocation lock before garage worksheet
        // locks, so neither side can invalidate the other's compatibility check.
        await using var allocationLock = await funds.AcquireAllocationLockAsync(cancellationToken);
        await using var scope = await repository.BeginWriteAsync(request.GarageIds.ToArray(), cancellationToken);
        var source = await ReadSourceAsync(serviceId, request.ServiceVersion, cancellationToken);
        if (!source.Succeeded) return Failure<IReadOnlyList<GarageTariffAssignmentDto>, Source>(source);
        var terms = Validate(source.Value!.Tariff.CalculationBase, request.EffectiveFrom, request.EffectiveTo,
            request.Rate, request.Tiers, request.Comment, request.Reason);
        if (!terms.Succeeded) return Failure<IReadOnlyList<GarageTariffAssignmentDto>, Terms>(terms);
        if (!await HasCompatibleTimelineAsync(source.Value, request.EffectiveFrom, request.EffectiveTo, cancellationToken))
            return DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>.Failure("garage_tariff_calculation_base_conflict", "В выбранном периоде услуга меняет способ расчёта. Ограничьте индивидуальный тариф периодом с одной базой расчёта.");
        var selected = await garages.GetActiveByIdsAsync(request.GarageIds.ToArray(), cancellationToken);
        if (selected.Count != request.GarageIds.Count)
            return DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>.Failure("garage_not_found", "Один из выбранных гаражей не найден или удалён.");
        if (await repository.HasOverlapAsync(serviceId, request.GarageIds.ToArray(), request.EffectiveFrom, request.EffectiveTo, null, cancellationToken))
            return DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>.Failure("garage_tariff_period_overlap", "Для выбранного гаража уже есть индивидуальный тариф в этом периоде.");
        var tariff = NewRevision(source.Value.Tariff.CalculationBase, request.EffectiveFrom, terms.Value!);
        var rows = selected.Select(garage => new GarageTariffAssignment
        {
            GarageId = garage.Id,
            Garage = garage,
            ChargeServiceSettingId = serviceId,
            ChargeServiceSetting = source.Value.Service,
            TariffId = tariff.Id,
            Tariff = tariff,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Comment = terms.Value!.Comment,
            CreatedAtUtc = clock.GetUtcNow(),
            UpdatedAtUtc = clock.GetUtcNow()
        }).ToArray();
        foreach (var row in rows)
        {
            repository.Add(row);
            WriteAudit(row, source.Value.Service.Name, actor, "create", request.Reason, null);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
        {
            var result = await RecalculateAsync(row.GarageId, row.EffectiveFrom, row.EffectiveTo, actor, cancellationToken);
            if (!result.Succeeded) return Failure<IReadOnlyList<GarageTariffAssignmentDto>, bool>(result);
        }
        await scope.CommitAsync(cancellationToken);
        return DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>.Success(rows.Select(Map).ToArray());
    }

    public async Task<DictionaryResult<GarageTariffAssignmentDto>> UpdateAsync(Guid serviceId, Guid id,
        UpdateGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken)
    {
        await using var allocationLock = await funds.AcquireAllocationLockAsync(cancellationToken);
        var garageId = await repository.FindGarageIdAsync(serviceId, id, cancellationToken);
        if (!garageId.HasValue) return Missing();
        await using var scope = await repository.BeginWriteAsync([garageId.Value], cancellationToken);
        var row = await repository.FindForUpdateAsync(serviceId, id, cancellationToken);
        if (row is null || row.IsArchived) return Missing();
        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, row);
        if (row.Garage.IsArchived)
            return DictionaryResult<GarageTariffAssignmentDto>.Failure("garage_not_found", "Гараж удалён. Его индивидуальное назначение можно отменить, но нельзя изменить.");
        var source = await ReadSourceAsync(serviceId, request.ServiceVersion, cancellationToken);
        if (!source.Succeeded) return Failure<GarageTariffAssignmentDto, Source>(source);
        var terms = Validate(source.Value!.Tariff.CalculationBase, request.EffectiveFrom, request.EffectiveTo,
            request.Rate, request.Tiers, request.Comment, request.Reason);
        if (!terms.Succeeded) return Failure<GarageTariffAssignmentDto, Terms>(terms);
        if (!await HasCompatibleTimelineAsync(source.Value, request.EffectiveFrom, request.EffectiveTo, cancellationToken))
            return DictionaryResult<GarageTariffAssignmentDto>.Failure("garage_tariff_calculation_base_conflict", "В выбранном периоде услуга меняет способ расчёта. Ограничьте индивидуальный тариф периодом с одной базой расчёта.");
        if (await repository.HasOverlapAsync(serviceId, [garageId.Value], request.EffectiveFrom, request.EffectiveTo, id, cancellationToken))
            return DictionaryResult<GarageTariffAssignmentDto>.Failure("garage_tariff_period_overlap", "Период пересекается с другим индивидуальным тарифом гаража.");
        var oldValues = Values(row);
        var from = row.EffectiveFrom < request.EffectiveFrom ? row.EffectiveFrom : request.EffectiveFrom;
        var to = row.EffectiveTo is null || request.EffectiveTo is null ? null
            : row.EffectiveTo > request.EffectiveTo ? row.EffectiveTo : request.EffectiveTo;
        var tariff = NewRevision(source.Value.Tariff.CalculationBase, request.EffectiveFrom, terms.Value!);
        row.Tariff = tariff;
        row.TariffId = tariff.Id;
        row.EffectiveFrom = request.EffectiveFrom;
        row.EffectiveTo = request.EffectiveTo;
        row.Comment = terms.Value!.Comment;
        row.UpdatedAtUtc = clock.GetUtcNow();
        WriteAudit(row, source.Value.Service.Name, actor, "update", request.Reason, oldValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var result = await RecalculateAsync(garageId.Value, from, to, actor, cancellationToken);
        if (!result.Succeeded) return Failure<GarageTariffAssignmentDto, bool>(result);
        await scope.CommitAsync(cancellationToken);
        return DictionaryResult<GarageTariffAssignmentDto>.Success(Map(row));
    }

    public async Task<DictionaryResult<GarageTariffAssignmentDto>> ArchiveAsync(Guid serviceId, Guid id,
        ArchiveGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken)
    {
        await using var allocationLock = await funds.AcquireAllocationLockAsync(cancellationToken);
        var garageId = await repository.FindGarageIdAsync(serviceId, id, cancellationToken);
        if (!garageId.HasValue) return Missing();
        await using var scope = await repository.BeginWriteAsync([garageId.Value], cancellationToken);
        var row = await repository.FindForUpdateAsync(serviceId, id, cancellationToken);
        if (row is null || row.IsArchived) return Missing();
        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, row);
        if (InvalidReason(request.Reason)) return DictionaryResult<GarageTariffAssignmentDto>.Failure("action_reason_required", "Укажите причину отмены не длиннее 1000 символов.");
        var oldValues = Values(row);
        row.IsArchived = true;
        row.UpdatedAtUtc = clock.GetUtcNow();
        WriteAudit(row, "Индивидуальный тариф", actor, "archive", request.Reason, oldValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Archiving a garage freezes its financial history. An obsolete assignment
        // can still be retired, but must not recalculate archived garage finances.
        if (!row.Garage.IsArchived)
        {
            var result = await RecalculateAsync(garageId.Value, row.EffectiveFrom, row.EffectiveTo, actor, cancellationToken);
            if (!result.Succeeded) return Failure<GarageTariffAssignmentDto, bool>(result);
        }
        await scope.CommitAsync(cancellationToken);
        return DictionaryResult<GarageTariffAssignmentDto>.Success(Map(row));
    }

    private async Task<DictionaryResult<Source>> ReadSourceAsync(Guid serviceId, Guid version, CancellationToken cancellationToken)
    {
        var service = await services.FindActiveAsync(serviceId, cancellationToken);
        if (service is null) return DictionaryResult<Source>.Failure("charge_service_not_found", "Услуга не найдена.");
        OptimisticConcurrencyGuard.EnsureCurrent(version, service);
        var tariff = service.TariffId.HasValue ? await tariffs.FindActiveAsync(service.TariffId.Value, cancellationToken) : null;
        if (!service.IsRegular || !service.IncomeTypeId.HasValue || tariff is null || !TariffCalculationBases.IsSupported(tariff.CalculationBase))
            return DictionaryResult<Source>.Failure("garage_tariff_service_invalid", "Индивидуальный тариф доступен для регулярной услуги с действующим общим тарифом.");
        return DictionaryResult<Source>.Success(new(service, tariff));
    }

    private async Task<bool> HasCompatibleTimelineAsync(Source source, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        // A future assignment is not recalculated immediately. Validate its whole
        // configured timeline now instead of discovering incompatible units months later.
        var schedule = await services.GetActiveTariffScheduleAsync(source.Service.Id, cancellationToken);
        return !schedule.Periods.Any(period => !period.Tariff.IsArchived
            && (!to.HasValue || period.EffectiveFrom <= to.Value)
            && (!period.EffectiveTo.HasValue || period.EffectiveTo.Value >= from)
            && period.Tariff.CalculationBase != source.Tariff.CalculationBase);
    }

    private static bool InvalidReason(string? reason) => reason?.Trim().Length > 1000
        || ActionCommentRequirementContext.IsRequired && string.IsNullOrWhiteSpace(reason);

    private static DictionaryResult<Terms> Validate(string calculationBase, DateOnly from, DateOnly? to,
        decimal rate, IReadOnlyList<UpsertElectricityTariffTierRequest>? tiers, string? comment, string? reason)
    {
        if (from.Year < 2000 || from.Year > 9998 || to < from || to?.Year > 9998)
            return DictionaryResult<Terms>.Failure("garage_tariff_period_invalid", "Проверьте период действия индивидуального тарифа (с 2000 года).");
        if (rate is < 0.0001m or > 999999999m || comment?.Length > 2000 || InvalidReason(reason))
            return DictionaryResult<Terms>.Failure("garage_tariff_terms_invalid", "Укажите положительную ставку, допустимый комментарий и причину изменения.");
        var normalized = new List<ElectricityTariffTierDto>();
        if (tiers is { Count: > 0 })
        {
            if (calculationBase is not (TariffCalculationBases.MeterElectricity or TariffCalculationBases.MeterWater) || tiers.Count is < 2 or > 20)
                return DictionaryResult<Terms>.Failure("garage_tariff_tiers_invalid", "Укажите от 2 до 20 ступеней только для услуги по счётчику.");
            decimal previous = 0;
            var ids = new HashSet<Guid>();
            for (var index = 0; index < tiers.Count; index++)
            {
                var tier = tiers[index];
                if (tier is null) return DictionaryResult<Terms>.Failure("garage_tariff_tiers_invalid", "Заполните все тарифные ступени.");
                var id = tier.Id ?? Guid.NewGuid();
                var upper = MoneyMath.RoundMeterValue(tier.UpperBound);
                if (id == Guid.Empty || !ids.Add(id) || string.IsNullOrWhiteSpace(tier.Name) || tier.Name.Trim().Length > 120
                    || tier.Rate is < 0.0001m or > 999999999m
                    || (index == tiers.Count - 1 ? upper.HasValue : !upper.HasValue || upper <= previous || upper > 999999999m))
                    return DictionaryResult<Terms>.Failure("garage_tariff_tiers_invalid", "Проверьте ставки и возрастающие границы ступеней. Последняя ступень должна быть без верхней границы.");
                normalized.Add(new(id, tier.Name.Trim(), upper, MoneyMath.RoundRate(tier.Rate), true));
                previous = upper ?? previous;
            }
        }
        return DictionaryResult<Terms>.Success(new(MoneyMath.RoundRate(rate), normalized, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()));
    }

    private Tariff NewRevision(string calculationBase, DateOnly from, Terms terms)
    {
        var revision = new Tariff
        {
            Name = $"Индивидуальный тариф {Guid.NewGuid():N}",
            CalculationBase = calculationBase,
            Rate = terms.Rate,
            EffectiveFrom = from,
            IsIndividual = true,
            ElectricityTiersJson = terms.Tiers.Count == 0 ? null : JsonSerializer.Serialize(terms.Tiers, JsonOptions),
            CreatedAtUtc = clock.GetUtcNow(),
            UpdatedAtUtc = clock.GetUtcNow()
        };
        tariffs.Add(revision);
        return revision;
    }

    private async Task<DictionaryResult<bool>> RecalculateAsync(Guid garageId, DateOnly from, DateOnly? to,
        Guid? actor, CancellationToken cancellationToken)
    {
        var lastDate = to.HasValue && to.Value < businessDate.Today ? to.Value : businessDate.Today;
        if (from > lastDate) return DictionaryResult<bool>.Success(true);
        var month = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(lastDate.Year, lastDate.Month, 1);
        while (month <= lastMonth)
        {
            var span = (lastMonth.Year - month.Year) * 12 + lastMonth.Month - month.Month;
            var end = month.AddMonths(Math.Min(span, 599));
            var result = await finance.CalculateGarageIncomeWorksheetAsync(garageId, new(month, end), actor, cancellationToken);
            if (!result.Succeeded) return DictionaryResult<bool>.Failure(result.ErrorCode!, result.ErrorMessage!);
            if (end == lastMonth) break;
            month = end.AddMonths(1);
        }
        return DictionaryResult<bool>.Success(true);
    }

    private void WriteAudit(GarageTariffAssignment row, string serviceName, Guid? actor, string kind,
        string? reason, IReadOnlyDictionary<string, object?>? oldValues) => audit.Add(new(actor,
        $"garage_tariff.{kind}", "GarageTariffAssignment", row.Id.ToString(),
        Summary: $"Индивидуальный тариф: {serviceName}, гараж {row.Garage.Number}", Section: "Тарифы и сборы",
        ActionKind: kind, EntityDisplayName: $"{serviceName}, гараж {row.Garage.Number}", Reason: reason?.Trim(),
        OldValues: oldValues, NewValues: Values(row), RelatedGarageId: row.GarageId.ToString(), RelatedGarageNumber: row.Garage.Number));

    private static IReadOnlyDictionary<string, object?> Values(GarageTariffAssignment row) => new Dictionary<string, object?>
    {
        ["garageId"] = row.GarageId,
        ["serviceId"] = row.ChargeServiceSettingId,
        ["tariffId"] = row.TariffId,
        ["rate"] = row.Tariff.Rate,
        ["tiers"] = row.Tariff.ElectricityTiersJson,
        ["effectiveFrom"] = row.EffectiveFrom,
        ["effectiveTo"] = row.EffectiveTo,
        ["comment"] = row.Comment,
        ["isArchived"] = row.IsArchived
    };

    private static GarageTariffAssignmentDto Map(GarageTariffAssignment row) => new(row.Id, row.GarageId,
        row.Garage.Number, row.ChargeServiceSettingId, row.TariffId, row.Tariff.CalculationBase, row.Tariff.Rate,
        string.IsNullOrWhiteSpace(row.Tariff.ElectricityTiersJson) ? []
            : JsonSerializer.Deserialize<ElectricityTariffTierDto[]>(row.Tariff.ElectricityTiersJson, JsonOptions) ?? [],
        row.EffectiveFrom, row.EffectiveTo, row.Comment, row.IsArchived, row.Version);

    private static DictionaryResult<GarageTariffAssignmentDto> Missing() => DictionaryResult<GarageTariffAssignmentDto>.Failure("garage_tariff_not_found", "Действующий индивидуальный тариф не найден.");
    private static DictionaryResult<T> Failure<T, TSource>(DictionaryResult<TSource> result) => DictionaryResult<T>.Failure(result.ErrorCode!, result.ErrorMessage!);
    private sealed record Source(ChargeServiceSetting Service, Tariff Tariff);
    private sealed record Terms(decimal Rate, IReadOnlyList<ElectricityTariffTierDto> Tiers, string? Comment);
}
