using System.Globalization;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class DictionaryService(GarageBalanceDbContext dbContext) : IDictionaryService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;

    public async Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null)
    {
        var query = dbContext.Owners.AsNoTracking().Include(owner => owner.Garages).Where(owner => !owner.IsArchived);
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
            .Take(NormalizeListLimit(limit))
            .Select(owner => ToOwnerDto(owner))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Owners.AsNoTracking().Include(owner => owner.Garages).Where(owner => !owner.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(owner =>
                owner.LastName.ToLower().Contains(normalizedSearch) ||
                owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (owner.MiddleName != null && owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (owner.Phone != null && owner.Phone.ToLower().Contains(normalizedSearch)));
        }

        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(owner => owner.LastName)
            .ThenBy(owner => owner.FirstName)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(owner => ToOwnerDto(owner))
            .ToListAsync(cancellationToken);

        return new PagedResult<OwnerDto>(items, totalCount, normalizedOffset, normalizedLimit);
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

    public async Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.Owners.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден.");
        }

        owner.IsArchived = true;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_archived", "owner", owner.Id, $"Архивирован владелец {owner.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => !garage.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var sqliteGarages = await query
                .OrderBy(garage => garage.Number)
                .ToListAsync(cancellationToken);
            return sqliteGarages
                .Where(garage => GarageMatchesSearch(garage, searchValue))
                .Take(normalizedLimit)
                .Select(garage => ToGarageDto(garage))
                .ToList();
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(garage =>
                garage.Number.ToLower().Contains(normalizedSearch) ||
                (garage.Owner != null && (
                    garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                    garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                    (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                    (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch))));
        }

        var garages = await query
            .OrderBy(garage => garage.Number)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
        return garages.Select(garage => ToGarageDto(garage)).ToList();
    }

    public async Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => !garage.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var sqliteGarages = await query
                .OrderBy(garage => garage.Number)
                .ToListAsync(cancellationToken);
            var filteredGarages = sqliteGarages
                .Where(garage => GarageMatchesSearch(garage, searchValue))
                .ToList();
            var items = filteredGarages
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .Select(garage => ToGarageDto(garage))
                .ToList();
            return new PagedResult<GarageDto>(items, filteredGarages.Count, normalizedOffset, normalizedLimit);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(garage =>
                garage.Number.ToLower().Contains(normalizedSearch) ||
                (garage.Owner != null && (
                    garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                    garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                    (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                    (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch))));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var garages = await query
            .OrderBy(garage => garage.Number)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
        return new PagedResult<GarageDto>(garages.Select(garage => ToGarageDto(garage)).ToList(), totalCount, normalizedOffset, normalizedLimit);
    }

    private static bool GarageMatchesSearch(Garage garage, string normalizedSearch)
    {
        return garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool IsSqliteProvider()
    {
        return dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var number = request.Number.Trim();
        if (await dbContext.Garages.AnyAsync(garage => !garage.IsArchived && garage.Number == number, cancellationToken))
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
            StartingBalance = MoneyMath.RoundMoney(request.StartingBalance),
            OwnerId = request.OwnerId,
            Owner = owner,
            InitialWaterMeterValue = MoneyMath.RoundMeterValue(request.InitialWaterMeterValue),
            InitialElectricityMeterValue = MoneyMath.RoundMeterValue(request.InitialElectricityMeterValue),
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
        if (await dbContext.Garages.AnyAsync(item => item.Id != id && !item.IsArchived && item.Number == number, cancellationToken))
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
        garage.StartingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        garage.OwnerId = request.OwnerId;
        garage.Owner = owner;
        garage.InitialWaterMeterValue = MoneyMath.RoundMeterValue(request.InitialWaterMeterValue);
        garage.InitialElectricityMeterValue = MoneyMath.RoundMeterValue(request.InitialElectricityMeterValue);
        garage.Comment = NormalizeOptional(request.Comment);
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_updated", "garage", garage.Id, $"Обновлен гараж N {garage.Number}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        garage.IsArchived = true;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_archived", "garage", garage.Id, $"Архивирован гараж N {garage.Number}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken, int? limit = null)
    {
        return await dbContext.SupplierGroups.AsNoTracking()
            .Where(group => !group.IsArchived)
            .OrderBy(group => group.Name)
            .Take(NormalizeListLimit(limit))
            .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.SupplierGroups.AsNoTracking().Where(group => !group.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(group => group.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierGroupDto>(items, totalCount, normalizedOffset, normalizedLimit);
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

    public async Task<DictionaryResult<SupplierGroupDto>> UpdateSupplierGroupAsync(Guid id, UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await dbContext.SupplierGroups.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Группа поставщиков не найдена.");
        }

        if (group.IsSystem)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_system", "Системную группу поставщиков нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await dbContext.SupplierGroups.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Группа поставщиков с таким названием уже существует.");
        }

        group.Name = name;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_group_updated", "supplier_group", group.Id, $"Обновлена группа поставщиков {group.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await dbContext.SupplierGroups.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Группа поставщиков не найдена.");
        }

        if (group.IsSystem)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_system", "Системную группу поставщиков нельзя архивировать.");
        }

        group.IsArchived = true;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_group_archived", "supplier_group", group.Id, $"Архивирована группа поставщиков {group.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null)
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
            .Take(NormalizeListLimit(limit))
            .Select(supplier => ToSupplierDto(supplier))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, CancellationToken cancellationToken)
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

        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(supplier => ToSupplierDto(supplier))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierDto>(items, totalCount, normalizedOffset, normalizedLimit);
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
            StartingBalance = MoneyMath.RoundMoney(request.StartingBalance),
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
        supplier.StartingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        supplier.Comment = NormalizeOptional(request.Comment);
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_updated", "supplier", supplier.Id, $"Обновлен поставщик {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.Include(item => item.Group).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        supplier.IsArchived = true;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_archived", "supplier", supplier.Id, $"Архивирован поставщик {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken, int? limit = null)
    {
        return await dbContext.IncomeTypes.AsNoTracking()
            .Where(item => !item.IsArchived)
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.IncomeTypes.AsNoTracking().Where(item => !item.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);

        return new PagedResult<AccountingTypeDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.IncomeTypes.AnyAsync(item => item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Вид поступления с таким названием уже существует.");
        }

        var incomeType = new IncomeType
        {
            Name = name,
            Code = NormalizeOptional(request.Code)
        };

        dbContext.IncomeTypes.Add(incomeType);
        AddAudit(actorUserId, "dictionary.income_type_created", "income_type", incomeType.Id, $"Создан вид поступления {incomeType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> UpdateIncomeTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        if (incomeType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_system", "Системный вид поступления нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await dbContext.IncomeTypes.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Вид поступления с таким названием уже существует.");
        }

        incomeType.Name = name;
        incomeType.Code = NormalizeOptional(request.Code);
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_updated", "income_type", incomeType.Id, $"Обновлен вид поступления {incomeType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        if (incomeType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_system", "Системный вид поступления нельзя архивировать.");
        }

        incomeType.IsArchived = true;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_archived", "income_type", incomeType.Id, $"Архивирован вид поступления {incomeType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken, int? limit = null)
    {
        return await dbContext.ExpenseTypes.AsNoTracking()
            .Where(item => !item.IsArchived)
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.ExpenseTypes.AsNoTracking().Where(item => !item.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);

        return new PagedResult<AccountingTypeDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<AccountingTypeDto>> CreateExpenseTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.ExpenseTypes.AnyAsync(item => item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Вид выплаты с таким названием уже существует.");
        }

        var expenseType = new ExpenseType
        {
            Name = name,
            Code = NormalizeOptional(request.Code)
        };

        dbContext.ExpenseTypes.Add(expenseType);
        AddAudit(actorUserId, "dictionary.expense_type_created", "expense_type", expenseType.Id, $"Создан вид выплаты {expenseType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> UpdateExpenseTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        if (expenseType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_system", "Системный вид выплаты нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await dbContext.ExpenseTypes.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Вид выплаты с таким названием уже существует.");
        }

        expenseType.Name = name;
        expenseType.Code = NormalizeOptional(request.Code);
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_updated", "expense_type", expenseType.Id, $"Обновлен вид выплаты {expenseType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        if (expenseType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_system", "Системный вид выплаты нельзя архивировать.");
        }

        expenseType.IsArchived = true;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_archived", "expense_type", expenseType.Id, $"Архивирован вид выплаты {expenseType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => !item.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                item.CalculationBase.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => ToTariffDto(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => !item.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                item.CalculationBase.ToLower().Contains(normalizedSearch));
        }

        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenBy(item => item.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(item => ToTariffDto(item))
            .ToListAsync(cancellationToken);

        return new PagedResult<TariffDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    private static int NormalizeListLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
    }

    private static int NormalizeListOffset(int? offset)
    {
        if (offset is null or < 0)
        {
            return 0;
        }

        return offset.Value;
    }

    public async Task<DictionaryResult<TariffDto>> CreateTariffAsync(UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var calculationBase = request.CalculationBase.Trim();
        if (!TariffCalculationBases.IsSupported(calculationBase))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_calculation_base_invalid", "База расчета тарифа должна быть fixed, people, meter_water или meter_electricity.");
        }

        var electricityTiers = ValidateElectricityTiers(calculationBase, request);
        if (!electricityTiers.Succeeded)
        {
            return DictionaryResult<TariffDto>.Failure(electricityTiers.ErrorCode!, electricityTiers.ErrorMessage!);
        }

        if (await dbContext.Tariffs.AnyAsync(item => item.Name == name && item.EffectiveFrom == request.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.");
        }

        var tariff = new Tariff
        {
            Name = name,
            CalculationBase = calculationBase,
            Rate = MoneyMath.RoundRate(request.Rate),
            EffectiveFrom = request.EffectiveFrom,
            Comment = NormalizeOptional(request.Comment)
        };
        ApplyElectricityTiers(tariff, electricityTiers.Value);

        dbContext.Tariffs.Add(tariff);
        AddAudit(actorUserId, "dictionary.tariff_created", "tariff", tariff.Id, $"Создан тариф {FormatTariffAuditDetails(tariff)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> UpdateTariffAsync(Guid id, UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var tariff = await dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден.");
        }

        var name = request.Name.Trim();
        var calculationBase = request.CalculationBase.Trim();
        if (!TariffCalculationBases.IsSupported(calculationBase))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_calculation_base_invalid", "База расчета тарифа должна быть fixed, people, meter_water или meter_electricity.");
        }

        var electricityTiers = ValidateElectricityTiers(calculationBase, request);
        if (!electricityTiers.Succeeded)
        {
            return DictionaryResult<TariffDto>.Failure(electricityTiers.ErrorCode!, electricityTiers.ErrorMessage!);
        }

        if (await dbContext.Tariffs.AnyAsync(item => item.Id != id && item.Name == name && item.EffectiveFrom == request.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.");
        }

        tariff.Name = name;
        tariff.CalculationBase = calculationBase;
        tariff.Rate = MoneyMath.RoundRate(request.Rate);
        tariff.EffectiveFrom = request.EffectiveFrom;
        tariff.Comment = NormalizeOptional(request.Comment);
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyElectricityTiers(tariff, electricityTiers.Value);

        AddAudit(actorUserId, "dictionary.tariff_updated", "tariff", tariff.Id, $"Изменен тариф {FormatTariffAuditDetails(tariff)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var tariff = await dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден.");
        }

        tariff.IsArchived = true;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.tariff_archived", "tariff", tariff.Id, $"Архивирован тариф {FormatTariffAuditDetails(tariff)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
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

    private static string FormatTariffAuditDetails(Tariff tariff)
    {
        var baseDetails = $"{tariff.Name} с {tariff.EffectiveFrom:dd.MM.yyyy}, база {tariff.CalculationBase}, ставка {tariff.Rate.ToString("0.####", CultureInfo.InvariantCulture)}";
        if (!HasElectricityTiers(tariff))
        {
            return baseDetails;
        }

        return $"{baseDetails}, электричество: до {tariff.ElectricityFirstThreshold!.Value.ToString("0.####", CultureInfo.InvariantCulture)} кВт по {tariff.ElectricityFirstRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}, до {tariff.ElectricitySecondThreshold!.Value.ToString("0.####", CultureInfo.InvariantCulture)} кВт по {tariff.ElectricitySecondRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}, свыше по {tariff.ElectricityThirdRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}";
    }

    private static DictionaryResult<ElectricityTierConfig?> ValidateElectricityTiers(string calculationBase, UpsertTariffRequest request)
    {
        if (calculationBase != TariffCalculationBases.MeterElectricity)
        {
            return DictionaryResult<ElectricityTierConfig?>.Success(null);
        }

        var values = new decimal?[]
        {
            request.ElectricityFirstThreshold,
            request.ElectricitySecondThreshold,
            request.ElectricityFirstRate,
            request.ElectricitySecondRate,
            request.ElectricityThirdRate
        };
        var hasAnyTierValue = values.Any(value => value.HasValue);
        if (!hasAnyTierValue)
        {
            return DictionaryResult<ElectricityTierConfig?>.Success(null);
        }

        if (values.Any(value => !value.HasValue))
        {
            return DictionaryResult<ElectricityTierConfig?>.Failure(
                "tariff_electricity_tiers_incomplete",
                "Для трехтарифной электроэнергии нужно заполнить два порога и три ставки.");
        }

        var firstThreshold = MoneyMath.RoundMeterValue(request.ElectricityFirstThreshold!.Value);
        var secondThreshold = MoneyMath.RoundMeterValue(request.ElectricitySecondThreshold!.Value);
        var firstRate = MoneyMath.RoundRate(request.ElectricityFirstRate!.Value);
        var secondRate = MoneyMath.RoundRate(request.ElectricitySecondRate!.Value);
        var thirdRate = MoneyMath.RoundRate(request.ElectricityThirdRate!.Value);

        if (firstThreshold <= 0 || secondThreshold <= 0 || firstRate <= 0 || secondRate <= 0 || thirdRate <= 0)
        {
            return DictionaryResult<ElectricityTierConfig?>.Failure(
                "tariff_electricity_tiers_positive_required",
                "Пороги и ставки электроэнергии должны быть больше 0.");
        }

        if (secondThreshold <= firstThreshold)
        {
            return DictionaryResult<ElectricityTierConfig?>.Failure(
                "tariff_electricity_second_threshold_invalid",
                "Второй порог электроэнергии должен быть больше первого.");
        }

        return DictionaryResult<ElectricityTierConfig?>.Success(new ElectricityTierConfig(firstThreshold, secondThreshold, firstRate, secondRate, thirdRate));
    }

    private static void ApplyElectricityTiers(Tariff tariff, ElectricityTierConfig? tiers)
    {
        tariff.ElectricityFirstThreshold = tiers?.FirstThreshold;
        tariff.ElectricitySecondThreshold = tiers?.SecondThreshold;
        tariff.ElectricityFirstRate = tiers?.FirstRate;
        tariff.ElectricitySecondRate = tiers?.SecondRate;
        tariff.ElectricityThirdRate = tiers?.ThirdRate;
    }

    private static bool HasElectricityTiers(Tariff tariff)
    {
        return tariff.ElectricityFirstThreshold.HasValue
            && tariff.ElectricitySecondThreshold.HasValue
            && tariff.ElectricityFirstRate.HasValue
            && tariff.ElectricitySecondRate.HasValue
            && tariff.ElectricityThirdRate.HasValue;
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
        return new OwnerDto(owner.Id, owner.LastName, owner.FirstName, owner.MiddleName, owner.FullName, owner.Phone, owner.Address, owner.MeterNotes, owner.IsArchived)
        {
            GarageNumbers = owner.Garages
                .Where(garage => !garage.IsArchived)
                .OrderBy(garage => garage.Number)
                .Select(garage => garage.Number)
                .ToList()
        };
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
            garage.StartingBalance,
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

    private static TariffDto ToTariffDto(Tariff tariff)
    {
        return new TariffDto(
            tariff.Id,
            tariff.Name,
            tariff.CalculationBase,
            tariff.Rate,
            tariff.EffectiveFrom,
            tariff.Comment,
            tariff.IsArchived,
            tariff.ElectricityFirstThreshold,
            tariff.ElectricitySecondThreshold,
            tariff.ElectricityFirstRate,
            tariff.ElectricitySecondRate,
            tariff.ElectricityThirdRate);
    }

    private sealed record ElectricityTierConfig(
        decimal FirstThreshold,
        decimal SecondThreshold,
        decimal FirstRate,
        decimal SecondRate,
        decimal ThirdRate);
}
