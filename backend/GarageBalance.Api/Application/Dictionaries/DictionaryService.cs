using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class DictionaryService(GarageBalanceDbContext dbContext) : IDictionaryService
{
    private const int ListLimit = 100;

    public async Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Owners.AsNoTracking().Where(owner => !owner.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(owner =>
                owner.LastName.ToLower().Contains(normalizedSearch) ||
                owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (owner.MiddleName != null && owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (owner.Phone != null && owner.Phone.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .Take(ListLimit)
            .Select(owner => ToOwnerDto(owner))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<OwnerDto>> CreateOwnerAsync(UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = new Owner
        {
            LastName = request.LastName.Trim(),
            FirstName = request.FirstName.Trim(),
            MiddleName = NormalizeOptional(request.MiddleName),
            Phone = NormalizeOptional(request.Phone),
            Address = NormalizeOptional(request.Address),
            MeterNotes = NormalizeOptional(request.MeterNotes)
        };

        dbContext.Owners.Add(owner);
        AddAudit(actorUserId, "dictionary.owner_created", "owner", owner.Id, $"Создан владелец {owner.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> UpdateOwnerAsync(Guid id, UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.Owners.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден.");
        }

        owner.LastName = request.LastName.Trim();
        owner.FirstName = request.FirstName.Trim();
        owner.MiddleName = NormalizeOptional(request.MiddleName);
        owner.Phone = NormalizeOptional(request.Phone);
        owner.Address = NormalizeOptional(request.Address);
        owner.MeterNotes = NormalizeOptional(request.MeterNotes);
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_updated", "owner", owner.Id, $"Обновлен владелец {owner.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => !garage.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(garage =>
                garage.Number.ToLower().Contains(normalizedSearch) ||
                (garage.Owner != null && (
                    garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                    garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                    (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)))));
        }

        return await query
            .OrderBy(garage => garage.Number)
            .Take(ListLimit)
            .Select(garage => ToGarageDto(garage))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var number = request.Number.Trim();
        if (await dbContext.Garages.AnyAsync(garage => garage.Number == number, cancellationToken))
        {
            return DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Гараж с таким номером уже существует.");
        }

        var owner = await FindOwnerOrNullAsync(request.OwnerId, cancellationToken);
        if (request.OwnerId is not null && owner is null)
        {
            return DictionaryResult<GarageDto>.Failure("owner_not_found", "Владелец гаража не найден.");
        }

        var garage = new Garage
        {
            Number = number,
            PeopleCount = request.PeopleCount,
            FloorCount = request.FloorCount,
            OwnerId = request.OwnerId,
            Owner = owner,
            InitialWaterMeterValue = request.InitialWaterMeterValue,
            InitialElectricityMeterValue = request.InitialElectricityMeterValue,
            Comment = NormalizeOptional(request.Comment)
        };

        dbContext.Garages.Add(garage);
        AddAudit(actorUserId, "dictionary.garage_created", "garage", garage.Id, $"Создан гараж N {garage.Number}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<DictionaryResult<GarageDto>> UpdateGarageAsync(Guid id, UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        var number = request.Number.Trim();
        if (await dbContext.Garages.AnyAsync(item => item.Id != id && item.Number == number, cancellationToken))
        {
            return DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Гараж с таким номером уже существует.");
        }

        var owner = await FindOwnerOrNullAsync(request.OwnerId, cancellationToken);
        if (request.OwnerId is not null && owner is null)
        {
            return DictionaryResult<GarageDto>.Failure("owner_not_found", "Владелец гаража не найден.");
        }

        garage.Number = number;
        garage.PeopleCount = request.PeopleCount;
        garage.FloorCount = request.FloorCount;
        garage.OwnerId = request.OwnerId;
        garage.Owner = owner;
        garage.InitialWaterMeterValue = request.InitialWaterMeterValue;
        garage.InitialElectricityMeterValue = request.InitialElectricityMeterValue;
        garage.Comment = NormalizeOptional(request.Comment);
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_updated", "garage", garage.Id, $"Обновлен гараж N {garage.Number}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SupplierGroups.AsNoTracking()
            .Where(group => !group.IsArchived)
            .OrderBy(group => group.Name)
            .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.SupplierGroups.AnyAsync(group => group.Name == name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Группа поставщиков с таким названием уже существует.");
        }

        var group = new SupplierGroup { Name = name };
        dbContext.SupplierGroups.Add(group);
        AddAudit(actorUserId, "dictionary.supplier_group_created", "supplier_group", group.Id, $"Создана группа поставщиков {group.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Suppliers.AsNoTracking().Include(supplier => supplier.Group).Where(supplier => !supplier.IsArchived);
        if (groupId is not null)
        {
            query = query.Where(supplier => supplier.GroupId == groupId);
        }

        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(supplier =>
                supplier.Name.ToLower().Contains(normalizedSearch) ||
                (supplier.Inn != null && supplier.Inn.ToLower().Contains(normalizedSearch)) ||
                (supplier.ContactPerson != null && supplier.ContactPerson.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .Take(ListLimit)
            .Select(supplier => ToSupplierDto(supplier))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await dbContext.SupplierGroups.SingleOrDefaultAsync(item => item.Id == request.GroupId && !item.IsArchived, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Группа поставщика не найдена.");
        }

        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            GroupId = group.Id,
            Group = group,
            Inn = NormalizeOptional(request.Inn),
            LegalAddress = NormalizeOptional(request.LegalAddress),
            ContactPerson = NormalizeOptional(request.ContactPerson),
            Phone = NormalizeOptional(request.Phone),
            Email = NormalizeOptional(request.Email),
            StartingBalance = request.StartingBalance,
            Comment = NormalizeOptional(request.Comment)
        };

        dbContext.Suppliers.Add(supplier);
        AddAudit(actorUserId, "dictionary.supplier_created", "supplier", supplier.Id, $"Создан поставщик {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> UpdateSupplierAsync(Guid id, UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.Include(item => item.Group).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        var group = await dbContext.SupplierGroups.SingleOrDefaultAsync(item => item.Id == request.GroupId && !item.IsArchived, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Группа поставщика не найдена.");
        }

        supplier.Name = request.Name.Trim();
        supplier.GroupId = group.Id;
        supplier.Group = group;
        supplier.Inn = NormalizeOptional(request.Inn);
        supplier.LegalAddress = NormalizeOptional(request.LegalAddress);
        supplier.ContactPerson = NormalizeOptional(request.ContactPerson);
        supplier.Phone = NormalizeOptional(request.Phone);
        supplier.Email = NormalizeOptional(request.Email);
        supplier.StartingBalance = request.StartingBalance;
        supplier.Comment = NormalizeOptional(request.Comment);
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_updated", "supplier", supplier.Id, $"Обновлен поставщик {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    private async Task<Owner?> FindOwnerOrNullAsync(Guid? ownerId, CancellationToken cancellationToken)
    {
        return ownerId is null
            ? null
            : await dbContext.Owners.SingleOrDefaultAsync(owner => owner.Id == ownerId && !owner.IsArchived, cancellationToken);
    }

    private void AddAudit(Guid? actorUserId, string action, string entityType, Guid entityId, string summary)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Summary = summary
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeSearch(string? search)
    {
        return string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
    }

    private static OwnerDto ToOwnerDto(Owner owner)
    {
        return new OwnerDto(owner.Id, owner.LastName, owner.FirstName, owner.MiddleName, owner.FullName, owner.Phone, owner.Address, owner.MeterNotes, owner.IsArchived);
    }

    private static GarageDto ToGarageDto(Garage garage)
    {
        return new GarageDto(
            garage.Id,
            garage.Number,
            garage.PeopleCount,
            garage.FloorCount,
            garage.OwnerId,
            garage.Owner?.FullName,
            garage.InitialWaterMeterValue,
            garage.InitialElectricityMeterValue,
            garage.Comment,
            garage.IsArchived);
    }

    private static SupplierDto ToSupplierDto(Supplier supplier)
    {
        return new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.GroupId,
            supplier.Group.Name,
            supplier.Inn,
            supplier.LegalAddress,
            supplier.ContactPerson,
            supplier.Phone,
            supplier.Email,
            supplier.StartingBalance,
            supplier.Comment,
            supplier.IsArchived);
    }
}
