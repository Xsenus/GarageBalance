using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class SupplierServiceCatalog(
    ISupplierServiceRepository repository,
    IFundRepository mutationLock,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditWriter) : ISupplierServiceCatalog
{
    public async Task<PagedResult<SupplierServiceDto>> GetPageAsync(string? search, int offset, int limit, bool includeArchived, CancellationToken cancellationToken)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedOffset = Math.Max(0, offset);
        var normalizedLimit = QueryLimits.NormalizePageSize(limit);
        var page = await repository.GetPageAsync(normalizedSearch, normalizedOffset, normalizedLimit, includeArchived, cancellationToken);
        return new PagedResult<SupplierServiceDto>(page.Items.Select(ToDto).ToArray(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<SupplierServiceDto>> CreateAsync(UpsertSupplierServiceRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (ValidateName(name) is { } error) return error;
        await using var lease = await mutationLock.AcquireAllocationLockAsync(cancellationToken);
        if (await repository.ActiveNameExistsAsync(null, name, cancellationToken)) return Duplicate();
        var service = new SupplierService { Name = name };
        repository.Add(service);
        AddAudit(service, actorUserId, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierServiceDto>.Success(ToDto(service));
    }

    public async Task<DictionaryResult<SupplierServiceDto>> UpdateAsync(Guid id, UpsertSupplierServiceRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (ValidateName(name) is { } error) return error;
        await using var lease = await mutationLock.AcquireAllocationLockAsync(cancellationToken);
        var service = await repository.FindActiveAsync(id, cancellationToken);
        if (service is null) return DictionaryResult<SupplierServiceDto>.Failure("supplier_service_not_found", "Услуга поставщика не найдена или находится в архиве.");
        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, service);
        if (service.Name == name) return DictionaryResult<SupplierServiceDto>.Success(ToDto(service));
        if (await repository.ActiveNameExistsAsync(id, name, cancellationToken)) return Duplicate();
        var oldName = service.Name;
        service.Name = name;
        service.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(service, actorUserId, oldName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierServiceDto>.Success(ToDto(service));
    }

    private static DictionaryResult<SupplierServiceDto>? ValidateName(string name) => name.Length is 0 or > 200
        ? DictionaryResult<SupplierServiceDto>.Failure("supplier_service_name_invalid", "Укажите наименование услуги длиной от 1 до 200 символов.")
        : null;

    private static DictionaryResult<SupplierServiceDto> Duplicate() => DictionaryResult<SupplierServiceDto>.Failure(
        "supplier_service_duplicate", "Услуга поставщика с таким наименованием уже существует.");

    private void AddAudit(SupplierService service, Guid? actorUserId, string? oldName) => auditWriter.Add(new AuditEventWriteRequest(
        actorUserId,
        oldName is null ? "dictionary.supplier_service_created" : "dictionary.supplier_service_updated",
        "supplier_service", service.Id.ToString(),
        oldName is null ? $"Создана услуга поставщика {service.Name}." : $"Переименована услуга поставщика {service.Name}.",
        Section: "dictionary", ActionKind: oldName is null ? "create" : "update", EntityDisplayName: service.Name,
        OldValues: oldName is null ? null : new Dictionary<string, object?> { ["name"] = oldName },
        NewValues: new Dictionary<string, object?> { ["name"] = service.Name }));

    private static SupplierServiceDto ToDto(SupplierService service) => new(service.Id, service.Name, service.IsArchived, service.Version);
}
