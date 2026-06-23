using GarageBalance.Api.Application.Common;
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

    public async Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.Garages.AsNoTracking().Include(garage => garage.Owner).Where(garage => !garage.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        var garages = await query
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        if (normalizedSearch is not null)
        {
            garages = garages
                .Where(garage =>
                    garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return garages.Take(ListLimit).Select(garage => ToGarageDto(garage)).ToList();
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
            StartingBalance = MoneyMath.RoundMoney(request.StartingBalance),
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
        garage.StartingBalance = MoneyMath.RoundMoney(request.StartingBalance);
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

    public async Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.IncomeTypes.AsNoTracking()
            .Where(item => !item.IsArchived)
            .OrderBy(item => item.Name)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExpenseTypes.AsNoTracking()
            .Where(item => !item.IsArchived)
            .OrderBy(item => item.Name)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken)
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
            .Take(ListLimit)
            .Select(item => ToTariffDto(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<TariffDto>> CreateTariffAsync(UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var calculationBase = request.CalculationBase.Trim();
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

        dbContext.Tariffs.Add(tariff);
        AddAudit(actorUserId, "dictionary.tariff_created", "tariff", tariff.Id, $"Создан тариф {tariff.Name} с {tariff.EffectiveFrom:dd.MM.yyyy}.");
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

        AddAudit(actorUserId, "dictionary.tariff_archived", "tariff", tariff.Id, $"Архивирован тариф {tariff.Name} с {tariff.EffectiveFrom:dd.MM.yyyy}.");
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
            tariff.IsArchived);
    }
}
