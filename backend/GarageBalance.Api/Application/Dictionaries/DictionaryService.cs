using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed class DictionaryService(
    IOwnerRepository ownerRepository,
    IGarageRepository garageRepository,
    ISupplierGroupRepository supplierGroupRepository,
    ISupplierRepository supplierRepository,
    ISupplierContactRepository supplierContactRepository,
    IStaffDepartmentRepository staffDepartmentRepository,
    IStaffMemberRepository staffMemberRepository,
    IIncomeTypeRepository incomeTypeRepository,
    IExpenseTypeRepository expenseTypeRepository,
    ITariffRepository tariffRepository,
    IIrregularPaymentRepository irregularPaymentRepository,
    IChargeServiceSettingRepository chargeServiceSettingRepository,
    IFeeCampaignRepository feeCampaignRepository,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter) : IDictionaryService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const string OtherIncomeIncomeTypeCode = "other_income";
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
        ["service"] = "Услуга",
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
        ["electricityTiers"] = "Ступени тарифа электроэнергии",
        ["isRegular"] = "Регулярные платежи",
        ["periodicityMonths"] = "Периодичность",
        ["accrualStartMonth"] = "Учитывать платеж с",
        ["paymentDueDay"] = "День оплаты",
        ["paymentDueMonth"] = "Месяц оплаты",
        ["overdueGraceDays"] = "Перенос долга в просроченный",
        ["isMetered"] = "По счетчику",
        ["hasTieredTariff"] = "Пороговая тарификация",
        ["incomeTypeId"] = "Вид поступления",
        ["tariffId"] = "Тариф",
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

    public async Task<IReadOnlyList<OwnerDto>> GetOwnersAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var owners = await ownerRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return owners.Select(ToOwnerDto).ToList();
    }

    public async Task<PagedResult<OwnerDto>> GetOwnersPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await ownerRepository.GetPageAsync(normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<OwnerDto>(page.Items.Select(ToOwnerDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
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

        ownerRepository.Add(owner);
        AddAudit(actorUserId, "dictionary.owner_created", "owner", owner.Id, $"Создан владелец {owner.FullName}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> UpdateOwnerAsync(Guid id, UpsertOwnerRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.FindActiveAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> ArchiveOwnerAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<OwnerDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var owner = await ownerRepository.FindActiveAsync(id, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден.");
        }

        owner.IsArchived = true;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_archived", "owner", owner.Id, $"Архивирован владелец {owner.FullName}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<DictionaryResult<OwnerDto>> RestoreOwnerAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.FindArchivedWithGaragesAsync(id, cancellationToken);
        if (owner is null)
        {
            return DictionaryResult<OwnerDto>.Failure("owner_not_found", "Владелец не найден в архиве.");
        }

        owner.IsArchived = false;
        owner.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.owner_restored", "owner", owner.Id, $"Восстановлен владелец {owner.FullName}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OwnerDto>.Success(ToOwnerDto(owner));
    }

    public async Task<IReadOnlyList<GarageDto>> GetGaragesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedLimit = NormalizeListLimit(limit);
        var garages = await garageRepository.GetListAsync(normalizedSearch, includeArchived, normalizedLimit, cancellationToken);
        return await ToGarageDtosWithBalancesAsync(garages, cancellationToken);
    }

    public async Task<PagedResult<GarageDto>> GetGaragesPageAsync(string? search, int? offset, int? limit, string? sortBy, string? sortDirection, CancellationToken cancellationToken, bool includeArchived = false, bool debtorsOnly = false, string? number = null, int? peopleCountMin = null, int? peopleCountMax = null, int? floorCountMin = null, int? floorCountMax = null)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var normalizedSortBy = sortBy?.Trim() switch { "peopleCount" => "peopleCount", "floorCount" => "floorCount", "owner" => "owner", "phone" => "phone", "overdueDebt" => "overdueDebt", _ => "number" };
        var sortDescending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        var filters = new GarageColumnFilters(NormalizeSearch(number), peopleCountMin, peopleCountMax, floorCountMin, floorCountMax);
        var page = await garageRepository.GetPageAsync(normalizedSearch, filters, includeArchived, debtorsOnly, normalizedOffset, normalizedLimit, normalizedSortBy, sortDescending, cancellationToken);
        return new PagedResult<GarageDto>(await ToGarageDtosWithBalancesAsync(page.Items, cancellationToken), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    private async Task<GarageDto> ToGarageDtoWithBalanceAsync(Garage garage, CancellationToken cancellationToken)
    {
        var totals = await garageRepository.GetBalanceTotalsAsync([garage.Id], cancellationToken);
        var balance = garage.StartingBalance +
            totals.AccrualTotals.GetValueOrDefault(garage.Id) -
            totals.IncomeTotals.GetValueOrDefault(garage.Id);
        balance = Math.Round(balance, 2, MidpointRounding.AwayFromZero);
        var unallocatedIncome = totals.IncomeTotals.GetValueOrDefault(garage.Id) -
            totals.AllocatedIncomeTotals.GetValueOrDefault(garage.Id);
        var overdueDebt = garage.StartingBalance +
            totals.OverdueAccrualTotals.GetValueOrDefault(garage.Id) -
            unallocatedIncome;
        overdueDebt = Math.Round(Math.Max(overdueDebt, 0m), 2, MidpointRounding.AwayFromZero);
        return ToGarageDto(garage, balance, overdueDebt);
    }

    private async Task<IReadOnlyList<GarageDto>> ToGarageDtosWithBalancesAsync(IReadOnlyList<GarageListItemData> garages, CancellationToken cancellationToken)
    {
        if (garages.Count == 0)
        {
            return [];
        }

        var garageIds = garages.Select(garage => garage.Id).ToArray();
        var totals = await garageRepository.GetBalanceTotalsAsync(garageIds, cancellationToken);

        return garages
            .Select(garage =>
            {
                var balance = garage.StartingBalance +
                    totals.AccrualTotals.GetValueOrDefault(garage.Id) -
                    totals.IncomeTotals.GetValueOrDefault(garage.Id);
                balance = Math.Round(balance, 2, MidpointRounding.AwayFromZero);
                var unallocatedIncome = totals.IncomeTotals.GetValueOrDefault(garage.Id) -
                    totals.AllocatedIncomeTotals.GetValueOrDefault(garage.Id);
                var overdueDebt = garage.StartingBalance +
                    totals.OverdueAccrualTotals.GetValueOrDefault(garage.Id) -
                    unallocatedIncome;
                overdueDebt = Math.Round(Math.Max(overdueDebt, 0m), 2, MidpointRounding.AwayFromZero);
                return ToGarageDto(garage, balance, overdueDebt);
            })
            .ToList();
    }

    private static AccountingTypeDto ToAccountingTypeDto(IncomeType item) =>
        new(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived);

    private static AccountingTypeDto ToAccountingTypeDto(ExpenseType item) =>
        new(item.Id, item.Name, item.Code, item.IsSystem, item.IsArchived);

    public async Task<DictionaryResult<GarageDto>> CreateGarageAsync(UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var number = request.Number.Trim();
        if (await garageRepository.ActiveNumberExistsAsync(null, number, cancellationToken))
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

        garageRepository.Add(garage);
        AddAudit(actorUserId, "dictionary.garage_created", "garage", garage.Id, $"Создан гараж N {garage.Number}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
    }

    public async Task<DictionaryResult<GarageDto>> UpdateGarageAsync(Guid id, UpsertGarageRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindActiveWithOwnerAsync(id, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        var number = request.Number.Trim();
        if (await garageRepository.ActiveNumberExistsAsync(id, number, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
    }

    public async Task<DictionaryResult<GarageDto>> ArchiveGarageAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<GarageDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(id, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        garage.IsArchived = true;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_archived", "garage", garage.Id, $"Архивирован гараж N {garage.Number}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
    }

    public async Task<DictionaryResult<GarageDto>> RestoreGarageAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindArchivedWithOwnerAsync(id, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<GarageDto>.Failure("garage_not_found", "Гараж не найден в архиве.");
        }

        if (await garageRepository.ActiveNumberExistsAsync(id, garage.Number, cancellationToken))
        {
            return DictionaryResult<GarageDto>.Failure("garage_number_duplicate", "Активный гараж с таким номером уже существует.");
        }

        garage.IsArchived = false;
        garage.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.garage_restored", "garage", garage.Id, $"Восстановлен гараж N {garage.Number}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<GarageDto>.Success(await ToGarageDtoWithBalanceAsync(garage, cancellationToken));
    }

    public async Task<IReadOnlyList<SupplierGroupDto>> GetSupplierGroupsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedLimit = NormalizeListLimit(limit);
        var groups = await supplierGroupRepository.GetListAsync(normalizedSearch, includeArchived, normalizedLimit, cancellationToken);
        return groups.Select(ToSupplierGroupDto).ToList();
    }

    public async Task<PagedResult<SupplierGroupDto>> GetSupplierGroupsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await supplierGroupRepository.GetPageAsync(normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<SupplierGroupDto>(page.Items.Select(ToSupplierGroupDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<SupplierGroupDto>> CreateSupplierGroupAsync(UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await supplierGroupRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Группа поставщиков с таким названием уже существует.");
        }

        var group = new SupplierGroup { Name = name };
        supplierGroupRepository.Add(group);
        AddAudit(actorUserId, "dictionary.supplier_group_created", "supplier_group", group.Id, $"Создана группа поставщиков {group.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> UpdateSupplierGroupAsync(Guid id, UpsertSupplierGroupRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await supplierGroupRepository.FindActiveAsync(id, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Группа поставщиков не найдена.");
        }

        if (group.IsSystem)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_system", "Системную группу поставщиков нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await supplierGroupRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> ArchiveSupplierGroupAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierGroupDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var group = await supplierGroupRepository.FindActiveAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<DictionaryResult<SupplierGroupDto>> RestoreSupplierGroupAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await supplierGroupRepository.FindArchivedAsync(id, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_not_found", "Группа поставщиков не найдена в архиве.");
        }

        if (await supplierGroupRepository.ActiveDuplicateExistsAsync(id, group.Name, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure("supplier_group_duplicate", "Активная группа поставщиков с таким названием уже существует.");
        }

        group.IsArchived = false;
        group.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_group_restored", "supplier_group", group.Id, $"Восстановлена группа поставщиков {group.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierGroupDto>.Success(new SupplierGroupDto(group.Id, group.Name, group.IsSystem, group.IsArchived));
    }

    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(Guid? groupId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var suppliers = await supplierRepository.GetListAsync(groupId, normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        var debtTotals = await supplierRepository.GetDebtTotalsAsync(suppliers.Select(item => item.Id).ToArray(), cancellationToken);
        return suppliers.Select(item => ToSupplierDto(item, debt: debtTotals.GetValueOrDefault(item.Id, item.StartingBalance))).ToList();
    }

    public async Task<PagedResult<SupplierDto>> GetSuppliersPageAsync(Guid? groupId, string? search, int? offset, int? limit, string? sortBy, string? sortDirection, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var normalizedSortBy = sortBy?.Trim() switch
        {
            "name" => "name",
            "debt" => "debt",
            "contactPerson" => "contactPerson",
            "phone" => "phone",
            "email" => "email",
            _ => "service"
        };
        var sortDescending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        var page = await supplierRepository.GetPageAsync(groupId, normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, normalizedSortBy, sortDescending, cancellationToken);
        var debtTotals = await supplierRepository.GetDebtTotalsAsync(page.Items.Select(item => item.Supplier.Id).ToArray(), cancellationToken);
        return new PagedResult<SupplierDto>(page.Items.Select(item => ToSupplierDto(item.Supplier, item.PrimaryContact, debtTotals.GetValueOrDefault(item.Supplier.Id, item.Supplier.StartingBalance))).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var group = await supplierGroupRepository.FindActiveAsync(request.GroupId, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Группа поставщика не найдена.");
        }

        var chargeService = request.ChargeServiceSettingId.HasValue
            ? await chargeServiceSettingRepository.FindActiveAsync(request.ChargeServiceSettingId.Value, cancellationToken)
            : null;
        if (request.ChargeServiceSettingId.HasValue && chargeService is null)
        {
            return DictionaryResult<SupplierDto>.Failure("charge_service_not_found", "Услуга из раздела тарифов не найдена.");
        }

        var name = request.Name.Trim();
        if (await supplierRepository.ActiveDuplicateExistsAsync(null, group.Id, name, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
        }

        var supplier = new Supplier
        {
            Name = name,
            GroupId = group.Id,
            Group = group,
            ChargeServiceSettingId = chargeService?.Id,
            ChargeServiceSetting = chargeService,
            Inn = NormalizeOptional(request.Inn),
            LegalAddress = NormalizeOptional(request.LegalAddress),
            ContactPerson = NormalizeOptional(request.ContactPerson),
            Phone = NormalizeOptional(request.Phone),
            Email = NormalizeOptional(request.Email),
            StartingBalance = MoneyMath.RoundMoney(request.StartingBalance),
            Comment = NormalizeOptional(request.Comment)
        };

        supplierRepository.Add(supplier);
        AddAudit(actorUserId, "dictionary.supplier_created", "supplier", supplier.Id, $"Создан поставщик {supplier.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> UpdateSupplierAsync(Guid id, UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await supplierRepository.FindActiveWithGroupAsync(id, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        var group = await supplierGroupRepository.FindActiveAsync(request.GroupId, cancellationToken);
        if (group is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Группа поставщика не найдена.");
        }

        var chargeService = request.ChargeServiceSettingId == supplier.ChargeServiceSettingId
            ? supplier.ChargeServiceSetting
            : request.ChargeServiceSettingId.HasValue
                ? await chargeServiceSettingRepository.FindActiveAsync(request.ChargeServiceSettingId.Value, cancellationToken)
                : null;
        if (request.ChargeServiceSettingId.HasValue && chargeService is null)
        {
            return DictionaryResult<SupplierDto>.Failure("charge_service_not_found", "Услуга из раздела тарифов не найдена.");
        }

        var name = request.Name.Trim();
        var inn = NormalizeOptional(request.Inn);
        var legalAddress = NormalizeOptional(request.LegalAddress);
        var contactPerson = NormalizeOptional(request.ContactPerson);
        var phone = NormalizeOptional(request.Phone);
        var email = NormalizeOptional(request.Email);
        var startingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        var comment = NormalizeOptional(request.Comment);
        if (SupplierMatches(supplier, name, group.Id, chargeService?.Id, inn, legalAddress, contactPerson, phone, email, startingBalance, comment))
        {
            return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
        }

        if (await supplierRepository.ActiveDuplicateExistsAsync(id, group.Id, name, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = supplier.Name,
            ["group"] = supplier.Group.Name,
            ["service"] = supplier.ChargeServiceSetting?.Name,
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
            ["service"] = chargeService?.Name,
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
        supplier.ChargeServiceSettingId = chargeService?.Id;
        supplier.ChargeServiceSetting = chargeService;
        supplier.Inn = inn;
        supplier.LegalAddress = legalAddress;
        supplier.ContactPerson = contactPerson;
        supplier.Phone = phone;
        supplier.Email = email;
        supplier.StartingBalance = startingBalance;
        supplier.Comment = comment;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_updated", "supplier", supplier.Id, $"Обновлен поставщик {supplier.Name}.", oldValues: oldValues, newValues: newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> ArchiveSupplierAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(id, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        supplier.IsArchived = true;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_archived", "supplier", supplier.Id, $"Архивирован поставщик {supplier.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<DictionaryResult<SupplierDto>> RestoreSupplierAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await supplierRepository.FindArchivedWithGroupAsync(id, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_not_found", "Поставщик не найден в архиве.");
        }

        if (supplier.Group.IsArchived)
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_group_not_found", "Сначала восстановите группу поставщика.");
        }

        if (await supplierRepository.ActiveDuplicateExistsAsync(id, supplier.GroupId, supplier.Name, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
        }

        supplier.IsArchived = false;
        supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_restored", "supplier", supplier.Id, $"Восстановлен поставщик {supplier.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierDto>.Success(ToSupplierDto(supplier));
    }

    public async Task<IReadOnlyList<SupplierContactDto>> GetSupplierContactsAsync(Guid? supplierId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var contacts = await supplierContactRepository.GetListAsync(supplierId, normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return contacts.Select(ToSupplierContactDto).ToList();
    }

    public async Task<DictionaryResult<SupplierContactDto>> CreateSupplierContactAsync(UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        var contact = new SupplierContact { FullName = request.FullName.Trim(), SupplierId = supplier.Id, Supplier = supplier };
        ApplySupplierContact(contact, request);
        supplierContactRepository.Add(contact);
        AddAudit(actorUserId, "dictionary.supplier_contact_created", "supplier_contact", contact.Id, $"Создан контакт {contact.FullName} поставщика {supplier.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> UpdateSupplierContactAsync(Guid id, UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var contact = await supplierContactRepository.FindActiveWithSupplierAsync(id, cancellationToken);
        if (contact is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Контакт поставщика не найден.");
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> ArchiveSupplierContactAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<SupplierContactDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var contact = await supplierContactRepository.FindActiveWithSupplierAsync(id, cancellationToken);
        if (contact is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_contact_not_found", "Контакт поставщика не найден.");
        }

        contact.IsArchived = true;
        contact.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_contact_archived", "supplier_contact", contact.Id, $"Архивирован контакт {contact.FullName} поставщика {contact.Supplier.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> RestoreSupplierContactAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var contact = await supplierContactRepository.FindArchivedWithSupplierGroupAsync(id, cancellationToken);
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

            if (await supplierRepository.ActiveDuplicateExistsAsync(contact.Supplier.Id, contact.Supplier.GroupId, contact.Supplier.Name, cancellationToken))
            {
                return DictionaryResult<SupplierContactDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
            }

            contact.Supplier.IsArchived = false;
            contact.Supplier.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(actorUserId, "dictionary.supplier_restored", "supplier", contact.Supplier.Id, $"Восстановлен поставщик {contact.Supplier.Name} при восстановлении контакта.");
        }

        contact.IsArchived = false;
        contact.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.supplier_contact_restored", "supplier_contact", contact.Id, $"Восстановлен контакт {contact.FullName} поставщика {contact.Supplier.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<IReadOnlyList<StaffDepartmentDto>> GetStaffDepartmentsAsync(CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var departments = await staffDepartmentRepository.GetListAsync(includeArchived, NormalizeListLimit(limit), cancellationToken);
        return departments.Select(ToStaffDepartmentDto).ToList();
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> CreateStaffDepartmentAsync(UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await staffDepartmentRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_duplicate", "Отдел с таким названием уже существует.");
        }

        var department = new StaffDepartment { Name = name };
        staffDepartmentRepository.Add(department);
        AddAudit(actorUserId, "dictionary.staff_department_created", "staff_department", department.Id, $"Создан отдел {department.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> UpdateStaffDepartmentAsync(Guid id, UpsertStaffDepartmentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await staffDepartmentRepository.FindActiveAsync(id, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        var name = request.Name.Trim();
        if (await staffDepartmentRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> ArchiveStaffDepartmentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<StaffDepartmentDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var department = await staffDepartmentRepository.FindActiveAsync(id, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден.");
        }

        if (await staffDepartmentRepository.HasActiveMembersAsync(id, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_used", "В отделе есть активные сотрудники.");
        }

        department.IsArchived = true;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_department_archived", "staff_department", department.Id, $"Архивирован отдел {department.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<DictionaryResult<StaffDepartmentDto>> RestoreStaffDepartmentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await staffDepartmentRepository.FindArchivedAsync(id, cancellationToken);
        if (department is null)
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_not_found", "Отдел не найден в архиве.");
        }

        if (await staffDepartmentRepository.ActiveDuplicateExistsAsync(id, department.Name, cancellationToken))
        {
            return DictionaryResult<StaffDepartmentDto>.Failure("staff_department_duplicate", "Активный отдел с таким названием уже существует.");
        }

        department.IsArchived = false;
        department.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_department_restored", "staff_department", department.Id, $"Восстановлен отдел {department.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffDepartmentDto>.Success(new StaffDepartmentDto(department.Id, department.Name, department.IsArchived));
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffMembersAsync(Guid? departmentId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var members = await staffMemberRepository.GetListAsync(departmentId, normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return members.Select(ToStaffMemberDto).ToList();
    }

    public async Task<PagedResult<StaffMemberDto>> GetStaffMembersPageAsync(Guid? departmentId, string? search, int? offset, int? limit, string? sortBy, string? sortDirection, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var normalizedSortBy = sortBy?.Trim() switch
        {
            "department" => "department",
            "rate" => "rate",
            _ => "fullName"
        };
        var sortDescending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        var page = await staffMemberRepository.GetPageAsync(departmentId, normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, normalizedSortBy, sortDescending, cancellationToken);
        return new PagedResult<StaffMemberDto>(page.Items.Select(ToStaffMemberDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<StaffMemberDto>> CreateStaffMemberAsync(UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var department = await staffDepartmentRepository.FindActiveAsync(request.DepartmentId, cancellationToken);
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

        staffMemberRepository.Add(member);
        AddAudit(actorUserId, "dictionary.staff_member_created", "staff_member", member.Id, $"Создан сотрудник {member.FullName}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> UpdateStaffMemberAsync(Guid id, UpsertStaffMemberRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var member = await staffMemberRepository.FindActiveAsync(id, cancellationToken);
        if (member is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Сотрудник не найден.");
        }

        var department = await staffDepartmentRepository.FindActiveAsync(request.DepartmentId, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> ArchiveStaffMemberAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<StaffMemberDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var member = await staffMemberRepository.FindActiveAsync(id, cancellationToken);
        if (member is null)
        {
            return DictionaryResult<StaffMemberDto>.Failure("staff_member_not_found", "Сотрудник не найден.");
        }

        member.IsArchived = true;
        member.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.staff_member_archived", "staff_member", member.Id, $"Архивирован сотрудник {member.FullName}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<DictionaryResult<StaffMemberDto>> RestoreStaffMemberAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var member = await staffMemberRepository.FindArchivedAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<StaffMemberDto>.Success(ToStaffMemberDto(member));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetIncomeTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var incomeTypes = await incomeTypeRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return incomeTypes.Select(ToAccountingTypeDto).ToList();
    }

    public async Task<PagedResult<AccountingTypeDto>> GetIncomeTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await incomeTypeRepository.GetPageAsync(normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<AccountingTypeDto>(page.Items.Select(ToAccountingTypeDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<AccountingTypeDto>> CreateIncomeTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await incomeTypeRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Вид поступления с таким названием уже существует.");
        }

        var incomeType = new IncomeType
        {
            Name = name,
            Code = NormalizeOptional(request.Code)
        };

        incomeTypeRepository.Add(incomeType);
        AddAudit(actorUserId, "dictionary.income_type_created", "income_type", incomeType.Id, $"Создан вид поступления {incomeType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> UpdateIncomeTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var incomeType = await incomeTypeRepository.FindActiveAsync(id, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        if (incomeType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_system", "Системный вид поступления нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await incomeTypeRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveIncomeTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<AccountingTypeDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> RestoreIncomeTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var incomeType = await incomeTypeRepository.FindArchivedAsync(id, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_not_found", "Вид поступления не найден в архиве.");
        }

        if (await incomeTypeRepository.ActiveDuplicateExistsAsync(id, incomeType.Name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Активный вид поступления с таким названием уже существует.");
        }

        incomeType.IsArchived = false;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_restored", "income_type", incomeType.Id, $"Восстановлен вид поступления {incomeType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(incomeType.Id, incomeType.Name, incomeType.Code, incomeType.IsSystem, incomeType.IsArchived));
    }

    public async Task<IReadOnlyList<AccountingTypeDto>> GetExpenseTypesAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var expenseTypes = await expenseTypeRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return expenseTypes.Select(ToAccountingTypeDto).ToList();
    }

    public async Task<PagedResult<AccountingTypeDto>> GetExpenseTypesPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await expenseTypeRepository.GetPageAsync(normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<AccountingTypeDto>(page.Items.Select(ToAccountingTypeDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<AccountingTypeDto>> CreateExpenseTypeAsync(UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Вид выплаты с таким названием уже существует.");
        }

        var expenseType = new ExpenseType
        {
            Name = name,
            Code = NormalizeOptional(request.Code)
        };

        expenseTypeRepository.Add(expenseType);
        AddAudit(actorUserId, "dictionary.expense_type_created", "expense_type", expenseType.Id, $"Создан вид выплаты {expenseType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> UpdateExpenseTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await expenseTypeRepository.FindActiveAsync(id, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        if (expenseType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_system", "Системный вид выплаты нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> ArchiveExpenseTypeAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<AccountingTypeDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> RestoreExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await expenseTypeRepository.FindArchivedAsync(id, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Вид выплаты не найден в архиве.");
        }

        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(id, expenseType.Name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Активный вид выплаты с таким названием уже существует.");
        }

        expenseType.IsArchived = false;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_restored", "expense_type", expenseType.Id, $"Восстановлен вид выплаты {expenseType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var tariffs = await tariffRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return tariffs.Select(ToTariffDto).ToList();
    }

    public async Task<PagedResult<TariffDto>> GetTariffsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await tariffRepository.GetPageAsync(normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<TariffDto>(page.Items.Select(ToTariffDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
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

        if (await tariffRepository.ActiveDuplicateExistsAsync(null, name, request.EffectiveFrom, cancellationToken))
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

        tariffRepository.Add(tariff);
        AddAudit(actorUserId, "dictionary.tariff_created", "tariff", tariff.Id, $"Создан тариф {FormatTariffAuditDetails(tariff)}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> UpdateTariffAsync(Guid id, UpsertTariffRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var tariff = await tariffRepository.FindActiveAsync(id, cancellationToken);
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

        var electricityTiers = ValidateElectricityTiers(calculationBase, request, tariff);
        if (!electricityTiers.Succeeded)
        {
            return DictionaryResult<TariffDto>.Failure(electricityTiers.ErrorCode!, electricityTiers.ErrorMessage!);
        }

        if (await tariffRepository.ActiveDuplicateExistsAsync(id, name, request.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Тариф с таким названием и датой действия уже существует.");
        }

        if (request.EffectiveFrom > tariff.EffectiveFrom)
        {
            var earliestAccrualMonth = await tariffRepository.GetEarliestRegularAccrualMonthAsync(tariff.Id, cancellationToken);
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

        var oldElectricityTiers = ReadElectricityTiers(tariff);
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
            ["electricityThirdRate"] = tariff.ElectricityThirdRate,
            ["electricityTiers"] = oldElectricityTiers.Count == 0 ? null : oldElectricityTiers
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
            ["electricityThirdRate"] = electricityTiers.Value?.ThirdRate,
            ["electricityTiers"] = electricityTiers.Value?.Items
        };

        tariff.Name = name;
        tariff.CalculationBase = calculationBase;
        tariff.Rate = rate;
        tariff.EffectiveFrom = request.EffectiveFrom;
        tariff.Comment = comment;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyElectricityTiers(tariff, electricityTiers.Value);

        AddAudit(
            actorUserId,
            "dictionary.tariff_updated",
            "tariff",
            tariff.Id,
            $"Изменен тариф {FormatTariffAuditDetails(tariff)}.",
            NormalizeOptional(request.ElectricityTierChangeReason),
            oldValues,
            newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> ArchiveTariffAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<TariffDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var tariff = await tariffRepository.FindActiveAsync(id, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден.");
        }

        tariff.IsArchived = true;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.tariff_archived", "tariff", tariff.Id, $"Архивирован тариф {FormatTariffAuditDetails(tariff)}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<DictionaryResult<TariffDto>> RestoreTariffAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var tariff = await tariffRepository.FindArchivedAsync(id, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<TariffDto>.Failure("tariff_not_found", "Тариф не найден в архиве.");
        }

        if (await tariffRepository.ActiveDuplicateExistsAsync(id, tariff.Name, tariff.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure("tariff_duplicate", "Активный тариф с таким названием и датой действия уже существует.");
        }

        tariff.IsArchived = false;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.tariff_restored", "tariff", tariff.Id, $"Восстановлен тариф {FormatTariffAuditDetails(tariff)}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<TariffDto>.Success(ToTariffDto(tariff));
    }

    public async Task<IReadOnlyList<ChargeServiceSettingDto>> GetChargeServiceSettingsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var settings = await chargeServiceSettingRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return settings.Select(ToChargeServiceSettingDto).ToList();
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
        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Услуга с таким наименованием уже существует.");
        }

        var setting = new ChargeServiceSetting { Name = name };
        ApplyChargeServiceSetting(setting, request);

        chargeServiceSettingRepository.Add(setting);
        AddAudit(actorUserId, "dictionary.charge_service_created", "charge_service", setting.Id, $"Создана настройка услуги {setting.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> UpdateChargeServiceSettingAsync(Guid id, UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var setting = await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken);
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
        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> ArchiveChargeServiceSettingAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<ChargeServiceSettingDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var setting = await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена.");
        }

        setting.IsArchived = true;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.charge_service_archived", "charge_service", setting.Id, $"Архивирована настройка услуги {setting.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> RestoreChargeServiceSettingAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var setting = await chargeServiceSettingRepository.FindArchivedAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена в архиве.");
        }

        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(id, setting.Name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Активная услуга с таким наименованием уже существует.");
        }

        setting.IsArchived = false;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.charge_service_restored", "charge_service", setting.Id, $"Восстановлена настройка услуги {setting.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<IReadOnlyList<IrregularPaymentDto>> GetIrregularPaymentsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var items = await irregularPaymentRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return await ToIrregularPaymentDtosAsync(items, cancellationToken);
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> CreateIrregularPaymentAsync(UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await irregularPaymentRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_duplicate", "Нерегулярный платеж с таким наименованием уже существует.");
        }

        var payment = new IrregularPayment
        {
            Name = name,
            Amount = MoneyMath.RoundMoney(request.Amount),
            IsActive = request.IsActive
        };

        irregularPaymentRepository.Add(payment);
        AddAudit(actorUserId, "dictionary.irregular_payment_created", "irregular_payment", payment.Id, $"Создан нерегулярный платеж {payment.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> UpdateIrregularPaymentAsync(Guid id, UpsertIrregularPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await irregularPaymentRepository.FindActiveAsync(id, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден.");
        }

        var name = request.Name.Trim();
        if (await irregularPaymentRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> SetIrregularPaymentStatusAsync(Guid id, UpdateIrregularPaymentStatusRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await irregularPaymentRepository.FindActiveAsync(id, cancellationToken);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> ArchiveIrregularPaymentAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<IrregularPaymentDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var payment = await irregularPaymentRepository.FindActiveAsync(id, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден.");
        }

        if (await irregularPaymentRepository.IsUsedAsync(payment.Id, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_used", "Удаление недоступно: нерегулярный платеж уже используется в платежах или начислениях.");
        }

        payment.IsArchived = true;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.irregular_payment_archived", "irregular_payment", payment.Id, $"Удален нерегулярный платеж {payment.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<DictionaryResult<IrregularPaymentDto>> RestoreIrregularPaymentAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var payment = await irregularPaymentRepository.FindArchivedAsync(id, cancellationToken);
        if (payment is null)
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_not_found", "Нерегулярный платеж не найден в архиве.");
        }

        if (await irregularPaymentRepository.ActiveDuplicateExistsAsync(id, payment.Name, cancellationToken))
        {
            return DictionaryResult<IrregularPaymentDto>.Failure("irregular_payment_duplicate", "Активный нерегулярный платеж с таким наименованием уже существует.");
        }

        payment.IsArchived = false;
        payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.irregular_payment_restored", "irregular_payment", payment.Id, $"Восстановлен нерегулярный платеж {payment.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<IrregularPaymentDto>.Success(await ToIrregularPaymentDtoAsync(payment, cancellationToken));
    }

    public async Task<IReadOnlyList<FeeCampaignDto>> GetFeeCampaignsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var campaigns = await feeCampaignRepository.GetListAsync(normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return campaigns.Select(ToFeeCampaignDto).ToList();
    }

    public async Task<DictionaryResult<FeeCampaignDto>> CreateFeeCampaignAsync(UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateFeeCampaignRequest(request) is { } validationError)
        {
            return validationError;
        }

        var name = request.Name.Trim();
        if (await feeCampaignRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        var incomeType = await incomeTypeRepository.FindFirstActiveByCodeAsync(OtherIncomeIncomeTypeCode, cancellationToken);
        if (incomeType is null || !incomeType.IsSystem || !incomeType.DestinationFundId.HasValue)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "other_income_destination_not_configured",
                "Системное назначение «Прочие доходы» не настроено или не связано с фондом.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        var targetAmount = await CalculateFeeCampaignTargetAmountAsync(request, participants.Value!, cancellationToken);

        var campaign = new FeeCampaign { Name = name };
        ApplyFeeCampaign(campaign, request, incomeType.Id, targetAmount);
        campaign.IncomeType = incomeType;
        SyncFeeCampaignParticipants(campaign, participants.Value!);

        feeCampaignRepository.Add(campaign);
        AddAudit(actorUserId, "dictionary.fee_campaign_created", "fee_campaign", campaign.Id, $"Объявлен сбор {campaign.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> UpdateFeeCampaignAsync(Guid id, UpsertFeeCampaignRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateFeeCampaignRequest(request) is { } validationError)
        {
            return validationError;
        }

        var campaign = await feeCampaignRepository.FindActiveWithDetailsAsync(id, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        var name = request.Name.Trim();
        if (await feeCampaignRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        var incomeType = await incomeTypeRepository.FindFirstActiveByCodeAsync(OtherIncomeIncomeTypeCode, cancellationToken);
        if (incomeType is null || !incomeType.IsSystem || !incomeType.DestinationFundId.HasValue)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "other_income_destination_not_configured",
                "Системное назначение «Прочие доходы» не настроено или не связано с фондом.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        var targetAmount = await CalculateFeeCampaignTargetAmountAsync(request, participants.Value!, cancellationToken);

        if (!FeeCampaignParticipantsMatch(campaign, request, participants.Value!) &&
            await feeCampaignRepository.HasAccrualsAsync(campaign.Id, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_participants_locked",
                "Нельзя изменить состав участников сбора после создания начислений. Исторический состав должен оставаться неизменным.");
        }

        if (FeeCampaignMatches(campaign, request, participants.Value!, incomeType.Id, targetAmount))
        {
            return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
        }

        var oldValues = ToFeeCampaignAuditValues(campaign);
        ApplyFeeCampaign(campaign, request, incomeType.Id, targetAmount);
        campaign.IncomeType = incomeType;
        SyncFeeCampaignParticipants(campaign, participants.Value!);
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToFeeCampaignAuditValues(campaign);

        AddAudit(actorUserId, "dictionary.fee_campaign_updated", "fee_campaign", campaign.Id, $"Изменен сбор {campaign.Name}.", oldValues: oldValues, newValues: newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> ArchiveFeeCampaignAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<FeeCampaignDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var campaign = await feeCampaignRepository.FindActiveWithDetailsAsync(id, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        campaign.IsArchived = true;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.fee_campaign_archived", "fee_campaign", campaign.Id, $"Архивирован сбор {campaign.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> RestoreFeeCampaignAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var campaign = await feeCampaignRepository.FindArchivedWithDetailsAsync(id, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден в архиве.");
        }

        if (await feeCampaignRepository.ActiveDuplicateExistsAsync(id, campaign.Name, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_duplicate", "Активный сбор с таким наименованием уже существует.");
        }

        campaign.IsArchived = false;
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.fee_campaign_restored", "fee_campaign", campaign.Id, $"Восстановлен сбор {campaign.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    private async Task<Owner?> FindOwnerOrNullAsync(Guid? ownerId, CancellationToken cancellationToken)
    {
        return ownerId is null
            ? null
            : await ownerRepository.FindActiveAsync(ownerId.Value, cancellationToken);
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
        var baseDetails = $"{tariff.Name} с {tariff.EffectiveFrom:dd.MM.yyyy}, база {tariff.CalculationBase}, ставка {MoneyFormatting.Format(tariff.Rate)}";
        var tiers = ReadElectricityTiers(tariff);
        if (tiers.Count == 0)
        {
            return baseDetails;
        }

        var tierDetails = string.Join(", ", tiers.Select(tier =>
            tier.UpperBound.HasValue
                ? $"{tier.Name} до {tier.UpperBound.Value.ToString("0.####", CultureInfo.InvariantCulture)} кВт по {MoneyFormatting.Format(tier.Rate)}"
                : $"{tier.Name} по {MoneyFormatting.Format(tier.Rate)}"));
        return $"{baseDetails}, электричество: {tierDetails}";
    }

    private static DictionaryResult<ElectricityTierConfig?> ValidateElectricityTiers(string calculationBase, UpsertTariffRequest request, Tariff? existingTariff = null)
    {
        if (calculationBase != TariffCalculationBases.MeterElectricity)
        {
            return DictionaryResult<ElectricityTierConfig?>.Success(null);
        }

        if (request.ElectricityTiers is not null)
        {
            if (request.ElectricityTiers.Count is < 2 or > 20)
            {
                return DictionaryResult<ElectricityTierConfig?>.Failure(
                    "tariff_electricity_tier_count_invalid",
                    "Для электроэнергии укажите от 2 до 20 тарифных ступеней.");
            }

            var existingTiers = existingTariff is null
                ? []
                : ReadElectricityTiers(existingTariff);
            var normalized = new List<ElectricityTierConfigItem>(request.ElectricityTiers.Count);
            decimal? previousUpperBound = null;
            for (var index = 0; index < request.ElectricityTiers.Count; index++)
            {
                var requestedTier = request.ElectricityTiers[index];
                var name = requestedTier.Name.Trim();
                if (name.Length == 0)
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_name_required",
                        $"Укажите название ступени {index + 1}.");
                }

                var rate = MoneyMath.RoundRate(requestedTier.Rate);
                if (rate <= 0)
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_rate_positive_required",
                        $"Ставка ступени «{name}» должна быть больше 0.");
                }

                var isLast = index == request.ElectricityTiers.Count - 1;
                decimal? upperBound = requestedTier.UpperBound.HasValue
                    ? MoneyMath.RoundMeterValue(requestedTier.UpperBound.Value)
                    : null;
                if (!isLast && !upperBound.HasValue)
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_upper_bound_required",
                        $"Для ступени «{name}» укажите верхнюю границу.");
                }

                if (isLast && upperBound.HasValue)
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_last_tier_unbounded_required",
                        "Последняя ступень электроэнергии должна применяться без верхней границы.");
                }

                if (upperBound.HasValue && (upperBound <= 0 || previousUpperBound.HasValue && upperBound <= previousUpperBound))
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_upper_bound_invalid",
                        "Границы ступеней электроэнергии должны быть положительными и строго возрастать.");
                }

                var existingTier = requestedTier.Id.HasValue
                    ? existingTiers.FirstOrDefault(tier => tier.Id == requestedTier.Id.Value)
                    : null;
                if (existingTariff is not null && requestedTier.Id.HasValue && existingTier is null)
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_not_found",
                        "Одна из изменяемых ступеней тарифа не найдена. Обновите страницу и повторите действие.");
                }

                normalized.Add(new ElectricityTierConfigItem(
                    requestedTier.Id ?? Guid.NewGuid(),
                    name,
                    upperBound,
                    rate,
                    existingTier?.IsCustom ?? true));
                previousUpperBound = upperBound;
            }

            return DictionaryResult<ElectricityTierConfig?>.Success(new ElectricityTierConfig(normalized, true));
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

        return DictionaryResult<ElectricityTierConfig?>.Success(new ElectricityTierConfig(
        [
            new ElectricityTierConfigItem(Guid.Empty, firstTierName, firstThreshold, firstRate, false),
            new ElectricityTierConfigItem(Guid.Empty, secondTierName, secondThreshold, secondRate, false),
            new ElectricityTierConfigItem(Guid.Empty, thirdTierName, null, thirdRate, false)
        ], false));
    }

    private static void ApplyElectricityTiers(Tariff tariff, ElectricityTierConfig? tiers)
    {
        tariff.ElectricityTiersJson = tiers?.UsesGenericConfiguration == true
            ? JsonSerializer.Serialize(tiers.Items)
            : null;
        tariff.ElectricityFirstThreshold = tiers?.FirstThreshold;
        tariff.ElectricitySecondThreshold = tiers?.SecondThreshold;
        tariff.ElectricityFirstTierName = tiers?.FirstTierName;
        tariff.ElectricitySecondTierName = tiers?.SecondTierName;
        tariff.ElectricityThirdTierName = tiers?.ThirdTierName;
        tariff.ElectricityFirstRate = tiers?.FirstRate;
        tariff.ElectricitySecondRate = tiers?.SecondRate;
        tariff.ElectricityThirdRate = tiers?.ThirdRate;
    }

    private static IReadOnlyList<ElectricityTierConfigItem> ReadElectricityTiers(Tariff tariff)
    {
        if (!string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<ElectricityTierConfigItem>>(tariff.ElectricityTiersJson);
                if (stored is { Count: >= 2 })
                {
                    return stored;
                }
            }
            catch (JsonException)
            {
                // Старые поля ниже остаются безопасным вариантом чтения поврежденной конфигурации.
            }
        }

        if (!HasElectricityTiers(tariff))
        {
            return [];
        }

        return
        [
            new ElectricityTierConfigItem(CreateLegacyTierId(tariff.Id, 1), tariff.ElectricityFirstTierName ?? "Порог 1", tariff.ElectricityFirstThreshold, tariff.ElectricityFirstRate!.Value, false),
            new ElectricityTierConfigItem(CreateLegacyTierId(tariff.Id, 2), tariff.ElectricitySecondTierName ?? "Порог 2", tariff.ElectricitySecondThreshold, tariff.ElectricitySecondRate!.Value, false),
            new ElectricityTierConfigItem(CreateLegacyTierId(tariff.Id, 3), tariff.ElectricityThirdTierName ?? "Порог 3", null, tariff.ElectricityThirdRate!.Value, false)
        ];
    }

    private static Guid CreateLegacyTierId(Guid tariffId, int tierNumber)
    {
        var bytes = tariffId.ToByteArray();
        bytes[^1] ^= (byte)tierNumber;
        return new Guid(bytes);
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
            if (!request.PeriodicityMonths.HasValue)
            {
                return DictionaryResult<object>.Failure("charge_service_periodicity_required", "Для регулярной услуги укажите периодичность.");
            }

            if (request.PeriodicityMonths.Value is not 1 and not 12)
            {
                return DictionaryResult<object>.Failure("charge_service_periodicity_invalid", "Периодичность регулярной услуги должна быть ежемесячной или ежегодной.");
            }

            if (!request.AccrualStartMonth.HasValue)
            {
                return DictionaryResult<object>.Failure("charge_service_accrual_start_month_required", "Для регулярной услуги укажите месяц начала учета.");
            }

            if (!request.PaymentDueDay.HasValue)
            {
                return DictionaryResult<object>.Failure("charge_service_payment_day_required", "Для регулярной услуги укажите день оплаты.");
            }

            if (request.PeriodicityMonths.Value == 12 && !request.PaymentDueMonth.HasValue)
            {
                return DictionaryResult<object>.Failure("charge_service_annual_payment_month_required", "Для ежегодной услуги укажите месяц оплаты.");
            }
        }

        if (request.AccrualStartMonth is < 1 or > 12 || request.PaymentDueMonth is < 1 or > 12)
        {
            return DictionaryResult<object>.Failure("charge_service_month_invalid", "Месяц должен быть от 1 до 12.");
        }

        if (!request.IsRegular && request.PaymentDueDay.HasValue != request.PaymentDueMonth.HasValue)
        {
            return DictionaryResult<object>.Failure("charge_service_payment_date_incomplete", "Для даты оплаты заполните и день, и месяц.");
        }

        var normalizedPaymentDueMonth = NormalizeChargeServicePaymentDueMonth(request);
        if (request.PaymentDueDay.HasValue)
        {
            var maxDay = normalizedPaymentDueMonth.HasValue
                ? DateTime.DaysInMonth(2026, normalizedPaymentDueMonth.Value)
                : 31;
            if (request.PaymentDueDay.Value < 1 || request.PaymentDueDay.Value > maxDay)
            {
                var message = normalizedPaymentDueMonth.HasValue
                    ? $"В выбранном месяце нельзя указать день больше {maxDay}."
                    : "Для ежемесячной услуги укажите день оплаты от 1 до 31.";
                return DictionaryResult<object>.Failure("charge_service_payment_day_invalid", message);
            }
        }

        if (request.IsRegular && request.HasTieredTariff && !request.IsMetered)
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
            return DictionaryResult<object>.Failure("charge_service_regular_link_incomplete", "Для регулярной услуги заполните и вид поступления, и тариф.");
        }

        if (!request.IncomeTypeId.HasValue)
        {
            return DictionaryResult<object>.Success(new object());
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId.Value, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<object>.Failure("charge_service_income_type_not_found", "Вид поступления для услуги не найден.");
        }

        var tariff = await tariffRepository.FindActiveAsync(request.TariffId!.Value, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_not_found", "Тариф для услуги не найден.");
        }

        if (!IsIncomeTypeCompatibleWithTariff(incomeType.Code, tariff.CalculationBase))
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_mismatch", "Выбранный тариф не подходит для вида поступления услуги.");
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
        setting.PaymentDueMonth = NormalizeChargeServicePaymentDueMonth(request);
        setting.OverdueGraceDays = request.OverdueGraceDays;
        setting.IncomeTypeId = request.IsRegular ? request.IncomeTypeId : null;
        setting.TariffId = request.IsRegular ? request.TariffId : null;
        setting.IsMetered = request.IsRegular && request.IsMetered;
        setting.HasTieredTariff = request.IsRegular && request.IsMetered && request.HasTieredTariff;
        setting.UnitName = NormalizeOptional(request.UnitName);
    }

    private static bool ChargeServiceSettingMatches(ChargeServiceSetting setting, UpsertChargeServiceSettingRequest request)
    {
        return StringEquals(setting.Name, request.Name.Trim()) &&
            setting.IsRegular == request.IsRegular &&
            setting.PeriodicityMonths == (request.IsRegular ? request.PeriodicityMonths : null) &&
            setting.AccrualStartMonth == (request.IsRegular ? request.AccrualStartMonth : null) &&
            setting.PaymentDueDay == request.PaymentDueDay &&
            setting.PaymentDueMonth == NormalizeChargeServicePaymentDueMonth(request) &&
            setting.OverdueGraceDays == request.OverdueGraceDays &&
            setting.IncomeTypeId == (request.IsRegular ? request.IncomeTypeId : null) &&
            setting.TariffId == (request.IsRegular ? request.TariffId : null) &&
            setting.IsMetered == (request.IsRegular && request.IsMetered) &&
            setting.HasTieredTariff == (request.IsRegular && request.IsMetered && request.HasTieredTariff) &&
            StringEquals(setting.UnitName, NormalizeOptional(request.UnitName));
    }

    private static int? NormalizeChargeServicePaymentDueMonth(UpsertChargeServiceSettingRequest request) =>
        request.IsRegular && request.PeriodicityMonths == 1 ? null : request.PaymentDueMonth;

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

        if (MoneyMath.RoundMoney(request.ContributionAmount) < 0m)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_contribution_amount_invalid", "Сумма взноса не может быть отрицательной.");
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
        var garages = await garageRepository.GetActiveByIdsAsync(participantIds, cancellationToken);

        if (garages.Count != participantIds.Count)
        {
            return DictionaryResult<IReadOnlyList<Garage>>.Failure("fee_campaign_participant_garage_not_found", "Один из выбранных гаражей не найден или архивирован.");
        }

        return DictionaryResult<IReadOnlyList<Garage>>.Success(garages);
    }

    private async Task<decimal> CalculateFeeCampaignTargetAmountAsync(
        UpsertFeeCampaignRequest request,
        IReadOnlyList<Garage> participants,
        CancellationToken cancellationToken)
    {
        var participantCount = request.AppliesToAllGarages
            ? await garageRepository.CountActiveAsync(cancellationToken)
            : participants.Count;

        return MoneyMath.RoundMoney(MoneyMath.RoundMoney(request.ContributionAmount) * participantCount);
    }

    private static void ApplyFeeCampaign(FeeCampaign campaign, UpsertFeeCampaignRequest request, Guid incomeTypeId, decimal targetAmount)
    {
        campaign.Name = request.Name.Trim();
        campaign.IncomeTypeId = incomeTypeId;
        campaign.Goal = NormalizeOptional(request.Goal);
        campaign.ContributionAmount = MoneyMath.RoundMoney(request.ContributionAmount);
        campaign.TargetAmount = targetAmount;
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

    private static bool FeeCampaignMatches(
        FeeCampaign campaign,
        UpsertFeeCampaignRequest request,
        IReadOnlyList<Garage> participants,
        Guid incomeTypeId,
        decimal targetAmount)
    {
        return StringEquals(campaign.Name, request.Name.Trim()) &&
            campaign.IncomeTypeId == incomeTypeId &&
            StringEquals(campaign.Goal, NormalizeOptional(request.Goal)) &&
            campaign.ContributionAmount == MoneyMath.RoundMoney(request.ContributionAmount) &&
            campaign.TargetAmount == targetAmount &&
            campaign.StartsOn == request.StartsOn &&
            campaign.EndsOn == request.EndsOn &&
            campaign.OverdueGraceDays == request.OverdueGraceDays &&
            FeeCampaignParticipantsMatch(campaign, request, participants);
    }

    private static bool FeeCampaignParticipantsMatch(
        FeeCampaign campaign,
        UpsertFeeCampaignRequest request,
        IReadOnlyList<Garage> participants)
    {
        if (campaign.AppliesToAllGarages != request.AppliesToAllGarages)
        {
            return false;
        }

        var currentParticipantIds = campaign.ParticipantGarages
            .Select(participant => participant.GarageId)
            .Order()
            .ToArray();
        var nextParticipantIds = participants
            .Select(garage => garage.Id)
            .Order()
            .ToArray();
        return currentParticipantIds.SequenceEqual(nextParticipantIds);
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

    private static bool SupplierMatches(Supplier supplier, string name, Guid groupId, Guid? chargeServiceSettingId, string? inn, string? legalAddress, string? contactPerson, string? phone, string? email, decimal startingBalance, string? comment)
    {
        return StringEquals(supplier.Name, name) &&
            supplier.GroupId == groupId &&
            supplier.ChargeServiceSettingId == chargeServiceSettingId &&
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
            StringEquals(
                tariff.ElectricityTiersJson,
                tiers?.UsesGenericConfiguration == true ? JsonSerializer.Serialize(tiers.Items) : null) &&
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

    private static SupplierGroupDto ToSupplierGroupDto(SupplierGroup group) =>
        new(group.Id, group.Name, group.IsSystem, group.IsArchived);

    private static StaffDepartmentDto ToStaffDepartmentDto(StaffDepartment department) =>
        new(department.Id, department.Name, department.IsArchived);

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
            overdueDebt ?? Math.Max(calculatedBalance, 0m),
            garage.Owner?.Phone);
    }

    private static GarageDto ToGarageDto(GarageListItemData garage, decimal balance, decimal overdueDebt) =>
        new(
            garage.Id,
            garage.Number,
            garage.PeopleCount,
            garage.FloorCount,
            garage.OwnerId,
            garage.OwnerName,
            garage.StartingBalance,
            garage.InitialWaterMeterValue,
            garage.InitialElectricityMeterValue,
            garage.Comment,
            garage.IsArchived,
            balance,
            overdueDebt,
            garage.OwnerPhone);

    private static SupplierDto ToSupplierDto(Supplier supplier, decimal? debt = null)
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
            supplier.IsArchived,
            debt ?? supplier.StartingBalance,
            supplier.ChargeServiceSettingId,
            supplier.ChargeServiceSetting?.Name);
    }

    private static SupplierDto ToSupplierDto(Supplier supplier, SupplierPrimaryContactData? primaryContact, decimal? debt = null)
    {
        return ToSupplierDto(supplier, debt) with
        {
            ContactPerson = primaryContact?.FullName ?? supplier.ContactPerson,
            Phone = primaryContact?.Phone ?? supplier.Phone,
            Email = primaryContact?.Email ?? supplier.Email
        };
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
        var usedNames = await irregularPaymentRepository.GetUsedNamesAsync(
            payments.Select(payment => payment.Name).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);

        return payments
            .Select(payment => new IrregularPaymentDto(
                payment.Id,
                payment.Name,
                payment.Amount,
                payment.IsActive,
                payment.IsArchived,
                usedNames.Contains(payment.Name)))
            .ToList();
    }

    private async Task<IrregularPaymentDto> ToIrregularPaymentDtoAsync(IrregularPayment payment, CancellationToken cancellationToken)
    {
        return new IrregularPaymentDto(
            payment.Id,
            payment.Name,
            payment.Amount,
            payment.IsActive,
            payment.IsArchived,
            await irregularPaymentRepository.IsUsedAsync(payment.Id, cancellationToken));
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
            tariff.ElectricityThirdRate,
            ReadElectricityTiers(tariff)
                .Select(tier => new ElectricityTariffTierDto(tier.Id, tier.Name, tier.UpperBound, tier.Rate, tier.IsCustom))
                .ToArray());
    }

    private sealed record ElectricityTierConfig(
        IReadOnlyList<ElectricityTierConfigItem> Items,
        bool UsesGenericConfiguration)
    {
        private bool HasLegacyShape => Items.Count == 3;
        public decimal? FirstThreshold => HasLegacyShape ? Items[0].UpperBound : null;
        public decimal? SecondThreshold => HasLegacyShape ? Items[1].UpperBound : null;
        public string? FirstTierName => HasLegacyShape ? Items[0].Name : null;
        public string? SecondTierName => HasLegacyShape ? Items[1].Name : null;
        public string? ThirdTierName => HasLegacyShape ? Items[2].Name : null;
        public decimal? FirstRate => HasLegacyShape ? Items[0].Rate : null;
        public decimal? SecondRate => HasLegacyShape ? Items[1].Rate : null;
        public decimal? ThirdRate => HasLegacyShape ? Items[2].Rate : null;
    }

    private sealed record ElectricityTierConfigItem(
        Guid Id,
        string Name,
        decimal? UpperBound,
        decimal Rate,
        bool IsCustom);
}
