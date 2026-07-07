using System.Text.Json;
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
    public async Task OwnerAudit_UsesWriterStructuredFieldsAndArchiveReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Ivanov", "Ivan", null, "+7 900", "Private address", null),
            actorUserId,
            CancellationToken.None);
        var archived = await service.ArchiveOwnerAsync(created.Value!.Id, "Дубликат карточки", actorUserId, CancellationToken.None);

        Assert.True(archived.Succeeded);
        var createAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.owner_created");
        Assert.Equal(actorUserId, createAudit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), createAudit.EntityId);
        Assert.Equal("dictionary", createAudit.Section);
        Assert.Equal("create", createAudit.ActionKind);
        Assert.Equal("Создан владелец Ivanov Ivan", createAudit.EntityDisplayName);
        Assert.DoesNotContain("+7 900", createAudit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Private address", createAudit.MetadataJson, StringComparison.Ordinal);

        var archiveAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived");
        Assert.Equal("dictionary", archiveAudit.Section);
        Assert.Equal("archive", archiveAudit.ActionKind);
        Assert.Contains("Архивирован владелец Ivanov Ivan.", archiveAudit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(archiveAudit.MetadataJson!);
        Assert.Equal("owner", metadata.RootElement.GetProperty("dictionaryEntityType").GetString());
        Assert.Equal("Дубликат карточки", metadata.RootElement.GetProperty("reason").GetString());
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
        Assert.Equal(2, (await service.GetSupplierGroupsAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetSuppliersAsync(null, null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetIncomeTypesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetExpenseTypesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetTariffsAsync(null, CancellationToken.None, 2)).Count);
    }

    [Fact]
    public async Task ListMethods_SearchSupplierGroupsAndAccountingTypes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        Assert.True((await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Бухгалтерия"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership_fee"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой сбор", "target_fee"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электроэнергия поставщику", "electricity_supplier"), null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Зарплата бухгалтера", "salary_accountant"), null, CancellationToken.None)).Succeeded);

        var supplierGroups = await service.GetSupplierGroupsAsync("коммун", CancellationToken.None);
        var incomeTypes = await service.GetIncomeTypesAsync("membership", CancellationToken.None);
        var expenseTypesPage = await service.GetExpenseTypesPageAsync("электро", 0, 25, CancellationToken.None);

        Assert.Equal("Коммунальные услуги", Assert.Single(supplierGroups).Name);
        Assert.Equal("Членский взнос", Assert.Single(incomeTypes).Name);
        Assert.Equal(1, expenseTypesPage.TotalCount);
        Assert.Equal("Электроэнергия поставщику", Assert.Single(expenseTypesPage.Items).Name);
    }

    [Fact]
    public async Task ArchiveOwnerAsync_HidesOwnerFromListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(ownerResult.Value!.Id, "Закрытие карточки", actorUserId, CancellationToken.None);
        var owners = await service.GetOwnersAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsArchived);
        Assert.Empty(owners);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task ArchiveOwnerAsync_RejectsEmptyReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(ownerResult.Value!.Id, "   ", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("dictionary_archive_reason_required", result.ErrorCode);
        Assert.False(database.Context.Owners.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived");
    }

    [Fact]
    public async Task ListMethods_ReturnArchivedRecordsOnlyWhenRequested()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Архивов", "Олег", null, null, null, null), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("ARCH-1", 1, 1, null, 0, null, null, null), null, CancellationToken.None);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Архивная группа"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Архивный поставщик", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Архивное поступление", "arch_income"), null, CancellationToken.None);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Архивная выплата", "arch_expense"), null, CancellationToken.None);
        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Архивный тариф", "fixed", 10, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);

        await service.ArchiveOwnerAsync(owner.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveGarageAsync(garage.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(group.Value.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveIncomeTypeAsync(incomeType.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveExpenseTypeAsync(expenseType.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveTariffAsync(tariff.Value!.Id, "Тестовая причина", null, CancellationToken.None);

        Assert.Empty(await service.GetOwnersAsync(null, CancellationToken.None));
        Assert.Empty(await service.GetGaragesAsync(null, CancellationToken.None));
        Assert.Empty(await service.GetSupplierGroupsAsync(null, CancellationToken.None));
        Assert.Empty(await service.GetSuppliersAsync(null, null, CancellationToken.None));
        Assert.Empty(await service.GetIncomeTypesAsync(null, CancellationToken.None));
        Assert.Empty(await service.GetExpenseTypesAsync(null, CancellationToken.None));
        Assert.Empty(await service.GetTariffsAsync(null, CancellationToken.None));

        Assert.Single(await service.GetOwnersAsync(null, CancellationToken.None, includeArchived: true));
        Assert.Single((await service.GetOwnersPageAsync(null, 0, 10, CancellationToken.None, includeArchived: true)).Items);
        Assert.Single(await service.GetGaragesAsync(null, CancellationToken.None, includeArchived: true));
        Assert.Single(await service.GetSupplierGroupsAsync(null, CancellationToken.None, includeArchived: true));
        Assert.Single(await service.GetSuppliersAsync(null, null, CancellationToken.None, includeArchived: true));
        Assert.Single(await service.GetIncomeTypesAsync(null, CancellationToken.None, includeArchived: true));
        Assert.Single(await service.GetExpenseTypesAsync(null, CancellationToken.None, includeArchived: true));
        Assert.Single(await service.GetTariffsAsync(null, CancellationToken.None, includeArchived: true));
    }

    [Fact]
    public async Task RestoreOwnerAsync_ReturnsOwnerToListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        await service.ArchiveOwnerAsync(ownerResult.Value!.Id, "Тестовая причина", null, CancellationToken.None);

        var result = await service.RestoreOwnerAsync(ownerResult.Value.Id, actorUserId, CancellationToken.None);
        var owners = await service.GetOwnersAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsArchived);
        Assert.Single(owners);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_restored" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RestoreMethods_RejectAlreadyActiveRecordsAndDoNotWriteDuplicateAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        const string archiveReason = "Duplicate restore guard";

        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Owner", "Active", null, null, null, null), null, CancellationToken.None);
        await service.ArchiveOwnerAsync(owner.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreOwnerAsync(owner.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var ownerAgain = await service.RestoreOwnerAsync(owner.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(ownerAgain.Succeeded);
        Assert.Equal("owner_not_found", ownerAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.owner_restored"));

        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("ACTIVE-1", 1, 1, null, 0, null, null, null), null, CancellationToken.None);
        await service.ArchiveGarageAsync(garage.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreGarageAsync(garage.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var garageAgain = await service.RestoreGarageAsync(garage.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(garageAgain.Succeeded);
        Assert.Equal("garage_not_found", garageAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.garage_restored"));

        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Active group"), null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(group.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreSupplierGroupAsync(group.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var groupAgain = await service.RestoreSupplierGroupAsync(group.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(groupAgain.Succeeded);
        Assert.Equal("supplier_group_not_found", groupAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.supplier_group_restored"));

        var supplierGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Supplier group"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Active supplier", supplierGroup.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreSupplierAsync(supplier.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var supplierAgain = await service.RestoreSupplierAsync(supplier.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(supplierAgain.Succeeded);
        Assert.Equal("supplier_not_found", supplierAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.supplier_restored"));

        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Active income", "active_income"), null, CancellationToken.None);
        await service.ArchiveIncomeTypeAsync(incomeType.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreIncomeTypeAsync(incomeType.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var incomeAgain = await service.RestoreIncomeTypeAsync(incomeType.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(incomeAgain.Succeeded);
        Assert.Equal("income_type_not_found", incomeAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.income_type_restored"));

        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Active expense", "active_expense"), null, CancellationToken.None);
        await service.ArchiveExpenseTypeAsync(expenseType.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreExpenseTypeAsync(expenseType.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var expenseAgain = await service.RestoreExpenseTypeAsync(expenseType.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(expenseAgain.Succeeded);
        Assert.Equal("expense_type_not_found", expenseAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.expense_type_restored"));

        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Active tariff", "fixed", 100m, new DateOnly(2026, 7, 1), null), null, CancellationToken.None);
        await service.ArchiveTariffAsync(tariff.Value!.Id, archiveReason, null, CancellationToken.None);
        Assert.True((await service.RestoreTariffAsync(tariff.Value.Id, actorUserId, CancellationToken.None)).Succeeded);
        var tariffAgain = await service.RestoreTariffAsync(tariff.Value.Id, actorUserId, CancellationToken.None);
        Assert.False(tariffAgain.Succeeded);
        Assert.Equal("tariff_not_found", tariffAgain.ErrorCode);
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.tariff_restored"));
    }

    [Fact]
    public async Task UpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", "Иванович", "+7 900", "Адрес", "Счетчик"), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, owner.Value!.Id, 10.005m, 1.2345m, 9.8765m, "Угловой"), null, CancellationToken.None);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, "123", "Юр. адрес", "Петров", "+7 901", "mail@example.com", 20.005m, "Комментарий"), null, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership"), null, CancellationToken.None);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электрик", "electrician"), null, CancellationToken.None);
        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), "Комментарий"), null, CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.UpdateOwnerAsync(owner.Value.Id, new UpsertOwnerRequest(" Иванов ", " Иван ", " Иванович ", " +7 900 ", " Адрес ", " Счетчик "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateGarageAsync(garage.Value!.Id, new UpsertGarageRequest(" 12 ", 2, 1, owner.Value.Id, 10.005m, 1.2345m, 9.8765m, " Угловой "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateSupplierGroupAsync(group.Value.Id, new UpsertSupplierGroupRequest(" Коммунальные услуги "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateSupplierAsync(supplier.Value!.Id, new UpsertSupplierRequest(" Водоканал ", group.Value.Id, " 123 ", " Юр. адрес ", " Петров ", " +7 901 ", " mail@example.com ", 20.005m, " Комментарий "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateIncomeTypeAsync(incomeType.Value!.Id, new UpsertAccountingTypeRequest(" Членский взнос ", " membership "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateExpenseTypeAsync(expenseType.Value!.Id, new UpsertAccountingTypeRequest(" Электрик ", " electrician "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateTariffAsync(tariff.Value!.Id, new UpsertTariffRequest(" Вода ", " meter_water ", 12.34555m, new DateOnly(2026, 7, 1), " Комментарий "), actorUserId, CancellationToken.None)).Succeeded);

        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateOwnerAsync_WritesOldAndNewValuesToAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOwnerAsync(
            owner.Value!.Id,
            new UpsertOwnerRequest("Петров", "Иван", null, null, null, null),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.owner_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("owner", metadata.RootElement.GetProperty("dictionaryEntityType").GetString());
        Assert.Equal("Фамилия", metadata.RootElement.GetProperty("fieldName").GetString());
        Assert.Equal("Иванов", metadata.RootElement.GetProperty("oldValue").GetString());
        Assert.Equal("Петров", metadata.RootElement.GetProperty("newValue").GetString());
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
        await service.ArchiveGarageAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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
        await service.ArchiveGarageAsync(archivedGarage.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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

        var result = await service.ArchiveGarageAsync(garageResult.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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
    public async Task GetGaragesAsync_ReturnsCalculatedBalanceAndOverdueDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Electricity", "electricity"), null, CancellationToken.None);
        var debtGarage = await service.CreateGarageAsync(new UpsertGarageRequest("BAL-1", 1, 1, null, 100m, null, null, null), null, CancellationToken.None);
        var overpaidGarage = await service.CreateGarageAsync(new UpsertGarageRequest("BAL-2", 1, 1, null, 0m, null, null, null), null, CancellationToken.None);

        database.Context.Accruals.AddRange(
            new Accrual
            {
                GarageId = debtGarage.Value!.Id,
                IncomeTypeId = incomeType.Value!.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 500m,
                Source = AccrualSources.Manual
            },
            new Accrual
            {
                GarageId = debtGarage.Value.Id,
                IncomeTypeId = incomeType.Value.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 999m,
                Source = AccrualSources.Manual,
                IsCanceled = true
            },
            new Accrual
            {
                GarageId = overpaidGarage.Value!.Id,
                IncomeTypeId = incomeType.Value.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 50m,
                Source = AccrualSources.Manual
            });
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = debtGarage.Value.Id,
                IncomeTypeId = incomeType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 15),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 250m
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = debtGarage.Value.Id,
                IncomeTypeId = incomeType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 16),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 999m,
                IsCanceled = true
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = overpaidGarage.Value.Id,
                IncomeTypeId = incomeType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 15),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 75m
            });
        await database.Context.SaveChangesAsync();

        var garages = await service.GetGaragesAsync("BAL", CancellationToken.None);

        var debt = Assert.Single(garages, garage => garage.Number == "BAL-1");
        Assert.Equal(350m, debt.Balance);
        Assert.Equal(350m, debt.OverdueDebt);
        var overpaid = Assert.Single(garages, garage => garage.Number == "BAL-2");
        Assert.Equal(-25m, overpaid.Balance);
        Assert.Equal(0m, overpaid.OverdueDebt);
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
        await service.ArchiveSupplierGroupAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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
    public async Task SupplierContactAsync_SavesContactAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null),
            null,
            CancellationToken.None);

        var created = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, " Петров И.А. ", " Директор ", " +7 ", "contact@example.com", "Работает", " Основной "),
            actorUserId,
            CancellationToken.None);
        var updated = await service.UpdateSupplierContactAsync(
            created.Value!.Id,
            new UpsertSupplierContactRequest(supplier.Value.Id, "Петров И.А.", "Менеджер", "+7", "contact@example.com", "Не работает", "Уволен"),
            actorUserId,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal("Петров И.А.", created.Value.FullName);
        Assert.True(updated.Succeeded);
        Assert.Equal("Менеджер", updated.Value!.Position);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_contact_created" && item.ActorUserId == actorUserId);
        var updateAudit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_contact_updated");
        Assert.Equal(actorUserId, updateAudit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), updateAudit.EntityId);
        Assert.Contains("Обновлен контакт Петров И.А.", updateAudit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreSupplierContactAsync_RestoresSupplierAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null),
            null,
            CancellationToken.None);
        var contact = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Петров", null, null, null, "Работает", null),
            null,
            CancellationToken.None);
        await service.ArchiveSupplierContactAsync(contact.Value!.Id, "Контакт временно не нужен", actorUserId, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value.Id, "Поставщик временно скрыт", actorUserId, CancellationToken.None);

        var restored = await service.RestoreSupplierContactAsync(contact.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(restored.Succeeded);
        Assert.False(restored.Value!.IsArchived);
        Assert.False((await database.Context.Suppliers.FindAsync(new object[] { supplier.Value.Id }, CancellationToken.None))!.IsArchived);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_restored" && item.Summary.Contains("при восстановлении контакта", StringComparison.Ordinal));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_contact_restored" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task StaffDepartmentAndMemberAsync_WriteAuditAndBlockUsedDepartmentArchive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var department = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Бухгалтерия"), actorUserId, CancellationToken.None);
        var member = await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Петрова Ольга", department.Value!.Id, 40000.005m), actorUserId, CancellationToken.None);
        var archiveDepartment = await service.ArchiveStaffDepartmentAsync(department.Value.Id, "Отдел больше не нужен", actorUserId, CancellationToken.None);
        var updatedMember = await service.UpdateStaffMemberAsync(
            member.Value!.Id,
            new UpsertStaffMemberRequest("Петрова Ольга", department.Value.Id, 41000),
            actorUserId,
            CancellationToken.None);

        Assert.True(department.Succeeded);
        Assert.True(member.Succeeded);
        Assert.Equal(40000.01m, member.Value.Rate);
        Assert.False(archiveDepartment.Succeeded);
        Assert.Equal("staff_department_used", archiveDepartment.ErrorCode);
        Assert.True(updatedMember.Succeeded);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.staff_department_created");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.staff_member_created");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.staff_member_updated");
    }

    [Fact]
    public async Task RestoreStaffMemberAsync_RestoresOnlyWhenDepartmentIsActiveAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var department = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Accounting"), actorUserId, CancellationToken.None);
        var member = await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Olga Petrova", department.Value!.Id, 40000m), actorUserId, CancellationToken.None);

        var archivedMember = await service.ArchiveStaffMemberAsync(member.Value!.Id, "Employee left", actorUserId, CancellationToken.None);
        var activeMembers = await service.GetStaffMembersAsync(department.Value.Id, null, CancellationToken.None);
        var allMembers = await service.GetStaffMembersAsync(department.Value.Id, null, CancellationToken.None, includeArchived: true);
        var archivedDepartment = await service.ArchiveStaffDepartmentAsync(department.Value.Id, "Department closed", actorUserId, CancellationToken.None);
        var restoreWithArchivedDepartment = await service.RestoreStaffMemberAsync(member.Value.Id, actorUserId, CancellationToken.None);

        var restoredDepartment = await service.RestoreStaffDepartmentAsync(department.Value.Id, actorUserId, CancellationToken.None);
        var restoredMember = await service.RestoreStaffMemberAsync(member.Value.Id, actorUserId, CancellationToken.None);
        var activeMembersAfterRestore = await service.GetStaffMembersAsync(department.Value.Id, "olga", CancellationToken.None);

        Assert.True(archivedMember.Succeeded);
        Assert.True(archivedMember.Value!.IsArchived);
        Assert.Empty(activeMembers);
        Assert.Contains(allMembers, item => item.Id == member.Value.Id && item.IsArchived);
        Assert.True(archivedDepartment.Succeeded);
        Assert.False(restoreWithArchivedDepartment.Succeeded);
        Assert.Equal("staff_department_not_found", restoreWithArchivedDepartment.ErrorCode);
        Assert.True(restoredDepartment.Succeeded);
        Assert.True(restoredMember.Succeeded);
        Assert.False(restoredMember.Value!.IsArchived);
        Assert.Contains(activeMembersAfterRestore, item => item.Id == member.Value.Id && !item.IsArchived);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.staff_member_archived" &&
            item.ActorUserId == actorUserId &&
            item.Summary.Contains("Olga Petrova", StringComparison.Ordinal) &&
            item.MetadataJson != null &&
            item.MetadataJson.Contains("Employee left", StringComparison.Ordinal));
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.staff_department_archived" &&
            item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.staff_department_restored" &&
            item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.staff_member_restored" &&
            item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RestoreSupplierGroupAsync_RejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var archived = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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
        await service.ArchiveSupplierAsync(supplier.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(group.Value.Id, "Тестовая причина", null, CancellationToken.None);

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
        await service.ArchiveIncomeTypeAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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
        await service.ArchiveIncomeTypeAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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

        var result = await service.ArchiveIncomeTypeAsync(systemType.Id, "Тестовая причина", null, CancellationToken.None);

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
        await service.ArchiveExpenseTypeAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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
        await service.ArchiveExpenseTypeAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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
        await service.ArchiveTariffAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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
                ElectricityFirstThreshold: 50.55555m,
                ElectricitySecondThreshold: 100.77777m,
                ElectricityFirstRate: 3.11111m,
                ElectricitySecondRate: 4.22222m,
                ElectricityThirdRate: 5.33333m),
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
        Assert.Contains("электричество: От 0 кВт до 50.556 кВт по 3.1111", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("От 50.556 кВт до 100.778 кВт по 4.2222", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("От 100.778 кВт по 5.3333", audit.Summary, StringComparison.Ordinal);
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
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("tariff", metadata.RootElement.GetProperty("dictionaryEntityType").GetString());
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Наименование", changedFields, StringComparison.Ordinal);
        Assert.Contains("База расчета", changedFields, StringComparison.Ordinal);
        Assert.Contains("Ставка", changedFields, StringComparison.Ordinal);
        Assert.Contains("Дата начала", changedFields, StringComparison.Ordinal);
        Assert.Equal("5", metadata.RootElement.GetProperty("changesCount").GetString());
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
                ElectricityFirstThreshold: 50m,
                ElectricitySecondThreshold: 100m,
                ElectricityFirstRate: 3m,
                ElectricitySecondRate: 4m,
                ElectricityThirdRate: 5m),
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
    public async Task CreateChargeServiceSettingAsync_SavesServiceSettingsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Электроэнергия", true, 1, 1, 30, 6, 30, true, true, "кВт"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.True(result.Value.IsRegular);
        Assert.Equal(1, result.Value.PeriodicityMonths);
        Assert.Equal(1, result.Value.AccrualStartMonth);
        Assert.Equal(30, result.Value.PaymentDueDay);
        Assert.Equal(6, result.Value.PaymentDueMonth);
        Assert.Equal(30, result.Value.OverdueGraceDays);
        Assert.True(result.Value.IsMetered);
        Assert.True(result.Value.HasTieredTariff);
        Assert.Equal("кВт", result.Value.UnitName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Электроэнергия", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_SavesAccountingLinksAndRejectsMismatch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var waterTariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.IncomeTypes.Add(incomeType);
        database.Context.Tariffs.AddRange(tariff, waterTariff);
        await database.Context.SaveChangesAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос", true, 12, 1, 30, 6, 30, false, false, "руб.", incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);
        var mismatch = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода как членский", true, 1, 1, 30, 6, 30, true, false, "м3", incomeType.Id, waterTariff.Id),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(incomeType.Id, result.Value!.IncomeTypeId);
        Assert.Equal(tariff.Id, result.Value.TariffId);
        Assert.False(mismatch.Succeeded);
        Assert.Equal("charge_service_tariff_mismatch", mismatch.ErrorCode);
    }

    [Fact]
    public async Task UpdateChargeServiceSettingAsync_WritesChangedFieldsAndSkipsNoOp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var created = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", true, 1, 1, 30, 6, 30, true, false, "м3"),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var actorUserId = Guid.NewGuid();
        var result = await service.UpdateChargeServiceSettingAsync(
            created.Value!.Id,
            new UpsertChargeServiceSettingRequest("Водоснабжение", true, 12, 2, 31, 12, 45, true, false, "куб."),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Водоснабжение", result.Value!.Name);
        Assert.Equal(12, result.Value.PeriodicityMonths);
        Assert.Equal(31, result.Value.PaymentDueDay);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        var changedFields = metadata.RootElement.GetProperty("changedFields").GetString();
        Assert.Contains("Наименование", changedFields, StringComparison.Ordinal);
        Assert.Contains("Периодичность", changedFields, StringComparison.Ordinal);
        Assert.Contains("День оплаты", changedFields, StringComparison.Ordinal);

        var noOp = await service.UpdateChargeServiceSettingAsync(
            created.Value.Id,
            new UpsertChargeServiceSettingRequest("Водоснабжение", true, 12, 2, 31, 12, 45, true, false, "куб."),
            actorUserId,
            CancellationToken.None);

        Assert.True(noOp.Succeeded);
        Assert.Single(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_RejectsInvalidFebruaryDayAndTieredWithoutMeter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var februaryResult = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос", true, 1, 1, 29, 2, 30, false, false, "руб."),
            Guid.NewGuid(),
            CancellationToken.None);
        var tieredResult = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Порог без счетчика", true, 1, 1, 30, 6, 30, false, true, "руб."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(februaryResult.Succeeded);
        Assert.Equal("charge_service_payment_day_invalid", februaryResult.ErrorCode);
        Assert.False(tieredResult.Succeeded);
        Assert.Equal("charge_service_tiered_requires_meter", tieredResult.ErrorCode);
        Assert.Empty(database.Context.ChargeServiceSettings);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task ArchiveChargeServiceSettingAsync_HidesSettingRequiresReasonAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Electricity", true, 1, 1, 30, 6, 30, true, true, "kWh"),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var emptyReason = await service.ArchiveChargeServiceSettingAsync(created.Value!.Id, " ", actorUserId, CancellationToken.None);
        var archive = await service.ArchiveChargeServiceSettingAsync(created.Value.Id, "No longer used", actorUserId, CancellationToken.None);
        var activeSettings = await service.GetChargeServiceSettingsAsync("electric", CancellationToken.None);
        var archivedSettings = await service.GetChargeServiceSettingsAsync("electric", CancellationToken.None, includeArchived: true);

        Assert.False(emptyReason.Succeeded);
        Assert.Equal("dictionary_archive_reason_required", emptyReason.ErrorCode);
        Assert.True(archive.Succeeded);
        Assert.True(archive.Value!.IsArchived);
        Assert.DoesNotContain(activeSettings, item => item.Id == created.Value.Id);
        Assert.Contains(archivedSettings, item => item.Id == created.Value.Id && item.IsArchived);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_archived");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), audit.EntityId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("No longer used", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RestoreChargeServiceSettingAsync_RestoresArchivedSettingAndRejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var archived = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Electricity", true, 1, 1, 30, 6, 30, true, true, "kWh"),
            null,
            CancellationToken.None);
        await service.ArchiveChargeServiceSettingAsync(archived.Value!.Id, "No longer used", actorUserId, CancellationToken.None);

        var restored = await service.RestoreChargeServiceSettingAsync(archived.Value.Id, actorUserId, CancellationToken.None);
        var activeSettings = await service.GetChargeServiceSettingsAsync("electric", CancellationToken.None);

        await service.ArchiveChargeServiceSettingAsync(restored.Value!.Id, "Check duplicate", actorUserId, CancellationToken.None);
        await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Electricity", true, 1, 1, 30, 6, 30, true, true, "kWh"),
            null,
            CancellationToken.None);
        var duplicateRestore = await service.RestoreChargeServiceSettingAsync(archived.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(restored.Succeeded);
        Assert.False(restored.Value.IsArchived);
        Assert.Contains(activeSettings, item => item.Id == archived.Value.Id && !item.IsArchived);
        Assert.False(duplicateRestore.Succeeded);
        Assert.Equal("charge_service_duplicate", duplicateRestore.ErrorCode);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.charge_service_restored" &&
            item.ActorUserId == actorUserId &&
            item.EntityId == archived.Value.Id.ToString());
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

        var result = await service.ArchiveTariffAsync(created.Value!.Id, "Тариф заменен новым", actorUserId, CancellationToken.None);

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
        await service.ArchiveTariffAsync(created.Value!.Id, "Тестовая причина", null, CancellationToken.None);

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
        await service.ArchiveTariffAsync(archived.Value!.Id, "Тестовая причина", null, CancellationToken.None);
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

    [Fact]
    public async Task IrregularPaymentAsync_SavesStatusAndBlocksArchiveWhenUsed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Вступительный взнос", 1500m), actorUserId, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Вступительный взнос", "entry"), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("1", 1, 1, null, 0m, null, null, null), null, CancellationToken.None);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = garage.Value!.Id,
            IncomeTypeId = incomeType.Value!.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 1500m,
            Source = AccrualSources.Manual
        });
        await database.Context.SaveChangesAsync();

        var status = await service.SetIrregularPaymentStatusAsync(created.Value!.Id, new UpdateIrregularPaymentStatusRequest(false, "Временно отключен"), actorUserId, CancellationToken.None);
        var archive = await service.ArchiveIrregularPaymentAsync(created.Value.Id, "Больше не используется", actorUserId, CancellationToken.None);
        var payments = await service.GetIrregularPaymentsAsync(null, CancellationToken.None);

        Assert.True(status.Succeeded);
        Assert.False(status.Value!.IsActive);
        Assert.False(archive.Succeeded);
        Assert.Equal("irregular_payment_used", archive.ErrorCode);
        Assert.True(Assert.Single(payments).IsUsed);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.irregular_payment_created" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.irregular_payment_status_changed" && item.ActorUserId == actorUserId);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.irregular_payment_archived");
    }

    [Fact]
    public async Task ArchiveIrregularPaymentAsync_HidesUnusedPaymentAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Штраф за пропуск", 300m), null, CancellationToken.None);

        var emptyReason = await service.ArchiveIrregularPaymentAsync(created.Value!.Id, " ", actorUserId, CancellationToken.None);
        var archive = await service.ArchiveIrregularPaymentAsync(created.Value!.Id, "Больше не применяется", actorUserId, CancellationToken.None);
        var activePayments = await service.GetIrregularPaymentsAsync(null, CancellationToken.None);
        var archivedPayments = await service.GetIrregularPaymentsAsync(null, CancellationToken.None, includeArchived: true);

        Assert.False(emptyReason.Succeeded);
        Assert.Equal("dictionary_archive_reason_required", emptyReason.ErrorCode);
        Assert.True(archive.Succeeded);
        Assert.Empty(activePayments);
        Assert.True(Assert.Single(archivedPayments).IsArchived);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.irregular_payment_archived");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(created.Value.Id.ToString(), audit.EntityId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Больше не применяется", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RestoreIrregularPaymentAsync_RestoresUnusedPaymentAndRejectsDuplicateActiveName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var archived = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Gate repair", 500m), null, CancellationToken.None);
        await service.ArchiveIrregularPaymentAsync(archived.Value!.Id, "Finished", actorUserId, CancellationToken.None);

        var restored = await service.RestoreIrregularPaymentAsync(archived.Value.Id, actorUserId, CancellationToken.None);
        var activePayments = await service.GetIrregularPaymentsAsync("gate", CancellationToken.None);

        await service.ArchiveIrregularPaymentAsync(restored.Value!.Id, "Check duplicate", actorUserId, CancellationToken.None);
        await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Gate repair", 700m), null, CancellationToken.None);
        var duplicateRestore = await service.RestoreIrregularPaymentAsync(archived.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(restored.Succeeded);
        Assert.False(restored.Value.IsArchived);
        Assert.Contains(activePayments, item => item.Id == archived.Value.Id && !item.IsArchived);
        Assert.False(duplicateRestore.Succeeded);
        Assert.Equal("irregular_payment_duplicate", duplicateRestore.ErrorCode);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.irregular_payment_restored" &&
            item.ActorUserId == actorUserId &&
            item.EntityId == archived.Value.Id.ToString());
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
