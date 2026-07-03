using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class DictionaryService(
    GarageBalanceDbContext dbContext,
    IAuditEventWriter auditEventWriter) : IDictionaryService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private static readonly IReadOnlyDictionary<string, string> DictionaryFieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["lastName"] = "Фамилия",
        ["firstName"] = "Имя",
        ["middleName"] = "Отчество",
        ["phone"] = "Телефон",
        ["address"] = "Адрес",
        ["meterNotes"] = "Счетчики",
        ["number"] = "Номер",
        ["peopleCount"] = "Количество людей",
        ["floorCount"] = "Количество этажей",
        ["owner"] = "Владелец",
        ["startingBalance"] = "Стартовый баланс",
        ["initialWaterMeterValue"] = "Стартовое показание воды",
        ["initialElectricityMeterValue"] = "Стартовое показание электроэнергии",
        ["comment"] = "Комментарий",
        ["name"] = "Наименование",
        ["group"] = "Группа",
        ["inn"] = "ИНН",
        ["legalAddress"] = "Юр. адрес",
        ["contactPerson"] = "Контактное лицо",
        ["email"] = "Почта",
        ["code"] = "Код",
        ["calculationBase"] = "База расчета",
        ["rate"] = "Ставка",
        ["effectiveFrom"] = "Дата начала",
        ["electricityFirstThreshold"] = "Порог 1",
        ["electricitySecondThreshold"] = "Порог 2",
        ["electricityFirstTierName"] = "Наименование порога 1",
        ["electricitySecondTierName"] = "Наименование порога 2",
        ["electricityThirdTierName"] = "Наименование порога 3",
        ["electricityFirstRate"] = "Цена за ед. порога 1",
        ["electricitySecondRate"] = "Цена за ед. порога 2",
        ["electricityThirdRate"] = "Цена сверх порога 2"
    };

    public DictionaryService(GarageBalanceDbContext dbContext)
        : this(dbContext, new AuditEventWriter(dbContext))
    {
    }

    public async Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.Owners.AsNoTracking().Include(owner => owner.Garages).Where(owner => includeArchived || !owner.IsArchived);
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

    public async Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.Owners.AsNoTracking().Include(owner => owner.Garages).Where(owner => includeArchived || !owner.IsArchived);
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

        var lastName = request.LastName.Trim();
        var firstName = request.FirstName.Trim();
        var middleName = NormalizeOptional(request.MiddleName);
        var phone = NormalizeOptional(request.Phone);
        var address = NormalizeOptional(request.Address);
        var meterNotes = NormalizeOptional(request.MeterNotes);
        if (OwnerMatches(owner, lastName, firstName, middleName, phone, address, meterNotes))
        {
            return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["lastName"] = owner.LastName,
            ["firstName"] = owner.FirstName,
            ["middleName"] = owner.MiddleName,
            ["phone"] = owner.Phone,
            ["address"] = owner.Address,
            ["meterNotes"] = owner.MeterNotes
        };
        var newValues = new Dictionary<string, object?>
        {
            ["lastName"] = lastName,
            ["firstName"] = firstName,
            ["middleName"] = middleName,
            ["phone"] = phone,
            ["address"] = address,
            ["meterNotes"] = meterNotes
        };

        owner.LastName = lastName;
        owner.FirstName = firstName;
        owner.MiddleName = middleName;
        owner.Phone = phone;
        owner.Address = address;
        owner.MeterNotes = meterNotes;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_updated", "owner", owner.Id, $"Обновлен владелец {owner.FullName}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<OwnerDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var owner = await dbContext.Owners.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден.");
        }

        owner.IsArchived = true;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_archived", "owner", owner.Id, $"Архивирован владелец {owner.FullName}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> RestoreOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.Owners.Include(item => item.Garages).SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден в архиве.");
        }

        owner.IsArchived = false;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_restored", "owner", owner.Id, $"Восстановлен владелец {owner.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => includeArchived || !garage.IsArchived);
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

    public async Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => includeArchived || !garage.IsArchived);
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

        var startingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        var initialWaterMeterValue = MoneyMath.RoundMeterValue(request.InitialWaterMeterValue);
        var initialElectricityMeterValue = MoneyMath.RoundMeterValue(request.InitialElectricityMeterValue);
        var comment = NormalizeOptional(request.Comment);
        if (GarageMatches(garage, number, request.PeopleCount, request.FloorCount, request.OwnerId, startingBalance, initialWaterMeterValue, initialElectricityMeterValue, comment))
        {
            return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["number"] = garage.Number,
            ["peopleCount"] = garage.PeopleCount,
            ["floorCount"] = garage.FloorCount,
            ["owner"] = garage.Owner?.FullName,
            ["startingBalance"] = garage.StartingBalance,
            ["initialWaterMeterValue"] = garage.InitialWaterMeterValue,
            ["initialElectricityMeterValue"] = garage.InitialElectricityMeterValue,
            ["comment"] = garage.Comment
        };
        var newValues = new Dictionary<string, object?>
        {
            ["number"] = number,
            ["peopleCount"] = request.PeopleCount,
            ["floorCount"] = request.FloorCount,
            ["owner"] = owner?.FullName,
            ["startingBalance"] = startingBalance,
            ["initialWaterMeterValue"] = initialWaterMeterValue,
            ["initialElectricityMeterValue"] = initialElectricityMeterValue,
            ["comment"] = comment
        };

        garage.Number = number;
        garage.PeopleCount = request.PeopleCount;
        garage.FloorCount = request.FloorCount;
        garage.StartingBalance = startingBalance;
        garage.OwnerId = request.OwnerId;
        garage.Owner = owner;
        garage.InitialWaterMeterValue = initialWaterMeterValue;
        garage.InitialElectricityMeterValue = initialElectricityMeterValue;
        garage.Comment = comment;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_updated", "garage", garage.Id, $"Обновлен гараж N {garage.Number}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<GarageDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        garage.IsArchived = true;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_archived", "garage", garage.Id, $"Архивирован гараж N {garage.Number}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<DictionaryResult<GarageDto>> RestoreGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден в архиве.");
        }

        if (await dbContext.Garages.AnyAsync(item => item.Id != id && !item.IsArchived && item.Number == garage.Number, cancellationToken))
        {
            return DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Активный гараж с таким номером уже существует.");
        }

        garage.IsArchived = false;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_restored", "garage", garage.Id, $"Восстановлен гараж N {garage.Number}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(ToGarageDto(garage));
    }

    public async Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        return await dbContext.SupplierGroups.AsNoTracking()
            .Where(group => includeArchived || !group.IsArchived)
            .OrderBy(group => group.Name)
            .Take(NormalizeListLimit(limit))
            .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.SupplierGroups.AsNoTracking().Where(group => includeArchived || !group.IsArchived);
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
        if (await dbContext.SupplierGroups.AnyAsync(group => !group.IsArchived && group.Name == name, cancellationToken))
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
        if (await dbContext.SupplierGroups.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Группа поставщиков с таким названием уже существует.");
        }

        if (StringEquals(group.Name, name))
        {
            return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = group.Name
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name
        };

        group.Name = name;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_group_updated", "supplier_group", group.Id, $"Обновлена группа поставщиков {group.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierGroupDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

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

        AddAudit(actorUserId, "dictionary.supplier_group_archived", "supplier_group", group.Id, $"Архивирована группа поставщиков {group.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> RestoreSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await dbContext.SupplierGroups.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Группа поставщиков не найдена в архиве.");
        }

        if (await dbContext.SupplierGroups.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == group.Name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Активная группа поставщиков с таким названием уже существует.");
        }

        group.IsArchived = false;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_group_restored", "supplier_group", group.Id, $"Восстановлена группа поставщиков {group.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.Suppliers.AsNoTracking().Include(supplier => supplier.Group).Where(supplier => includeArchived || !supplier.IsArchived);
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

    public async Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.Suppliers.AsNoTracking().Include(supplier => supplier.Group).Where(supplier => includeArchived || !supplier.IsArchived);
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

        var name = request.Name.Trim();
        var inn = NormalizeOptional(request.Inn);
        var legalAddress = NormalizeOptional(request.LegalAddress);
        var contactPerson = NormalizeOptional(request.ContactPerson);
        var phone = NormalizeOptional(request.Phone);
        var email = NormalizeOptional(request.Email);
        var startingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        var comment = NormalizeOptional(request.Comment);
        if (SupplierMatches(supplier, name, group.Id, inn, legalAddress, contactPerson, phone, email, startingBalance, comment))
        {
            return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = supplier.Name,
            ["group"] = supplier.Group.Name,
            ["inn"] = supplier.Inn,
            ["legalAddress"] = supplier.LegalAddress,
            ["contactPerson"] = supplier.ContactPerson,
            ["phone"] = supplier.Phone,
            ["email"] = supplier.Email,
            ["startingBalance"] = supplier.StartingBalance,
            ["comment"] = supplier.Comment
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["group"] = group.Name,
            ["inn"] = inn,
            ["legalAddress"] = legalAddress,
            ["contactPerson"] = contactPerson,
            ["phone"] = phone,
            ["email"] = email,
            ["startingBalance"] = startingBalance,
            ["comment"] = comment
        };

        supplier.Name = name;
        supplier.GroupId = group.Id;
        supplier.Group = group;
        supplier.Inn = inn;
        supplier.LegalAddress = legalAddress;
        supplier.ContactPerson = contactPerson;
        supplier.Phone = phone;
        supplier.Email = email;
        supplier.StartingBalance = startingBalance;
        supplier.Comment = comment;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_updated", "supplier", supplier.Id, $"Обновлен поставщик {supplier.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var supplier = await dbContext.Suppliers.Include(item => item.Group).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        supplier.IsArchived = true;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_archived", "supplier", supplier.Id, $"Архивирован поставщик {supplier.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> RestoreSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.Include(item => item.Group).SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден в архиве.");
        }

        if (supplier.Group.IsArchived)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Сначала восстановите группу поставщика.");
        }

        supplier.IsArchived = false;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_restored", "supplier", supplier.Id, $"Восстановлен поставщик {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        return await dbContext.IncomeTypes.AsNoTracking()
            .Where(item => includeArchived || !item.IsArchived)
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.IncomeTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
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
        if (await dbContext.IncomeTypes.AnyAsync(item => !item.IsArchived && item.Name == name, cancellationToken))
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
        if (await dbContext.IncomeTypes.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Вид поступления с таким названием уже существует.");
        }

        var code = NormalizeOptional(request.Code);
        if (AccountingTypeMatches(incomeType, name, code))
        {
            return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = incomeType.Name,
            ["code"] = incomeType.Code
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = code
        };

        incomeType.Name = name;
        incomeType.Code = code;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_updated", "income_type", incomeType.Id, $"Обновлен вид поступления {incomeType.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<AccountingTypeDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

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

        AddAudit(actorUserId, "dictionary.income_type_archived", "income_type", incomeType.Id, $"Архивирован вид поступления {incomeType.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> RestoreIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Вид поступления не найден в архиве.");
        }

        if (await dbContext.IncomeTypes.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == incomeType.Name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Активный вид поступления с таким названием уже существует.");
        }

        incomeType.IsArchived = false;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_restored", "income_type", incomeType.Id, $"Восстановлен вид поступления {incomeType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        return await dbContext.ExpenseTypes.AsNoTracking()
            .Where(item => includeArchived || !item.IsArchived)
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.ExpenseTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
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
        if (await dbContext.ExpenseTypes.AnyAsync(item => !item.IsArchived && item.Name == name, cancellationToken))
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
        if (await dbContext.ExpenseTypes.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Вид выплаты с таким названием уже существует.");
        }

        var code = NormalizeOptional(request.Code);
        if (AccountingTypeMatches(expenseType, name, code))
        {
            return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = expenseType.Name,
            ["code"] = expenseType.Code
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = code
        };

        expenseType.Name = name;
        expenseType.Code = code;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_updated", "expense_type", expenseType.Id, $"Обновлен вид выплаты {expenseType.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<AccountingTypeDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

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

        AddAudit(actorUserId, "dictionary.expense_type_archived", "expense_type", expenseType.Id, $"Архивирован вид выплаты {expenseType.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> RestoreExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Вид выплаты не найден в архиве.");
        }

        if (await dbContext.ExpenseTypes.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == expenseType.Name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Активный вид выплаты с таким названием уже существует.");
        }

        expenseType.IsArchived = false;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_restored", "expense_type", expenseType.Id, $"Восстановлен вид выплаты {expenseType.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
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

    public async Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var query = dbContext.Tariffs.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
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

        if (await dbContext.Tariffs.AnyAsync(item => !item.IsArchived && item.Name == name && item.EffectiveFrom == request.EffectiveFrom, cancellationToken))
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

        if (await dbContext.Tariffs.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name && item.EffectiveFrom == request.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.");
        }

        if (request.EffectiveFrom > tariff.EffectiveFrom)
        {
            var earliestAccrualMonth = await dbContext.Accruals.AsNoTracking()
                .Where(accrual => !accrual.IsCanceled && accrual.Source == AccrualSources.Regular && accrual.TariffId == tariff.Id)
                .MinAsync(accrual => (DateOnly?)accrual.AccountingMonth, cancellationToken);
            if (earliestAccrualMonth is not null && request.EffectiveFrom > earliestAccrualMonth.Value)
            {
                return DictionaryResult<TariffDto>.Failure(
                    "tariff_effective_from_after_accrual",
                    $"Дата начала тарифа не может быть позже уже созданного начисления за {earliestAccrualMonth.Value:MM.yyyy}.");
            }
        }

        var rate = MoneyMath.RoundRate(request.Rate);
        var comment = NormalizeOptional(request.Comment);
        if (TariffMatches(tariff, name, calculationBase, rate, request.EffectiveFrom, comment, electricityTiers.Value))
        {
            return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = tariff.Name,
            ["calculationBase"] = tariff.CalculationBase,
            ["rate"] = tariff.Rate,
            ["effectiveFrom"] = tariff.EffectiveFrom,
            ["comment"] = tariff.Comment,
            ["electricityFirstThreshold"] = tariff.ElectricityFirstThreshold,
            ["electricitySecondThreshold"] = tariff.ElectricitySecondThreshold,
            ["electricityFirstTierName"] = tariff.ElectricityFirstTierName,
            ["electricitySecondTierName"] = tariff.ElectricitySecondTierName,
            ["electricityThirdTierName"] = tariff.ElectricityThirdTierName,
            ["electricityFirstRate"] = tariff.ElectricityFirstRate,
            ["electricitySecondRate"] = tariff.ElectricitySecondRate,
            ["electricityThirdRate"] = tariff.ElectricityThirdRate
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["calculationBase"] = calculationBase,
            ["rate"] = rate,
            ["effectiveFrom"] = request.EffectiveFrom,
            ["comment"] = comment,
            ["electricityFirstThreshold"] = electricityTiers.Value?.FirstThreshold,
            ["electricitySecondThreshold"] = electricityTiers.Value?.SecondThreshold,
            ["electricityFirstTierName"] = electricityTiers.Value?.FirstTierName,
            ["electricitySecondTierName"] = electricityTiers.Value?.SecondTierName,
            ["electricityThirdTierName"] = electricityTiers.Value?.ThirdTierName,
            ["electricityFirstRate"] = electricityTiers.Value?.FirstRate,
            ["electricitySecondRate"] = electricityTiers.Value?.SecondRate,
            ["electricityThirdRate"] = electricityTiers.Value?.ThirdRate
        };

        tariff.Name = name;
        tariff.CalculationBase = calculationBase;
        tariff.Rate = rate;
        tariff.EffectiveFrom = request.EffectiveFrom;
        tariff.Comment = comment;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyElectricityTiers(tariff, electricityTiers.Value);

        AddAudit(actorUserId, "dictionary.tariff_updated", "tariff", tariff.Id, $"Изменен тариф {FormatTariffAuditDetails(tariff)}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<TariffDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var tariff = await dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден.");
        }

        tariff.IsArchived = true;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.tariff_archived", "tariff", tariff.Id, $"Архивирован тариф {FormatTariffAuditDetails(tariff)}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> RestoreTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var tariff = await dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден в архиве.");
        }

        if (await dbContext.Tariffs.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == tariff.Name && item.EffectiveFrom == tariff.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Активный тариф с таким названием и датой действия уже существует.");
        }

        tariff.IsArchived = false;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.tariff_restored", "tariff", tariff.Id, $"Восстановлен тариф {FormatTariffAuditDetails(tariff)}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    private async Task<Owner?> FindOwnerOrNullAsync(Guid? ownerId, CancellationToken cancellationToken)
    {
        return ownerId is null
            ? null
            : await dbContext.Owners.SingleOrDefaultAsync(owner => owner.Id == ownerId && !owner.IsArchived, cancellationToken);
    }

    private static DictionaryResult<T>? ValidateArchiveReason<T>(string? reason, out string normalizedReason)
    {
        normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0)
        {
            return DictionaryResult<T>.Failure("dictionary_archive_reason_required", "Укажите причину удаления записи.");
        }

        if (normalizedReason.Length > 1000)
        {
            return DictionaryResult<T>.Failure("dictionary_archive_reason_too_long", "Причина удаления не должна быть длиннее 1000 символов.");
        }

        return null;
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            entityType,
            entityId.ToString(),
            Summary: summary,
            EntityDisplayName: NormalizeAuditDisplayName(summary),
            Reason: reason,
            OldValues: oldValues,
            NewValues: newValues,
            FieldLabels: oldValues is null || newValues is null ? null : DictionaryFieldLabels,
            Metadata: new Dictionary<string, object?>
            {
                ["dictionaryEntityType"] = entityType
            }));
    }

    private static string NormalizeAuditDisplayName(string summary)
    {
        return summary.Trim().TrimEnd('.');
    }

    private static string FormatTariffAuditDetails(Tariff tariff)
    {
        var baseDetails = $"{tariff.Name} с {tariff.EffectiveFrom:dd.MM.yyyy}, база {tariff.CalculationBase}, ставка {tariff.Rate.ToString("0.####", CultureInfo.InvariantCulture)}";
        if (!HasElectricityTiers(tariff))
        {
            return baseDetails;
        }

        return $"{baseDetails}, электричество: {tariff.ElectricityFirstTierName ?? "Порог 1"} до {tariff.ElectricityFirstThreshold!.Value.ToString("0.####", CultureInfo.InvariantCulture)} кВт по {tariff.ElectricityFirstRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}, {tariff.ElectricitySecondTierName ?? "Порог 2"} до {tariff.ElectricitySecondThreshold!.Value.ToString("0.####", CultureInfo.InvariantCulture)} кВт по {tariff.ElectricitySecondRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}, {tariff.ElectricityThirdTierName ?? "Порог 3"} по {tariff.ElectricityThirdRate!.Value.ToString("0.####", CultureInfo.InvariantCulture)}";
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
        var firstTierName = NormalizeOptional(request.ElectricityFirstTierName) ?? "От 0 кВт";
        var secondTierName = NormalizeOptional(request.ElectricitySecondTierName) ?? $"От {firstThreshold.ToString("0.####", CultureInfo.InvariantCulture)} кВт";
        var thirdTierName = NormalizeOptional(request.ElectricityThirdTierName) ?? $"От {secondThreshold.ToString("0.####", CultureInfo.InvariantCulture)} кВт";
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

        return DictionaryResult<ElectricityTierConfig?>.Success(new ElectricityTierConfig(firstThreshold, secondThreshold, firstTierName, secondTierName, thirdTierName, firstRate, secondRate, thirdRate));
    }

    private static void ApplyElectricityTiers(Tariff tariff, ElectricityTierConfig? tiers)
    {
        tariff.ElectricityFirstThreshold = tiers?.FirstThreshold;
        tariff.ElectricitySecondThreshold = tiers?.SecondThreshold;
        tariff.ElectricityFirstTierName = tiers?.FirstTierName;
        tariff.ElectricitySecondTierName = tiers?.SecondTierName;
        tariff.ElectricityThirdTierName = tiers?.ThirdTierName;
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

    private static bool OwnerMatches(Owner owner, string lastName, string firstName, string? middleName, string? phone, string? address, string? meterNotes)
    {
        return StringEquals(owner.LastName, lastName) &&
            StringEquals(owner.FirstName, firstName) &&
            StringEquals(owner.MiddleName, middleName) &&
            StringEquals(owner.Phone, phone) &&
            StringEquals(owner.Address, address) &&
            StringEquals(owner.MeterNotes, meterNotes);
    }

    private static bool GarageMatches(Garage garage, string number, int peopleCount, int floorCount, Guid? ownerId, decimal startingBalance, decimal? initialWaterMeterValue, decimal? initialElectricityMeterValue, string? comment)
    {
        return StringEquals(garage.Number, number) &&
            garage.PeopleCount == peopleCount &&
            garage.FloorCount == floorCount &&
            garage.OwnerId == ownerId &&
            garage.StartingBalance == startingBalance &&
            garage.InitialWaterMeterValue == initialWaterMeterValue &&
            garage.InitialElectricityMeterValue == initialElectricityMeterValue &&
            StringEquals(garage.Comment, comment);
    }

    private static bool SupplierMatches(Supplier supplier, string name, Guid groupId, string? inn, string? legalAddress, string? contactPerson, string? phone, string? email, decimal startingBalance, string? comment)
    {
        return StringEquals(supplier.Name, name) &&
            supplier.GroupId == groupId &&
            StringEquals(supplier.Inn, inn) &&
            StringEquals(supplier.LegalAddress, legalAddress) &&
            StringEquals(supplier.ContactPerson, contactPerson) &&
            StringEquals(supplier.Phone, phone) &&
            StringEquals(supplier.Email, email) &&
            supplier.StartingBalance == startingBalance &&
            StringEquals(supplier.Comment, comment);
    }

    private static bool AccountingTypeMatches(IncomeType accountingType, string name, string? code)
    {
        return StringEquals(accountingType.Name, name) && StringEquals(accountingType.Code, code);
    }

    private static bool AccountingTypeMatches(ExpenseType accountingType, string name, string? code)
    {
        return StringEquals(accountingType.Name, name) && StringEquals(accountingType.Code, code);
    }

    private static bool TariffMatches(Tariff tariff, string name, string calculationBase, decimal rate, DateOnly effectiveFrom, string? comment, ElectricityTierConfig? tiers)
    {
        return StringEquals(tariff.Name, name) &&
            StringEquals(tariff.CalculationBase, calculationBase) &&
            tariff.Rate == rate &&
            tariff.EffectiveFrom == effectiveFrom &&
            StringEquals(tariff.Comment, comment) &&
            tariff.ElectricityFirstThreshold == tiers?.FirstThreshold &&
            tariff.ElectricitySecondThreshold == tiers?.SecondThreshold &&
            StringEquals(tariff.ElectricityFirstTierName, tiers?.FirstTierName) &&
            StringEquals(tariff.ElectricitySecondTierName, tiers?.SecondTierName) &&
            StringEquals(tariff.ElectricityThirdTierName, tiers?.ThirdTierName) &&
            tariff.ElectricityFirstRate == tiers?.FirstRate &&
            tariff.ElectricitySecondRate == tiers?.SecondRate &&
            tariff.ElectricityThirdRate == tiers?.ThirdRate;
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
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
            tariff.ElectricityFirstTierName,
            tariff.ElectricitySecondTierName,
            tariff.ElectricityThirdTierName,
            tariff.ElectricityFirstRate,
            tariff.ElectricitySecondRate,
            tariff.ElectricityThirdRate);
    }

    private sealed record ElectricityTierConfig(
        decimal FirstThreshold,
        decimal SecondThreshold,
        string FirstTierName,
        string SecondTierName,
        string ThirdTierName,
        decimal FirstRate,
        decimal SecondRate,
        decimal ThirdRate);
}
