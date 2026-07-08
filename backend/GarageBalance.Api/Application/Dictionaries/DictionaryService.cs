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
        ["fullName"] = "ФИО",
        ["position"] = "Должность",
        ["status"] = "Статус",
        ["department"] = "Отдел",
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
        ["electricityThirdRate"] = "Цена сверх порога 2",
        ["isRegular"] = "Регулярные платежи",
        ["periodicityMonths"] = "Периодичность",
        ["accrualStartMonth"] = "Учитывать платеж с",
        ["paymentDueDay"] = "День оплаты",
        ["paymentDueMonth"] = "Месяц оплаты",
        ["overdueGraceDays"] = "Перенос долга в просроченный",
        ["isMetered"] = "По счетчику",
        ["hasTieredTariff"] = "Пороговая тарификация",
        ["unitName"] = "Единица измерения",
        ["amount"] = "Сумма",
        ["goal"] = "Цель",
        ["contributionAmount"] = "Сумма взноса",
        ["targetAmount"] = "Сумма сбора",
        ["startsOn"] = "Дата начала",
        ["endsOn"] = "Дата окончания",
        ["appliesToAllGarages"] = "Участники",
        ["isActive"] = "Статус"
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
            var matchedGarages = sqliteGarages
                .Where(garage => GarageMatchesSearch(garage, searchValue))
                .Take(normalizedLimit)
                .ToList();
            return await ToGarageDtosWithBalancesAsync(matchedGarages, cancellationToken);
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
        return await ToGarageDtosWithBalancesAsync(garages, cancellationToken);
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
                .ToList();
            return new PagedResult<GarageDto>(await ToGarageDtosWithBalancesAsync(items, cancellationToken), filteredGarages.Count, normalizedOffset, normalizedLimit);
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
        return new PagedResult<GarageDto>(await ToGarageDtosWithBalancesAsync(garages, cancellationToken), totalCount, normalizedOffset, normalizedLimit);
    }

    private async Task<GarageDto> ToGarageDtoWithBalanceAsync(Garage garage, CancellationToken cancellationToken)
    {
        return (await ToGarageDtosWithBalancesAsync([garage], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<GarageDto>> ToGarageDtosWithBalancesAsync(IReadOnlyList<Garage> garages, CancellationToken cancellationToken)
    {
        if (garages.Count == 0)
        {
            return [];
        }

        var garageIds = garages.Select(garage => garage.Id).ToArray();
        var accrualTotals = await dbContext.Accruals
            .AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && garageIds.Contains(accrual.GarageId))
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        var incomeTotals = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId.HasValue &&
                garageIds.Contains(operation.GarageId.Value))
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);

        return garages
            .Select(garage =>
            {
                var balance = garage.StartingBalance +
                    accrualTotals.GetValueOrDefault(garage.Id) -
                    incomeTotals.GetValueOrDefault(garage.Id);
                balance = Math.Round(balance, 2, MidpointRounding.AwayFromZero);
                return ToGarageDto(garage, balance, Math.Max(balance, 0m));
            })
            .ToList();
    }

    private static bool GarageMatchesSearch(Garage garage, string normalizedSearch)
    {
        return garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool AccountingTypeMatchesSearch(string name, string? code, string normalizedSearch)
    {
        return name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
            (code?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
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
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
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
            return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
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
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
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
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
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
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
    }

    public async Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.SupplierGroups.AsNoTracking().Where(group => includeArchived || !group.IsArchived);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            return (await query
                    .OrderBy(group => group.Name)
                    .ToListAsync(cancellationToken))
                .Where(group => group.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(normalizedLimit)
                .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
                .ToList();
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(group => group.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(group => group.Name)
            .Take(normalizedLimit)
            .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.SupplierGroups.AsNoTracking().Where(group => includeArchived || !group.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var filteredGroups = (await query
                    .OrderBy(group => group.Name)
                    .ToListAsync(cancellationToken))
                .Where(group => group.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var sqliteItems = filteredGroups
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .Select(group => new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived))
                .ToList();

            return new PagedResult<SupplierGroupDto>(sqliteItems, filteredGroups.Count, normalizedOffset, normalizedLimit);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(group => group.Name.ToLower().Contains(normalizedSearch));
        }

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

    public async Task<IReadOnlyList<SupplierContactDto>> GetSupplierContactsAsync(Guid? supplierId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.SupplierContacts.AsNoTracking().Include(contact => contact.Supplier).Where(contact => includeArchived || !contact.IsArchived);
        if (supplierId is not null)
        {
            query = query.Where(contact => contact.SupplierId == supplierId);
        }

        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(contact =>
                contact.FullName.ToLower().Contains(normalizedSearch) ||
                (contact.Position != null && contact.Position.ToLower().Contains(normalizedSearch)) ||
                (contact.Phone != null && contact.Phone.ToLower().Contains(normalizedSearch)) ||
                (contact.Email != null && contact.Email.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(contact => contact.Supplier.Name)
            .ThenBy(contact => contact.FullName)
            .Take(NormalizeListLimit(limit))
            .Select(contact => ToSupplierContactDto(contact))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<SupplierContactDto>> CreateSupplierContactAsync(UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        var contact = new SupplierContact { FullName = request.FullName.Trim(), SupplierId = supplier.Id, Supplier = supplier };
        ApplySupplierContact(contact, request);
        dbContext.SupplierContacts.Add(contact);
        AddAudit(actorUserId, "dictionary.supplier_contact_created", "supplier_contact", contact.Id, $"Создан контакт {contact.FullName} поставщика {supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> UpdateSupplierContactAsync(Guid id, UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var contact = await dbContext.SupplierContacts.Include(item => item.Supplier).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (contact is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Контакт поставщика не найден.");
        }

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        if (SupplierContactMatches(contact, request, supplier.Id))
        {
            return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
        }

        var oldValues = ToSupplierContactAuditValues(contact);
        contact.SupplierId = supplier.Id;
        contact.Supplier = supplier;
        ApplySupplierContact(contact, request);
        contact.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToSupplierContactAuditValues(contact);

        AddAudit(actorUserId, "dictionary.supplier_contact_updated", "supplier_contact", contact.Id, $"Обновлен контакт {contact.FullName} поставщика {supplier.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> ArchiveSupplierContactAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierContactDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var contact = await dbContext.SupplierContacts.Include(item => item.Supplier).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (contact is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Контакт поставщика не найден.");
        }

        contact.IsArchived = true;
        contact.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_contact_archived", "supplier_contact", contact.Id, $"Архивирован контакт {contact.FullName} поставщика {contact.Supplier.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> RestoreSupplierContactAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var contact = await dbContext.SupplierContacts.Include(item => item.Supplier).ThenInclude(supplier => supplier.Group).SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (contact is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Контакт поставщика не найден в архиве.");
        }

        if (contact.Supplier.IsArchived)
        {
            if (contact.Supplier.Group.IsArchived)
            {
                return DictionaryResult<SupplierContactDto>.Failure("supplier_group_not_found", "Сначала восстановите группу поставщика.");
            }

            contact.Supplier.IsArchived = false;
            contact.Supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(actorUserId, "dictionary.supplier_restored", "supplier", contact.Supplier.Id, $"Восстановлен поставщик {contact.Supplier.Name} при восстановлении контакта.");
        }

        contact.IsArchived = false;
        contact.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_contact_restored", "supplier_contact", contact.Id, $"Восстановлен контакт {contact.FullName} поставщика {contact.Supplier.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<IReadOnlyList<StaffDepartmentDto>> GetStaffDepartmentsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        return await dbContext.StaffDepartments.AsNoTracking()
            .Where(department => includeArchived || !department.IsArchived)
            .OrderBy(department => department.Name)
            .Take(NormalizeListLimit(limit))
            .Select(department => new StaffDepartmentDto(department.Id, department.Name, department.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> CreateStaffDepartmentAsync(UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.StaffDepartments.AnyAsync(department => !department.IsArchived && department.Name == name, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_duplicate", "Отдел с таким названием уже существует.");
        }

        var department = new StaffDepartment { Name = name };
        dbContext.StaffDepartments.Add(department);
        AddAudit(actorUserId, "dictionary.staff_department_created", "staff_department", department.Id, $"Создан отдел {department.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> UpdateStaffDepartmentAsync(Guid id, UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await dbContext.StaffDepartments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        var name = request.Name.Trim();
        if (await dbContext.StaffDepartments.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_duplicate", "Отдел с таким названием уже существует.");
        }

        if (StringEquals(department.Name, name))
        {
            return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
        }

        var oldValues = new Dictionary<string, object?> { ["name"] = department.Name };
        var newValues = new Dictionary<string, object?> { ["name"] = name };
        department.Name = name;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.staff_department_updated", "staff_department", department.Id, $"Обновлен отдел {department.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> ArchiveStaffDepartmentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<StaffDepartmentDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var department = await dbContext.StaffDepartments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        if (await dbContext.StaffMembers.AnyAsync(member => member.DepartmentId == id && !member.IsArchived, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_used", "В отделе есть активные сотрудники.");
        }

        department.IsArchived = true;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_department_archived", "staff_department", department.Id, $"Архивирован отдел {department.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> RestoreStaffDepartmentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await dbContext.StaffDepartments.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден в архиве.");
        }

        if (await dbContext.StaffDepartments.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == department.Name, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_duplicate", "Активный отдел с таким названием уже существует.");
        }

        department.IsArchived = false;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_department_restored", "staff_department", department.Id, $"Восстановлен отдел {department.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffMembersAsync(Guid? departmentId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.StaffMembers.AsNoTracking().Include(member => member.Department).Where(member => includeArchived || !member.IsArchived);
        if (departmentId is not null)
        {
            query = query.Where(member => member.DepartmentId == departmentId);
        }

        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(member =>
                member.FullName.ToLower().Contains(normalizedSearch) ||
                member.Department.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(member => member.Department.Name)
            .ThenBy(member => member.FullName)
            .Take(NormalizeListLimit(limit))
            .Select(member => ToStaffMemberDto(member))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<StaffMemberDto>> CreateStaffMemberAsync(UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await dbContext.StaffDepartments.SingleOrDefaultAsync(item => item.Id == request.DepartmentId && !item.IsArchived, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        var member = new StaffMember
        {
            FullName = request.FullName.Trim(),
            DepartmentId = department.Id,
            Department = department,
            Rate = MoneyMath.RoundMoney(request.Rate)
        };

        dbContext.StaffMembers.Add(member);
        AddAudit(actorUserId, "dictionary.staff_member_created", "staff_member", member.Id, $"Создан сотрудник {member.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> UpdateStaffMemberAsync(Guid id, UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var member = await dbContext.StaffMembers.Include(item => item.Department).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (member is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Сотрудник не найден.");
        }

        var department = await dbContext.StaffDepartments.SingleOrDefaultAsync(item => item.Id == request.DepartmentId && !item.IsArchived, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        var fullName = request.FullName.Trim();
        var rate = MoneyMath.RoundMoney(request.Rate);
        if (StringEquals(member.FullName, fullName) && member.DepartmentId == department.Id && member.Rate == rate)
        {
            return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["fullName"] = member.FullName,
            ["department"] = member.Department.Name,
            ["rate"] = member.Rate
        };
        var newValues = new Dictionary<string, object?>
        {
            ["fullName"] = fullName,
            ["department"] = department.Name,
            ["rate"] = rate
        };

        member.FullName = fullName;
        member.DepartmentId = department.Id;
        member.Department = department;
        member.Rate = rate;
        member.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.staff_member_updated", "staff_member", member.Id, $"Обновлен сотрудник {member.FullName}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> ArchiveStaffMemberAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<StaffMemberDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var member = await dbContext.StaffMembers.Include(item => item.Department).SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (member is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Сотрудник не найден.");
        }

        member.IsArchived = true;
        member.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_member_archived", "staff_member", member.Id, $"Архивирован сотрудник {member.FullName}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> RestoreStaffMemberAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var member = await dbContext.StaffMembers.Include(item => item.Department).SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (member is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Сотрудник не найден в архиве.");
        }

        if (member.Department.IsArchived)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_department_not_found", "Сначала восстановите отдел сотрудника.");
        }

        member.IsArchived = false;
        member.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_member_restored", "staff_member", member.Id, $"Восстановлен сотрудник {member.FullName}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.IncomeTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            return (await query
                    .OrderBy(item => item.Name)
                    .ToListAsync(cancellationToken))
                .Where(item => AccountingTypeMatchesSearch(item.Name, item.Code, searchValue))
                .Take(normalizedLimit)
                .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
                .ToList();
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch) || (item.Code != null && item.Code.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(normalizedLimit)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.IncomeTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var filteredTypes = (await query
                    .OrderBy(item => item.Name)
                    .ToListAsync(cancellationToken))
                .Where(item => AccountingTypeMatchesSearch(item.Name, item.Code, searchValue))
                .ToList();
            var sqliteItems = filteredTypes
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
                .ToList();

            return new PagedResult<AccountingTypeDto>(sqliteItems, filteredTypes.Count, normalizedOffset, normalizedLimit);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch) || (item.Code != null && item.Code.ToLower().Contains(normalizedSearch)));
        }

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

    public async Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.ExpenseTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            return (await query
                    .OrderBy(item => item.Name)
                    .ToListAsync(cancellationToken))
                .Where(item => AccountingTypeMatchesSearch(item.Name, item.Code, searchValue))
                .Take(normalizedLimit)
                .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
                .ToList();
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch) || (item.Code != null && item.Code.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(normalizedLimit)
            .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var query = dbContext.ExpenseTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var filteredTypes = (await query
                    .OrderBy(item => item.Name)
                    .ToListAsync(cancellationToken))
                .Where(item => AccountingTypeMatchesSearch(item.Name, item.Code, searchValue))
                .ToList();
            var sqliteItems = filteredTypes
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .Select(item => new AccountingTypeDto(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived))
                .ToList();

            return new PagedResult<AccountingTypeDto>(sqliteItems, filteredTypes.Count, normalizedOffset, normalizedLimit);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch) || (item.Code != null && item.Code.ToLower().Contains(normalizedSearch)));
        }

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

    public async Task<IReadOnlyList<ChargeServiceSettingDto>> GetChargeServiceSettingsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.ChargeServiceSettings.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => ToChargeServiceSettingDto(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> CreateChargeServiceSettingAsync(UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var validation = ValidateChargeServiceSettingRequest(request);
        if (!validation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var linkValidation = await ValidateChargeServiceAccountingLinksAsync(request, cancellationToken);
        if (!linkValidation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(linkValidation.ErrorCode!, linkValidation.ErrorMessage!);
        }

        var name = request.Name.Trim();
        if (await dbContext.ChargeServiceSettings.AnyAsync(item => !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Услуга с таким наименованием уже существует.");
        }

        var setting = new ChargeServiceSetting { Name = name };
        ApplyChargeServiceSetting(setting, request);

        dbContext.ChargeServiceSettings.Add(setting);
        AddAudit(actorUserId, "dictionary.charge_service_created", "charge_service", setting.Id, $"Создана настройка услуги {setting.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> UpdateChargeServiceSettingAsync(Guid id, UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var setting = await dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена.");
        }

        var validation = ValidateChargeServiceSettingRequest(request);
        if (!validation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var linkValidation = await ValidateChargeServiceAccountingLinksAsync(request, cancellationToken);
        if (!linkValidation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(linkValidation.ErrorCode!, linkValidation.ErrorMessage!);
        }

        var name = request.Name.Trim();
        if (await dbContext.ChargeServiceSettings.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Услуга с таким наименованием уже существует.");
        }

        if (ChargeServiceSettingMatches(setting, request))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
        }

        var oldValues = ToChargeServiceAuditValues(setting);
        ApplyChargeServiceSetting(setting, request);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToChargeServiceAuditValues(setting);

        AddAudit(actorUserId, "dictionary.charge_service_updated", "charge_service", setting.Id, $"Изменена настройка услуги {setting.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> ArchiveChargeServiceSettingAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<ChargeServiceSettingDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var setting = await dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена.");
        }

        setting.IsArchived = true;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.charge_service_archived", "charge_service", setting.Id, $"Архивирована настройка услуги {setting.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> RestoreChargeServiceSettingAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var setting = await dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена в архиве.");
        }

        if (await dbContext.ChargeServiceSettings.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == setting.Name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Активная услуга с таким наименованием уже существует.");
        }

        setting.IsArchived = false;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.charge_service_restored", "charge_service", setting.Id, $"Восстановлена настройка услуги {setting.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<IReadOnlyList<IrregularPaymentDto>> GetIrregularPaymentsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.IrregularPayments.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        var items = await query
            .OrderBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .ToListAsync(cancellationToken);
        return await ToIrregularPaymentDtosAsync(items, cancellationToken);
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> CreateIrregularPaymentAsync(UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await dbContext.IrregularPayments.AnyAsync(item => !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_duplicate", "Нерегулярный платеж с таким наименованием уже существует.");
        }

        var payment = new IrregularPayment
        {
            Name = name,
            Amount = MoneyMath.RoundMoney(request.Amount),
            IsActive = request.IsActive
        };

        dbContext.IrregularPayments.Add(payment);
        AddAudit(actorUserId, "dictionary.irregular_payment_created", "irregular_payment", payment.Id, $"Создан нерегулярный платеж {payment.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> UpdateIrregularPaymentAsync(Guid id, UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден.");
        }

        var name = request.Name.Trim();
        if (await dbContext.IrregularPayments.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_duplicate", "Нерегулярный платеж с таким наименованием уже существует.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (StringEquals(payment.Name, name) && payment.Amount == amount && payment.IsActive == request.IsActive)
        {
            return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = payment.Name,
            ["amount"] = payment.Amount,
            ["isActive"] = payment.IsActive
        };
        var newValues = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["amount"] = amount,
            ["isActive"] = request.IsActive
        };

        payment.Name = name;
        payment.Amount = amount;
        payment.IsActive = request.IsActive;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.irregular_payment_updated", "irregular_payment", payment.Id, $"Обновлен нерегулярный платеж {payment.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> SetIrregularPaymentStatusAsync(Guid id, UpdateIrregularPaymentStatusRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден.");
        }

        if (payment.IsActive == request.IsActive)
        {
            return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
        }

        var oldValues = new Dictionary<string, object?> { ["isActive"] = payment.IsActive };
        var newValues = new Dictionary<string, object?> { ["isActive"] = request.IsActive };
        payment.IsActive = request.IsActive;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var actionName = request.IsActive ? "активирован" : "деактивирован";
        AddAudit(actorUserId, "dictionary.irregular_payment_status_changed", "irregular_payment", payment.Id, $"Нерегулярный платеж {payment.Name} {actionName}.", NormalizeOptional(request.Reason), oldValues, newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> ArchiveIrregularPaymentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<IrregularPaymentDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var payment = await dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден.");
        }

        if (await IsIrregularPaymentUsedAsync(payment.Name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_used", "Удаление недоступно: нерегулярный платеж уже используется в платежах или начислениях.");
        }

        payment.IsArchived = true;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.irregular_payment_archived", "irregular_payment", payment.Id, $"Удален нерегулярный платеж {payment.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> RestoreIrregularPaymentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await dbContext.IrregularPayments.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден в архиве.");
        }

        if (await dbContext.IrregularPayments.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == payment.Name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_duplicate", "Активный нерегулярный платеж с таким наименованием уже существует.");
        }

        payment.IsArchived = false;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.irregular_payment_restored", "irregular_payment", payment.Id, $"Восстановлен нерегулярный платеж {payment.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<IReadOnlyList<FeeCampaignDto>> GetFeeCampaignsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var query = dbContext.FeeCampaigns
            .AsNoTracking()
            .Where(item => includeArchived || !item.IsArchived);
        var normalizedSearch = NormalizeSearch(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                (item.Goal != null && item.Goal.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(item => item.StartsOn)
            .ThenBy(item => item.Name)
            .Take(NormalizeListLimit(limit))
            .Select(item => new FeeCampaignDto(
                item.Id,
                item.Name,
                item.IncomeTypeId,
                item.IncomeType.Name,
                item.Goal,
                item.ContributionAmount,
                item.TargetAmount,
                item.StartsOn,
                item.EndsOn,
                item.AppliesToAllGarages,
                item.ParticipantGarages
                    .OrderBy(participant => participant.Garage.Number)
                    .Select(participant => participant.GarageId)
                    .ToList(),
                item.OverdueGraceDays,
                item.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<DictionaryResult<FeeCampaignDto>> CreateFeeCampaignAsync(UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateFeeCampaignRequest(request) is { } validationError)
        {
            return validationError;
        }

        var name = request.Name.Trim();
        if (await dbContext.FeeCampaigns.AnyAsync(item => !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("income_type_not_found", "Вид поступления для сбора не найден.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        var campaign = new FeeCampaign { Name = name };
        ApplyFeeCampaign(campaign, request);
        campaign.IncomeType = incomeType;
        SyncFeeCampaignParticipants(campaign, participants.Value!);

        dbContext.FeeCampaigns.Add(campaign);
        AddAudit(actorUserId, "dictionary.fee_campaign_created", "fee_campaign", campaign.Id, $"Объявлен сбор {campaign.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> UpdateFeeCampaignAsync(Guid id, UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateFeeCampaignRequest(request) is { } validationError)
        {
            return validationError;
        }

        var campaign = await dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        var name = request.Name.Trim();
        if (await dbContext.FeeCampaigns.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("income_type_not_found", "Вид поступления для сбора не найден.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        if (FeeCampaignMatches(campaign, request, participants.Value!))
        {
            return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
        }

        var oldValues = ToFeeCampaignAuditValues(campaign);
        ApplyFeeCampaign(campaign, request);
        campaign.IncomeType = incomeType;
        SyncFeeCampaignParticipants(campaign, participants.Value!);
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToFeeCampaignAuditValues(campaign);

        AddAudit(actorUserId, "dictionary.fee_campaign_updated", "fee_campaign", campaign.Id, $"Изменен сбор {campaign.Name}.", oldValues: oldValues, newValues: newValues);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> ArchiveFeeCampaignAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<FeeCampaignDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var campaign = await dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        campaign.IsArchived = true;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.fee_campaign_archived", "fee_campaign", campaign.Id, $"Архивирован сбор {campaign.Name}.", archiveReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> RestoreFeeCampaignAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var campaign = await dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
            .SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден в архиве.");
        }

        if (await dbContext.FeeCampaigns.AnyAsync(item => item.Id != id && !item.IsArchived && item.Name == campaign.Name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        campaign.IsArchived = false;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.fee_campaign_restored", "fee_campaign", campaign.Id, $"Восстановлен сбор {campaign.Name}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
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

    private static DictionaryResult<object> ValidateChargeServiceSettingRequest(UpsertChargeServiceSettingRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return DictionaryResult<object>.Failure("charge_service_name_required", "Укажите наименование услуги.");
        }

        if (request.IsRegular)
        {
            if (!request.PeriodicityMonths.HasValue || request.PeriodicityMonths.Value <= 0)
            {
                return DictionaryResult<object>.Failure("charge_service_periodicity_required", "Для регулярной услуги укажите периодичность.");
            }

            if (!request.AccrualStartMonth.HasValue)
            {
                return DictionaryResult<object>.Failure("charge_service_accrual_start_month_required", "Для регулярной услуги укажите месяц начала учета.");
            }
        }

        if (request.AccrualStartMonth is < 1 or > 12 || request.PaymentDueMonth is < 1 or > 12)
        {
            return DictionaryResult<object>.Failure("charge_service_month_invalid", "Месяц должен быть от 1 до 12.");
        }

        if (request.PaymentDueDay.HasValue != request.PaymentDueMonth.HasValue)
        {
            return DictionaryResult<object>.Failure("charge_service_payment_date_incomplete", "Для даты оплаты заполните и день, и месяц.");
        }

        if (request.PaymentDueDay.HasValue && request.PaymentDueMonth.HasValue)
        {
            var maxDay = DateTime.DaysInMonth(2026, request.PaymentDueMonth.Value);
            if (request.PaymentDueDay.Value < 1 || request.PaymentDueDay.Value > maxDay)
            {
                return DictionaryResult<object>.Failure("charge_service_payment_day_invalid", $"В выбранном месяце нельзя указать день больше {maxDay}.");
            }
        }

        if (request.HasTieredTariff && !request.IsMetered)
        {
            return DictionaryResult<object>.Failure("charge_service_tiered_requires_meter", "Пороговая тарификация доступна только для услуг по счетчику.");
        }

        return DictionaryResult<object>.Success(new object());
    }

    private async Task<DictionaryResult<object>> ValidateChargeServiceAccountingLinksAsync(UpsertChargeServiceSettingRequest request, CancellationToken cancellationToken)
    {
        if (!request.IsRegular)
        {
            return DictionaryResult<object>.Success(new object());
        }

        if (request.IncomeTypeId.HasValue != request.TariffId.HasValue)
        {
            return DictionaryResult<object>.Failure("charge_service_regular_link_incomplete", "Для регулярной услуги заполните и вид начисления, и тариф.");
        }

        if (!request.IncomeTypeId.HasValue)
        {
            return DictionaryResult<object>.Success(new object());
        }

        var incomeType = await dbContext.IncomeTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId.Value && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<object>.Failure("charge_service_income_type_not_found", "Вид начисления для услуги не найден.");
        }

        var tariff = await dbContext.Tariffs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.TariffId!.Value && !item.IsArchived, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_not_found", "Тариф для услуги не найден.");
        }

        if (!IsIncomeTypeCompatibleWithTariff(incomeType.Code, tariff.CalculationBase))
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_mismatch", "Выбранный тариф не подходит для вида начисления услуги.");
        }

        return DictionaryResult<object>.Success(new object());
    }

    private static void ApplyChargeServiceSetting(ChargeServiceSetting setting, UpsertChargeServiceSettingRequest request)
    {
        setting.Name = request.Name.Trim();
        setting.IsRegular = request.IsRegular;
        setting.PeriodicityMonths = request.IsRegular ? request.PeriodicityMonths : null;
        setting.AccrualStartMonth = request.IsRegular ? request.AccrualStartMonth : null;
        setting.PaymentDueDay = request.PaymentDueDay;
        setting.PaymentDueMonth = request.PaymentDueMonth;
        setting.OverdueGraceDays = request.OverdueGraceDays;
        setting.IncomeTypeId = request.IsRegular ? request.IncomeTypeId : null;
        setting.TariffId = request.IsRegular ? request.TariffId : null;
        setting.IsMetered = request.IsMetered;
        setting.HasTieredTariff = request.HasTieredTariff;
        setting.UnitName = NormalizeOptional(request.UnitName);
    }

    private static bool ChargeServiceSettingMatches(ChargeServiceSetting setting, UpsertChargeServiceSettingRequest request)
    {
        return StringEquals(setting.Name, request.Name.Trim()) &&
            setting.IsRegular == request.IsRegular &&
            setting.PeriodicityMonths == (request.IsRegular ? request.PeriodicityMonths : null) &&
            setting.AccrualStartMonth == (request.IsRegular ? request.AccrualStartMonth : null) &&
            setting.PaymentDueDay == request.PaymentDueDay &&
            setting.PaymentDueMonth == request.PaymentDueMonth &&
            setting.OverdueGraceDays == request.OverdueGraceDays &&
            setting.IncomeTypeId == (request.IsRegular ? request.IncomeTypeId : null) &&
            setting.TariffId == (request.IsRegular ? request.TariffId : null) &&
            setting.IsMetered == request.IsMetered &&
            setting.HasTieredTariff == request.HasTieredTariff &&
            StringEquals(setting.UnitName, NormalizeOptional(request.UnitName));
    }

    private static bool IsIncomeTypeCompatibleWithTariff(string? incomeTypeCode, string calculationBase)
    {
        return NormalizeOptional(incomeTypeCode)?.Trim().ToLowerInvariant() switch
        {
            "water" => calculationBase == TariffCalculationBases.MeterWater,
            "trash" => calculationBase == TariffCalculationBases.People,
            "electricity" => calculationBase == TariffCalculationBases.MeterElectricity,
            "membership" or "target" or "entry" or "connection" => calculationBase == TariffCalculationBases.Fixed,
            _ => true
        };
    }

    private static Dictionary<string, object?> ToChargeServiceAuditValues(ChargeServiceSetting setting)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = setting.Name,
            ["isRegular"] = setting.IsRegular,
            ["periodicityMonths"] = setting.PeriodicityMonths,
            ["accrualStartMonth"] = setting.AccrualStartMonth,
            ["paymentDueDay"] = setting.PaymentDueDay,
            ["paymentDueMonth"] = setting.PaymentDueMonth,
            ["overdueGraceDays"] = setting.OverdueGraceDays,
            ["incomeTypeId"] = setting.IncomeTypeId,
            ["tariffId"] = setting.TariffId,
            ["isMetered"] = setting.IsMetered,
            ["hasTieredTariff"] = setting.HasTieredTariff,
            ["unitName"] = setting.UnitName
        };
    }

    private static DictionaryResult<FeeCampaignDto>? ValidateFeeCampaignRequest(UpsertFeeCampaignRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_name_required", "Наименование сбора обязательно.");
        }

        if (request.IncomeTypeId == Guid.Empty)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_income_type_required", "Для сбора нужно выбрать вид поступления.");
        }

        if (MoneyMath.RoundMoney(request.ContributionAmount) < 0m)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_contribution_amount_invalid", "Сумма взноса не может быть отрицательной.");
        }

        if (MoneyMath.RoundMoney(request.TargetAmount) <= 0m)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_target_amount_invalid", "Сумма сбора должна быть больше нуля.");
        }

        if (request.OverdueGraceDays is < 0 or > 366)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_overdue_days_invalid", "Перенос долга в просроченный должен быть в диапазоне от 0 до 366 дней.");
        }

        if (request.EndsOn.HasValue && request.EndsOn.Value < request.StartsOn)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_period_invalid", "Дата окончания сбора не может быть раньше даты начала.");
        }

        if (!request.AppliesToAllGarages)
        {
            var participantIds = request.ParticipantGarageIds ?? [];
            if (participantIds.Count == 0)
            {
                return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_participants_required", "Для сбора не по всем гаражам нужно выбрать хотя бы один гараж.");
            }

            if (participantIds.Any(id => id == Guid.Empty) || participantIds.Distinct().Count() != participantIds.Count)
            {
                return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_participants_invalid", "Список участников сбора содержит некорректные или повторяющиеся гаражи.");
            }
        }

        return null;
    }

    private async Task<DictionaryResult<IReadOnlyList<Garage>>> ResolveFeeCampaignParticipantsAsync(UpsertFeeCampaignRequest request, CancellationToken cancellationToken)
    {
        if (request.AppliesToAllGarages)
        {
            return DictionaryResult<IReadOnlyList<Garage>>.Success([]);
        }

        var participantIds = request.ParticipantGarageIds ?? [];
        var garages = await dbContext.Garages
            .Where(garage => participantIds.Contains(garage.Id) && !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);

        if (garages.Count != participantIds.Count)
        {
            return DictionaryResult<IReadOnlyList<Garage>>.Failure("fee_campaign_participant_garage_not_found", "Один из выбранных гаражей не найден или архивирован.");
        }

        return DictionaryResult<IReadOnlyList<Garage>>.Success(garages);
    }

    private static void ApplyFeeCampaign(FeeCampaign campaign, UpsertFeeCampaignRequest request)
    {
        campaign.Name = request.Name.Trim();
        campaign.IncomeTypeId = request.IncomeTypeId;
        campaign.Goal = NormalizeOptional(request.Goal);
        campaign.ContributionAmount = MoneyMath.RoundMoney(request.ContributionAmount);
        campaign.TargetAmount = MoneyMath.RoundMoney(request.TargetAmount);
        campaign.StartsOn = request.StartsOn;
        campaign.EndsOn = request.EndsOn;
        campaign.AppliesToAllGarages = request.AppliesToAllGarages;
        campaign.OverdueGraceDays = request.OverdueGraceDays;
    }

    private static void SyncFeeCampaignParticipants(FeeCampaign campaign, IReadOnlyList<Garage> participants)
    {
        campaign.ParticipantGarages.Clear();
        foreach (var garage in participants)
        {
            campaign.ParticipantGarages.Add(new FeeCampaignGarage
            {
                FeeCampaign = campaign,
                FeeCampaignId = campaign.Id,
                Garage = garage,
                GarageId = garage.Id
            });
        }
    }

    private static bool FeeCampaignMatches(FeeCampaign campaign, UpsertFeeCampaignRequest request, IReadOnlyList<Garage> participants)
    {
        var currentParticipantIds = campaign.ParticipantGarages
            .Select(participant => participant.GarageId)
            .Order()
            .ToArray();
        var nextParticipantIds = participants
            .Select(garage => garage.Id)
            .Order()
            .ToArray();

        return StringEquals(campaign.Name, request.Name.Trim()) &&
            campaign.IncomeTypeId == request.IncomeTypeId &&
            StringEquals(campaign.Goal, NormalizeOptional(request.Goal)) &&
            campaign.ContributionAmount == MoneyMath.RoundMoney(request.ContributionAmount) &&
            campaign.TargetAmount == MoneyMath.RoundMoney(request.TargetAmount) &&
            campaign.StartsOn == request.StartsOn &&
            campaign.EndsOn == request.EndsOn &&
            campaign.AppliesToAllGarages == request.AppliesToAllGarages &&
            campaign.OverdueGraceDays == request.OverdueGraceDays &&
            currentParticipantIds.SequenceEqual(nextParticipantIds);
    }

    private static Dictionary<string, object?> ToFeeCampaignAuditValues(FeeCampaign campaign)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = campaign.Name,
            ["incomeTypeId"] = campaign.IncomeTypeId,
            ["incomeTypeName"] = campaign.IncomeType?.Name,
            ["goal"] = campaign.Goal,
            ["contributionAmount"] = campaign.ContributionAmount,
            ["targetAmount"] = campaign.TargetAmount,
            ["startsOn"] = campaign.StartsOn,
            ["endsOn"] = campaign.EndsOn,
            ["appliesToAllGarages"] = campaign.AppliesToAllGarages,
            ["participantGarageIds"] = string.Join(", ", campaign.ParticipantGarages
                .Select(participant => participant.GarageId)
                .Order()),
            ["participantGarageNumbers"] = string.Join(", ", campaign.ParticipantGarages
                .Select(participant => participant.Garage?.Number)
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .Order(StringComparer.Ordinal)),
            ["overdueGraceDays"] = campaign.OverdueGraceDays
        };
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

    private static void ApplySupplierContact(SupplierContact contact, UpsertSupplierContactRequest request)
    {
        contact.FullName = request.FullName.Trim();
        contact.Position = NormalizeOptional(request.Position);
        contact.Phone = NormalizeOptional(request.Phone);
        contact.Email = NormalizeOptional(request.Email);
        contact.Status = request.Status.Trim();
        contact.Comment = NormalizeOptional(request.Comment);
    }

    private static bool SupplierContactMatches(SupplierContact contact, UpsertSupplierContactRequest request, Guid supplierId)
    {
        return contact.SupplierId == supplierId &&
            StringEquals(contact.FullName, request.FullName.Trim()) &&
            StringEquals(contact.Position, NormalizeOptional(request.Position)) &&
            StringEquals(contact.Phone, NormalizeOptional(request.Phone)) &&
            StringEquals(contact.Email, NormalizeOptional(request.Email)) &&
            StringEquals(contact.Status, request.Status.Trim()) &&
            StringEquals(contact.Comment, NormalizeOptional(request.Comment));
    }

    private static Dictionary<string, object?> ToSupplierContactAuditValues(SupplierContact contact)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = contact.Supplier.Name,
            ["fullName"] = contact.FullName,
            ["position"] = contact.Position,
            ["phone"] = contact.Phone,
            ["email"] = contact.Email,
            ["status"] = contact.Status,
            ["comment"] = contact.Comment
        };
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

    private async Task<bool> IsIrregularPaymentUsedAsync(string name, CancellationToken cancellationToken)
    {
        var incomeTypeIds = await dbContext.IncomeTypes.AsNoTracking()
            .Where(item => item.Name == name)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (incomeTypeIds.Count == 0)
        {
            return false;
        }

        return await dbContext.Accruals.AsNoTracking()
                .AnyAsync(accrual => !accrual.IsCanceled && incomeTypeIds.Contains(accrual.IncomeTypeId), cancellationToken)
            || await dbContext.FinancialOperations.AsNoTracking()
                .AnyAsync(operation => !operation.IsCanceled && operation.IncomeTypeId.HasValue && incomeTypeIds.Contains(operation.IncomeTypeId.Value), cancellationToken);
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

    private static GarageDto ToGarageDto(Garage garage, decimal? balance = null, decimal? overdueDebt = null)
    {
        var calculatedBalance = balance ?? garage.StartingBalance;
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
            garage.IsArchived,
            calculatedBalance,
            overdueDebt ?? Math.Max(calculatedBalance, 0m));
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

    private static SupplierContactDto ToSupplierContactDto(SupplierContact contact)
    {
        return new SupplierContactDto(
            contact.Id,
            contact.SupplierId,
            contact.Supplier.Name,
            contact.FullName,
            contact.Position,
            contact.Phone,
            contact.Email,
            contact.Status,
            contact.Comment,
            contact.IsArchived);
    }

    private static StaffMemberDto ToStaffMemberDto(StaffMember member)
    {
        return new StaffMemberDto(
            member.Id,
            member.FullName,
            member.DepartmentId,
            member.Department.Name,
            member.Rate,
            member.IsArchived);
    }

    private static ChargeServiceSettingDto ToChargeServiceSettingDto(ChargeServiceSetting setting)
    {
        return new ChargeServiceSettingDto(
            setting.Id,
            setting.Name,
            setting.IsRegular,
            setting.PeriodicityMonths,
            setting.AccrualStartMonth,
            setting.PaymentDueDay,
            setting.PaymentDueMonth,
            setting.OverdueGraceDays,
            setting.IncomeTypeId,
            setting.TariffId,
            setting.IsMetered,
            setting.HasTieredTariff,
            setting.UnitName,
            setting.IsArchived);
    }

    private async Task<IReadOnlyList<IrregularPaymentDto>> ToIrregularPaymentDtosAsync(IReadOnlyList<IrregularPayment> payments, CancellationToken cancellationToken)
    {
        var result = new List<IrregularPaymentDto>(payments.Count);
        foreach (var payment in payments)
        {
            result.Add(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
        }

        return result;
    }

    private async Task<IrregularPaymentDto> ToIrregularPaymentDtoAsync(IrregularPayment payment, CancellationToken cancellationToken)
    {
        return new IrregularPaymentDto(
            payment.Id,
            payment.Name,
            payment.Amount,
            payment.IsActive,
            payment.IsArchived,
            await IsIrregularPaymentUsedAsync(payment.Name, cancellationToken));
    }

    private static FeeCampaignDto ToFeeCampaignDto(FeeCampaign campaign)
    {
        return new FeeCampaignDto(
            campaign.Id,
            campaign.Name,
            campaign.IncomeTypeId,
            campaign.IncomeType?.Name ?? string.Empty,
            campaign.Goal,
            campaign.ContributionAmount,
            campaign.TargetAmount,
            campaign.StartsOn,
            campaign.EndsOn,
            campaign.AppliesToAllGarages,
            campaign.ParticipantGarages
                .OrderBy(participant => participant.Garage?.Number)
                .Select(participant => participant.GarageId)
                .ToArray(),
            campaign.OverdueGraceDays,
            campaign.IsArchived);
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
