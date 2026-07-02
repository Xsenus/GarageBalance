using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionaryServiceTests
{
    [Fact]
    public async Task CreateOwnerAsync_TrimsFieldsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateOwnerAsync(
            new UpsertOwnerRequest(" Иванов ", " Иван ", " Иванович ", " +7 900 ", " Адрес ", " Счетчик "),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Иванов Иван Иванович", result.Value!.FullName);
        Assert.Equal("+7 900", result.Value.Phone);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task GetOwnersAsync_SearchesByNameAndPhone()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, "+7 111", null, null), null, CancellationToken.None);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Сидоров", "Сергей", null, "+7 222", null, null), null, CancellationToken.None);

        var result = await service.GetOwnersAsync("222", CancellationToken.None);

        var owner = Assert.Single(result);
        Assert.Equal("Сидоров", owner.LastName);
    }

    [Fact]
    public async Task ListMethods_ApplyExplicitLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var groupResults = new List<DictionaryResult<SupplierGroupDto>>();
        for (var index = 0; index < 3; index++)
        {
            var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest($"Владелец{index}", "Тест", null, null, null, null), null, CancellationToken.None);
            Assert.True(owner.Succeeded);
            Assert.True((await service.CreateGarageAsync(new UpsertGarageRequest($"L-{index}", 1, 1, owner.Value!.Id, 0, null, null, null), null, CancellationToken.None)).Succeeded);
            groupResults.Add(await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest($"Группа {index}"), null, CancellationToken.None));
            Assert.True(groupResults[index].Succeeded);
            Assert.True((await service.CreateSupplierAsync(new UpsertSupplierRequest($"Поставщик {index}", groupResults[index].Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest($"Поступление {index}", $"income_limit_{index}"), null, CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest($"Выплата {index}", $"expense_limit_{index}"), null, CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateTariffAsync(new UpsertTariffRequest($"Тариф {index}", "fixed", 10 + index, new DateOnly(2026, 1, 1).AddMonths(index), null), null, CancellationToken.None)).Succeeded);
        }

        Assert.Equal(2, (await service.GetOwnersAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetGaragesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetSupplierGroupsAsync(CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetSuppliersAsync(null, null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetIncomeTypesAsync(CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetExpenseTypesAsync(CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetTariffsAsync(null, CancellationToken.None, 2)).Count);
    }

    [Fact]
    public async Task ArchiveOwnerAsync_HidesOwnerFromListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(ownerResult.Value!.Id, actorUserId, CancellationToken.None);
        var owners = await service.GetOwnersAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsArchived);
        Assert.Empty(owners);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RestoreOwnerAsync_ReturnsOwnerToListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        await service.ArchiveOwnerAsync(ownerResult.Value!.Id, null, CancellationToken.None);

        var result = await service.RestoreOwnerAsync(ownerResult.Value.Id, actorUserId, CancellationToken.None);
        var owners = await service.GetOwnersAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Single(owners);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_restored" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task UpdateOwnerAsync_ReturnsNotFoundForMissingOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.UpdateOwnerAsync(
            Guid.NewGuid(),
            new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("owner_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateGarageAsync_RejectsMissingOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateGarageAsync(
            new UpsertGarageRequest("A-1", 1, 1, Guid.NewGuid(), 0, 10, 20, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("owner_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateGarageAsync_RejectsDuplicateNumber()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, null, 0, null, null, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreGarageAsync_RejectsDuplicateActiveNumber()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);
        await service.ArchiveGarageAsync(archived.Value!.Id, null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.RestoreGarageAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateGarageAsync_AllowsNumberFromArchivedGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archivedGarage = await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, "old import row"), null, CancellationToken.None);
        await service.ArchiveGarageAsync(archivedGarage.Value!.Id, null, CancellationToken.None);

        var result = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, null, 100m, null, null, "new active row"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("12", result.Value!.Number);
        Assert.False(result.Value.IsArchived);
        Assert.Equal(2, await database.Context.Garages.CountAsync(garage => garage.Number == "12"));
        Assert.Single(await service.GetGaragesAsync("12", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateGarageAsync_RejectsNumberOfAnotherActiveGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);
        var second = await service.CreateGarageAsync(new UpsertGarageRequest("21", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.UpdateGarageAsync(second.Value!.Id, new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task Dictionaries_RoundMoneyAndTariffRateBeforeSaving()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var supplierGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("17", 1, 1, null, 10.005m, 1.2345m, 9.8765m, null), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", supplierGroup.Value!.Id, null, null, null, null, null, 20.005m, null), null, CancellationToken.None);
        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);

        Assert.True(garage.Succeeded);
        Assert.Equal(10.01m, garage.Value!.StartingBalance);
        Assert.Equal(1.235m, garage.Value.InitialWaterMeterValue);
        Assert.Equal(9.877m, garage.Value.InitialElectricityMeterValue);
        Assert.True(supplier.Succeeded);
        Assert.Equal(20.01m, supplier.Value!.StartingBalance);
        Assert.True(tariff.Succeeded);
        Assert.Equal(12.3456m, tariff.Value!.Rate);
    }

    [Fact]
    public async Task UpdateGarageAsync_ChangesOwnerAndKeepsDtoOwnerName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Кузнецов", "Олег", null, null, null, null), null, CancellationToken.None);
        var garageResult = await service.CreateGarageAsync(new UpsertGarageRequest("15", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.UpdateGarageAsync(
            garageResult.Value!.Id,
            new UpsertGarageRequest("15A", 3, 2, ownerResult.Value!.Id, 250m, 1.5m, 9.75m, "угловой"),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("15A", result.Value!.Number);
        Assert.Equal("Кузнецов Олег", result.Value.OwnerName);
        Assert.Equal(3, result.Value.PeopleCount);
        Assert.Equal(250m, result.Value.StartingBalance);
    }

    [Fact]
    public async Task ArchiveGarageAsync_HidesGarageFromList()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var garageResult = await service.CreateGarageAsync(new UpsertGarageRequest("15", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveGarageAsync(garageResult.Value!.Id, null, CancellationToken.None);
        var garages = await service.GetGaragesAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsArchived);
        Assert.Empty(garages);
    }

    [Fact]
    public async Task GetGaragesAsync_SearchesByNumberAndOwnerName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var firstOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        var secondOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, firstOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("21", 1, 1, secondOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);

        var byNumber = await service.GetGaragesAsync("12", CancellationToken.None);
        var byOwner = await service.GetGaragesAsync("петров", CancellationToken.None);

        var garageByNumber = Assert.Single(byNumber);
        Assert.Equal("Иванов Иван", garageByNumber.OwnerName);
        var garageByOwner = Assert.Single(byOwner);
        Assert.Equal("21", garageByOwner.Number);
    }

    [Fact]
    public async Task CreateGarageAsync_AllowsSeveralActiveGaragesForOneOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var owner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Семенов", "Андрей", "Петрович", "+7 900 100", null, null),
            null,
            CancellationToken.None);

        var firstGarage = await service.CreateGarageAsync(
            new UpsertGarageRequest("44", 1, 1, owner.Value!.Id, 0, null, null, "основной гараж"),
            null,
            CancellationToken.None);
        var secondGarage = await service.CreateGarageAsync(
            new UpsertGarageRequest("45", 2, 1, owner.Value.Id, 0, null, null, "семейный гараж"),
            null,
            CancellationToken.None);

        var garagesByOwner = await service.GetGaragesAsync("семенов", CancellationToken.None);

        Assert.True(firstGarage.Succeeded);
        Assert.True(secondGarage.Succeeded);
        Assert.Equal(owner.Value.Id, firstGarage.Value!.OwnerId);
        Assert.Equal(owner.Value.Id, secondGarage.Value!.OwnerId);
        Assert.Equal(2, garagesByOwner.Count);
        Assert.Equal(["44", "45"], garagesByOwner.Select(garage => garage.Number).Order(StringComparer.Ordinal));
        Assert.All(garagesByOwner, garage => Assert.Equal("Семенов Андрей Петрович", garage.OwnerName));
    }

    [Fact]
    public async Task CreateSupplierGroupAsync_RejectsDuplicateName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        var result = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierGroupAsync_AllowsNameFromArchivedGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(archived.Value!.Id, null, CancellationToken.None);

        var result = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Equal(2, await database.Context.SupplierGroups.CountAsync(group => group.Name == "Коммунальные услуги"));
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", Guid.NewGuid(), "5400000000", null, null, null, null, 0, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreSupplierGroupAsync_RejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(archived.Value!.Id, null, CancellationToken.None);
        await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        var result = await service.RestoreSupplierGroupAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task GetSuppliersAsync_FiltersByGroupAndSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var utilityGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var bankGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Банки"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", utilityGroup.Value!.Id, "5401", null, "Мария", null, null, 100, null), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Альфа-Банк", bankGroup.Value!.Id, "7728", null, "Ольга", null, null, 0, null), null, CancellationToken.None);

        var result = await service.GetSuppliersAsync(utilityGroup.Value.Id, "5401", CancellationToken.None);

        var supplier = Assert.Single(result);
        Assert.Equal("Водоканал", supplier.Name);
        Assert.Equal("Коммунальные услуги", supplier.GroupName);
    }

    [Fact]
    public async Task RestoreSupplierAsync_RejectsArchivedSupplierGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value!.Id, null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(group.Value.Id, null, CancellationToken.None);

        var result = await service.RestoreSupplierAsync(supplier.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_RejectsDuplicateName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership"), null, CancellationToken.None);

        var result = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership2"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_AllowsNameFromArchivedType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой взнос", "target_old"), null, CancellationToken.None);
        await service.ArchiveIncomeTypeAsync(archived.Value!.Id, null, CancellationToken.None);

        var result = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой взнос", "target_new"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Equal("target_new", result.Value.Code);
        Assert.Equal(2, await database.Context.IncomeTypes.CountAsync(item => item.Name == "Целевой взнос"));
    }

    [Fact]
    public async Task RestoreIncomeTypeAsync_RejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой взнос", "target_old"), null, CancellationToken.None);
        await service.ArchiveIncomeTypeAsync(archived.Value!.Id, null, CancellationToken.None);
        await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой взнос", "target_new"), null, CancellationToken.None);

        var result = await service.RestoreIncomeTypeAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task ArchiveIncomeTypeAsync_RejectsSystemType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var systemType = new IncomeType
        {
            Name = "Членский взнос",
            Code = "membership",
            IsSystem = true
        };
        database.Context.IncomeTypes.Add(systemType);
        await database.Context.SaveChangesAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.ArchiveIncomeTypeAsync(systemType.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_system", result.ErrorCode);
        Assert.False(systemType.IsArchived);
    }

    [Fact]
    public void DefaultAccountingTypesMigration_ContainsInitialIncomeAndExpenseTypes()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260623152000_DefaultAccountingTypes.cs"));

        string[] incomeTypes =
        [
            "Вода",
            "Мусор",
            "Электроэнергия",
            "Членский взнос",
            "Целевой взнос",
            "Вступительный взнос",
            "Подключения",
            "Штраф",
            "Предписание"
        ];
        string[] expenseTypes =
        [
            "Электроэнергия",
            "Вывоз мусора",
            "Водоснабжение",
            "Банковские расходы",
            "Юридические расходы",
            "Зарплата",
            "Прочие расходы",
            "Штрафы"
        ];

        foreach (var type in incomeTypes.Concat(expenseTypes))
        {
            Assert.Contains(type, migration, StringComparison.Ordinal);
        }

        Assert.Equal(9, CountOccurrences(migration, "InsertIncomeType(migrationBuilder"));
        Assert.Equal(8, CountOccurrences(migration, "InsertExpenseType(migrationBuilder"));
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_WritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электроэнергия", "electricity"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.expense_type_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_AllowsNameFromArchivedType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Вывоз мусора", "trash_old"), null, CancellationToken.None);
        await service.ArchiveExpenseTypeAsync(archived.Value!.Id, null, CancellationToken.None);

        var result = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Вывоз мусора", "trash_new"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Equal("trash_new", result.Value.Code);
        Assert.Equal(2, await database.Context.ExpenseTypes.CountAsync(item => item.Name == "Вывоз мусора"));
    }

    [Fact]
    public async Task RestoreExpenseTypeAsync_RejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Вывоз мусора", "trash_old"), null, CancellationToken.None);
        await service.ArchiveExpenseTypeAsync(archived.Value!.Id, null, CancellationToken.None);
        await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Вывоз мусора", "trash_new"), null, CancellationToken.None);

        var result = await service.RestoreExpenseTypeAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("expense_type_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsDuplicateNameAndDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50.25m, effectiveFrom, null), null, CancellationToken.None);

        var result = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 60m, effectiveFrom, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateTariffAsync_AllowsNameAndDateFromArchivedTariff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        var archived = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50m, effectiveFrom, null), null, CancellationToken.None);
        await service.ArchiveTariffAsync(archived.Value!.Id, null, CancellationToken.None);

        var result = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 60m, effectiveFrom, "Новая редакция"), null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Equal(60m, result.Value.Rate);
        Assert.Equal(2, await database.Context.Tariffs.CountAsync(item => item.Name == "Вода" && item.EffectiveFrom == effectiveFrom));
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsUnsupportedCalculationBase()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateTariffAsync(
            new UpsertTariffRequest("Непонятный тариф", "unknown_base", 50m, new DateOnly(2026, 7, 1), null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_calculation_base_invalid", result.ErrorCode);
        Assert.Empty(database.Context.Tariffs);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateTariffAsync_WritesAuditWithBaseAndRate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateTariffAsync(
            new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), null),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Вода", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("база meter_water", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("ставка 12.3456", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTariffAsync_SavesElectricityTiersAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                4.5m,
                new DateOnly(2026, 7, 1),
                "Три зоны",
                50.55555m,
                100.77777m,
                3.11111m,
                4.22222m,
                5.33333m),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(50.556m, result.Value!.ElectricityFirstThreshold);
        Assert.Equal(100.778m, result.Value.ElectricitySecondThreshold);
        Assert.Equal(3.1111m, result.Value.ElectricityFirstRate);
        Assert.Equal(4.2222m, result.Value.ElectricitySecondRate);
        Assert.Equal(5.3333m, result.Value.ElectricityThirdRate);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("электричество: до 50.556 кВт по 3.1111", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("до 100.778 кВт по 4.2222", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("свыше по 5.3333", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsIncompleteElectricityTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                4.5m,
                new DateOnly(2026, 7, 1),
                null,
                ElectricityFirstThreshold: 50m),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_electricity_tiers_incomplete", result.ErrorCode);
        Assert.Empty(database.Context.Tariffs);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateTariffAsync_UpdatesTariffAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);

        var result = await service.UpdateTariffAsync(
            created.Value!.Id,
            new UpsertTariffRequest("Вода новая", "people", 20.55555m, new DateOnly(2026, 8, 1), "После собрания"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Вода новая", result.Value!.Name);
        Assert.Equal("people", result.Value.CalculationBase);
        Assert.Equal(20.5556m, result.Value.Rate);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.EffectiveFrom);
        Assert.Equal("После собрания", result.Value.Comment);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Вода новая", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("база people", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("ставка 20.5556", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateTariffAsync_ClearsElectricityTiersWhenBaseChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var created = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                4.5m,
                new DateOnly(2026, 7, 1),
                null,
                50m,
                100m,
                3m,
                4m,
                5m),
            null,
            CancellationToken.None);

        var result = await service.UpdateTariffAsync(
            created.Value!.Id,
            new UpsertTariffRequest("Вода", "meter_water", 20m, new DateOnly(2026, 8, 1), null),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ElectricityFirstThreshold);
        Assert.Null(result.Value.ElectricitySecondThreshold);
        Assert.Null(result.Value.ElectricityFirstRate);
        Assert.Null(result.Value.ElectricitySecondRate);
        Assert.Null(result.Value.ElectricityThirdRate);
    }

    [Fact]
    public async Task UpdateTariffAsync_RejectsUnsupportedCalculationBaseAndKeepsExistingValue()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var created = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateTariffAsync(
            created.Value!.Id,
            new UpsertTariffRequest("Вода новая", "unknown_base", 20.55555m, new DateOnly(2026, 8, 1), "После собрания"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_calculation_base_invalid", result.ErrorCode);
        var tariff = await database.Context.Tariffs.FindAsync(created.Value.Id);
        Assert.NotNull(tariff);
        Assert.Equal("meter_water", tariff.CalculationBase);
        Assert.Equal("Вода", tariff.Name);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateTariffAsync_RejectsEffectiveDateAfterExistingRegularAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var garage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            Tariff = tariff,
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 300m,
            Source = "regular"
        };
        database.Context.AddRange(tariff, accrual);
        await database.Context.SaveChangesAsync();
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateTariffAsync(
            tariff.Id,
            new UpsertTariffRequest("Членский тариф", "fixed", 350m, new DateOnly(2026, 7, 1), "Позже начисления"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_effective_from_after_accrual", result.ErrorCode);
        var storedTariff = await database.Context.Tariffs.FindAsync(tariff.Id);
        Assert.NotNull(storedTariff);
        Assert.Equal(new DateOnly(2026, 1, 1), storedTariff.EffectiveFrom);
        Assert.Equal(300m, storedTariff.Rate);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateTariffAsync_AllowsEffectiveDateOnExistingRegularAccrualMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var garage = new Garage { Number = "12", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            Tariff = tariff,
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 300m,
            Source = "regular"
        };
        database.Context.AddRange(tariff, accrual);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateTariffAsync(
            tariff.Id,
            new UpsertTariffRequest("Членский тариф", "fixed", 350m, new DateOnly(2026, 6, 1), "С месяца начисления"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.EffectiveFrom);
        Assert.Equal(350m, result.Value.Rate);
    }

    [Fact]
    public async Task ArchiveTariffAsync_WritesAuditWithBaseAndRate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100.5m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);

        var result = await service.ArchiveTariffAsync(created.Value!.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_archived");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Мусор", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("база people", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("ставка 100.5", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreTariffAsync_ReturnsTariffToListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100.5m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);
        await service.ArchiveTariffAsync(created.Value!.Id, null, CancellationToken.None);

        var result = await service.RestoreTariffAsync(created.Value.Id, actorUserId, CancellationToken.None);
        var tariffs = await service.GetTariffsAsync("people", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Single(tariffs);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_restored" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RestoreTariffAsync_RejectsDuplicateActiveNameAndEffectiveDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        var archived = await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100m, effectiveFrom, null), null, CancellationToken.None);
        await service.ArchiveTariffAsync(archived.Value!.Id, null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 120m, effectiveFrom, null), null, CancellationToken.None);

        var result = await service.RestoreTariffAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateTariffAsync_RejectsDuplicateNameAndDateOnAnotherTariff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        var first = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50m, effectiveFrom, null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100m, effectiveFrom, null), null, CancellationToken.None);

        var result = await service.UpdateTariffAsync(first.Value!.Id, new UpsertTariffRequest("Мусор", "people", 120m, effectiveFrom, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task GetTariffsAsync_SearchesAndOrdersByEffectiveDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 120m, new DateOnly(2026, 2, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);

        var result = await service.GetTariffsAsync("people", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateOnly(2026, 2, 1), result[0].EffectiveFrom);
        Assert.Equal(120m, result[0].Rate);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static string FindApiProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "GarageBalance.Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти проект GarageBalance.Api.");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
