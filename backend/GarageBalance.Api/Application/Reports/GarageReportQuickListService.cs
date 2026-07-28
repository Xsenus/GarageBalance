using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Reports;

namespace GarageBalance.Api.Application.Reports;

public sealed class GarageReportQuickListService(
    IGarageReportQuickListRepository repository,
    IAuditEventWriter auditEventWriter) : IGarageReportQuickListService
{
    private const int MaxNameLength = 100;
    private const int MaxGarageCount = 500;
    private const int MaxDeleteReasonLength = 1000;

    public async Task<IReadOnlyList<GarageReportQuickListDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var quickLists = await repository.GetAllAsync(cancellationToken);
        return quickLists.Select(ToDto).ToArray();
    }

    public async Task<ReportResult<GarageReportQuickListDto>> CreateAsync(
        UpsertGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (!validation.Succeeded)
        {
            return ReportResult<GarageReportQuickListDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var now = DateTimeOffset.UtcNow;
        var quickList = new GarageReportQuickList
        {
            Name = validation.Value!.Name,
            NormalizedName = validation.Value.NormalizedName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            UpdatedByUserId = actorUserId,
            Garages = validation.Value.Garages
                .Select(garage => new GarageReportQuickListGarage { GarageId = garage.Id, Garage = garage })
                .ToList()
        };
        repository.Add(quickList);
        AddAudit(quickList, actorUserId, "reports.garage_quick_list_created", "create", null);
        await repository.SaveChangesAsync(cancellationToken);
        return ReportResult<GarageReportQuickListDto>.Success(ToDto(quickList));
    }

    public async Task<ReportResult<GarageReportQuickListDto>> UpdateAsync(
        Guid id,
        UpsertGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var quickList = await repository.FindAsync(id, cancellationToken);
        if (quickList is null)
        {
            return ReportResult<GarageReportQuickListDto>.Failure("garage_quick_list_not_found", "Быстрый список гаражей не найден.");
        }

        var validation = await ValidateAsync(request, id, cancellationToken);
        if (!validation.Succeeded)
        {
            return ReportResult<GarageReportQuickListDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var oldName = quickList.Name;
        var oldGarageCount = quickList.Garages.Count;
        quickList.Name = validation.Value!.Name;
        quickList.NormalizedName = validation.Value.NormalizedName;
        quickList.UpdatedAtUtc = DateTimeOffset.UtcNow;
        quickList.UpdatedByUserId = actorUserId;
        var existingGarages = quickList.Garages.ToDictionary(item => item.GarageId);
        quickList.Garages = validation.Value.Garages
            .Select(garage => existingGarages.GetValueOrDefault(garage.Id)
                ?? new GarageReportQuickListGarage
                {
                    QuickListId = quickList.Id,
                    GarageId = garage.Id,
                    Garage = garage
                })
            .ToList();

        AddAudit(quickList, actorUserId, "reports.garage_quick_list_updated", "update", new Dictionary<string, object?>
        {
            ["name"] = oldName,
            ["garageCount"] = oldGarageCount
        });
        await repository.SaveChangesAsync(cancellationToken);
        return ReportResult<GarageReportQuickListDto>.Success(ToDto(quickList));
    }

    public async Task<ReportResult<bool>> DeleteAsync(
        Guid id,
        DeleteGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            return ReportResult<bool>.Failure("garage_quick_list_delete_reason_required", "Укажите причину удаления быстрого списка.");
        }
        if (reason.Length > MaxDeleteReasonLength)
        {
            return ReportResult<bool>.Failure(
                "garage_quick_list_delete_reason_too_long",
                $"Причина удаления не должна превышать {MaxDeleteReasonLength} символов.");
        }

        var quickList = await repository.FindAsync(id, cancellationToken);
        if (quickList is null)
        {
            return ReportResult<bool>.Failure("garage_quick_list_not_found", "Быстрый список гаражей не найден.");
        }

        quickList.IsArchived = true;
        quickList.ArchivedAtUtc = DateTimeOffset.UtcNow;
        quickList.ArchivedByUserId = actorUserId;
        quickList.UpdatedAtUtc = quickList.ArchivedAtUtc.Value;
        quickList.UpdatedByUserId = actorUserId;
        AddAudit(quickList, actorUserId, "reports.garage_quick_list_deleted", "delete", new Dictionary<string, object?>
        {
            ["name"] = quickList.Name,
            ["garageCount"] = quickList.Garages.Count
        }, reason);
        await repository.SaveChangesAsync(cancellationToken);
        return ReportResult<bool>.Success(true);
    }

    private async Task<ReportResult<ValidatedQuickList>> ValidateAsync(
        UpsertGarageReportQuickListRequest request,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Name);
        if (name.Length == 0)
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_name_required", "Укажите название быстрого списка.");
        }

        if (name.Length > MaxNameLength)
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_name_too_long", $"Название быстрого списка не должно превышать {MaxNameLength} символов.");
        }

        var normalizedName = name.ToUpperInvariant();
        if (await repository.NameExistsAsync(normalizedName, exceptId, cancellationToken))
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_name_conflict", "Быстрый список с таким названием уже существует.");
        }

        var garageIds = (request.GarageIds ?? []).Distinct().ToHashSet();
        if (garageIds.Count == 0)
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_garages_required", "Выберите хотя бы один гараж.");
        }

        if (garageIds.Count > MaxGarageCount)
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_too_many_garages", $"В быстрый список можно добавить не более {MaxGarageCount} гаражей.");
        }

        var garages = await repository.GetActiveGaragesAsync(garageIds, cancellationToken);
        if (garages.Count != garageIds.Count)
        {
            return ReportResult<ValidatedQuickList>.Failure("garage_quick_list_garage_invalid", "Один или несколько выбранных гаражей не найдены или удалены.");
        }

        return ReportResult<ValidatedQuickList>.Success(new ValidatedQuickList(name, normalizedName, garages));
    }

    private void AddAudit(
        GarageReportQuickList quickList,
        Guid? actorUserId,
        string action,
        string actionKind,
        IReadOnlyDictionary<string, object?>? oldValues,
        string? reason = null)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            "garage_report_quick_list",
            quickList.Id.ToString(),
            $"Быстрый список гаражей «{quickList.Name}»: {quickList.Garages.Count} гаражей.",
            Section: "reports",
            ActionKind: actionKind,
            EntityDisplayName: quickList.Name,
            Reason: reason,
            OldValues: oldValues,
            NewValues: actionKind == "delete" ? null : new Dictionary<string, object?>
            {
                ["name"] = quickList.Name,
                ["garageCount"] = quickList.Garages.Count
            },
            FieldLabels: new Dictionary<string, string>
            {
                ["name"] = "Название",
                ["garageCount"] = "Количество гаражей"
            }));
    }

    private static string NormalizeName(string? value)
    {
        return string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static GarageReportQuickListDto ToDto(GarageReportQuickList quickList)
    {
        var garages = quickList.Garages
            .Where(item => item.Garage is not null)
            .OrderBy(item => item.Garage.Number, StringComparer.OrdinalIgnoreCase)
            .Select(item => new GarageReportQuickListGarageDto(
                item.GarageId,
                item.Garage.Number,
                item.Garage.Owner?.FullName,
                item.Garage.IsArchived))
            .ToArray();
        return new GarageReportQuickListDto(
            quickList.Id,
            quickList.Name,
            garages,
            quickList.UpdatedAtUtc,
            quickList.UpdatedByUserId);
    }

    private sealed record ValidatedQuickList(
        string Name,
        string NormalizedName,
        IReadOnlyList<Domain.Dictionaries.Garage> Garages);
}
