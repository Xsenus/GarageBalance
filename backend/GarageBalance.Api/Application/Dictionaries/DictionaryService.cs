using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
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
    IMeasurementUnitRepository measurementUnitRepository,
    ITariffRepository tariffRepository,
    IIrregularPaymentRepository irregularPaymentRepository,
    IChargeServiceSettingRepository chargeServiceSettingRepository,
    IFeeCampaignRepository feeCampaignRepository,
    IFundRepository fundRepository,
    IOpeningBalanceAdjustmentRepository openingBalanceAdjustmentRepository,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter,
    IBusinessDateProvider businessDateProvider) : IDictionaryService
{
    private static readonly JsonSerializerOptions PersistedJsonOptions = new(JsonSerializerDefaults.Web);
    private const string ServiceIncomeTypeCodePrefix = "service_";
    private const string SupplierServiceExpenseTypeCodePrefix = "supplier_service_";
    private static readonly DateOnly OpenTariffScheduleStart = new(1900, 1, 1);
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
        ["expenseFund"] = "Фонд расходования",
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
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<OwnerDto>();
        }

        var owner = new Owner
        {
            LastName = request.LastName.Trim(),
            FirstName = request.FirstName.Trim(),
            MiddleName = NormalizeOptional(request.MiddleName),
            Phone = phone,
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
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<OwnerDto>();
        }
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

        if (await ownerRepository.HasActiveGaragesAsync(id, cancellationToken))
        {
            return DictionaryResult<OwnerDto>.Failure(
                "owner_has_active_garages",
                "Сначала архивируйте все гаражи этого владельца.");
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
        new(
            item.Id,
            item.Name,
            item.Code,
            item.IsSystem,
            item.IsArchived,
            item.DestinationFundId,
            item.DestinationFund?.Name);

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

        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, garage);

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
        var openingDataLock = await garageRepository.GetOpeningDataLockAsync(garage.Id, cancellationToken);
        if (garage.StartingBalance != startingBalance && openingDataLock.HasFinancialHistory)
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_starting_balance_locked",
                "Стартовый баланс нельзя менять после появления начислений или платежей. Оформите отдельную финансовую корректировку.");
        }

        if (garage.InitialWaterMeterValue != initialWaterMeterValue && openingDataLock.HasWaterMeterHistory)
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_initial_water_meter_locked",
                "Стартовое показание воды нельзя менять после внесения показаний. Для нового прибора оформите замену счетчика.");
        }

        if (garage.InitialElectricityMeterValue != initialElectricityMeterValue && openingDataLock.HasElectricityMeterHistory)
        {
            return DictionaryResult<GarageDto>.Failure(
                "garage_initial_electricity_meter_locked",
                "Стартовое показание электроэнергии нельзя менять после внесения показаний. Для нового прибора оформите замену счетчика.");
        }

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

    public Task<IReadOnlyList<OpeningBalanceAdjustmentDto>> GetGarageOpeningBalanceAdjustmentsAsync(Guid id, CancellationToken cancellationToken) =>
        GetOpeningBalanceAdjustmentsAsync(OpeningBalanceAdjustmentTargetKinds.Garage, id, cancellationToken);

    public async Task<DictionaryResult<OpeningBalanceAdjustmentDto>> AdjustGarageOpeningBalanceAsync(
        Guid id,
        CreateOpeningBalanceAdjustmentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validation = ValidateOpeningBalanceAdjustment(request);
        if (validation is not null)
        {
            return validation;
        }

        await using var updateLock = await openingBalanceAdjustmentRepository.AcquireUpdateLockAsync(
            OpeningBalanceAdjustmentTargetKinds.Garage, id, cancellationToken);
        var garage = await garageRepository.FindActiveWithOwnerAsync(id, cancellationToken);
        if (garage is null)
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("garage_not_found", "Гараж не найден.");
        }

        return await SaveOpeningBalanceAdjustmentAsync(
            OpeningBalanceAdjustmentTargetKinds.Garage,
            garage.Id,
            garage.Number,
            garage.StartingBalance,
            request,
            actorUserId,
            amount => garage.StartingBalance = amount,
            () => garage.UpdatedAtUtc = DateTimeOffset.UtcNow,
            cancellationToken);
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

        if (await supplierGroupRepository.HasActiveSuppliersAsync(id, cancellationToken))
        {
            return DictionaryResult<SupplierGroupDto>.Failure(
                "supplier_group_has_active_suppliers",
                "Сначала архивируйте всех поставщиков этой группы.");
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
        return new PagedResult<SupplierDto>(page.Items.Select(item => ToSupplierDto(item.Supplier, item.PrimaryContact, item.DebtTotal)).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<SupplierDto>> CreateSupplierAsync(UpsertSupplierRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<SupplierDto>();
        }

        await using var allocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);

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
        var expenseFund = request.ExpenseFundId.HasValue
            ? await fundRepository.FindFundForUpdateAsync(request.ExpenseFundId.Value, cancellationToken)
            : null;
        if (request.ExpenseFundId.HasValue && expenseFund is null)
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_expense_fund_not_found",
                "Фонд расходования поставщика не найден или недоступен.");
        }
        if (chargeService is not null && expenseFund is null)
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_expense_configuration_required",
                "Для поставщика с услугой выберите фонд расходования.");
        }

        var name = request.Name.Trim();
        if (await supplierRepository.ActiveDuplicateExistsAsync(null, group.Id, name, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
        }

        var expenseTypeResult = await ResolveSupplierExpenseTypeAsync(
            chargeService,
            request.ExpenseTypeId,
            currentExpenseType: null,
            actorUserId,
            cancellationToken);
        if (!expenseTypeResult.Succeeded)
        {
            return DictionaryResult<SupplierDto>.Failure(expenseTypeResult.ErrorCode!, expenseTypeResult.ErrorMessage!);
        }
        var expenseType = expenseTypeResult.Value;

        var supplier = new Supplier
        {
            Name = name,
            GroupId = group.Id,
            Group = group,
            ChargeServiceSettingId = chargeService?.Id,
            ChargeServiceSetting = chargeService,
            ExpenseTypeId = expenseType?.Id,
            ExpenseType = expenseType,
            ExpenseFundId = expenseFund?.Id,
            ExpenseFund = expenseFund,
            Inn = NormalizeOptional(request.Inn),
            LegalAddress = NormalizeOptional(request.LegalAddress),
            ContactPerson = NormalizeOptional(request.ContactPerson),
            Phone = phone,
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

        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, supplier);

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
        var expenseFund = request.ExpenseFundId == supplier.ExpenseFundId
            ? supplier.ExpenseFund
            : request.ExpenseFundId.HasValue
                ? await fundRepository.FindFundForUpdateAsync(request.ExpenseFundId.Value, cancellationToken)
                : null;
        if (request.ExpenseFundId.HasValue && expenseFund is null)
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_expense_fund_not_found",
                "Фонд расходования поставщика не найден или недоступен.");
        }
        if (chargeService is not null && expenseFund is null)
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_expense_configuration_required",
                "Для поставщика с услугой выберите фонд расходования.");
        }

        var name = request.Name.Trim();
        var inn = NormalizeOptional(request.Inn);
        var legalAddress = NormalizeOptional(request.LegalAddress);
        var contactPerson = NormalizeOptional(request.ContactPerson);
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<SupplierDto>();
        }
        var email = NormalizeOptional(request.Email);
        var startingBalance = MoneyMath.RoundMoney(request.StartingBalance);
        var comment = NormalizeOptional(request.Comment);
        if (supplier.StartingBalance != startingBalance && await supplierRepository.HasFinancialHistoryAsync(supplier.Id, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_starting_balance_locked",
                "Стартовый баланс поставщика нельзя менять после появления начислений или выплат. Оформите отдельную финансовую корректировку.");
        }

        if (await supplierRepository.ActiveDuplicateExistsAsync(id, group.Id, name, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure("supplier_duplicate", "Активный поставщик с таким названием уже существует в выбранной группе.");
        }

        var expenseTypeResult = await ResolveSupplierExpenseTypeAsync(
            chargeService,
            request.ExpenseTypeId,
            supplier.ChargeServiceSettingId == chargeService?.Id ? supplier.ExpenseType : null,
            actorUserId,
            cancellationToken);
        if (!expenseTypeResult.Succeeded)
        {
            return DictionaryResult<SupplierDto>.Failure(expenseTypeResult.ErrorCode!, expenseTypeResult.ErrorMessage!);
        }
        var expenseType = expenseTypeResult.Value;

        if (SupplierMatches(supplier, name, group.Id, chargeService?.Id, expenseType?.Id, expenseFund?.Id, inn, legalAddress, contactPerson, phone, email, startingBalance, comment))
        {
            return DictionaryResult<SupplierDto>.Success(await ToSupplierDtoWithDebtAsync(supplier, cancellationToken));
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = supplier.Name,
            ["group"] = supplier.Group.Name,
            ["service"] = supplier.ChargeServiceSetting?.Name,
            ["expenseType"] = supplier.ExpenseType?.Name,
            ["expenseFund"] = supplier.ExpenseFund?.Name,
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
            ["expenseType"] = expenseType?.Name,
            ["expenseFund"] = expenseFund?.Name,
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
        supplier.ExpenseTypeId = expenseType?.Id;
        supplier.ExpenseType = expenseType;
        supplier.ExpenseFundId = expenseFund?.Id;
        supplier.ExpenseFund = expenseFund;
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
        return DictionaryResult<SupplierDto>.Success(await ToSupplierDtoWithDebtAsync(supplier, cancellationToken));
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

        if (await supplierRepository.HasActiveContactsAsync(id, cancellationToken))
        {
            return DictionaryResult<SupplierDto>.Failure(
                "supplier_has_active_contacts",
                "Сначала архивируйте все контакты этого поставщика.");
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

    public Task<IReadOnlyList<OpeningBalanceAdjustmentDto>> GetSupplierOpeningBalanceAdjustmentsAsync(Guid id, CancellationToken cancellationToken) =>
        GetOpeningBalanceAdjustmentsAsync(OpeningBalanceAdjustmentTargetKinds.Supplier, id, cancellationToken);

    public async Task<DictionaryResult<OpeningBalanceAdjustmentDto>> AdjustSupplierOpeningBalanceAsync(
        Guid id,
        CreateOpeningBalanceAdjustmentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validation = ValidateOpeningBalanceAdjustment(request);
        if (validation is not null)
        {
            return validation;
        }

        await using var updateLock = await openingBalanceAdjustmentRepository.AcquireUpdateLockAsync(
            OpeningBalanceAdjustmentTargetKinds.Supplier, id, cancellationToken);
        var supplier = await supplierRepository.FindActiveWithGroupAsync(id, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        return await SaveOpeningBalanceAdjustmentAsync(
            OpeningBalanceAdjustmentTargetKinds.Supplier,
            supplier.Id,
            supplier.Name,
            supplier.StartingBalance,
            request,
            actorUserId,
            amount => supplier.StartingBalance = amount,
            () => supplier.UpdatedAtUtc = DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierContactDto>> GetSupplierContactsAsync(Guid? supplierId, string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var contacts = await supplierContactRepository.GetListAsync(supplierId, normalizedSearch, includeArchived, NormalizeListLimit(limit), cancellationToken);
        return contacts.Select(ToSupplierContactDto).ToList();
    }

    public async Task<PagedResult<SupplierContactDto>> GetSupplierContactsPageAsync(Guid? supplierId, string? search, int? offset, int? limit, string? sortBy, string? sortDirection, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedSearch = NormalizeSearch(search);
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var normalizedSortBy = sortBy?.Trim() switch
        {
            "supplier" => "supplier",
            "position" => "position",
            "status" => "status",
            _ => "fullName"
        };
        var sortDescending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        var page = await supplierContactRepository.GetPageAsync(supplierId, normalizedSearch, includeArchived, normalizedOffset, normalizedLimit, normalizedSortBy, sortDescending, cancellationToken);
        return new PagedResult<SupplierContactDto>(page.Items.Select(ToSupplierContactDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<SupplierContactDto>> CreateSupplierContactAsync(UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<SupplierContactDto>();
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return DictionaryResult<SupplierContactDto>.Failure("supplier_not_found", "Поставщик не найден.");
        }

        var contact = new SupplierContact { FullName = request.FullName.Trim(), SupplierId = supplier.Id, Supplier = supplier };
        ApplySupplierContact(contact, request, phone);
        supplierContactRepository.Add(contact);
        AddAudit(actorUserId, "dictionary.supplier_contact_created", "supplier_contact", contact.Id, $"Создан контакт {contact.FullName} поставщика {supplier.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
    }

    public async Task<DictionaryResult<SupplierContactDto>> UpdateSupplierContactAsync(Guid id, UpsertSupplierContactRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone))
        {
            return InvalidPhone<SupplierContactDto>();
        }

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

        if (SupplierContactMatches(contact, request, supplier.Id, phone))
        {
            return DictionaryResult<SupplierContactDto>.Success(ToSupplierContactDto(contact));
        }

        var oldValues = ToSupplierContactAuditValues(contact);
        contact.SupplierId = supplier.Id;
        contact.Supplier = supplier;
        ApplySupplierContact(contact, request, phone);
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
        var code = AccountingTypeCodePolicy.Normalize(request.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_invalid", "Код вида поступления должен начинаться с латинской буквы и содержать только строчные латинские буквы, цифры и знак подчёркивания.");
        }

        if (code is not null && AccountingTypeCodePolicy.IsReservedIncomeCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_reserved", "Этот код зарезервирован для системного вида поступления.");
        }

        if (await incomeTypeRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_duplicate", "Вид поступления с таким названием уже существует.");
        }

        var incomeType = new IncomeType
        {
            Name = name,
            Code = code
        };

        if (code is not null && await incomeTypeRepository.ActiveCodeExistsAsync(null, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_duplicate", "Активный вид поступления с таким кодом уже существует.");
        }

        var defaultFund = (await fundRepository.GetFundsAsync(cancellationToken))
            .FirstOrDefault(fund => fund.AllowOperations && fund.NormalizedName == "ПРОЧЕЕ");
        if (defaultFund is not null)
        {
            incomeType.DestinationFundId = defaultFund.Id;
        }

        incomeTypeRepository.Add(incomeType);
        AddAudit(actorUserId, "dictionary.income_type_created", "income_type", incomeType.Id, $"Создан вид поступления {incomeType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var createdDto = ToAccountingTypeDto(incomeType) with
        {
            DestinationFundName = defaultFund?.Name
        };
        return DictionaryResult<AccountingTypeDto>.Success(createdDto);
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

        var code = AccountingTypeCodePolicy.Normalize(request.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_invalid", "Код вида поступления должен начинаться с латинской буквы и содержать только строчные латинские буквы, цифры и знак подчёркивания.");
        }

        if (code is not null && AccountingTypeCodePolicy.IsReservedIncomeCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_reserved", "Этот код зарезервирован для системного вида поступления.");
        }

        if (code is not null && await incomeTypeRepository.ActiveCodeExistsAsync(id, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_duplicate", "Активный вид поступления с таким кодом уже существует.");
        }

        if (AccountingTypeMatches(incomeType, name, code))
        {
            return DictionaryResult<AccountingTypeDto>.Success(ToAccountingTypeDto(incomeType));
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
        return DictionaryResult<AccountingTypeDto>.Success(ToAccountingTypeDto(incomeType));
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

        if (await incomeTypeRepository.HasActiveServiceAssignmentsAsync(id, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure(
                "income_type_has_active_services",
                "Сначала назначьте услугам другой вид поступления или архивируйте эти услуги.");
        }

        incomeType.IsArchived = true;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_archived", "income_type", incomeType.Id, $"Архивирован вид поступления {incomeType.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(ToAccountingTypeDto(incomeType));
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

        var code = AccountingTypeCodePolicy.Normalize(incomeType.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_invalid", "Исправьте код архивного вида поступления перед восстановлением.");
        }

        if (!incomeType.IsSystem && code is not null && AccountingTypeCodePolicy.IsReservedIncomeCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_reserved", "Этот код зарезервирован для системного вида поступления.");
        }

        if (code is not null && await incomeTypeRepository.ActiveCodeExistsAsync(id, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("income_type_code_duplicate", "Активный вид поступления с таким кодом уже существует.");
        }

        incomeType.Code = code;
        incomeType.IsArchived = false;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.income_type_restored", "income_type", incomeType.Id, $"Восстановлен вид поступления {incomeType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(ToAccountingTypeDto(incomeType));
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
        var code = AccountingTypeCodePolicy.Normalize(request.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_invalid", "Код статьи расхода должен начинаться с латинской буквы и содержать только строчные латинские буквы, цифры и знак подчёркивания.");
        }

        if (code is not null && AccountingTypeCodePolicy.IsReservedExpenseCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_reserved", "Этот код зарезервирован для системной статьи расхода.");
        }

        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Статья расхода с таким названием уже существует.");
        }

        var expenseType = new ExpenseType
        {
            Name = name,
            Code = code
        };

        if (code is not null && await expenseTypeRepository.ActiveCodeExistsAsync(null, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_duplicate", "Активная статья расхода с таким кодом уже существует.");
        }

        expenseTypeRepository.Add(expenseType);
        AddAudit(actorUserId, "dictionary.expense_type_created", "expense_type", expenseType.Id, $"Создана статья расхода {expenseType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> UpdateExpenseTypeAsync(Guid id, UpsertAccountingTypeRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await expenseTypeRepository.FindActiveAsync(id, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Статья расхода не найдена.");
        }

        if (expenseType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_system", "Системную статью расхода нельзя изменять.");
        }

        var name = request.Name.Trim();
        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Статья расхода с таким названием уже существует.");
        }

        var code = AccountingTypeCodePolicy.Normalize(request.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_invalid", "Код статьи расхода должен начинаться с латинской буквы и содержать только строчные латинские буквы, цифры и знак подчёркивания.");
        }

        if (code is not null && AccountingTypeCodePolicy.IsReservedExpenseCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_reserved", "Этот код зарезервирован для системной статьи расхода.");
        }

        if (code is not null && await expenseTypeRepository.ActiveCodeExistsAsync(id, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_duplicate", "Активная статья расхода с таким кодом уже существует.");
        }

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

        AddAudit(actorUserId, "dictionary.expense_type_updated", "expense_type", expenseType.Id, $"Обновлена статья расхода {expenseType.Name}.", oldValues: oldValues, newValues: newValues);
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
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Статья расхода не найдена.");
        }

        if (expenseType.IsSystem)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_system", "Системную статью расхода нельзя архивировать.");
        }

        if (await expenseTypeRepository.HasActiveServiceAssignmentsAsync(id, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure(
                "expense_type_has_active_services",
                "Сначала назначьте услугам другую статью расхода или архивируйте эти услуги.");
        }

        expenseType.IsArchived = true;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_archived", "expense_type", expenseType.Id, $"Архивирована статья расхода {expenseType.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<DictionaryResult<AccountingTypeDto>> RestoreExpenseTypeAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expenseType = await expenseTypeRepository.FindArchivedAsync(id, cancellationToken);
        if (expenseType is null)
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_not_found", "Статья расхода не найдена в архиве.");
        }

        if (await expenseTypeRepository.ActiveDuplicateExistsAsync(id, expenseType.Name, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_duplicate", "Активная статья расхода с таким названием уже существует.");
        }

        var code = AccountingTypeCodePolicy.Normalize(expenseType.Code);
        if (!AccountingTypeCodePolicy.IsValid(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_invalid", "Исправьте код архивной статьи расхода перед восстановлением.");
        }

        if (!expenseType.IsSystem && code is not null && AccountingTypeCodePolicy.IsReservedExpenseCode(code))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_reserved", "Этот код зарезервирован для системной статьи расхода.");
        }

        if (code is not null && await expenseTypeRepository.ActiveCodeExistsAsync(id, code, cancellationToken))
        {
            return DictionaryResult<AccountingTypeDto>.Failure("expense_type_code_duplicate", "Активная статья расхода с таким кодом уже существует.");
        }

        expenseType.Code = code;
        expenseType.IsArchived = false;
        expenseType.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.expense_type_restored", "expense_type", expenseType.Id, $"Восстановлена статья расхода {expenseType.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<AccountingTypeDto>.Success(new AccountingTypeDto(expenseType.Id, expenseType.Name, expenseType.Code, expenseType.IsSystem, expenseType.IsArchived));
    }

    public async Task<IReadOnlyList<MeasurementUnitDto>> GetMeasurementUnitsAsync(string? search, CancellationToken cancellationToken, int? limit = null, bool includeArchived = false)
    {
        var units = await measurementUnitRepository.GetListAsync(NormalizeSearch(search), includeArchived, NormalizeListLimit(limit), cancellationToken);
        return units.Select(ToMeasurementUnitDto).ToList();
    }

    public async Task<PagedResult<MeasurementUnitDto>> GetMeasurementUnitsPageAsync(string? search, int? offset, int? limit, CancellationToken cancellationToken, bool includeArchived = false)
    {
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await measurementUnitRepository.GetPageAsync(NormalizeSearch(search), includeArchived, normalizedOffset, normalizedLimit, cancellationToken);
        return new PagedResult<MeasurementUnitDto>(page.Items.Select(ToMeasurementUnitDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<DictionaryResult<MeasurementUnitDto>> CreateMeasurementUnitAsync(UpsertMeasurementUnitRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var nameValidation = ValidateMeasurementUnitName(request.Name);
        if (!nameValidation.Succeeded)
        {
            return DictionaryResult<MeasurementUnitDto>.Failure(nameValidation.ErrorCode!, nameValidation.ErrorMessage!);
        }

        var name = nameValidation.Value!;
        if (await measurementUnitRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_duplicate", "Единица измерения с таким обозначением уже существует.");
        }

        var unit = new MeasurementUnit { Name = name };
        measurementUnitRepository.Add(unit);
        AddAudit(actorUserId, "dictionary.measurement_unit_created", "measurement_unit", unit.Id, $"Создана единица измерения {unit.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<MeasurementUnitDto>.Success(ToMeasurementUnitDto(unit));
    }

    public async Task<DictionaryResult<MeasurementUnitDto>> UpdateMeasurementUnitAsync(Guid id, UpsertMeasurementUnitRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var unit = await measurementUnitRepository.FindActiveAsync(id, cancellationToken);
        if (unit is null)
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_not_found", "Единица измерения не найдена.");
        }

        var nameValidation = ValidateMeasurementUnitName(request.Name);
        if (!nameValidation.Succeeded)
        {
            return DictionaryResult<MeasurementUnitDto>.Failure(nameValidation.ErrorCode!, nameValidation.ErrorMessage!);
        }

        var name = nameValidation.Value!;
        if (await measurementUnitRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_duplicate", "Единица измерения с таким обозначением уже существует.");
        }

        if (string.Equals(unit.Name, name, StringComparison.Ordinal))
        {
            return DictionaryResult<MeasurementUnitDto>.Success(ToMeasurementUnitDto(unit));
        }

        var previousName = unit.Name;
        await measurementUnitRepository.RenameServiceAssignmentsAsync(previousName, name, cancellationToken);
        unit.Name = name;
        unit.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(
            actorUserId,
            "dictionary.measurement_unit_updated",
            "measurement_unit",
            unit.Id,
            $"Изменена единица измерения {unit.Name}.",
            oldValues: new Dictionary<string, object?> { ["name"] = previousName },
            newValues: new Dictionary<string, object?> { ["name"] = name });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<MeasurementUnitDto>.Success(ToMeasurementUnitDto(unit));
    }

    public async Task<DictionaryResult<MeasurementUnitDto>> ArchiveMeasurementUnitAsync(Guid id, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (ValidateArchiveReason<MeasurementUnitDto>(reason, out var archiveReason) is { } reasonError)
        {
            return reasonError;
        }

        var unit = await measurementUnitRepository.FindActiveAsync(id, cancellationToken);
        if (unit is null)
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_not_found", "Единица измерения не найдена.");
        }

        if (await measurementUnitRepository.HasActiveServiceAssignmentsAsync(unit.Name, cancellationToken))
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_in_use", "Единица используется действующими услугами. Сначала выберите для них другое обозначение.");
        }

        unit.IsArchived = true;
        unit.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.measurement_unit_archived", "measurement_unit", unit.Id, $"Архивирована единица измерения {unit.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<MeasurementUnitDto>.Success(ToMeasurementUnitDto(unit));
    }

    public async Task<DictionaryResult<MeasurementUnitDto>> RestoreMeasurementUnitAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var unit = await measurementUnitRepository.FindArchivedAsync(id, cancellationToken);
        if (unit is null)
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_not_found", "Единица измерения не найдена в архиве.");
        }

        if (await measurementUnitRepository.ActiveDuplicateExistsAsync(id, unit.Name, cancellationToken))
        {
            return DictionaryResult<MeasurementUnitDto>.Failure("measurement_unit_duplicate", "Единица измерения с таким обозначением уже существует.");
        }

        unit.IsArchived = false;
        unit.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "dictionary.measurement_unit_restored", "measurement_unit", unit.Id, $"Восстановлена единица измерения {unit.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<MeasurementUnitDto>.Success(ToMeasurementUnitDto(unit));
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
        return QueryLimits.NormalizeListSize(limit);
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
            Comment = NormalizeOptional(request.Comment),
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

        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, tariff);

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

        var financialTermsChanged = !TariffMatches(
            tariff,
            tariff.Name,
            calculationBase,
            rate,
            request.EffectiveFrom,
            tariff.Comment,
            electricityTiers.Value);
        if (financialTermsChanged && await chargeServiceSettingRepository.HasTariffVersionAsync(tariff.Id, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure(
                "tariff_history_version_required",
                "Этот тариф уже используется услугой. Измените ставку, режим или пороги в разделе «Тарифы и сборы» и укажите дату начала новой версии.");
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

        if (await tariffRepository.HasActiveServiceAssignmentsAsync(id, cancellationToken) ||
            await chargeServiceSettingRepository.HasTariffVersionAsync(id, cancellationToken))
        {
            return DictionaryResult<TariffDto>.Failure(
                "tariff_has_active_services",
                "Тариф входит в историю услуги и нужен для расчётов прошлых периодов. Архивируйте саму услугу, если она больше не используется.");
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
        var settings = await chargeServiceSettingRepository.GetListAsync(
            normalizedSearch,
            includeArchived,
            NormalizeListLimit(limit),
            businessDateProvider.Today,
            cancellationToken);
        return settings.Select(ToChargeServiceSettingDto).ToList();
    }

    public async Task<DictionaryResult<CreatedChargeServiceWithTariffDto>> CreateChargeServiceWithTariffAsync(
        CreateChargeServiceWithTariffRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!request.Service.IsRegular)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_regular_required",
                "Тариф со ставкой можно создать только для регулярной услуги.");
        }

        if (request.Rate is < 0.0001m or > 999999999m)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(
                "charge_service_rate_invalid",
                "Стоимость услуги должна быть больше 0 и не превышать 999999999.");
        }

        var validation = ValidateChargeServiceSettingRequest(request.Service);
        if (!validation.Succeeded)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        await using var fundAllocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);
        var name = request.Service.Name.Trim();
        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(null, name, cancellationToken))
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure("charge_service_duplicate", "Услуга с таким наименованием уже существует.");
        }

        var setting = new ChargeServiceSetting { Name = name };
        var createdManagedIncomeType = false;
        var incomeType = request.Service.IncomeTypeId.HasValue
            ? await incomeTypeRepository.FindActiveAsync(request.Service.IncomeTypeId.Value, cancellationToken)
            : null;
        if (request.Service.IncomeTypeId.HasValue && incomeType is null)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure("charge_service_income_type_not_found", "Внутренняя категория поступления услуги не найдена.");
        }
        if (incomeType is null)
        {
            if (!request.IncomeFundId.HasValue || !await fundRepository.ActiveFundExistsAsync(request.IncomeFundId.Value, cancellationToken))
            {
                return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure("charge_service_fund_required", "Выберите действующий фонд поступления услуги.");
            }

            var managedIncomeTypeName = await ResolveManagedIncomeTypeNameAsync(null, name, cancellationToken);
            incomeType = new IncomeType
            {
                Name = managedIncomeTypeName,
                Code = $"service_{setting.Id:N}",
                DestinationFundId = request.IncomeFundId,
                IsSystem = true
            };
            incomeTypeRepository.Add(incomeType);
            createdManagedIncomeType = true;
            AddAudit(actorUserId, "dictionary.service_income_type_created", "income_type", incomeType.Id, $"Для услуги {name} создана внутренняя категория поступления.");
        }

        var serviceRequest = request.Service with { IncomeTypeId = incomeType.Id };
        var incomeFundUpdate = createdManagedIncomeType
            ? DictionaryResult<bool>.Success(false)
            : await ApplyRequestedIncomeFundAsync(
                incomeType.Id,
                request.IncomeFundId,
                actorUserId,
                cancellationToken);
        if (!incomeFundUpdate.Succeeded)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(incomeFundUpdate.ErrorCode!, incomeFundUpdate.ErrorMessage!);
        }

        var templateTariff = serviceRequest.TariffId.HasValue
            ? await tariffRepository.FindActiveAsync(serviceRequest.TariffId.Value, cancellationToken)
            : null;
        if (serviceRequest.TariffId.HasValue && templateTariff is null)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure("charge_service_tariff_not_found", "Исходный тариф для услуги не найден.");
        }

        var tariffName = CreateServiceTariffName(name);
        if (await tariffRepository.ActiveDuplicateExistsAsync(null, tariffName, request.EffectiveFrom, cancellationToken))
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(
                "tariff_duplicate",
                "Тариф для услуги с такой датой действия уже существует.");
        }

        var requestedMode = NormalizeOptional(request.TariffMode)
            ?? (request.Service.HasTieredTariff ? "metered_tiered" : request.Service.IsMetered ? "metered" : "regular");
        requestedMode = requestedMode.ToLowerInvariant();
        if (requestedMode is not "regular" and not "metered" and not "metered_tiered")
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_mode_invalid",
                "Режим тарифа должен быть обычным, по счетчику или по счетчику с порогами.");
        }

        var targetCalculationBase = ResolveTariffModeCalculationBase(
            requestedMode,
            incomeType.Code,
            templateTariff?.CalculationBase ?? NormalizeOptional(request.CalculationBase) ?? TariffCalculationBases.Fixed,
            NormalizeOptional(request.CalculationBase));
        if (targetCalculationBase is null)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(
                "charge_service_meter_kind_required",
                "Для расчета по счетчику выберите вид поступления «Вода» или «Электроэнергия».");
        }

        var requestedTiers = requestedMode == "metered_tiered"
            ? BuildTariffModeElectricityTiers(request.ElectricityTiers, templateTariff, MoneyMath.RoundRate(request.Rate))
            : null;
        var tariffValidationRequest = new UpsertTariffRequest(
            tariffName,
            targetCalculationBase,
            MoneyMath.RoundRate(request.Rate),
            request.EffectiveFrom,
            null,
            ElectricityTiers: requestedTiers);
        var tiersValidation = ValidateElectricityTiers(targetCalculationBase, tariffValidationRequest);
        if (!tiersValidation.Succeeded)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(tiersValidation.ErrorCode!, tiersValidation.ErrorMessage!);
        }

        var tariff = new Tariff
        {
            Name = tariffName,
            CalculationBase = targetCalculationBase,
            Rate = MoneyMath.RoundRate(request.Rate),
            EffectiveFrom = request.EffectiveFrom,
            Comment = $"Создан вместе с услугой «{name}».",
        };
        ApplyElectricityTiers(tariff, tiersValidation.Value);
        var canonicalUnitName = await EnsureMeasurementUnitExistsAsync(serviceRequest.UnitName, actorUserId, cancellationToken);
        serviceRequest = serviceRequest with { TariffId = tariff.Id, UnitName = canonicalUnitName };
        var targetLinkValidation = await ValidateChargeServiceAccountingLinksAsync(serviceRequest, cancellationToken, tariff, incomeType);
        if (!targetLinkValidation.Succeeded)
        {
            return DictionaryResult<CreatedChargeServiceWithTariffDto>.Failure(targetLinkValidation.ErrorCode!, targetLinkValidation.ErrorMessage!);
        }
        ApplyChargeServiceSetting(setting, serviceRequest);

        tariffRepository.Add(tariff);
        chargeServiceSettingRepository.Add(setting);
        await chargeServiceSettingRepository.SetTariffVersionAsync(setting.Id, tariff.Id, tariff.EffectiveFrom, cancellationToken);
        AddAudit(actorUserId, "dictionary.tariff_created", "tariff", tariff.Id, $"Создан тариф {FormatTariffAuditDetails(tariff)} вместе с услугой {name}.");
        AddAudit(actorUserId, "dictionary.charge_service_created", "charge_service", setting.Id, $"Создана настройка услуги {setting.Name} со ставкой {tariff.Rate.ToString(CultureInfo.InvariantCulture)}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DictionaryResult<CreatedChargeServiceWithTariffDto>.Success(
            new CreatedChargeServiceWithTariffDto(ToChargeServiceSettingDto(setting), ToTariffDto(tariff)));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> CreateChargeServiceSettingAsync(UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var validation = ValidateChargeServiceSettingRequest(request);
        if (!validation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        await using var fundAllocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);
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

        request = request with { UnitName = await EnsureMeasurementUnitExistsAsync(request.UnitName, actorUserId, cancellationToken) };
        var setting = new ChargeServiceSetting { Name = name };
        ApplyChargeServiceSetting(setting, request);

        chargeServiceSettingRepository.Add(setting);
        if (setting.TariffId.HasValue)
        {
            var linkedTariff = await tariffRepository.FindActiveAsync(setting.TariffId.Value, cancellationToken);
            if (linkedTariff is not null)
            {
                await chargeServiceSettingRepository.SetTariffVersionAsync(setting.Id, linkedTariff.Id, linkedTariff.EffectiveFrom, cancellationToken);
            }
        }
        AddAudit(actorUserId, "dictionary.charge_service_created", "charge_service", setting.Id, $"Создана настройка услуги {setting.Name}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> UpdateChargeServiceSettingAsync(Guid id, UpsertChargeServiceSettingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await using var fundAllocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);
        var setting = await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена.");
        }

        OptimisticConcurrencyGuard.EnsureCurrent(request.Version, setting);

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

        request = request with { UnitName = await EnsureMeasurementUnitExistsAsync(request.UnitName, actorUserId, cancellationToken) };
        var managedIncomeTypeChanged = await SynchronizeManagedServiceIncomeTypeAsync(setting, name, null, actorUserId, cancellationToken);
        if (ChargeServiceSettingMatches(setting, request))
        {
            if (managedIncomeTypeChanged)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
        }

        var previousTariffId = setting.TariffId;
        var oldValues = ToChargeServiceAuditValues(setting);
        ApplyChargeServiceSetting(setting, request);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToChargeServiceAuditValues(setting);

        if (setting.TariffId.HasValue && setting.TariffId != previousTariffId)
        {
            var linkedTariff = await tariffRepository.FindActiveAsync(setting.TariffId.Value, cancellationToken);
            if (linkedTariff is not null)
            {
                await chargeServiceSettingRepository.SetTariffVersionAsync(setting.Id, linkedTariff.Id, linkedTariff.EffectiveFrom, cancellationToken);
            }
        }

        AddAudit(actorUserId, "dictionary.charge_service_updated", "charge_service", setting.Id, $"Изменена настройка услуги {setting.Name}.", oldValues: oldValues, newValues: newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<UpdatedChargeServiceWithTariffDto>> UpdateChargeServiceWithTariffAsync(
        Guid id,
        UpdateChargeServiceWithTariffRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!request.Service.IsRegular || !request.Service.TariffId.HasValue)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_regular_required",
                "Изменить тариф можно только у регулярной услуги с назначенным тарифом.");
        }

        if (request.Rate is < 0.0001m or > 999999999m)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_rate_invalid",
                "Тариф услуги должен быть больше 0 и не превышать 999999999.");
        }

        await using var fundAllocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);
        var setting = await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_not_found",
                "Настройка услуги не найдена.");
        }

        OptimisticConcurrencyGuard.EnsureCurrent(request.Service.Version, setting);

        var validation = ValidateChargeServiceSettingRequest(request.Service);
        if (!validation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var name = request.Service.Name.Trim();
        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(id, name, cancellationToken))
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_duplicate",
                "Услуга с таким наименованием уже существует.");
        }

        var incomeFundUpdate = await ApplyRequestedIncomeFundAsync(
            request.Service.IncomeTypeId,
            request.IncomeFundId,
            actorUserId,
            cancellationToken);
        if (!incomeFundUpdate.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(incomeFundUpdate.ErrorCode!, incomeFundUpdate.ErrorMessage!);
        }
        var managedIncomeTypeChanged = await SynchronizeManagedServiceIncomeTypeAsync(
            setting,
            name,
            request.IncomeFundId,
            actorUserId,
            cancellationToken);

        request = request with
        {
            Service = request.Service with
            {
                UnitName = await EnsureMeasurementUnitExistsAsync(request.Service.UnitName, actorUserId, cancellationToken)
            }
        };

        if (!string.IsNullOrWhiteSpace(request.TariffMode))
        {
            return await ChangeChargeServiceTariffModeAsync(setting, request, name, actorUserId, cancellationToken);
        }

        var linkValidation = await ValidateChargeServiceAccountingLinksAsync(request.Service, cancellationToken);
        if (!linkValidation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(linkValidation.ErrorCode!, linkValidation.ErrorMessage!);
        }

        var tariff = await tariffRepository.FindActiveAsync(request.Service.TariffId!.Value, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_not_found",
                "Тариф для услуги не найден.");
        }

        OptimisticConcurrencyGuard.EnsureCurrent(request.TariffVersion, tariff);

        var roundedRate = MoneyMath.RoundRate(request.Rate);
        if (tariff.Rate != roundedRate && request.EffectiveFrom.HasValue && request.EffectiveFrom.Value != tariff.EffectiveFrom)
        {
            return await CreateChargeServiceTariffRateVersionAsync(
                setting,
                tariff,
                request,
                name,
                roundedRate,
                actorUserId,
                cancellationToken);
        }

        var serviceChanged = !ChargeServiceSettingMatches(setting, request.Service);
        var tariffChanged = tariff.Rate != roundedRate;
        if (!serviceChanged && !tariffChanged)
        {
            if (incomeFundUpdate.Value == true || managedIncomeTypeChanged)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Success(
                new UpdatedChargeServiceWithTariffDto(ToChargeServiceSettingDto(setting), ToTariffDto(tariff)));
        }

        var now = DateTimeOffset.UtcNow;
        if (serviceChanged)
        {
            var oldValues = ToChargeServiceAuditValues(setting);
            ApplyChargeServiceSetting(setting, request.Service);
            setting.UpdatedAtUtc = now;
            var newValues = ToChargeServiceAuditValues(setting);
            AddAudit(
                actorUserId,
                "dictionary.charge_service_updated",
                "charge_service",
                setting.Id,
                $"Изменена настройка услуги {setting.Name}.",
                oldValues: oldValues,
                newValues: newValues);
        }

        if (tariffChanged)
        {
            var oldValues = new Dictionary<string, object?> { ["rate"] = tariff.Rate };
            tariff.Rate = roundedRate;
            tariff.UpdatedAtUtc = now;
            var newValues = new Dictionary<string, object?> { ["rate"] = tariff.Rate };
            AddAudit(
                actorUserId,
                "dictionary.tariff_updated",
                "tariff",
                tariff.Id,
                $"Изменен тариф {FormatTariffAuditDetails(tariff)} вместе с услугой {name}.",
                oldValues: oldValues,
                newValues: newValues);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Success(
            new UpdatedChargeServiceWithTariffDto(ToChargeServiceSettingDto(setting), ToTariffDto(tariff)));
    }

    public async Task<DictionaryResult<IReadOnlyList<ChargeServiceTariffPeriodDto>>> GetChargeServiceTariffScheduleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken) is null)
        {
            return DictionaryResult<IReadOnlyList<ChargeServiceTariffPeriodDto>>.Failure(
                "charge_service_not_found",
                "Настройка услуги не найдена.");
        }

        var periods = await chargeServiceSettingRepository.GetTariffPeriodsAsync(id, false, cancellationToken);
        return DictionaryResult<IReadOnlyList<ChargeServiceTariffPeriodDto>>.Success(periods.Select(ToChargeServiceTariffPeriodDto).ToList());
    }

    public async Task<DictionaryResult<UpdatedChargeServiceTariffScheduleDto>> UpdateChargeServiceTariffScheduleAsync(
        Guid id,
        UpsertChargeServiceTariffScheduleRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var setting = await chargeServiceSettingRepository.FindActiveAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<UpdatedChargeServiceTariffScheduleDto>.Failure(
                "charge_service_not_found",
                "Настройка услуги не найдена.");
        }

        OptimisticConcurrencyGuard.EnsureCurrent(request.ServiceVersion, setting);
        if (!setting.IsRegular || !setting.TariffId.HasValue)
        {
            return DictionaryResult<UpdatedChargeServiceTariffScheduleDto>.Failure(
                "charge_service_tariff_regular_required",
                "Тарифную сетку можно настроить только для регулярной услуги с тарифом.");
        }

        var validation = ValidateTariffSchedule(request.Periods, request.AllowGaps);
        if (!validation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceTariffScheduleDto>.Failure(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var allExisting = await chargeServiceSettingRepository.GetTariffPeriodsAsync(id, true, cancellationToken);
        var existing = allExisting.Where(item => !item.IsArchived).ToList();
        var existingByTariff = existing.ToDictionary(item => item.TariffId);
        var fallbackTariff = existing.LastOrDefault()?.Tariff
            ?? await tariffRepository.FindActiveAsync(setting.TariffId.Value, cancellationToken);
        if (fallbackTariff is null)
        {
            return DictionaryResult<UpdatedChargeServiceTariffScheduleDto>.Failure(
                "charge_service_tariff_not_found",
                "Действующий тариф услуги не найден.");
        }

        var replacements = new List<ChargeServiceTariffVersion>(request.Periods.Count);
        var usedTariffIds = new HashSet<Guid>();
        foreach (var period in request.Periods.OrderBy(item => item.EffectiveFrom ?? OpenTariffScheduleStart))
        {
            var startsOn = period.EffectiveFrom ?? OpenTariffScheduleStart;
            var source = period.TariffId.HasValue && existingByTariff.TryGetValue(period.TariffId.Value, out var existingPeriod)
                ? existingPeriod.Tariff
                : fallbackTariff;
            if (period.TariffVersion.HasValue)
            {
                OptimisticConcurrencyGuard.EnsureCurrent(period.TariffVersion, source);
            }

            var roundedRate = MoneyMath.RoundRate(period.Rate);
            var canReuse = existingByTariff.TryGetValue(source.Id, out var sourcePeriod)
                && sourcePeriod.EffectiveFrom == startsOn
                && source.Rate == roundedRate
                && usedTariffIds.Add(source.Id);
            var tariff = canReuse ? source : CloneTariffForSchedule(source, setting.Name, startsOn, roundedRate, request.ChangeReason);
            if (!canReuse)
            {
                usedTariffIds.Add(tariff.Id);
                tariffRepository.Add(tariff);
            }

            replacements.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = id,
                TariffId = tariff.Id,
                Tariff = tariff,
                EffectiveFrom = startsOn,
                EffectiveTo = period.EffectiveTo
            });
        }

        chargeServiceSettingRepository.ReplaceTariffPeriods(id, allExisting, replacements);
        var businessDate = businessDateProvider.Today;
        var currentPeriod = replacements
            .Where(item => item.EffectiveFrom <= businessDate && (!item.EffectiveTo.HasValue || item.EffectiveTo.Value >= businessDate))
            .OrderByDescending(item => item.EffectiveFrom)
            .FirstOrDefault();
        // If the user explicitly saved a gap covering the working date, keep the
        // latest configured tariff as the service fallback. Date-aware accrual
        // and list queries still correctly return no tariff for that gap.
        var current = (currentPeriod ?? replacements[^1]).Tariff;
        setting.TariffId = current.Id;
        setting.Tariff = current;
        setting.IsMetered = current.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;
        setting.HasTieredTariff = setting.IsMetered &&
            (!string.IsNullOrWhiteSpace(current.ElectricityTiersJson) ||
             current.ElectricityFirstRate.HasValue && current.ElectricitySecondRate.HasValue);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(
            actorUserId,
            "dictionary.charge_service_tariff_schedule_updated",
            "charge_service",
            setting.Id,
            $"Обновлена тарифная сетка услуги {setting.Name}: {replacements.Count} период(ов).",
            NormalizeOptional(request.ChangeReason));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DictionaryResult<UpdatedChargeServiceTariffScheduleDto>.Success(
            new UpdatedChargeServiceTariffScheduleDto(
                ToChargeServiceSettingDto(setting),
                ToTariffDto(current),
                replacements.Select(ToChargeServiceTariffPeriodDto).ToList()));
    }

    private static DictionaryResult<bool> ValidateTariffSchedule(
        IReadOnlyList<UpsertChargeServiceTariffPeriodRequest> periods,
        bool allowGaps)
    {
        if (periods.Count is < 1 or > 120)
        {
            return DictionaryResult<bool>.Failure("tariff_schedule_count_invalid", "Укажите от 1 до 120 периодов тарифа.");
        }

        var ordered = periods.OrderBy(item => item.EffectiveFrom ?? OpenTariffScheduleStart).ToList();
        foreach (var period in ordered)
        {
            if (period.Rate is < 0.0001m or > 999999999m)
            {
                return DictionaryResult<bool>.Failure("tariff_schedule_rate_invalid", "Значение тарифа должно быть больше 0 и не превышать 999999999.");
            }

            if (period.EffectiveFrom.HasValue && period.EffectiveTo.HasValue && period.EffectiveFrom.Value > period.EffectiveTo.Value)
            {
                return DictionaryResult<bool>.Failure("tariff_schedule_range_invalid", "Конечная дата тарифа не может быть раньше начальной.");
            }
        }

        var hasGap = ordered[0].EffectiveFrom.HasValue || ordered[^1].EffectiveTo.HasValue;
        for (var index = 1; index < ordered.Count; index++)
        {
            var previousEnd = ordered[index - 1].EffectiveTo;
            var currentStart = ordered[index].EffectiveFrom ?? OpenTariffScheduleStart;
            if (!previousEnd.HasValue || currentStart <= previousEnd.Value)
            {
                return DictionaryResult<bool>.Failure("tariff_schedule_overlap", "Периоды тарифов пересекаются. Исправьте начальные и конечные даты.");
            }

            hasGap |= currentStart > previousEnd.Value.AddDays(1);
        }

        if (hasGap && !allowGaps)
        {
            return DictionaryResult<bool>.Failure(
                "tariff_schedule_gap",
                "В тарифной сетке есть период без тарифа. Заполните разрыв или явно подтвердите сохранение с разрывами.");
        }

        return DictionaryResult<bool>.Success(true);
    }

    private static Tariff CloneTariffForSchedule(
        Tariff source,
        string serviceName,
        DateOnly effectiveFrom,
        decimal rate,
        string? changeReason) => new()
        {
            Name = CreateServiceTariffVersionName(
                serviceName,
                !string.IsNullOrWhiteSpace(source.ElectricityTiersJson) ? "metered_tiered" :
                    source.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity ? "metered" : "regular"),
            CalculationBase = source.CalculationBase,
            Rate = rate,
            EffectiveFrom = effectiveFrom,
            Comment = NormalizeOptional(changeReason) ?? $"Период тарифной сетки услуги «{serviceName}».",
            ElectricityFirstThreshold = source.ElectricityFirstThreshold,
            ElectricitySecondThreshold = source.ElectricitySecondThreshold,
            ElectricityFirstTierName = source.ElectricityFirstTierName,
            ElectricitySecondTierName = source.ElectricitySecondTierName,
            ElectricityThirdTierName = source.ElectricityThirdTierName,
            ElectricityFirstRate = source.ElectricityFirstRate,
            ElectricitySecondRate = source.ElectricitySecondRate,
            ElectricityThirdRate = source.ElectricityThirdRate,
            ElectricityTiersJson = source.ElectricityTiersJson
        };

    private static ChargeServiceTariffPeriodDto ToChargeServiceTariffPeriodDto(ChargeServiceTariffVersion period) => new(
        period.TariffId,
        period.EffectiveFrom == OpenTariffScheduleStart ? null : period.EffectiveFrom,
        period.EffectiveTo,
        period.Tariff.Rate,
        period.Tariff.Version);

    private async Task<DictionaryResult<UpdatedChargeServiceWithTariffDto>> CreateChargeServiceTariffRateVersionAsync(
        ChargeServiceSetting setting,
        Tariff sourceTariff,
        UpdateChargeServiceWithTariffRequest request,
        string serviceName,
        decimal roundedRate,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var effectiveFrom = request.EffectiveFrom!.Value;
        var tariff = await chargeServiceSettingRepository.FindTariffVersionAsync(
            setting.Id,
            effectiveFrom,
            cancellationToken);
        var createdTariff = tariff is null;
        tariff ??= new Tariff
        {
            Name = CreateServiceTariffVersionName(
                serviceName,
                setting.HasTieredTariff ? "metered_tiered" : setting.IsMetered ? "metered" : "regular"),
            CalculationBase = sourceTariff.CalculationBase,
            Rate = roundedRate,
            EffectiveFrom = effectiveFrom,
            Comment = NormalizeOptional(request.ChangeReason) ?? $"Изменение ставки услуги «{serviceName}».",
            ElectricityFirstThreshold = sourceTariff.ElectricityFirstThreshold,
            ElectricitySecondThreshold = sourceTariff.ElectricitySecondThreshold,
            ElectricityFirstTierName = sourceTariff.ElectricityFirstTierName,
            ElectricitySecondTierName = sourceTariff.ElectricitySecondTierName,
            ElectricityThirdTierName = sourceTariff.ElectricityThirdTierName,
            ElectricityFirstRate = sourceTariff.ElectricityFirstRate,
            ElectricitySecondRate = sourceTariff.ElectricitySecondRate,
            ElectricityThirdRate = sourceTariff.ElectricityThirdRate,
            ElectricityTiersJson = sourceTariff.ElectricityTiersJson,
        };
        CopyTariffVersionTerms(
            tariff,
            sourceTariff,
            serviceName,
            setting.HasTieredTariff ? "metered_tiered" : setting.IsMetered ? "metered" : "regular",
            roundedRate,
            effectiveFrom,
            NormalizeOptional(request.ChangeReason));

        var targetServiceRequest = request.Service with { TariffId = tariff.Id };
        var linkValidation = await ValidateChargeServiceAccountingLinksAsync(targetServiceRequest, cancellationToken, tariff);
        if (!linkValidation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(linkValidation.ErrorCode!, linkValidation.ErrorMessage!);
        }

        var becomesCurrent = effectiveFrom >= sourceTariff.EffectiveFrom;
        var serviceRequest = becomesCurrent
            ? targetServiceRequest
            : request.Service with { TariffId = sourceTariff.Id };

        var oldValues = ToChargeServiceAuditValues(setting);
        ApplyChargeServiceSetting(setting, serviceRequest);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (createdTariff)
        {
            tariffRepository.Add(tariff);
        }
        await chargeServiceSettingRepository.SetTariffVersionAsync(
            setting.Id,
            sourceTariff.Id,
            sourceTariff.EffectiveFrom,
            cancellationToken);
        await chargeServiceSettingRepository.SetTariffVersionAsync(setting.Id, tariff.Id, effectiveFrom, cancellationToken);
        AddAudit(
            actorUserId,
            createdTariff ? "dictionary.tariff_created" : "dictionary.tariff_updated",
            "tariff",
            tariff.Id,
            createdTariff
                ? $"Создана версия ставки {FormatTariffAuditDetails(tariff)} для услуги {serviceName}."
                : $"Обновлена версия ставки {FormatTariffAuditDetails(tariff)} для услуги {serviceName}.",
            NormalizeOptional(request.ChangeReason));
        AddAudit(
            actorUserId,
            "dictionary.charge_service_tariff_version_changed",
            "charge_service",
            setting.Id,
            $"Для услуги {serviceName} добавлена ставка {roundedRate.ToString(CultureInfo.InvariantCulture)} с {effectiveFrom:dd.MM.yyyy}.",
            NormalizeOptional(request.ChangeReason),
            oldValues,
            ToChargeServiceAuditValues(setting));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Success(
            new UpdatedChargeServiceWithTariffDto(ToChargeServiceSettingDto(setting), ToTariffDto(tariff)));
    }

    private async Task<DictionaryResult<UpdatedChargeServiceWithTariffDto>> ChangeChargeServiceTariffModeAsync(
        ChargeServiceSetting setting,
        UpdateChargeServiceWithTariffRequest request,
        string serviceName,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var mode = request.TariffMode!.Trim().ToLowerInvariant();
        if (mode is not "regular" and not "metered" and not "metered_tiered")
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_mode_invalid",
                "Режим тарифа должен быть обычным, по счетчику или по счетчику с порогами.");
        }

        if (!request.EffectiveFrom.HasValue)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_mode_date_required",
                "Укажите дату начала действия новой версии тарифа.");
        }

        var sourceTariffId = setting.TariffId ?? request.Service.TariffId;
        var sourceTariff = sourceTariffId.HasValue
            ? await tariffRepository.FindActiveAsync(sourceTariffId.Value, cancellationToken)
            : null;
        if (sourceTariff is null)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_not_found",
                "Действующий тариф услуги не найден.");
        }

        OptimisticConcurrencyGuard.EnsureCurrent(request.TariffVersion, sourceTariff);

        var incomeType = request.Service.IncomeTypeId.HasValue
            ? await incomeTypeRepository.FindActiveAsync(request.Service.IncomeTypeId.Value, cancellationToken)
            : null;
        if (incomeType is null)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_income_type_not_found",
                "Вид поступления для услуги не найден.");
        }

        var targetCalculationBase = ResolveTariffModeCalculationBase(
            mode,
            incomeType.Code,
            sourceTariff.CalculationBase,
            NormalizeOptional(request.CalculationBase));
        if (targetCalculationBase is null)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_meter_kind_required",
                "Для расчета по счетчику выберите вид поступления «Вода» или «Электроэнергия».");
        }

        var isMetered = mode is "metered" or "metered_tiered";
        var isTiered = mode == "metered_tiered";
        if (isTiered && !IsMeterCalculationBase(targetCalculationBase))
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tiered_meter_required",
                "Пороговый режим доступен только для тарифа по счетчику.");
        }

        if (request.Service.IsMetered != isMetered || request.Service.HasTieredTariff != isTiered)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(
                "charge_service_tariff_mode_mismatch",
                "Параметры услуги не соответствуют выбранному режиму тарифа. Обновите страницу и повторите действие.");
        }

        var roundedRate = MoneyMath.RoundRate(request.Rate);
        var requestedTiers = isTiered
            ? BuildTariffModeElectricityTiers(request.ElectricityTiers, sourceTariff, roundedRate)
            : null;
        var tariffValidationRequest = new UpsertTariffRequest(
            serviceName,
            targetCalculationBase,
            roundedRate,
            request.EffectiveFrom.Value,
            request.ChangeReason,
            ElectricityTiers: requestedTiers);
        var tiersValidation = ValidateElectricityTiers(targetCalculationBase, tariffValidationRequest);
        if (!tiersValidation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(tiersValidation.ErrorCode!, tiersValidation.ErrorMessage!);
        }

        var tariff = await chargeServiceSettingRepository.FindTariffVersionAsync(
            setting.Id,
            request.EffectiveFrom.Value,
            cancellationToken);
        var createdTariff = tariff is null;
        tariff ??= new Tariff
        {
            Name = CreateServiceTariffVersionName(serviceName, mode),
            CalculationBase = targetCalculationBase,
            Rate = roundedRate,
            EffectiveFrom = request.EffectiveFrom.Value,
            Comment = NormalizeOptional(request.ChangeReason) ?? $"Новая версия режима услуги «{serviceName}».",
        };
        tariff.Name = CreateServiceTariffVersionName(serviceName, mode);
        tariff.CalculationBase = targetCalculationBase;
        tariff.Rate = roundedRate;
        tariff.Comment = NormalizeOptional(request.ChangeReason) ?? tariff.Comment;
        tariff.IsArchived = false;
        tariff.UpdatedAtUtc = DateTimeOffset.UtcNow;
        ApplyElectricityTiers(tariff, tiersValidation.Value);

        var targetServiceRequest = request.Service with
        {
            TariffId = tariff.Id,
            IsMetered = isMetered,
            HasTieredTariff = isTiered,
            UnitName = request.Service.UnitName
        };
        var linkValidation = await ValidateChargeServiceAccountingLinksAsync(targetServiceRequest, cancellationToken, tariff);
        if (!linkValidation.Succeeded)
        {
            return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Failure(linkValidation.ErrorCode!, linkValidation.ErrorMessage!);
        }


        var becomesCurrent = tariff.EffectiveFrom >= sourceTariff.EffectiveFrom;
        var serviceRequest = becomesCurrent
            ? targetServiceRequest
            : request.Service with
            {
                TariffId = sourceTariff.Id,
                IsMetered = setting.IsMetered,
                HasTieredTariff = setting.HasTieredTariff,
                UnitName = setting.UnitName
            };

        var oldValues = ToChargeServiceAuditValues(setting);
        ApplyChargeServiceSetting(setting, serviceRequest);
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (createdTariff)
        {
            tariffRepository.Add(tariff);
        }
        await chargeServiceSettingRepository.SetTariffVersionAsync(
            setting.Id,
            sourceTariff.Id,
            sourceTariff.EffectiveFrom,
            cancellationToken);
        await chargeServiceSettingRepository.SetTariffVersionAsync(setting.Id, tariff.Id, tariff.EffectiveFrom, cancellationToken);
        AddAudit(
            actorUserId,
            createdTariff ? "dictionary.tariff_created" : "dictionary.tariff_updated",
            "tariff",
            tariff.Id,
            createdTariff
                ? $"Создана версия тарифа {FormatTariffAuditDetails(tariff)} при смене режима услуги {serviceName}."
                : $"Обновлена версия тарифа {FormatTariffAuditDetails(tariff)} при смене режима услуги {serviceName}.",
            NormalizeOptional(request.ChangeReason));
        AddAudit(
            actorUserId,
            "dictionary.charge_service_tariff_mode_changed",
            "charge_service",
            setting.Id,
            $"Режим услуги {serviceName} изменен на {FormatTariffMode(mode)}.",
            NormalizeOptional(request.ChangeReason),
            oldValues,
            ToChargeServiceAuditValues(setting));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DictionaryResult<UpdatedChargeServiceWithTariffDto>.Success(
            new UpdatedChargeServiceWithTariffDto(ToChargeServiceSettingDto(setting), ToTariffDto(tariff)));
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

        if (await supplierRepository.HasActiveServiceAssignmentsAsync(id, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(
                "charge_service_has_active_suppliers",
                "Сначала назначьте активным поставщикам другую услугу или архивируйте этих поставщиков.");
        }

        setting.IsArchived = true;
        setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddAudit(actorUserId, "dictionary.charge_service_archived", "charge_service", setting.Id, $"Архивирована настройка услуги {setting.Name}.", archiveReason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<ChargeServiceSettingDto>.Success(ToChargeServiceSettingDto(setting));
    }

    public async Task<DictionaryResult<ChargeServiceSettingDto>> RestoreChargeServiceSettingAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await using var fundAllocationLock = await fundRepository.AcquireAllocationLockAsync(cancellationToken);
        var setting = await chargeServiceSettingRepository.FindArchivedAsync(id, cancellationToken);
        if (setting is null)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_not_found", "Настройка услуги не найдена в архиве.");
        }

        if (await chargeServiceSettingRepository.ActiveDuplicateExistsAsync(id, setting.Name, cancellationToken))
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure("charge_service_duplicate", "Активная услуга с таким наименованием уже существует.");
        }

        var fundValidation = await ValidateChargeServiceFundAsync(setting.IncomeTypeId, cancellationToken);
        if (!fundValidation.Succeeded)
        {
            return DictionaryResult<ChargeServiceSettingDto>.Failure(fundValidation.ErrorCode!, fundValidation.ErrorMessage!);
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
        var collectedAmounts = await feeCampaignRepository.GetCollectedAmountsAsync(
            campaigns.Select(campaign => campaign.Id).ToArray(),
            cancellationToken);
        return campaigns.Select(campaign => ToFeeCampaignDto(campaign, collectedAmounts.GetValueOrDefault(campaign.Id))).ToList();
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

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_income_type_not_found",
                "Выбранное назначение поступления не найдено.");
        }
        if (!incomeType.DestinationFundId.HasValue ||
            !await fundRepository.ActiveFundExistsAsync(incomeType.DestinationFundId.Value, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_fund_not_found",
                "Для выбранного назначения поступления должен быть настроен действующий фонд.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        var amounts = await CalculateFeeCampaignAmountsAsync(request, participants.Value!, cancellationToken);
        if (request.AmountCalculationMode == FeeCampaignAmountCalculationModes.Target && amounts.ContributionAmount <= 0m)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_participants_required", "Для расчёта суммы сбора нужен хотя бы один действующий гараж.");
        }

        var campaign = new FeeCampaign { Name = name };
        ApplyFeeCampaign(campaign, request, incomeType.Id, amounts.ContributionAmount, amounts.TargetAmount);
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

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_income_type_not_found",
                "Выбранное назначение поступления не найдено.");
        }
        if (!incomeType.DestinationFundId.HasValue ||
            !await fundRepository.ActiveFundExistsAsync(incomeType.DestinationFundId.Value, cancellationToken))
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_fund_not_found",
                "Для выбранного назначения поступления должен быть настроен действующий фонд.");
        }

        var participants = await ResolveFeeCampaignParticipantsAsync(request, cancellationToken);
        if (!participants.Succeeded)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(participants.ErrorCode!, participants.ErrorMessage!);
        }

        var amounts = await CalculateFeeCampaignAmountsAsync(request, participants.Value!, cancellationToken);
        if (request.AmountCalculationMode == FeeCampaignAmountCalculationModes.Target && amounts.ContributionAmount <= 0m)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_participants_required", "Для расчёта суммы сбора нужен хотя бы один действующий гараж.");
        }

        var participantsChanged = !FeeCampaignParticipantsMatch(campaign, request, participants.Value!);
        var incomeTypeChanged = campaign.IncomeTypeId != incomeType.Id;
        var hasAccruals = (participantsChanged || incomeTypeChanged) &&
            await feeCampaignRepository.HasAccrualsAsync(campaign.Id, cancellationToken);
        if (participantsChanged && hasAccruals)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_participants_locked",
                "Нельзя изменить состав участников сбора после создания начислений. Исторический состав должен оставаться неизменным.");
        }
        if (incomeTypeChanged && hasAccruals)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_income_type_locked",
                "Нельзя изменить назначение поступления после создания начислений по сбору. Исторические проводки должны оставаться в прежнем фонде.");
        }

        if (FeeCampaignMatches(campaign, request, participants.Value!, incomeType.Id, amounts.ContributionAmount, amounts.TargetAmount))
        {
            return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
        }

        var oldValues = ToFeeCampaignAuditValues(campaign);
        ApplyFeeCampaign(campaign, request, incomeType.Id, amounts.ContributionAmount, amounts.TargetAmount);
        campaign.IncomeType = incomeType;
        SyncFeeCampaignParticipants(campaign, participants.Value!);
        campaign.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = ToFeeCampaignAuditValues(campaign);

        AddAudit(actorUserId, "dictionary.fee_campaign_updated", "fee_campaign", campaign.Id, $"Изменен сбор {campaign.Name}.", oldValues: oldValues, newValues: newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<FeeCampaignDto>.Success(ToFeeCampaignDto(campaign));
    }

    public async Task<DictionaryResult<FeeCampaignDto>> CloseFeeCampaignAsync(
        Guid id,
        CloseFeeCampaignRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var campaign = await feeCampaignRepository.FindActiveWithDetailsAsync(id, cancellationToken);
        if (campaign is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        if (campaign.ClosedAtUtc.HasValue)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_already_closed", "Сбор уже закрыт.");
        }

        var comment = NormalizeOptional(request.Comment);
        if (comment?.Length > 1000)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_closure_comment_too_long", "Комментарий не должен превышать 1000 символов.");
        }

        var collectedAmount = decimal.Round(
            await feeCampaignRepository.GetCollectedAmountAsync(id, cancellationToken),
            2,
            MidpointRounding.AwayFromZero);
        var isClosedEarly = collectedAmount < campaign.TargetAmount;
        if (isClosedEarly && comment is null)
        {
            return DictionaryResult<FeeCampaignDto>.Failure(
                "fee_campaign_closure_comment_required",
                "Для досрочного закрытия сбора укажите обязательный комментарий.");
        }

        campaign.ClosedAtUtc = DateTimeOffset.UtcNow;
        campaign.ClosedByUserId = actorUserId;
        campaign.IsClosedEarly = isClosedEarly;
        campaign.ClosureComment = comment;
        campaign.UpdatedAtUtc = campaign.ClosedAtUtc.Value;

        AddAudit(
            actorUserId,
            "dictionary.fee_campaign_closed",
            "fee_campaign",
            campaign.Id,
            isClosedEarly
                ? $"Досрочно закрыт сбор {campaign.Name}. Собрано {collectedAmount:F2} из {campaign.TargetAmount:F2}."
                : $"Закрыт сбор {campaign.Name}. План {campaign.TargetAmount:F2} выполнен.",
            comment,
            newValues: ToFeeCampaignAuditValues(campaign));
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

    private async Task<IReadOnlyList<OpeningBalanceAdjustmentDto>> GetOpeningBalanceAdjustmentsAsync(
        string targetKind,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var items = await openingBalanceAdjustmentRepository.GetListAsync(targetKind, targetId, cancellationToken);
        return items.Select(ToOpeningBalanceAdjustmentDto).ToList();
    }

    private static DictionaryResult<OpeningBalanceAdjustmentDto>? ValidateOpeningBalanceAdjustment(CreateOpeningBalanceAdjustmentRequest request)
    {
        if (request.EffectiveDate == default)
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("opening_balance_effective_date_required", "Укажите дату корректировки.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("opening_balance_reason_required", "Укажите причину корректировки начального баланса.");
        }

        if (request.Reason.Trim().Length > 1000)
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("opening_balance_reason_too_long", "Причина корректировки не должна быть длиннее 1000 символов.");
        }

        return null;
    }

    private async Task<DictionaryResult<OpeningBalanceAdjustmentDto>> SaveOpeningBalanceAdjustmentAsync(
        string targetKind,
        Guid targetId,
        string targetName,
        decimal previousAmount,
        CreateOpeningBalanceAdjustmentRequest request,
        Guid? actorUserId,
        Action<decimal> updateAmount,
        Action touchTarget,
        CancellationToken cancellationToken)
    {
        var newAmount = MoneyMath.RoundMoney(request.NewAmount);
        previousAmount = MoneyMath.RoundMoney(previousAmount);
        if (newAmount == previousAmount)
        {
            return DictionaryResult<OpeningBalanceAdjustmentDto>.Failure("opening_balance_unchanged", "Новое значение совпадает с действующим начальным балансом.");
        }

        var adjustment = new OpeningBalanceAdjustment
        {
            TargetKind = targetKind,
            TargetId = targetId,
            EffectiveDate = request.EffectiveDate,
            PreviousAmount = previousAmount,
            NewAmount = newAmount,
            Reason = request.Reason.Trim(),
            CreatedByUserId = actorUserId
        };
        updateAmount(newAmount);
        touchTarget();
        openingBalanceAdjustmentRepository.Add(adjustment);

        var entityLabel = targetKind == OpeningBalanceAdjustmentTargetKinds.Garage ? "гаража" : "поставщика";
        AddAudit(
            actorUserId,
            $"dictionary.{targetKind}_opening_balance_adjusted",
            "opening_balance_adjustment",
            adjustment.Id,
            $"Скорректирован начальный баланс {entityLabel} {targetName}.",
            adjustment.Reason,
            new Dictionary<string, object?> { ["startingBalance"] = previousAmount },
            new Dictionary<string, object?> { ["startingBalance"] = newAmount });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DictionaryResult<OpeningBalanceAdjustmentDto>.Success(ToOpeningBalanceAdjustmentDto(adjustment));
    }

    private static OpeningBalanceAdjustmentDto ToOpeningBalanceAdjustmentDto(OpeningBalanceAdjustment adjustment) => new(
        adjustment.Id,
        adjustment.TargetKind,
        adjustment.TargetId,
        adjustment.EffectiveDate,
        adjustment.PreviousAmount,
        adjustment.NewAmount,
        adjustment.Reason,
        adjustment.CreatedByUserId,
        adjustment.CreatedAtUtc);

    private static MeasurementUnitDto ToMeasurementUnitDto(MeasurementUnit unit) => new(unit.Id, unit.Name, unit.IsArchived);

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

        var lowerBound = 0m;
        var tierDetails = string.Join(", ", tiers.Select(tier =>
        {
            var rangeName = FormatElectricityTierRangeName(
                lowerBound,
                tier.UpperBound,
                TariffCalculationBases.GetUnitName(tariff.CalculationBase));
            if (tier.UpperBound.HasValue)
            {
                lowerBound = tier.UpperBound.Value;
            }

            return $"{rangeName} по {MoneyFormatting.Format(tier.Rate)}";
        }));
        return $"{baseDetails}, пороги: {tierDetails}";
    }

    private static DictionaryResult<ElectricityTierConfig?> ValidateElectricityTiers(string calculationBase, UpsertTariffRequest request, Tariff? existingTariff = null)
    {
        if (!IsMeterCalculationBase(calculationBase))
        {
            return DictionaryResult<ElectricityTierConfig?>.Success(null);
        }

        var unitName = TariffCalculationBases.GetUnitName(calculationBase);

        if (request.ElectricityTiers is not null)
        {
            if (request.ElectricityTiers.Count is < 2 or > 20)
            {
                return DictionaryResult<ElectricityTierConfig?>.Failure(
                    "tariff_electricity_tier_count_invalid",
                    "Укажите от 2 до 20 тарифных ступеней: минимум один порог и последнюю ступень без верхней границы.");
            }

            var existingTiers = existingTariff is null
                ? []
                : ReadElectricityTiers(existingTariff);
            var normalized = new List<ElectricityTierConfigItem>(request.ElectricityTiers.Count);
            decimal? previousUpperBound = null;
            for (var index = 0; index < request.ElectricityTiers.Count; index++)
            {
                var requestedTier = request.ElectricityTiers[index];
                var name = $"Ступень {index + 1}";

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
                        "Последняя ступень тарифа должна применяться без верхней границы.");
                }

                if (upperBound.HasValue && (upperBound <= 0 || previousUpperBound.HasValue && upperBound <= previousUpperBound))
                {
                    return DictionaryResult<ElectricityTierConfig?>.Failure(
                        "tariff_electricity_tier_upper_bound_invalid",
                        "Границы тарифных ступеней должны быть положительными и строго возрастать.");
                }

                name = FormatElectricityTierRangeName(previousUpperBound ?? 0m, upperBound, unitName);

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
        var firstTierName = FormatElectricityTierRangeName(0m, firstThreshold, unitName);
        var secondTierName = FormatElectricityTierRangeName(firstThreshold, secondThreshold, unitName);
        var thirdTierName = FormatElectricityTierRangeName(secondThreshold, null, unitName);
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

    private static string FormatElectricityTierRangeName(decimal lowerBound, decimal? upperBound, string unitName = "кВт·ч")
    {
        var lower = lowerBound.ToString("0.####", CultureInfo.InvariantCulture);
        return upperBound.HasValue
            ? $"{lower}–{upperBound.Value.ToString("0.####", CultureInfo.InvariantCulture)} {unitName}"
            : $"{lower}+ {unitName}";
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

    private static void CopyTariffVersionTerms(
        Tariff target,
        Tariff source,
        string serviceName,
        string mode,
        decimal rate,
        DateOnly effectiveFrom,
        string? changeReason)
    {
        target.Name = CreateServiceTariffVersionName(serviceName, mode);
        target.CalculationBase = source.CalculationBase;
        target.Rate = rate;
        target.EffectiveFrom = effectiveFrom;
        target.Comment = changeReason ?? $"Изменение ставки услуги «{serviceName}».";
        target.ElectricityFirstThreshold = source.ElectricityFirstThreshold;
        target.ElectricitySecondThreshold = source.ElectricitySecondThreshold;
        target.ElectricityFirstTierName = source.ElectricityFirstTierName;
        target.ElectricitySecondTierName = source.ElectricitySecondTierName;
        target.ElectricityThirdTierName = source.ElectricityThirdTierName;
        target.ElectricityFirstRate = source.ElectricityFirstRate;
        target.ElectricitySecondRate = source.ElectricitySecondRate;
        target.ElectricityThirdRate = source.ElectricityThirdRate;
        target.ElectricityTiersJson = source.ElectricityTiersJson;
        target.IsArchived = false;
        target.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<ElectricityTierConfigItem> ReadElectricityTiers(Tariff tariff)
    {
        if (!string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<ElectricityTierConfigItem>>(
                    tariff.ElectricityTiersJson,
                    PersistedJsonOptions);
                if (stored is { Count: >= 2 })
                {
                    return stored
                        .Select((item, index) => item with
                        {
                            Id = item.Id == Guid.Empty ? CreateLegacyTierId(tariff.Id, index + 1) : item.Id,
                            Name = string.IsNullOrWhiteSpace(item.Name) ? $"Порог {index + 1}" : item.Name
                        })
                        .ToArray();
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

    private async Task<DictionaryResult<object>> ValidateChargeServiceAccountingLinksAsync(
        UpsertChargeServiceSettingRequest request,
        CancellationToken cancellationToken,
        Tariff? tariffOverride = null,
        IncomeType? incomeTypeOverride = null)
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

        var incomeType = incomeTypeOverride?.Id == request.IncomeTypeId.Value
            ? incomeTypeOverride
            : await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId.Value, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<object>.Failure("charge_service_income_type_not_found", "Вид поступления для услуги не найден.");
        }

        if (!incomeType.DestinationFundId.HasValue)
        {
            return DictionaryResult<object>.Failure(
                "charge_service_fund_required",
                "Для услуги должен быть назначен действующий фонд поступления.");
        }
        if (!await fundRepository.ActiveFundExistsAsync(incomeType.DestinationFundId.Value, cancellationToken))
        {
            return DictionaryResult<object>.Failure(
                "charge_service_fund_not_found",
                "Фонд поступления услуги удалён. Выберите другой фонд.");
        }

        var tariff = tariffOverride ?? await tariffRepository.FindActiveAsync(request.TariffId!.Value, cancellationToken);
        if (tariff is null)
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_not_found", "Тариф для услуги не найден.");
        }

        if (!IsIncomeTypeCompatibleWithTariff(incomeType.Code, tariff.CalculationBase))
        {
            return DictionaryResult<object>.Failure("charge_service_tariff_mismatch", "Выбранный тариф не подходит для вида поступления услуги.");
        }

        var isMeterTariff = tariff.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;
        if (request.IsMetered != isMeterTariff)
        {
            return DictionaryResult<object>.Failure(
                "charge_service_meter_mode_mismatch",
                request.IsMetered
                    ? "Для расчета по счетчику выберите тариф воды или электроэнергии."
                    : "Для тарифа воды или электроэнергии включите расчет по счетчику.");
        }

        if (string.IsNullOrWhiteSpace(request.UnitName))
        {
            return DictionaryResult<object>.Failure(
                "charge_service_unit_required",
                "Укажите единицу измерения услуги.");
        }

        if (request.UnitName.Trim().Length > 40)
        {
            return DictionaryResult<object>.Failure(
                "charge_service_unit_too_long",
                "Единица измерения должна содержать не более 40 символов.");
        }

        return DictionaryResult<object>.Success(new object());
    }

    private static DictionaryResult<string> ValidateMeasurementUnitName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return DictionaryResult<string>.Failure("measurement_unit_name_required", "Укажите обозначение единицы измерения.");
        }

        return name.Length > 40
            ? DictionaryResult<string>.Failure("measurement_unit_name_too_long", "Обозначение единицы измерения должно содержать не более 40 символов.")
            : DictionaryResult<string>.Success(name);
    }

    private async Task<string?> EnsureMeasurementUnitExistsAsync(string? requestedName, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return null;
        }

        var name = requestedName.Trim();
        var existing = await measurementUnitRepository.FindActiveByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            return existing.Name;
        }

        var unit = new MeasurementUnit { Name = name };
        measurementUnitRepository.Add(unit);
        AddAudit(actorUserId, "dictionary.measurement_unit_created", "measurement_unit", unit.Id, $"Создана единица измерения {unit.Name} из формы услуги.");
        return unit.Name;
    }

    private async Task<DictionaryResult<object>> ValidateChargeServiceFundAsync(
        Guid? incomeTypeId,
        CancellationToken cancellationToken)
    {
        if (!incomeTypeId.HasValue)
        {
            return DictionaryResult<object>.Success(new object());
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(incomeTypeId.Value, cancellationToken);
        if (incomeType is null || !incomeType.DestinationFundId.HasValue)
        {
            return DictionaryResult<object>.Failure(
                "charge_service_fund_required",
                "Для вида поступления услуги должен быть назначен действующий фонд.");
        }

        if (!await fundRepository.ActiveFundExistsAsync(incomeType.DestinationFundId.Value, cancellationToken))
        {
            return DictionaryResult<object>.Failure(
                "charge_service_fund_not_found",
                "Фонд вида поступления удален. Выберите другой вид поступления.");
        }

        return DictionaryResult<object>.Success(new object());
    }

    private async Task<DictionaryResult<bool>> ApplyRequestedIncomeFundAsync(
        Guid? incomeTypeId,
        Guid? requestedFundId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!requestedFundId.HasValue)
        {
            return DictionaryResult<bool>.Success(false);
        }

        if (!incomeTypeId.HasValue)
        {
            return DictionaryResult<bool>.Failure(
                "charge_service_income_type_required",
                "Сначала выберите вид поступления услуги.");
        }

        if (!await fundRepository.ActiveFundExistsAsync(requestedFundId.Value, cancellationToken))
        {
            return DictionaryResult<bool>.Failure(
                "charge_service_fund_not_found",
                "Выбранный фонд поступления удалён. Выберите действующий фонд.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(incomeTypeId.Value, cancellationToken);
        if (incomeType is null)
        {
            return DictionaryResult<bool>.Failure(
                "charge_service_income_type_not_found",
                "Вид поступления для услуги не найден.");
        }

        if (incomeType.DestinationFundId == requestedFundId)
        {
            return DictionaryResult<bool>.Success(false);
        }

        var previousFundId = incomeType.DestinationFundId;
        incomeType.DestinationFundId = requestedFundId;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(
            actorUserId,
            "dictionary.income_type_destination_fund_updated",
            "income_type",
            incomeType.Id,
            $"Для вида поступления {incomeType.Name} изменён фонд поступления.",
            oldValues: new Dictionary<string, object?> { ["destinationFundId"] = previousFundId },
            newValues: new Dictionary<string, object?> { ["destinationFundId"] = requestedFundId });
        return DictionaryResult<bool>.Success(true);
    }

    private async Task<bool> SynchronizeManagedServiceIncomeTypeAsync(
        ChargeServiceSetting setting,
        string serviceName,
        Guid? requestedFundId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (!setting.IncomeTypeId.HasValue)
        {
            return false;
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(setting.IncomeTypeId.Value, cancellationToken);
        if (incomeType?.Code?.StartsWith(ServiceIncomeTypeCodePrefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        var normalizedName = await ResolveManagedIncomeTypeNameAsync(incomeType.Id, serviceName, cancellationToken);
        var targetFundId = requestedFundId ?? incomeType.DestinationFundId;
        if (string.Equals(incomeType.Name, normalizedName, StringComparison.Ordinal) && incomeType.DestinationFundId == targetFundId)
        {
            return false;
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["name"] = incomeType.Name,
            ["destinationFundId"] = incomeType.DestinationFundId
        };
        incomeType.Name = normalizedName;
        incomeType.DestinationFundId = targetFundId;
        incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(
            actorUserId,
            "dictionary.service_income_type_updated",
            "income_type",
            incomeType.Id,
            $"Внутренняя категория поступления синхронизирована с услугой {normalizedName}.",
            oldValues: oldValues,
            newValues: new Dictionary<string, object?>
            {
                ["name"] = incomeType.Name,
                ["destinationFundId"] = incomeType.DestinationFundId
            });
        return true;
    }

    private async Task<string> ResolveManagedIncomeTypeNameAsync(Guid? ignoredId, string serviceName, CancellationToken cancellationToken)
    {
        var normalizedName = serviceName.Trim();
        if (!await incomeTypeRepository.ActiveDuplicateExistsAsync(ignoredId, normalizedName, cancellationToken))
        {
            return normalizedName;
        }

        const string suffix = " · услуга";
        var trimmedName = normalizedName[..Math.Min(normalizedName.Length, 200 - suffix.Length)];
        return $"{trimmedName}{suffix}";
    }

    private static void ApplyChargeServiceSetting(ChargeServiceSetting setting, UpsertChargeServiceSettingRequest request)
    {
        setting.MeterKind ??= MeterKinds.ForService(setting.Id);
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

    private static string CreateServiceTariffName(string serviceName)
    {
        const string suffix = " — тариф";
        var maxServiceNameLength = 200 - suffix.Length;
        return $"{serviceName[..Math.Min(serviceName.Length, maxServiceNameLength)]}{suffix}";
    }

    private static string CreateServiceTariffVersionName(string serviceName, string mode)
    {
        var suffix = $" — {FormatTariffMode(mode)}";
        var maxServiceNameLength = 200 - suffix.Length;
        return $"{serviceName[..Math.Min(serviceName.Length, maxServiceNameLength)]}{suffix}";
    }

    private static string FormatTariffMode(string mode) => mode switch
    {
        "regular" => "обычный",
        "metered" => "по счетчику",
        "metered_tiered" => "по счетчику с порогами",
        _ => mode
    };

    private static string? ResolveTariffModeCalculationBase(
        string mode,
        string? incomeTypeCode,
        string sourceCalculationBase,
        string? requestedCalculationBase)
    {
        if (mode == "regular")
        {
            return requestedCalculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.People
                ? requestedCalculationBase
                : sourceCalculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.People
                    ? sourceCalculationBase
                    : TariffCalculationBases.Fixed;
        }

        if (requestedCalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity)
        {
            return requestedCalculationBase;
        }

        return NormalizeOptional(incomeTypeCode)?.Trim().ToLowerInvariant() switch
        {
            "water" => TariffCalculationBases.MeterWater,
            "electricity" => TariffCalculationBases.MeterElectricity,
            _ when sourceCalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity => sourceCalculationBase,
            _ => TariffCalculationBases.MeterElectricity
        };
    }

    private static bool IsMeterCalculationBase(string calculationBase) =>
        calculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;

    private static IReadOnlyList<UpsertElectricityTariffTierRequest> BuildTariffModeElectricityTiers(
        IReadOnlyList<UpsertElectricityTariffTierRequest>? requestedTiers,
        Tariff? sourceTariff,
        decimal rate)
    {
        if (requestedTiers is { Count: > 0 })
        {
            return requestedTiers;
        }

        var sourceTiers = sourceTariff is null ? [] : ReadElectricityTiers(sourceTariff);
        if (sourceTiers.Count >= 2)
        {
            return sourceTiers
                .Select(tier => new UpsertElectricityTariffTierRequest(tier.Id, tier.Name, tier.UpperBound, tier.Rate))
                .ToList();
        }

        return
        [
            new UpsertElectricityTariffTierRequest(null, "0–1100 кВт·ч", 1100m, rate),
            new UpsertElectricityTariffTierRequest(null, "1100–1700 кВт·ч", 1700m, rate),
            new UpsertElectricityTariffTierRequest(null, "1700+ кВт·ч", null, rate)
        ];
    }

    private static bool IsIncomeTypeCompatibleWithTariff(string? incomeTypeCode, string calculationBase)
    {
        return NormalizeOptional(incomeTypeCode)?.Trim().ToLowerInvariant() switch
        {
            "water" => calculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity,
            "trash" => calculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.People or TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity,
            "electricity" => calculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity,
            "membership" or "target" or "entry" or "connection" => calculationBase is TariffCalculationBases.Fixed or TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity,
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
            ["meterKind"] = setting.MeterKind,
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

        if (request.AmountCalculationMode is not null
            && request.AmountCalculationMode is not FeeCampaignAmountCalculationModes.Contribution and not FeeCampaignAmountCalculationModes.Target)
        {
            return DictionaryResult<FeeCampaignDto>.Failure("fee_campaign_amount_mode_invalid", "Неизвестный способ расчёта суммы сбора.");
        }

        if (request.AmountCalculationMode == FeeCampaignAmountCalculationModes.Target
            && MoneyMath.RoundMoney(request.TargetAmount) <= 0m)
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
        var garages = await garageRepository.GetActiveByIdsAsync(participantIds, cancellationToken);

        if (garages.Count != participantIds.Count)
        {
            return DictionaryResult<IReadOnlyList<Garage>>.Failure("fee_campaign_participant_garage_not_found", "Один из выбранных гаражей не найден или архивирован.");
        }

        return DictionaryResult<IReadOnlyList<Garage>>.Success(garages);
    }

    private async Task<(decimal ContributionAmount, decimal TargetAmount)> CalculateFeeCampaignAmountsAsync(
        UpsertFeeCampaignRequest request,
        IReadOnlyList<Garage> participants,
        CancellationToken cancellationToken)
    {
        var participantCount = request.AppliesToAllGarages
            ? await garageRepository.CountActiveAsync(cancellationToken)
            : participants.Count;

        if (request.AmountCalculationMode == FeeCampaignAmountCalculationModes.Target)
        {
            var targetAmount = MoneyMath.RoundMoney(request.TargetAmount);
            var contributionAmount = participantCount <= 0
                ? 0m
                : decimal.Ceiling(targetAmount * 100m / participantCount) / 100m;
            return (MoneyMath.RoundMoney(contributionAmount), targetAmount);
        }

        var roundedContribution = MoneyMath.RoundMoney(request.ContributionAmount);
        return (roundedContribution, MoneyMath.RoundMoney(roundedContribution * participantCount));
    }

    private static void ApplyFeeCampaign(
        FeeCampaign campaign,
        UpsertFeeCampaignRequest request,
        Guid incomeTypeId,
        decimal contributionAmount,
        decimal targetAmount)
    {
        campaign.Name = request.Name.Trim();
        campaign.IncomeTypeId = incomeTypeId;
        campaign.Goal = NormalizeOptional(request.Goal);
        campaign.ContributionAmount = contributionAmount;
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
        decimal contributionAmount,
        decimal targetAmount)
    {
        return StringEquals(campaign.Name, request.Name.Trim()) &&
            campaign.IncomeTypeId == incomeTypeId &&
            StringEquals(campaign.Goal, NormalizeOptional(request.Goal)) &&
            campaign.ContributionAmount == contributionAmount &&
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
            ["overdueGraceDays"] = campaign.OverdueGraceDays,
            ["closedAtUtc"] = campaign.ClosedAtUtc,
            ["closedByUserId"] = campaign.ClosedByUserId,
            ["isClosedEarly"] = campaign.IsClosedEarly,
            ["closureComment"] = campaign.ClosureComment
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

    private async Task<DictionaryResult<ExpenseType?>> ResolveSupplierExpenseTypeAsync(
        ChargeServiceSetting? chargeService,
        Guid? requestedExpenseTypeId,
        ExpenseType? currentExpenseType,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (chargeService is null)
        {
            return DictionaryResult<ExpenseType?>.Success(null);
        }

        if (currentExpenseType is { IsArchived: false } &&
            (!requestedExpenseTypeId.HasValue || requestedExpenseTypeId == currentExpenseType.Id))
        {
            return DictionaryResult<ExpenseType?>.Success(currentExpenseType);
        }

        if (requestedExpenseTypeId.HasValue)
        {
            var requested = await expenseTypeRepository.FindActiveAsync(requestedExpenseTypeId.Value, cancellationToken);
            return requested is null
                ? DictionaryResult<ExpenseType?>.Failure(
                    "supplier_expense_type_not_found",
                    "Внутренняя категория расхода поставщика не найдена.")
                : DictionaryResult<ExpenseType?>.Success(requested);
        }

        var managedCode = $"{SupplierServiceExpenseTypeCodePrefix}{chargeService.Id:N}";
        var existing = await expenseTypeRepository.FindActiveByCodeAsync(managedCode, cancellationToken)
            ?? await expenseTypeRepository.FindActiveByNameAsync(chargeService.Name, cancellationToken);
        if (existing is not null)
        {
            return DictionaryResult<ExpenseType?>.Success(existing);
        }

        var created = new ExpenseType
        {
            Name = chargeService.Name,
            Code = managedCode,
            IsSystem = true
        };
        expenseTypeRepository.Add(created);
        AddAudit(
            actorUserId,
            "dictionary.supplier_expense_type_created",
            "expense_type",
            created.Id,
            $"Для услуги {chargeService.Name} создана внутренняя категория расходов поставщиков.");
        return DictionaryResult<ExpenseType?>.Success(created);
    }

    private static bool SupplierMatches(Supplier supplier, string name, Guid groupId, Guid? chargeServiceSettingId, Guid? expenseTypeId, Guid? expenseFundId, string? inn, string? legalAddress, string? contactPerson, string? phone, string? email, decimal startingBalance, string? comment)
    {
        return StringEquals(supplier.Name, name) &&
            supplier.GroupId == groupId &&
            supplier.ChargeServiceSettingId == chargeServiceSettingId &&
            supplier.ExpenseTypeId == expenseTypeId &&
            supplier.ExpenseFundId == expenseFundId &&
            StringEquals(supplier.Inn, inn) &&
            StringEquals(supplier.LegalAddress, legalAddress) &&
            StringEquals(supplier.ContactPerson, contactPerson) &&
            StringEquals(supplier.Phone, phone) &&
            StringEquals(supplier.Email, email) &&
            supplier.StartingBalance == startingBalance &&
            StringEquals(supplier.Comment, comment);
    }

    private static void ApplySupplierContact(SupplierContact contact, UpsertSupplierContactRequest request, string? phone)
    {
        contact.FullName = request.FullName.Trim();
        contact.Position = NormalizeOptional(request.Position);
        contact.Phone = phone;
        contact.Email = NormalizeOptional(request.Email);
        contact.Status = request.Status.Trim();
        contact.Comment = NormalizeOptional(request.Comment);
    }

    private static bool SupplierContactMatches(SupplierContact contact, UpsertSupplierContactRequest request, Guid supplierId, string? phone)
    {
        return contact.SupplierId == supplierId &&
            StringEquals(contact.FullName, request.FullName.Trim()) &&
            StringEquals(contact.Position, NormalizeOptional(request.Position)) &&
            StringEquals(contact.Phone, phone) &&
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
            garage.Owner?.Phone,
            garage.Version);
    }

    private static DictionaryResult<T> InvalidPhone<T>() =>
        DictionaryResult<T>.Failure("phone_invalid", $"Укажите телефон в формате {PhoneNumberNormalizer.FormatHint}.");

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
            garage.OwnerPhone,
            garage.Version);

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
            supplier.ChargeServiceSetting?.Name,
            supplier.Version,
            supplier.ExpenseTypeId,
            supplier.ExpenseType?.Name,
            supplier.ExpenseFundId,
            supplier.ExpenseFund?.Name,
            supplier.ExpenseFund?.Balance);
    }

    private async Task<SupplierDto> ToSupplierDtoWithDebtAsync(Supplier supplier, CancellationToken cancellationToken)
    {
        var debtTotals = await supplierRepository.GetDebtTotalsAsync([supplier.Id], cancellationToken);
        return ToSupplierDto(supplier, debtTotals.GetValueOrDefault(supplier.Id, supplier.StartingBalance));
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
            setting.IsArchived,
            setting.Tariff?.CalculationBase,
            setting.Version,
            setting.MeterKind);
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

    private static FeeCampaignDto ToFeeCampaignDto(FeeCampaign campaign, decimal collectedAmount = 0m)
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
            campaign.IsArchived,
            campaign.ClosedAtUtc,
            campaign.IsClosedEarly,
            campaign.ClosureComment,
            collectedAmount,
            campaign.IncomeType?.DestinationFundId,
            campaign.IncomeType?.DestinationFund?.Name);
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
                .ToArray(),
            tariff.Version);
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
