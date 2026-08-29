using System.Data.Common;
using System.Text.Json;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionaryServiceTests
{
    [Fact]
    public async Task MeasurementUnits_RejectDuplicatesRenameAssignedServicesAndProtectUsedEntries()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateMeasurementUnitAsync(
            new UpsertMeasurementUnitRequest(" упаковка "),
            actorUserId,
            CancellationToken.None);
        var duplicate = await service.CreateMeasurementUnitAsync(
            new UpsertMeasurementUnitRequest("УПАКОВКА"),
            actorUserId,
            CancellationToken.None);
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Выдача комплектов",
            UnitName = "упаковка"
        });
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateMeasurementUnitAsync(
            created.Value!.Id,
            new UpsertMeasurementUnitRequest("комплект"),
            actorUserId,
            CancellationToken.None);
        var archive = await service.ArchiveMeasurementUnitAsync(
            created.Value.Id,
            "Больше не используется",
            actorUserId,
            CancellationToken.None);
        var page = await service.GetMeasurementUnitsPageAsync("КОМП", 0, 25, CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.Equal("measurement_unit_duplicate", duplicate.ErrorCode);
        Assert.True(updated.Succeeded);
        Assert.Equal("комплект", database.Context.ChargeServiceSettings.Single().UnitName);
        Assert.False(archive.Succeeded);
        Assert.Equal("measurement_unit_in_use", archive.ErrorCode);
        Assert.Single(page.Items);
        Assert.Equal("комплект", page.Items[0].Name);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.measurement_unit_updated" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateOwnerAsync_TrimsFieldsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateOwnerAsync(
            new UpsertOwnerRequest(" Иванов ", " Иван ", " Иванович ", " 8 900 123-45-67 ", " Адрес ", " Счетчик "),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Иванов Иван Иванович", result.Value!.FullName);
        Assert.Equal("+7 (900) 123-45-67", result.Value.Phone);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task PhoneFields_NormalizeRecognizedValuesAndRejectIncompleteNumbers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Связь"), null, CancellationToken.None);

        var owner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Иванов", "Иван", null, "9131234567", null, null),
            null,
            CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Оператор", group.Value!.Id, null, null, null, "8 913 765 43 21", null, 0, null),
            null,
            CancellationToken.None);
        var contact = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Петров Петр", null, "79130001122", null, "Работает", null),
            null,
            CancellationToken.None);
        var invalidOwner = await service.UpdateOwnerAsync(
            owner.Value!.Id,
            new UpsertOwnerRequest("Иванов", "Иван", null, "+7 913", null, null),
            null,
            CancellationToken.None);
        var invalidSupplier = await service.UpdateSupplierAsync(
            supplier.Value.Id,
            new UpsertSupplierRequest("Оператор", group.Value.Id, null, null, null, "номер 9131234567", null, 0, null),
            null,
            CancellationToken.None);
        var invalidContact = await service.UpdateSupplierContactAsync(
            contact.Value!.Id,
            new UpsertSupplierContactRequest(supplier.Value.Id, "Петров Петр", null, "123", null, "Работает", null),
            null,
            CancellationToken.None);

        Assert.Equal("+7 (913) 123-45-67", owner.Value.Phone);
        Assert.Equal("+7 (913) 765-43-21", supplier.Value.Phone);
        Assert.Equal("+7 (913) 000-11-22", contact.Value.Phone);
        Assert.False(invalidOwner.Succeeded);
        Assert.Equal("phone_invalid", invalidOwner.ErrorCode);
        Assert.False(invalidSupplier.Succeeded);
        Assert.Equal("phone_invalid", invalidSupplier.ErrorCode);
        Assert.False(invalidContact.Succeeded);
        Assert.Equal("phone_invalid", invalidContact.ErrorCode);
        Assert.Equal("+7 (913) 123-45-67", (await service.GetOwnersAsync(null, CancellationToken.None)).Single().Phone);
    }

    [Fact]
    public async Task OwnerAudit_UsesWriterStructuredFieldsAndArchiveReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var created = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Ivanov", "Ivan", null, "+7 (900) 123-45-67", "Private address", null),
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
        Assert.DoesNotContain("+7 (900) 123-45-67", createAudit.MetadataJson, StringComparison.Ordinal);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, "+7 (911) 000-00-01", null, null), null, CancellationToken.None);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Сидоров", "Сергей", null, "+7 (922) 000-00-02", null, null), null, CancellationToken.None);

        var result = await service.GetOwnersAsync("922", CancellationToken.None);

        var owner = Assert.Single(result);
        Assert.Equal("Сидоров", owner.LastName);
    }

    [Fact]
    public async Task ListMethods_ApplyExplicitLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var groupResults = new List<DictionaryResult<SupplierGroupDto>>();
        for (var index = 0; index < 3; index++)
        {
            var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest($"Владелец{index}", "Тест", null, null, null, null), null, CancellationToken.None);
            Assert.True(owner.Succeeded);
            Assert.True((await service.CreateGarageAsync(new UpsertGarageRequest($"L-{index}", 1, 1, owner.Value!.Id, 0, null, null, null), null, CancellationToken.None)).Succeeded);
            groupResults.Add(await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest($"Группа {index}"), null, CancellationToken.None));
            Assert.True(groupResults[index].Succeeded);
            Assert.True((await service.CreateSupplierAsync(new UpsertSupplierRequest($"Поставщик {index}", groupResults[index].Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None)).Succeeded);
            var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest($"Поступление {index}", $"income_limit_{index}"), null, CancellationToken.None);
            Assert.True(incomeType.Succeeded);
            Assert.True((await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest($"Выплата {index}", $"expense_limit_{index}"), null, CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateTariffAsync(new UpsertTariffRequest($"Тариф {index}", "fixed", 10 + index, new DateOnly(2026, 1, 1).AddMonths(index), null), null, CancellationToken.None)).Succeeded);
            Assert.True((await service.CreateFeeCampaignAsync(new UpsertFeeCampaignRequest($"Сбор {index}", otherIncome.Id, null, 100 + index, 1000 + index, new DateOnly(2026, 1, 1).AddMonths(index), null, true, 30), null, CancellationToken.None)).Succeeded);
        }

        Assert.Equal(2, (await service.GetOwnersAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetGaragesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetSupplierGroupsAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetSuppliersAsync(null, null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetIncomeTypesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetExpenseTypesAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetTariffsAsync(null, CancellationToken.None, 2)).Count);
        Assert.Equal(2, (await service.GetFeeCampaignsAsync(null, CancellationToken.None, 2)).Count);
    }

    [Fact]
    public async Task GetFeeCampaignsAsync_PrioritizesNewestActiveCampaignsBeforeArchivedRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var oldestActive = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Старый активный сбор", otherIncome.Id, null, 100m, 0m, new DateOnly(2026, 1, 1), null, true, 30),
            null,
            CancellationToken.None);
        var archived = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Архивный сбор", otherIncome.Id, null, 100m, 0m, new DateOnly(2026, 4, 1), null, true, 30),
            null,
            CancellationToken.None);
        var newestActive = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Новый активный сбор", otherIncome.Id, null, 100m, 0m, new DateOnly(2026, 5, 1), null, true, 30),
            null,
            CancellationToken.None);
        Assert.True(oldestActive.Succeeded, oldestActive.ErrorMessage);
        Assert.True(archived.Succeeded, archived.ErrorMessage);
        Assert.True(newestActive.Succeeded, newestActive.ErrorMessage);
        Assert.True((await service.ArchiveFeeCampaignAsync(archived.Value!.Id, "Сбор завершён", null, CancellationToken.None)).Succeeded);

        var campaigns = await service.GetFeeCampaignsAsync(null, CancellationToken.None, 2, includeArchived: true);

        Assert.Equal([newestActive.Value!.Id, oldestActive.Value!.Id], campaigns.Select(campaign => campaign.Id));
        Assert.All(campaigns, campaign => Assert.False(campaign.IsArchived));
    }

    [Fact]
    public async Task ListMethods_SearchSupplierGroupsAndAccountingTypes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(ownerResult.Value!.Id, "   ", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("dictionary_archive_reason_required", result.ErrorCode);
        Assert.False(database.Context.Owners.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived");
    }

    [Fact]
    public async Task ArchiveOwnerAsync_RejectsOwnerWithActiveGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, owner.Value!.Id, 0, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(owner.Value.Id, "Карточка больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("owner_has_active_garages", result.ErrorCode);
        Assert.False(database.Context.Owners.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived");
    }

    [Fact]
    public async Task ArchiveSupplierGroupAsync_RejectsGroupWithActiveSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        var result = await service.ArchiveSupplierGroupAsync(group.Value.Id, "Группа больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_has_active_suppliers", result.ErrorCode);
        Assert.False(database.Context.SupplierGroups.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_group_archived");
    }

    [Fact]
    public async Task ArchiveSupplierAsync_RejectsSupplierWithActiveContact()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Петров Петр", null, null, null, "Работает", null),
            null,
            CancellationToken.None);

        var result = await service.ArchiveSupplierAsync(supplier.Value.Id, "Поставщик больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_has_active_contacts", result.ErrorCode);
        Assert.False(database.Context.Suppliers.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_archived");
    }

    [Fact]
    public async Task ArchiveTariffAsync_RejectsTariffAssignedToActiveService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 10, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting { Name = "Водоснабжение", TariffId = tariff.Value!.Id });
        await database.Context.SaveChangesAsync();

        var result = await service.ArchiveTariffAsync(tariff.Value.Id, "Тариф больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_has_active_services", result.ErrorCode);
        Assert.False(database.Context.Tariffs.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_archived");
    }

    [Fact]
    public async Task ArchiveIncomeTypeAsync_RejectsTypeAssignedToActiveService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Дополнительный взнос", "additional_fee"), null, CancellationToken.None);
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting { Name = "Дополнительная услуга", IncomeTypeId = incomeType.Value!.Id });
        await database.Context.SaveChangesAsync();

        var result = await service.ArchiveIncomeTypeAsync(incomeType.Value.Id, "Вид больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_has_active_services", result.ErrorCode);
        Assert.False(database.Context.IncomeTypes.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.income_type_archived");
    }

    [Fact]
    public async Task ArchiveExpenseTypeAsync_RejectsTypeAssignedToActiveSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Дополнительный расход", "additional_expense"), null, CancellationToken.None);
        var group = new SupplierGroup { Name = "Дополнительные поставщики" };
        database.Context.Suppliers.Add(new Supplier { Name = "Дополнительный поставщик", Group = group, ExpenseTypeId = expenseType.Value!.Id });
        await database.Context.SaveChangesAsync();

        var result = await service.ArchiveExpenseTypeAsync(expenseType.Value.Id, "Статья больше не используется", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("expense_type_has_active_services", result.ErrorCode);
        Assert.False(database.Context.ExpenseTypes.Single().IsArchived);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "dictionary.expense_type_archived");
    }

    [Fact]
    public async Task ArchiveChargeServiceSettingAsync_AllowsServiceAssignedToActiveSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var setting = new ChargeServiceSetting { Name = "Водоснабжение" };
        database.Context.ChargeServiceSettings.Add(setting);
        database.Context.Suppliers.Add(new Supplier { Name = "Водоканал", GroupId = group.Value!.Id, ChargeServiceSettingId = setting.Id });
        await database.Context.SaveChangesAsync();

        var result = await service.ArchiveChargeServiceSettingAsync(setting.Id, "Услуга больше не используется", null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(database.Context.ChargeServiceSettings.Single().IsArchived);
        Assert.Equal(setting.Id, database.Context.Suppliers.Single().ChargeServiceSettingId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_archived");
    }

    [Fact]
    public async Task ListMethods_ReturnArchivedRecordsOnlyWhenRequested()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var owner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", "Иванович", "+7 (900) 123-45-67", "Адрес", "Счетчик"), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, owner.Value!.Id, 10.005m, 1.2345m, 9.8765m, "Угловой"), null, CancellationToken.None);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, "123", "Юр. адрес", "Петров", "+7 (901) 123-45-67", "mail@example.com", 20.005m, "Комментарий"), null, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership_custom"), null, CancellationToken.None);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электрик", "electrician"), null, CancellationToken.None);
        var tariff = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 12.34555m, new DateOnly(2026, 7, 1), "Комментарий"), null, CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.UpdateOwnerAsync(owner.Value.Id, new UpsertOwnerRequest(" Иванов ", " Иван ", " Иванович ", " +7 (900) 123-45-67 ", " Адрес ", " Счетчик "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateGarageAsync(garage.Value!.Id, new UpsertGarageRequest(" 12 ", 2, 1, owner.Value.Id, 10.005m, 1.2345m, 9.8765m, " Угловой "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateSupplierGroupAsync(group.Value.Id, new UpsertSupplierGroupRequest(" Коммунальные услуги "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateSupplierAsync(supplier.Value!.Id, new UpsertSupplierRequest(" Водоканал ", group.Value.Id, " 123 ", " Юр. адрес ", " Петров ", " +7 (901) 123-45-67 ", " mail@example.com ", 20.005m, " Комментарий "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateIncomeTypeAsync(incomeType.Value!.Id, new UpsertAccountingTypeRequest(" Членский взнос ", " membership_custom "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateExpenseTypeAsync(expenseType.Value!.Id, new UpsertAccountingTypeRequest(" Электрик ", " electrician "), actorUserId, CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateTariffAsync(tariff.Value!.Id, new UpsertTariffRequest(" Вода ", " meter_water ", 12.34555m, new DateOnly(2026, 7, 1), " Комментарий "), actorUserId, CancellationToken.None)).Succeeded);

        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CurrentExtendedUpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0m, null), null, CancellationToken.None);
        var contact = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Петров И.А.", "Директор", "+7 (901) 123-45-67", "contact@example.com", "Работает", "Основной"),
            null,
            CancellationToken.None);
        var department = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Бухгалтерия"), null, CancellationToken.None);
        var staffMember = await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Петрова Ольга", department.Value!.Id, 40000.005m), null, CancellationToken.None);
        var irregularPayment = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Вступительный взнос", 1500.005m), null, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой сбор", "target_fee"), null, CancellationToken.None);
        var feeCampaign = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Ремонт ворот", otherIncome.Id, "Замена механизма", 100.005m, 1000.005m, new DateOnly(2026, 7, 1), null, true, 30),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.UpdateSupplierContactAsync(
            contact.Value!.Id,
            new UpsertSupplierContactRequest(supplier.Value.Id, " Петров И.А. ", " Директор ", " +7 (901) 123-45-67 ", " contact@example.com ", " Работает ", " Основной "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateStaffDepartmentAsync(
            department.Value!.Id,
            new UpsertStaffDepartmentRequest(" Бухгалтерия "),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateStaffMemberAsync(
            staffMember.Value!.Id,
            new UpsertStaffMemberRequest(" Петрова Ольга ", department.Value.Id, 40000.005m),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateIrregularPaymentAsync(
            irregularPayment.Value!.Id,
            new UpsertIrregularPaymentRequest(" Вступительный взнос ", 1500.005m),
            actorUserId,
            CancellationToken.None)).Succeeded);
        Assert.True((await service.UpdateFeeCampaignAsync(
            feeCampaign.Value!.Id,
            new UpsertFeeCampaignRequest(" Ремонт ворот ", otherIncome.Id, " Замена механизма ", 100.005m, 1000.005m, new DateOnly(2026, 7, 1), null, true, 30),
            actorUserId,
            CancellationToken.None)).Succeeded);

        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateOwnerAsync_WritesOldAndNewValuesToAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, null, 0, null, null, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreGarageAsync_RejectsDuplicateActiveNumber()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
    public async Task UpdateGarageAsync_LocksOpeningBalanceAndMeterBaselinesAfterHistoryExists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var garageResult = await service.CreateGarageAsync(
            new UpsertGarageRequest("15", 1, 1, null, 100m, 10m, 20m, null),
            null,
            CancellationToken.None);
        var garage = await database.Context.Garages.SingleAsync(item => item.Id == garageResult.Value!.Id);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
        database.Context.IncomeTypes.Add(incomeType);
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 1),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 50m,
            Garage = garage,
            IncomeType = incomeType
        });
        database.Context.MeterReadings.AddRange(
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = new DateOnly(2026, 7, 1), ReadingDate = new DateOnly(2026, 7, 20), PreviousValue = 10m, CurrentValue = 12m, Consumption = 2m },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Electricity, AccountingMonth = new DateOnly(2026, 7, 1), ReadingDate = new DateOnly(2026, 7, 20), PreviousValue = 20m, CurrentValue = 25m, Consumption = 5m });
        await database.Context.SaveChangesAsync();

        var balance = await service.UpdateGarageAsync(
            garage.Id,
            new UpsertGarageRequest("15", 1, 1, null, 101m, 10m, 20m, null),
            null,
            CancellationToken.None);
        var water = await service.UpdateGarageAsync(
            garage.Id,
            new UpsertGarageRequest("15", 1, 1, null, 100m, 11m, 20m, null),
            null,
            CancellationToken.None);
        var electricity = await service.UpdateGarageAsync(
            garage.Id,
            new UpsertGarageRequest("15", 1, 1, null, 100m, 10m, 21m, null),
            null,
            CancellationToken.None);

        Assert.Equal("garage_starting_balance_locked", balance.ErrorCode);
        Assert.Equal("garage_initial_water_meter_locked", water.ErrorCode);
        Assert.Equal("garage_initial_electricity_meter_locked", electricity.ErrorCode);
        Assert.Equal(100m, garage.StartingBalance);
        Assert.Equal(10m, garage.InitialWaterMeterValue);
        Assert.Equal(20m, garage.InitialElectricityMeterValue);
    }

    [Fact]
    public async Task ArchiveGarageAsync_HidesGarageFromList()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var firstOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, "+7 900 111-22-33", "Лишний адрес", "Лишние заметки"), null, CancellationToken.None);
        var secondOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, firstOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("21", 1, 1, secondOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);

        var byNumber = await service.GetGaragesAsync("12", CancellationToken.None);
        var byOwner = await service.GetGaragesAsync("петров", CancellationToken.None);

        var garageByNumber = Assert.Single(byNumber);
        Assert.Equal("Иванов Иван", garageByNumber.OwnerName);
        Assert.Equal("+7 (900) 111-22-33", garageByNumber.OwnerPhone);
        var garageByOwner = Assert.Single(byOwner);
        Assert.Equal("21", garageByOwner.Number);
        Assert.Null(garageByOwner.OwnerPhone);
    }

    [Fact]
    public async Task GarageSearch_RanksExactNumberThenPrefixesThenContainedMatches()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        foreach (var number in new[] { "107", "117", "7", "710", "70", "71" })
        {
            await service.CreateGarageAsync(
                new UpsertGarageRequest(number, 1, 1, null, 0, null, null, null),
                null,
                CancellationToken.None);
        }

        var list = await service.GetGaragesAsync("7", CancellationToken.None, 20);
        var page = await service.GetGaragesPageAsync("7", 0, 20, null, null, CancellationToken.None);

        var expected = new[] { "7", "70", "71", "710", "107", "117" };
        Assert.Equal(expected, list.Select(garage => garage.Number));
        Assert.Equal(expected, page.Items.Select(garage => garage.Number));
    }

    [Fact]
    public async Task GarageRepository_ReturnsCompleteActiveBatchAndHistoricalStartingBalance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var first = new Garage { Number = "20", StartingBalance = 200m, Owner = owner };
        var second = new Garage { Number = "10", StartingBalance = 100m, Owner = owner };
        var archived = new Garage { Number = "05", StartingBalance = 500m, Owner = owner, IsArchived = true };
        database.Context.AddRange(owner, first, second, archived);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        var repository = new EfGarageRepository(database.Context);

        var active = await repository.GetAllActiveWithOwnerAsync(CancellationToken.None);
        var historicalStartingBalance = await repository.GetStartingBalanceAsync(archived.Id, CancellationToken.None);

        Assert.Equal(["10", "20"], active.Select(garage => garage.Number));
        Assert.All(active, garage => Assert.Equal("Иванов Иван", garage.Owner?.FullName));
        Assert.Equal(500m, historicalStartingBalance);
    }

    [Fact]
    public async Task GetGaragesAsync_ReturnsCalculatedBalanceAndOverdueDebt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Electricity", "electricity_custom"), null, CancellationToken.None);
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
    public async Task GarageRepository_OverdueDebtStartsAfterGraceAndRequiresFullPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var garage = new Garage { Number = "DUE-1" };
        var incomeType = new IncomeType { Name = "Annual", Code = "annual" };
        var annualAccrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 1, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 1200m,
            Source = AccrualSources.Regular
        };
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 6, 20),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = 1199m,
            Garage = garage,
            IncomeType = incomeType
        };
        database.Context.AddRange(annualAccrual, payment);
        database.Context.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
        {
            Accrual = annualAccrual,
            FinancialOperation = payment,
            Amount = 1199m
        });
        await database.Context.SaveChangesAsync();

        var businessDate = new TestBusinessDateProvider(new DateOnly(2026, 7, 30));
        var garageRepository = new EfGarageRepository(database.Context, businessDate);
        var beforeGrace = await garageRepository.GetBalanceTotalsAsync([garage.Id], CancellationToken.None);
        businessDate.SetOverride(new DateOnly(2026, 7, 31));
        var afterGrace = await garageRepository.GetBalanceTotalsAsync([garage.Id], CancellationToken.None);

        Assert.Equal(0m, beforeGrace.OverdueAccrualTotals.GetValueOrDefault(garage.Id));
        Assert.Equal(1m, afterGrace.OverdueAccrualTotals.GetValueOrDefault(garage.Id));
        Assert.Equal(1199m, afterGrace.AllocatedIncomeTotals.GetValueOrDefault(garage.Id));
    }

    [Fact]
    public async Task GetGaragesPageAsync_SortsFieldsBeforePagination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var zedOwner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Zed", "Owner", null, "+7 (900) 200-00-00", null, null),
            null,
            CancellationToken.None);
        var alphaOwner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Alpha", "Owner", null, "+7 (900) 100-00-00", null, null),
            null,
            CancellationToken.None);
        var debtGarage = await service.CreateGarageAsync(
            new UpsertGarageRequest("01", 1, 2, zedOwner.Value!.Id, 0, null, null, null),
            null,
            CancellationToken.None);
        await service.CreateGarageAsync(
            new UpsertGarageRequest("99", 4, 1, alphaOwner.Value!.Id, 0, null, null, null),
            null,
            CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Sorting income", "sorting_income"),
            null,
            CancellationToken.None);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = debtGarage.Value!.Id,
            IncomeTypeId = incomeType.Value!.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 175m,
            Source = AccrualSources.Manual
        });
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            GarageId = debtGarage.Value.Id,
            IncomeTypeId = incomeType.Value.Id,
            OperationDate = new DateOnly(2026, 7, 15),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 75m
        });
        await database.Context.SaveChangesAsync();

        var byOwner = await service.GetGaragesPageAsync(null, 0, 1, "owner", "asc", CancellationToken.None);
        var byPeople = await service.GetGaragesPageAsync(null, 0, 1, "peopleCount", "desc", CancellationToken.None);
        var byOverdueDebt = await service.GetGaragesPageAsync(null, 0, 1, "overdueDebt", "desc", CancellationToken.None);

        Assert.Equal(2, byOwner.TotalCount);
        Assert.Equal("99", Assert.Single(byOwner.Items).Number);
        Assert.Equal("99", Assert.Single(byPeople.Items).Number);
        Assert.Equal("01", Assert.Single(byOverdueDebt.Items).Number);
        Assert.Equal(100m, byOverdueDebt.Items[0].OverdueDebt);
    }

    [Fact]
    public async Task GetGaragesPageAsync_FiltersOverdueDebtorsBeforePagination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var incomeType = new IncomeType
        {
            Name = "Debt filter income",
            Code = "debt_filter_income"
        };
        var lowerDebt = new Garage { Number = "10", StartingBalance = 100m };
        var higherDebt = new Garage { Number = "20", StartingBalance = 300m };
        var freshDebt = new Garage { Number = "30" };
        var paidDebt = new Garage { Number = "40", StartingBalance = 200m };
        var archivedDebt = new Garage { Number = "50", StartingBalance = 500m, IsArchived = true };

        database.Context.AddRange(incomeType, lowerDebt, higherDebt, freshDebt, paidDebt, archivedDebt);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = freshDebt.Id,
            IncomeTypeId = incomeType.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = DateOnly.MaxValue,
            OverdueFromDate = DateOnly.MaxValue,
            Amount = 700m,
            Source = AccrualSources.Manual
        });
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            GarageId = paidDebt.Id,
            IncomeTypeId = incomeType.Id,
            OperationDate = new DateOnly(2026, 7, 1),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 200m
        });
        await database.Context.SaveChangesAsync();

        var firstPage = await service.GetGaragesPageAsync(
            null, 0, 1, "overdueDebt", "desc", CancellationToken.None,
            includeArchived: true,
            debtorsOnly: true);
        var secondPage = await service.GetGaragesPageAsync(
            null, 1, 1, "overdueDebt", "desc", CancellationToken.None,
            includeArchived: true,
            debtorsOnly: true);
        var emptyFreshDebtPage = await service.GetGaragesPageAsync(
            "30", 0, 25, "overdueDebt", "desc", CancellationToken.None,
            includeArchived: true,
            debtorsOnly: true);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal("20", Assert.Single(firstPage.Items).Number);
        Assert.Equal(300m, firstPage.Items[0].OverdueDebt);
        Assert.Equal(2, secondPage.TotalCount);
        Assert.Equal("10", Assert.Single(secondPage.Items).Number);
        Assert.Equal(100m, secondPage.Items[0].OverdueDebt);
        Assert.Empty(emptyFreshDebtPage.Items);
        Assert.Equal(0, emptyFreshDebtPage.TotalCount);
    }

    [Fact]
    public async Task GetGaragesPageAsync_CombinesGreenColumnRangesBeforePaginationAndKeepsArchivedRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        database.Context.Garages.AddRange(
            new Garage { Number = "А-10", PeopleCount = 2, FloorCount = 1 },
            new Garage { Number = "А-20", PeopleCount = 3, FloorCount = 2, IsArchived = true },
            new Garage { Number = "Б-30", PeopleCount = 3, FloorCount = 2 },
            new Garage { Number = "А-40", PeopleCount = 5, FloorCount = 3 });
        await database.Context.SaveChangesAsync();

        var firstPage = await service.GetGaragesPageAsync(
            null, 0, 1, "number", "asc", CancellationToken.None,
            includeArchived: true,
            number: "а-",
            peopleCountMin: 2,
            peopleCountMax: 3,
            floorCountMin: 1,
            floorCountMax: 2);
        var secondPage = await service.GetGaragesPageAsync(
            null, 1, 1, "number", "asc", CancellationToken.None,
            includeArchived: true,
            number: "А-",
            peopleCountMin: 2,
            peopleCountMax: 3,
            floorCountMin: 1,
            floorCountMax: 2);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal("А-10", Assert.Single(firstPage.Items).Number);
        Assert.Equal("А-20", Assert.Single(secondPage.Items).Number);
        Assert.True(secondPage.Items[0].IsArchived);
    }

    [Fact]
    public async Task CreateGarageAsync_AllowsSeveralActiveGaragesForOneOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var owner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Семенов", "Андрей", "Петрович", "+7 (900) 100-00-00", null, null),
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        var result = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierGroupAsync_AllowsNameFromArchivedGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", Guid.NewGuid(), "5400000000", null, null, null, null, 0, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateAndUpdateSupplierAsync_UsesUnifiedChargeServiceCatalog()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var expenseFund = new Fund { Name = "Коммунальные расходы", NormalizedName = "КОММУНАЛЬНЫЕ РАСХОДЫ" };
        database.Context.Add(expenseFund);
        await database.Context.SaveChangesAsync();
        var existingWaterExpense = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Вода", "water_expense"), null, CancellationToken.None);
        var water = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);
        var electricity = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Электроэнергия", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);

        var created = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Ресурсоснабжающая организация", group.Value!.Id, null, null, null, null, null, 0, null, water.Value!.Id, ExpenseFundId: expenseFund.Id),
            null,
            CancellationToken.None);
        var updated = await service.UpdateSupplierAsync(
            created.Value!.Id,
            new UpsertSupplierRequest(created.Value.Name, group.Value.Id, null, null, null, null, null, 0, null, electricity.Value!.Id, ExpenseFundId: expenseFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);
        var serviceSortedPage = await service.GetSuppliersPageAsync(
            null,
            null,
            0,
            10,
            "service",
            "asc",
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(water.Value.Id, created.Value.ChargeServiceSettingId);
        Assert.Equal("Вода", created.Value.ChargeServiceSettingName);
        Assert.Equal(existingWaterExpense.Value!.Id, created.Value.ExpenseTypeId);
        Assert.Equal("Вода", created.Value.ExpenseTypeName);
        Assert.Equal(expenseFund.Id, created.Value.ExpenseFundId);
        Assert.True(updated.Succeeded);
        Assert.Equal(electricity.Value.Id, updated.Value!.ChargeServiceSettingId);
        Assert.Equal("Электроэнергия", updated.Value.ChargeServiceSettingName);
        Assert.NotNull(updated.Value.ExpenseTypeId);
        Assert.NotEqual(created.Value.ExpenseTypeId, updated.Value.ExpenseTypeId);
        Assert.Equal("Электроэнергия", updated.Value.ExpenseTypeName);
        Assert.Equal(expenseFund.Id, updated.Value.ExpenseFundId);
        var listedSupplier = Assert.Single(serviceSortedPage.Items);
        Assert.Equal(created.Value.Id, listedSupplier.Id);
        Assert.Equal(electricity.Value.Id, listedSupplier.ChargeServiceSettingId);
        Assert.Equal("Электроэнергия", listedSupplier.ChargeServiceSettingName);
        Assert.Equal(updated.Value.ExpenseTypeId, listedSupplier.ExpenseTypeId);
        Assert.Single(database.Context.ExpenseTypes.Where(item => item.IsSystem));
        Assert.Single(database.Context.AuditEvents.Where(item => item.Action == "dictionary.supplier_expense_type_created"));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_updated");
    }

    [Fact]
    public async Task CreateAndUpdateSupplierAsync_StoresExpenseConfigurationDirectly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var serviceFund = new Fund { Name = "Фонд услуги", NormalizedName = "ФОНД УСЛУГИ" };
        var manualFund = new Fund { Name = "Ручной фонд", NormalizedName = "РУЧНОЙ ФОНД" };
        database.Context.AddRange(serviceFund, manualFund);
        await database.Context.SaveChangesAsync();
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Водоснабжение", "water_supplier"), null, CancellationToken.None);
        var chargeService = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);

        var created = await service.CreateSupplierAsync(
            new UpsertSupplierRequest(
                "Водоканал",
                group.Value!.Id,
                null,
                null,
                null,
                null,
                null,
                0,
                null,
                ChargeServiceSettingId: chargeService.Value!.Id,
                ExpenseTypeId: expenseType.Value!.Id,
                ExpenseFundId: manualFund.Id),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(manualFund.Id, created.Value!.ExpenseFundId);
        Assert.Equal(manualFund.Id, created.Value.ExpenseFundId);
        Assert.Equal("Ручной фонд", created.Value.ExpenseFundName);

        var updated = await service.UpdateSupplierAsync(
            created.Value.Id,
            new UpsertSupplierRequest(
                created.Value.Name,
                group.Value.Id,
                null,
                null,
                null,
                null,
                null,
                0,
                null,
                ChargeServiceSettingId: chargeService.Value.Id,
                Version: created.Value.Version,
                ExpenseTypeId: expenseType.Value.Id,
                ExpenseFundId: serviceFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(updated.Succeeded);
        Assert.Equal(serviceFund.Id, updated.Value!.ExpenseFundId);
        Assert.Equal("Фонд услуги", updated.Value.ExpenseFundName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_updated");
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Фонд расходования", metadata.RootElement.GetProperty("fieldName").GetString());
        Assert.Equal("Ручной фонд", metadata.RootElement.GetProperty("oldValue").GetString());
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingOrArchivedManualExpenseFund()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var archivedFund = new Fund { Name = "Архивный фонд", NormalizedName = "АРХИВНЫЙ ФОНД", IsArchived = true };
        database.Context.Add(archivedFund);
        await database.Context.SaveChangesAsync();

        var missing = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Первый поставщик", group.Value!.Id, null, null, null, null, null, 0, null, ExpenseFundId: Guid.NewGuid()),
            null,
            CancellationToken.None);
        var archived = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Второй поставщик", group.Value.Id, null, null, null, null, null, 0, null, ExpenseFundId: archivedFund.Id),
            null,
            CancellationToken.None);

        Assert.False(missing.Succeeded);
        Assert.Equal("supplier_expense_fund_not_found", missing.ErrorCode);
        Assert.False(archived.Succeeded);
        Assert.Equal("supplier_expense_fund_not_found", archived.ErrorCode);
        Assert.Empty(database.Context.Suppliers);
    }

    [Fact]
    public async Task CreateSupplierAsync_RequiresOnlyExpenseFundAndCreatesInternalExpenseType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var serviceWithoutExpenseType = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Не настроенная услуга", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);
        var expenseFund = new Fund { Name = "Фонд расходов", NormalizedName = "ФОНД РАСХОДОВ" };
        database.Context.Add(expenseFund);
        await database.Context.SaveChangesAsync();

        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Поставщик без вида начисления", group.Value!.Id, null, null, null, null, null, 0, null, serviceWithoutExpenseType.Value!.Id, ExpenseFundId: expenseFund.Id),
            null,
            CancellationToken.None);
        var secondResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Второй поставщик", group.Value.Id, null, null, null, null, null, 0, null, serviceWithoutExpenseType.Value.Id, ExpenseFundId: expenseFund.Id),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expenseFund.Id, result.Value!.ExpenseFundId);
        Assert.Equal("Не настроенная услуга", result.Value.ExpenseTypeName);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(result.Value.ExpenseTypeId, secondResult.Value!.ExpenseTypeId);
        var internalExpenseType = Assert.Single(database.Context.ExpenseTypes);
        Assert.True(internalExpenseType.IsSystem);
        Assert.StartsWith("supplier_service_", internalExpenseType.Code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateGarageAsync_StoresDistinctStartingOverdueDebtAndRejectsAmountAboveBalance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var created = await service.CreateGarageAsync(
            new UpsertGarageRequest("OPEN-1", 1, 1, null, 1000m, null, null, null, StartingOverdueDebt: 300.005m),
            null,
            CancellationToken.None);
        var invalid = await service.CreateGarageAsync(
            new UpsertGarageRequest("OPEN-2", 1, 1, null, 200m, null, null, null, StartingOverdueDebt: 300m),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(300.01m, created.Value!.StartingOverdueDebt);
        Assert.Equal(300.01m, created.Value.OverdueDebt);
        Assert.Equal(300.01m, (await database.Context.Garages.FindAsync(created.Value.Id))!.StartingOverdueDebt);
        Assert.False(invalid.Succeeded);
        Assert.Equal("garage_starting_overdue_debt_invalid", invalid.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierAsync_RequiresExpenseFundForChargeService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var chargeService = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);

        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null, chargeService.Value!.Id),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_expense_configuration_required", result.ErrorCode);
        Assert.Equal("Для поставщика с услугой выберите фонд расходования.", result.ErrorMessage);
        Assert.Empty(database.Context.Suppliers);
        Assert.Empty(database.Context.ExpenseTypes);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingExpenseType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Поставщики"), null, CancellationToken.None);
        var chargeService = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Услуга", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);
        var expenseFund = new Fund { Name = "Фонд услуги", NormalizedName = "ФОНД УСЛУГИ" };
        database.Context.Add(expenseFund);
        await database.Context.SaveChangesAsync();
        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Поставщик", group.Value!.Id, null, null, null, null, null, 0, null, chargeService.Value!.Id, ExpenseTypeId: Guid.NewGuid(), ExpenseFundId: expenseFund.Id),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_expense_type_not_found", result.ErrorCode);
        Assert.Empty(database.Context.Suppliers);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingOrArchivedChargeService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var expenseFund = new Fund { Name = "Архивный фонд", NormalizedName = "АРХИВНЫЙ ФОНД" };
        database.Context.Add(expenseFund);
        await database.Context.SaveChangesAsync();
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Архивная услуга", "archived_service"), null, CancellationToken.None);
        var archived = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Архивная услуга", false, null, null, null, null, 0, false, false, null),
            null,
            CancellationToken.None);
        var existingSupplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Существующий поставщик", group.Value!.Id, null, null, null, null, null, 0, null, archived.Value!.Id, ExpenseTypeId: expenseType.Value!.Id, ExpenseFundId: expenseFund.Id),
            null,
            CancellationToken.None);
        var archivedWithActiveSupplier = await service.ArchiveChargeServiceSettingAsync(archived.Value!.Id, "Услуга больше не используется", null, CancellationToken.None);
        Assert.True(archivedWithActiveSupplier.Succeeded, archivedWithActiveSupplier.ErrorMessage);
        Assert.False(existingSupplier.Value!.IsArchived);

        var missingResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Первый поставщик", group.Value!.Id, null, null, null, null, null, 0, null, Guid.NewGuid()),
            null,
            CancellationToken.None);
        var archivedResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Второй поставщик", group.Value.Id, null, null, null, null, null, 0, null, archived.Value.Id),
            null,
            CancellationToken.None);
        Assert.False(missingResult.Succeeded);
        Assert.Equal("charge_service_not_found", missingResult.ErrorCode);
        Assert.False(archivedResult.Succeeded);
        Assert.Equal("charge_service_not_found", archivedResult.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsDuplicateNameInActiveGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Utilities"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        var result = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierAsync_AllowsDuplicateNameInDifferentGroupAndArchivedSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var utilityGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Utilities"), null, CancellationToken.None);
        var bankGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Banks"), null, CancellationToken.None);
        var archived = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", utilityGroup.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.ArchiveSupplierAsync(archived.Value!.Id, "Archived supplier duplicate check", null, CancellationToken.None);

        var sameGroupAfterArchive = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", utilityGroup.Value.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        var differentGroup = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", bankGroup.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        Assert.True(sameGroupAfterArchive.Succeeded);
        Assert.True(differentGroup.Succeeded);
        Assert.Equal(3, await database.Context.Suppliers.CountAsync(item => item.Name == "Water"));
    }

    [Fact]
    public async Task UpdateSupplierAsync_RejectsDuplicateNameInActiveGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Utilities"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Electricity", group.Value.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        var result = await service.UpdateSupplierAsync(
            supplier.Value!.Id,
            new UpsertSupplierRequest("Water", group.Value.Id, null, null, null, null, null, 0, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateSupplierAsync_LocksStartingBalanceAfterFinancialHistoryExists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные"), null, CancellationToken.None);
        var supplierResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 100m, null),
            null,
            CancellationToken.None);
        var supplier = await database.Context.Suppliers.SingleAsync(item => item.Id == supplierResult.Value!.Id);
        var expenseType = new ExpenseType { Name = "Водоснабжение", Code = "water_supply" };
        database.Context.ExpenseTypes.Add(expenseType);
        database.Context.SupplierAccruals.Add(new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 50m,
            Source = "manual"
        });
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateSupplierAsync(
            supplier.Id,
            new UpsertSupplierRequest("Водоканал", group.Value.Id, null, null, null, null, null, 101m, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_starting_balance_locked", result.ErrorCode);
        Assert.Equal(100m, supplier.StartingBalance);
    }

    [Fact]
    public async Task OpeningBalanceAdjustments_SaveImmutableDocumentsAndAuditForGarageAndSupplier()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actor = new AppUser
        {
            Email = "opening-adjustments@example.test",
            NormalizedEmail = "OPENING-ADJUSTMENTS@EXAMPLE.TEST",
            DisplayName = "Бухгалтер корректировок",
            PasswordHash = "not-used"
        };
        database.Context.Users.Add(actor);
        await database.Context.SaveChangesAsync();
        var actorId = actor.Id;
        var garage = await service.CreateGarageAsync(
            new UpsertGarageRequest("ADJ-1", 1, 1, null, 100m, null, null, null),
            actorId,
            CancellationToken.None);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Корректировки"), actorId, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Поставщик корректировок", group.Value!.Id, null, null, null, null, null, 200m, null),
            actorId,
            CancellationToken.None);

        var garageAdjustment = await service.AdjustGarageOpeningBalanceAsync(
            garage.Value!.Id,
            new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 1), 125.555m, "Исправление акта сверки"),
            actorId,
            CancellationToken.None);
        var supplierAdjustment = await service.AdjustSupplierOpeningBalanceAsync(
            supplier.Value!.Id,
            new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 2), 180m, "Уточнение входящего долга"),
            actorId,
            CancellationToken.None);

        Assert.True(garageAdjustment.Succeeded);
        Assert.Equal(100m, garageAdjustment.Value!.PreviousAmount);
        Assert.Equal(125.56m, garageAdjustment.Value.NewAmount);
        Assert.True(supplierAdjustment.Succeeded);
        Assert.Equal(200m, supplierAdjustment.Value!.PreviousAmount);
        Assert.Equal(180m, supplierAdjustment.Value.NewAmount);
        Assert.Equal(125.56m, (await database.Context.Garages.FindAsync(garage.Value.Id))!.StartingBalance);
        Assert.Equal(180m, (await database.Context.Suppliers.FindAsync(supplier.Value.Id))!.StartingBalance);
        Assert.Single(await service.GetGarageOpeningBalanceAdjustmentsAsync(garage.Value.Id, CancellationToken.None));
        Assert.Single(await service.GetSupplierOpeningBalanceAdjustmentsAsync(supplier.Value.Id, CancellationToken.None));
        Assert.Equal(2, await database.Context.OpeningBalanceAdjustments.CountAsync());
        Assert.Equal(2, await database.Context.AuditEvents.CountAsync(item => item.Action.EndsWith("opening_balance_adjusted")));
    }

    [Fact]
    public async Task OpeningBalanceAdjustment_RequiresReasonAndChangedAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var garage = await service.CreateGarageAsync(
            new UpsertGarageRequest("ADJ-VALIDATION", 1, 1, null, 100m, null, null, null),
            null,
            CancellationToken.None);

        var missingReason = await service.AdjustGarageOpeningBalanceAsync(
            garage.Value!.Id,
            new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 1), 120m, " "),
            null,
            CancellationToken.None);
        var unchanged = await service.AdjustGarageOpeningBalanceAsync(
            garage.Value.Id,
            new CreateOpeningBalanceAdjustmentRequest(new DateOnly(2026, 7, 1), 100m, "Проверка"),
            null,
            CancellationToken.None);

        Assert.Equal("opening_balance_reason_required", missingReason.ErrorCode);
        Assert.Equal("opening_balance_unchanged", unchanged.ErrorCode);
        Assert.Empty(database.Context.OpeningBalanceAdjustments);
    }

    [Fact]
    public async Task SupplierContactAsync_SavesContactAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null),
            null,
            CancellationToken.None);

        var created = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, " Петров И.А. ", " Директор ", " +7 (901) 123-45-67 ", "contact@example.com", "Работает", " Основной "),
            actorUserId,
            CancellationToken.None);
        var updated = await service.UpdateSupplierContactAsync(
            created.Value!.Id,
            new UpsertSupplierContactRequest(supplier.Value.Id, "Петров И.А.", "Менеджер", "+7 (901) 765-43-21", "contact@example.com", "Не работает", "Уволен"),
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
    public async Task GetStaffMembersPageAsync_AppliesFiltersAndReturnsRequestedPage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var department = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Бухгалтерия"), null, CancellationToken.None);
        for (var index = 1; index <= 30; index++)
        {
            await service.CreateStaffMemberAsync(
                new UpsertStaffMemberRequest($"Сотрудник {index:D2}", department.Value!.Id, 40000 + index),
                null,
                CancellationToken.None);
        }

        var page = await service.GetStaffMembersPageAsync(department.Value!.Id, null, 10, 5, "fullName", "asc", CancellationToken.None);
        var filtered = await service.GetStaffMembersPageAsync(null, "29", 0, 25, "fullName", "asc", CancellationToken.None);
        var highestRate = await service.GetStaffMembersPageAsync(null, null, 0, 1, "rate", "desc", CancellationToken.None);
        var safeFallback = await service.GetStaffMembersPageAsync(null, null, 0, 1, "unsupported", "desc", CancellationToken.None);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(10, page.Offset);
        Assert.Equal(5, page.Limit);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal("Сотрудник 11", page.Items[0].FullName);
        Assert.Equal("Сотрудник 15", page.Items[^1].FullName);
        Assert.Single(filtered.Items);
        Assert.Equal("Сотрудник 29", filtered.Items[0].FullName);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("Сотрудник 30", highestRate.Items[0].FullName);
        Assert.Equal("Сотрудник 30", safeFallback.Items[0].FullName);
    }

    [Fact]
    public async Task RestoreStaffMemberAsync_RestoresOnlyWhenDepartmentIsActiveAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var utilityGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var bankGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Банки"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", utilityGroup.Value!.Id, "5401", null, "Мария", null, null, 100, null), null, CancellationToken.None);
        var bankSupplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Альфа-Банк", bankGroup.Value!.Id, "7728", null, "Ольга", null, null, 0, null), null, CancellationToken.None);
        var waterSupplier = Assert.Single(await service.GetSuppliersAsync(utilityGroup.Value.Id, "5401", CancellationToken.None));
        await service.CreateSupplierContactAsync(new UpsertSupplierContactRequest(waterSupplier.Id, "Яковлев Яков", null, "+7 (999) 000-00-09", "z@example.test", "Работает", null), null, CancellationToken.None);
        await service.CreateSupplierContactAsync(new UpsertSupplierContactRequest(bankSupplier.Value!.Id, "Анна Алексеева", null, "+7 (911) 000-00-01", "a@example.test", "Работает", null), null, CancellationToken.None);

        var result = await service.GetSuppliersAsync(utilityGroup.Value.Id, "5401", CancellationToken.None);

        var supplier = Assert.Single(result);
        Assert.Equal("Водоканал", supplier.Name);
        Assert.Equal("Коммунальные услуги", supplier.GroupName);

        var highestDebt = await service.GetSuppliersPageAsync(null, null, 0, 1, "debt", "desc", CancellationToken.None);
        var safeFallback = await service.GetSuppliersPageAsync(null, null, 0, 1, "unsupported", "asc", CancellationToken.None);
        var primaryContact = await service.GetSuppliersPageAsync(null, null, 0, 1, "contactPerson", "asc", CancellationToken.None);
        var contactPage = await service.GetSupplierContactsPageAsync(null, "example.test", 1, 1, "status", "desc", CancellationToken.None);
        PagedResult<SupplierContactDto>[] contactSortPages =
        [
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "supplier", "asc", CancellationToken.None),
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "supplier", "desc", CancellationToken.None),
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "position", "asc", CancellationToken.None),
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "position", "desc", CancellationToken.None),
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "status", "asc", CancellationToken.None),
            await service.GetSupplierContactsPageAsync(null, null, 0, 10, "unsupported", "desc", CancellationToken.None)
        ];
        Assert.Equal("Водоканал", highestDebt.Items[0].Name);
        Assert.Equal("Альфа-Банк", safeFallback.Items[0].Name);
        Assert.Equal("Альфа-Банк", primaryContact.Items[0].Name);
        Assert.Equal("Анна Алексеева", primaryContact.Items[0].ContactPerson);
        Assert.Equal("+7 (911) 000-00-01", primaryContact.Items[0].Phone);
        Assert.Equal("a@example.test", primaryContact.Items[0].Email);
        Assert.Equal(2, contactPage.TotalCount);
        Assert.Single(contactPage.Items);
        Assert.Equal(1, contactPage.Offset);
        Assert.Equal(1, contactPage.Limit);
        Assert.All(contactSortPages, page => Assert.Equal(2, page.TotalCount));
    }

    [Fact]
    public async Task GetSuppliersAsync_CalculatesDebtFromStartingBalanceAccrualsAndPayments()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var first = await service.CreateSupplierAsync(new UpsertSupplierRequest("Первый", group.Value!.Id, null, null, null, null, null, 100m, null), null, CancellationToken.None);
        var second = await service.CreateSupplierAsync(new UpsertSupplierRequest("Второй", group.Value.Id, null, null, null, null, null, 500m, null), null, CancellationToken.None);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Услуги", "supplier_services"), null, CancellationToken.None);

        database.Context.SupplierAccruals.AddRange(
            new SupplierAccrual
            {
                SupplierId = first.Value!.Id,
                ExpenseTypeId = expenseType.Value!.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 900m,
                Source = "manual"
            },
            new SupplierAccrual
            {
                SupplierId = first.Value.Id,
                ExpenseTypeId = expenseType.Value.Id,
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 999m,
                Source = "manual",
                IsCanceled = true
            });
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                SupplierId = first.Value.Id,
                ExpenseTypeId = expenseType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 15),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 250m
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                SupplierId = first.Value.Id,
                ExpenseTypeId = expenseType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 16),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 999m,
                IsCanceled = true
            });
        await database.Context.SaveChangesAsync();

        var suppliers = await service.GetSuppliersAsync(group.Value.Id, null, CancellationToken.None);
        var debtPage = await service.GetSuppliersPageAsync(group.Value.Id, null, 0, 1, "debt", "desc", CancellationToken.None);

        Assert.Equal(750m, Assert.Single(suppliers, item => item.Id == first.Value.Id).Debt);
        Assert.Equal(500m, Assert.Single(suppliers, item => item.Id == second.Value!.Id).Debt);
        Assert.Equal(first.Value.Id, Assert.Single(debtPage.Items).Id);
    }

    [Fact]
    public async Task UpdateSupplierAsync_ReturnsFullDebtWhenNonOpeningDetailsAreUpdated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 100m, null),
            null,
            CancellationToken.None);
        var expenseType = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Водоснабжение", "water_supply_custom"), null, CancellationToken.None);
        database.Context.SupplierAccruals.Add(new SupplierAccrual
        {
            SupplierId = supplier.Value!.Id,
            ExpenseTypeId = expenseType.Value!.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 900m,
            Source = "manual"
        });
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            SupplierId = supplier.Value.Id,
            ExpenseTypeId = expenseType.Value.Id,
            OperationDate = new DateOnly(2026, 7, 15),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 250m
        });
        await database.Context.SaveChangesAsync();

        var unchanged = await service.UpdateSupplierAsync(
            supplier.Value.Id,
            new UpsertSupplierRequest("Водоканал", group.Value.Id, null, null, null, null, null, 100m, null),
            Guid.NewGuid(),
            CancellationToken.None);
        var updated = await service.UpdateSupplierAsync(
            supplier.Value.Id,
            new UpsertSupplierRequest("Водоканал", group.Value.Id, null, null, null, null, null, 100m, "Уточнены реквизиты"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(unchanged.Succeeded);
        Assert.Equal(750m, unchanged.Value!.Debt);
        Assert.True(updated.Succeeded);
        Assert.Equal(750m, updated.Value!.Debt);
    }

    [Fact]
    public async Task RestoreSupplierAsync_RejectsArchivedSupplierGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value!.Id, "Тестовая причина", null, CancellationToken.None);
        await service.ArchiveSupplierGroupAsync(group.Value.Id, "Тестовая причина", null, CancellationToken.None);

        var result = await service.RestoreSupplierAsync(supplier.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreSupplierAsync_RejectsDuplicateActiveNameInGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Utilities"), null, CancellationToken.None);
        var archived = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        await service.ArchiveSupplierAsync(archived.Value!.Id, "Archived supplier duplicate restore check", null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        var result = await service.RestoreSupplierAsync(archived.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreSupplierContactAsync_RejectsDuplicateSupplierRestore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Utilities"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value!.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);
        var contact = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Contact", null, null, null, "Active", null),
            null,
            CancellationToken.None);
        await service.ArchiveSupplierContactAsync(contact.Value!.Id, "Archived contact duplicate restore check", null, CancellationToken.None);
        await service.ArchiveSupplierAsync(supplier.Value.Id, "Archived supplier duplicate restore check", null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Water", group.Value.Id, null, null, null, null, null, 0, null), null, CancellationToken.None);

        var result = await service.RestoreSupplierContactAsync(contact.Value.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_RejectsDuplicateName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership_custom"), null, CancellationToken.None);

        var result = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership2"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_NormalizesCodeAndRejectsInvalidReservedOrDuplicateCodes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var created = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Охрана", "  SECURITY_2026  "),
            null,
            CancellationToken.None);
        var duplicate = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Охрана территории", "security_2026"),
            null,
            CancellationToken.None);
        var invalid = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Русский код", "охрана"),
            null,
            CancellationToken.None);
        var reserved = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Пользовательская вода", "water"),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal("security_2026", created.Value!.Code);
        Assert.Equal("income_type_code_duplicate", duplicate.ErrorCode);
        Assert.Equal("income_type_code_invalid", invalid.ErrorCode);
        Assert.Equal("income_type_code_reserved", reserved.ErrorCode);
    }

    [Fact]
    public async Task RestoreIncomeTypeAsync_RejectsDuplicateActiveCode()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var archived = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Старый тип", "shared_income_code"),
            null,
            CancellationToken.None);
        await service.ArchiveIncomeTypeAsync(archived.Value!.Id, "Проверка конфликта кода", null, CancellationToken.None);
        await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Новый тип", "shared_income_code"),
            null,
            CancellationToken.None);

        var result = await service.RestoreIncomeTypeAsync(archived.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_code_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_AutomaticallyLinksCustomTypeToOtherFund()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherFund = new Fund
        {
            Name = "Прочее",
            NormalizedName = "ПРОЧЕЕ",
            AllowOperations = true
        };
        database.Context.Funds.Add(otherFund);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateIncomeTypeAsync(
            new UpsertAccountingTypeRequest("Охрана", "security"),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(otherFund.Id, result.Value!.DestinationFundId);
        Assert.Equal(otherFund.Name, result.Value.DestinationFundName);
        Assert.Equal(
            otherFund.Id,
            await database.Context.IncomeTypes
                .Where(item => item.Id == result.Value.Id)
                .Select(item => item.DestinationFundId)
                .SingleAsync());
    }

    [Fact]
    public async Task CreateIncomeTypeAsync_AllowsNameFromArchivedType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
    public void RegularAccrualCatalogRepairMigration_RestoresOnlyMissingDefaultsWithoutOverwritingCatalog()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260715100229_RestoreRegularAccrualCatalogAfterCleanup.cs"));

        string[] regularIncomeCodes = ["water", "trash", "electricity", "membership", "target", "outdoor_lighting"];
        foreach (var code in regularIncomeCodes)
        {
            Assert.Contains($"'{code}'", migration, StringComparison.Ordinal);
        }

        Assert.Contains("FROM income_types existing", migration, StringComparison.Ordinal);
        Assert.Contains("existing.\"Id\" = defaults.\"Id\"", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(BTRIM(existing.\"Name\"))", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(BTRIM(existing.\"Code\"))", migration, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN tariffs tariff", migration, StringComparison.Ordinal);
        Assert.Contains("tariff.\"IsArchived\" = FALSE", migration, StringComparison.Ordinal);
        Assert.Contains("FROM charge_service_settings existing", migration, StringComparison.Ordinal);
        Assert.Contains("service.\"IncomeTypeId\" IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("income_type.\"Code\" = 'outdoor_lighting'", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT DO NOTHING", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.regular_accrual_catalog_restored", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE tariffs", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"IsArchived\" = FALSE,", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplierChargeServiceCatalogMigration_BackfillsOnlyMatchingActiveUnifiedServicesAndWritesAudit()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260715112440_SupplierChargeServiceCatalog.cs"));

        Assert.Contains("ADD", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ChargeServiceSettingId", migration, StringComparison.Ordinal);
        Assert.Contains("lower(btrim(service.\"Name\")) = lower(btrim(supplier_group.\"Name\"))", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT service.\"IsArchived\"", migration, StringComparison.Ordinal);
        Assert.Contains("supplier.\"ChargeServiceSettingId\" IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.supplier_services_unified", migration, StringComparison.Ordinal);
        Assert.Contains("linkedSupplierCount", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"Id\") DO NOTHING", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM suppliers", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM supplier_groups", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM charge_service_settings", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoTariffCatalogMigration_SeedsOnlyEmptyCatalogWithCustomerFormValuesAndAudit()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260713065213_SeedDemoTariffCatalog.cs"));

        Assert.Contains("NOT EXISTS (SELECT 1 FROM tariffs)", migration, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM charge_service_settings)", migration, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM irregular_payments)", migration, StringComparison.Ordinal);
        Assert.Contains("Gelendzhik municipal decision 304/2025", migration, StringComparison.Ordinal);
        Assert.Contains("'Тариф на воду', 'meter_water', 100.6000", migration, StringComparison.Ordinal);
        Assert.Contains("'Электроэнергия', 'meter_electricity', 7.4700", migration, StringComparison.Ordinal);
        Assert.Contains("1100.0000, 1700.0000", migration, StringComparison.Ordinal);
        Assert.Contains("7.4700, 10.1700, 14.8800", migration, StringComparison.Ordinal);
        Assert.Contains("'Сумма членского взноса', 'fixed', 500.0000", migration, StringComparison.Ordinal);
        Assert.Contains("'Сумма целевого взноса', 'fixed', 1200.0000", migration, StringComparison.Ordinal);
        Assert.Contains("'Ставка за вывоз мусора', 'people', 128.6900", migration, StringComparison.Ordinal);
        Assert.Contains("'Наружное освещение', 'fixed', 300.0000", migration, StringComparison.Ordinal);
        Assert.Contains("NULL, '8a92bf70-9339-4bbc-8e5d-a05cda185106', FALSE, FALSE, 'руб.'", migration, StringComparison.Ordinal);
        Assert.Contains("'Электрики', 'fixed', 500.0000", migration, StringComparison.Ordinal);
        Assert.Contains("'Бухгалтерия', 'fixed', 700.0000", migration, StringComparison.Ordinal);
        Assert.Contains("'Руководство', 'fixed', 900.0000", migration, StringComparison.Ordinal);
        Assert.Contains("'Вступительный взнос', 5000.00", migration, StringComparison.Ordinal);
        Assert.Contains("'Подключение канализации', 10000.00", migration, StringComparison.Ordinal);
        Assert.Contains("'Подключение линии электросети', 15000.00", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("'Штраф за то', 500.00", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("'Штраф за это', 1000.00", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.demo_tariff_catalog_seeded", migration, StringComparison.Ordinal);
        Assert.Contains("решение Думы от 19.12.2025 № 304", migration, StringComparison.Ordinal);
        Assert.Contains("приказ ДГРТ от 17.12.2025 № 18/2025-э", migration, StringComparison.Ordinal);
        Assert.Contains("Южный региональный оператор", migration, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM charge_service_settings", migration, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM irregular_payments", migration, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM tariffs", migration, StringComparison.Ordinal);
        Assert.Contains("FROM accruals", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void GelendzhikTariffDefaultsMigration_UpdatesOnlyKnownSeedValuesAndRemovesPlaceholderFines()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260714024144_ApplyGelendzhik2026TariffDefaults.cs"));

        Assert.Contains("AND \"Rate\" = 45.0000", migration, StringComparison.Ordinal);
        Assert.Contains("AND \"Rate\" = 6.2000", migration, StringComparison.Ordinal);
        Assert.Contains("AND \"Rate\" = 300.0000", migration, StringComparison.Ordinal);
        Assert.Contains("\"Rate\" = 100.6000", migration, StringComparison.Ordinal);
        Assert.Contains("\"ElectricityFirstRate\" = 7.4700", migration, StringComparison.Ordinal);
        Assert.Contains("\"ElectricitySecondRate\" = 10.1700", migration, StringComparison.Ordinal);
        Assert.Contains("\"ElectricityThirdRate\" = 14.8800", migration, StringComparison.Ordinal);
        Assert.Contains("\"Rate\" = 128.6900", migration, StringComparison.Ordinal);
        Assert.Contains("\"Id\" = 'c865fd0a-ae14-4de6-83ef-b5d692327104' AND \"Name\" = 'Штраф за то'", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.gelendzhik_tariff_defaults_applied", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void IrregularPaymentDefaultsMigration_RestoresCustomerPaymentsIdempotentlyAndWritesAudit()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260715022028_RestoreIrregularPaymentDefaults.cs"));

        Assert.Contains("'Вступительный взнос', 5000.00::numeric", migration, StringComparison.Ordinal);
        Assert.Contains("'Подключение канализации', 10000.00::numeric", migration, StringComparison.Ordinal);
        Assert.Contains("'Подключение к линии электросети', 15000.00::numeric", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"Id\") DO UPDATE", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(BTRIM(existing.\"Name\")) = LOWER(BTRIM(defaults.\"Name\"))", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER('Подключение линии электросети')", migration, StringComparison.Ordinal);
        Assert.Contains("\"IsArchived\" = FALSE", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.irregular_payment_defaults_restored", migration, StringComparison.Ordinal);
        Assert.Contains("\"CreatedAtUtc\" = TIMESTAMPTZ '2026-07-15T02:20:28Z'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoTariffCatalogNormalizationMigration_UpdatesOnlyKnownLightingRelationshipAndWritesAudit()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindApiProjectRoot(),
            "Infrastructure",
            "Data",
            "Migrations",
            "20260713160338_NormalizeDemoTariffCatalogRelationships.cs"));

        Assert.Contains("WHERE \"Id\" = 'f0d7ed2e-ec55-42b4-8a79-01b37c287106'", migration, StringComparison.Ordinal);
        Assert.Contains("AND \"Name\" = 'Наружное освещение'", migration, StringComparison.Ordinal);
        Assert.Contains("AND \"TariffId\" IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("\"TariffId\" = '8a92bf70-9339-4bbc-8e5d-a05cda185106'", migration, StringComparison.Ordinal);
        Assert.Contains("dictionary.demo_tariff_catalog_normalized", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"Id\") DO NOTHING", migration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_WritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электроэнергия", "electricity_custom"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.expense_type_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_NormalizesCodeAndRejectsInvalidReservedOrDuplicateCodes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var created = await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Ремонт", "  REPAIR_2026  "),
            null,
            CancellationToken.None);
        var duplicate = await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Ремонт кровли", "repair_2026"),
            null,
            CancellationToken.None);
        var invalid = await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Русский код", "ремонт"),
            null,
            CancellationToken.None);
        var reserved = await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Пользовательская зарплата", "salary"),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal("repair_2026", created.Value!.Code);
        Assert.Equal("expense_type_code_duplicate", duplicate.ErrorCode);
        Assert.Equal("expense_type_code_invalid", invalid.ErrorCode);
        Assert.Equal("expense_type_code_reserved", reserved.ErrorCode);
    }

    [Fact]
    public async Task RestoreExpenseTypeAsync_RejectsDuplicateActiveCode()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var archived = await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Старая статья", "shared_expense_code"),
            null,
            CancellationToken.None);
        await service.ArchiveExpenseTypeAsync(archived.Value!.Id, "Проверка конфликта кода", null, CancellationToken.None);
        await service.CreateExpenseTypeAsync(
            new UpsertAccountingTypeRequest("Новая статья", "shared_expense_code"),
            null,
            CancellationToken.None);

        var result = await service.RestoreExpenseTypeAsync(archived.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("expense_type_code_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_AllowsNameFromArchivedType()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50.25m, effectiveFrom, null), null, CancellationToken.None);

        var result = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 60m, effectiveFrom, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateTariffAsync_AllowsSameNameWithDifferentEffectiveDateAsNewVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var first = await service.CreateTariffAsync(
            new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 7, 1), "Первая версия"),
            Guid.NewGuid(),
            CancellationToken.None);

        var second = await service.CreateTariffAsync(
            new UpsertTariffRequest("Вода", "meter_water", 60m, new DateOnly(2026, 8, 1), "Новая версия"),
            Guid.NewGuid(),
            CancellationToken.None);
        var versions = (await service.GetTariffsAsync(null, CancellationToken.None))
            .Where(item => item.Name == "Вода")
            .ToArray();

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, versions.Length);
        Assert.Equal(new DateOnly(2026, 8, 1), versions[0].EffectiveFrom);
        Assert.Equal(60m, versions[0].Rate);
        Assert.Equal(new DateOnly(2026, 7, 1), versions[1].EffectiveFrom);
        Assert.Equal(50m, versions[1].Rate);
        Assert.Equal(2, database.Context.AuditEvents.Count(item => item.Action == "dictionary.tariff_created"));
    }

    [Fact]
    public async Task CreateTariffAsync_AllowsNameAndDateFromArchivedTariff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        Assert.Contains("ставка 12.35", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTariffAsync_SavesElectricityTiersAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        Assert.Contains("пороги: 0–50.556 кВт·ч по 3.11", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("50.556–100.778 кВт·ч по 4.22", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("100.778+ кВт·ч по 5.33", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTariffsAsync_ReadsLegacyCamelCaseElectricityTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.Tariffs.Add(new Tariff
        {
            Name = "Legacy electricity",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 7.5m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            ElectricityTiersJson = """
                [
                  {"upperBound":1100,"rate":7.5},
                  {"upperBound":1700,"rate":10},
                  {"upperBound":null,"rate":15}
                ]
                """
        });
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var tariff = Assert.Single(await service.GetTariffsAsync("Legacy electricity", CancellationToken.None));

        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyList<ElectricityTariffTierDto>>(tariff.ElectricityTiers),
            tier => { Assert.Equal(1100m, tier.UpperBound); Assert.Equal(7.5m, tier.Rate); },
            tier => { Assert.Equal(1700m, tier.UpperBound); Assert.Equal(10m, tier.Rate); },
            tier => { Assert.Null(tier.UpperBound); Assert.Equal(15m, tier.Rate); });
        Assert.All(tariff.ElectricityTiers!, tier =>
        {
            Assert.NotEqual(Guid.Empty, tier.Id);
            Assert.False(string.IsNullOrWhiteSpace(tier.Name));
        });
        Assert.Equal(3, tariff.ElectricityTiers!.Select(tier => tier.Id).Distinct().Count());
    }

    [Fact]
    public async Task GetStaffDepartmentSalaryFundAsync_GroupsOnlyActiveStaffRatesByActiveDepartment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var accounting = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Бухгалтерия"), null, CancellationToken.None);
        var security = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Охрана"), null, CancellationToken.None);
        await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Петрова Ольга", accounting.Value!.Id, 40000m), null, CancellationToken.None);
        await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Сидоров Иван", accounting.Value.Id, 10000m), null, CancellationToken.None);
        var archivedMember = await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Архивный сотрудник", security.Value!.Id, 90000m), null, CancellationToken.None);
        await service.ArchiveStaffMemberAsync(archivedMember.Value!.Id, "Сотрудник уволен", null, CancellationToken.None);

        var result = await service.GetStaffDepartmentSalaryFundAsync(CancellationToken.None);

        var accountingRow = Assert.Single(result, row => row.DepartmentId == accounting.Value.Id);
        Assert.Equal(2, accountingRow.StaffCount);
        Assert.Equal(50000m, accountingRow.TotalRate);
        var securityRow = Assert.Single(result, row => row.DepartmentId == security.Value.Id);
        Assert.Equal(0, securityRow.StaffCount);
        Assert.Equal(0m, securityRow.TotalRate);
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsIncompleteElectricityTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        Assert.Contains("ставка 20.56", audit.Summary, StringComparison.Ordinal);
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
    public async Task UpdateTariffAsync_RejectsFinancialMutationOfAssignedHistoricalVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Членские взносы", 10);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership", DestinationFundId = fund.Id };
        var tariff = new Tariff { Name = "Членский взнос", CalculationBase = "fixed", Rate = 500m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = tariff.Id,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, tariff, setting);
        database.Context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
        {
            ChargeServiceSettingId = setting.Id,
            TariffId = tariff.Id,
            EffectiveFrom = tariff.EffectiveFrom
        });
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateTariffAsync(
            tariff.Id,
            new UpsertTariffRequest(tariff.Name, tariff.CalculationBase, 650m, tariff.EffectiveFrom, tariff.Comment),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_history_version_required", result.ErrorCode);
        Assert.Equal(500m, tariff.Rate);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateTariffAsync_ClearsElectricityTiersWhenBaseChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Электроэнергия", true, 1, 1, 30, 6, 30, true, true, "кВт·ч"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.True(result.Value.IsRegular);
        Assert.Equal(1, result.Value.PeriodicityMonths);
        Assert.Equal(1, result.Value.AccrualStartMonth);
        Assert.Equal(30, result.Value.PaymentDueDay);
        Assert.Null(result.Value.PaymentDueMonth);
        Assert.Equal(30, result.Value.OverdueGraceDays);
        Assert.True(result.Value.IsMetered);
        Assert.True(result.Value.HasTieredTariff);
        Assert.Equal("кВт·ч", result.Value.UnitName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_created");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Contains("Электроэнергия", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateChargeServiceWithTariffAsync_SavesDedicatedRateAndServiceInOneOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд охраны", 10);
        var incomeType = new IncomeType { Name = "Охрана", Code = "membership", DestinationFundId = fund.Id };
        var templateTariff = new Tariff { Name = "Шаблон фиксированного тарифа", CalculationBase = "fixed", Rate = 1200m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.AddRange(fund, incomeType, templateTariff);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest("Охрана территории", true, 1, 1, 20, null, 15, false, false, "руб.", incomeType.Id, templateTariff.Id),
                1750.12555m,
                new DateOnly(2026, 7, 23)),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Охрана территории", result.Value!.Service.Name);
        Assert.Equal(result.Value.Tariff.Id, result.Value.Service.TariffId);
        Assert.NotEqual(templateTariff.Id, result.Value.Tariff.Id);
        Assert.Equal("Охрана территории — тариф", result.Value.Tariff.Name);
        Assert.Equal(1750.1256m, result.Value.Tariff.Rate);
        Assert.Equal("fixed", result.Value.Tariff.CalculationBase);
        Assert.Equal(new DateOnly(2026, 7, 23), result.Value.Tariff.EffectiveFrom);
        Assert.Equal(2, await database.Context.Tariffs.CountAsync());
        Assert.Single(database.Context.ChargeServiceSettings);
        database.Context.ChangeTracker.Clear();
        var persistedSetting = await database.Context.ChargeServiceSettings.SingleAsync();
        Assert.Equal(result.Value.Tariff.Id, persistedSetting.TariffId);
        Assert.Equal(3, database.Context.AuditEvents.Count());
        Assert.Contains(database.Context.AuditEvents, audit => audit.Action == "dictionary.measurement_unit_created");
        Assert.All(database.Context.AuditEvents, audit => Assert.Equal(actorUserId, audit.ActorUserId));
    }

    [Fact]
    public async Task CreateChargeServiceWithTariffAsync_SavesSelectedIncomeFundCalculationUnitAndCustomTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var previousFund = CreateFund("Прежний фонд электроэнергии", 10);
        var selectedFund = CreateFund("Новый фонд электроэнергии", 20);
        var incomeType = new IncomeType
        {
            Name = "Электроэнергия",
            Code = "electricity",
            DestinationFundId = previousFund.Id
        };
        var templateTariff = new Tariff
        {
            Name = "Шаблон электроэнергии",
            CalculationBase = "fixed",
            Rate = 7m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.AddRange(previousFund, selectedFund, incomeType, templateTariff);
        await database.Context.SaveChangesAsync();
        var actorUserId = Guid.NewGuid();

        var result = await DictionaryServiceTestFactory.Create(database.Context).CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    "Электроэнергия по зонам",
                    true,
                    1,
                    1,
                    25,
                    null,
                    20,
                    true,
                    true,
                    "кВт·ч",
                    incomeType.Id,
                    templateTariff.Id),
                3.25m,
                new DateOnly(2026, 8, 1),
                selectedFund.Id,
                "metered_tiered",
                [
                    new(null, "До 125", 125m, 3.25m),
                    new(null, "Свыше 125", null, 4.75m)
                ],
                "meter_electricity"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(selectedFund.Id, incomeType.DestinationFundId);
        Assert.Equal("meter_electricity", result.Value!.Tariff.CalculationBase);
        Assert.Equal("кВт·ч", result.Value.Service.UnitName);
        Assert.True(result.Value.Service.IsMetered);
        Assert.True(result.Value.Service.HasTieredTariff);
        Assert.Collection(
            result.Value.Tariff.ElectricityTiers!,
            tier =>
            {
                Assert.Equal(125m, tier.UpperBound);
                Assert.Equal(3.25m, tier.Rate);
            },
            tier =>
            {
                Assert.Null(tier.UpperBound);
                Assert.Equal(4.75m, tier.Rate);
            });
        Assert.Contains(
            database.Context.AuditEvents,
            item => item.Action == "dictionary.income_type_destination_fund_updated" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task RegularService_CanBeCreatedWithoutTechnicalDictionariesAndConvertedToCustomMeter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд охраны", 10);
        database.Context.Funds.Add(fund);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var created = await service.CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest("Охрана", true, 1, 1, 30, null, 30, false, false, "руб."),
                500m,
                new DateOnly(2026, 8, 1),
                fund.Id,
                "regular",
                CalculationBase: TariffCalculationBases.Fixed),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.NotNull(created.Value!.Service.IncomeTypeId);
        Assert.NotNull(created.Value.Service.TariffId);
        var managedIncomeType = await database.Context.IncomeTypes.SingleAsync();
        Assert.True(managedIncomeType.IsSystem);
        Assert.StartsWith("service_", managedIncomeType.Code, StringComparison.Ordinal);
        Assert.Equal(fund.Id, managedIncomeType.DestinationFundId);

        var converted = await service.UpdateChargeServiceWithTariffAsync(
            created.Value.Service.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    "Охрана",
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    true,
                    false,
                    "ч",
                    created.Value.Service.IncomeTypeId,
                    created.Value.Service.TariffId,
                    created.Value.Service.Version),
                12.5m,
                "metered",
                new DateOnly(2026, 8, 2),
                ChangeReason: "Переход на расчёт по счётчику",
                IncomeFundId: fund.Id,
                CalculationBase: TariffCalculationBases.MeterElectricity),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(converted.Succeeded, converted.ErrorMessage);
        Assert.True(converted.Value!.Service.IsMetered);
        Assert.False(converted.Value.Service.HasTieredTariff);
        Assert.Equal(TariffCalculationBases.MeterElectricity, converted.Value.Tariff.CalculationBase);
        Assert.Equal(12.5m, converted.Value.Tariff.Rate);
        Assert.Equal(MeterKinds.ForService(converted.Value.Service.Id), converted.Value.Service.MeterKind);
        Assert.StartsWith("service_", converted.Value.Service.MeterKind, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateChargeServiceWithTariffAsync_RejectsInvalidModeAndRateWithoutAddingRecords()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var baseRequest = new UpsertChargeServiceSettingRequest("Разовая услуга", false, null, null, null, null, 0, false, false, null);

        var nonRegular = await service.CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(baseRequest, 100m, new DateOnly(2026, 7, 23)),
            null,
            CancellationToken.None);
        var invalidRate = await service.CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(baseRequest with { IsRegular = true }, 0m, new DateOnly(2026, 7, 23)),
            null,
            CancellationToken.None);

        Assert.False(nonRegular.Succeeded);
        Assert.Equal("charge_service_tariff_regular_required", nonRegular.ErrorCode);
        Assert.False(invalidRate.Succeeded);
        Assert.Equal("charge_service_rate_invalid", invalidRate.ErrorCode);
        Assert.Empty(database.Context.Tariffs);
        Assert.Empty(database.Context.ChargeServiceSettings);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateChargeServiceWithTariffAsync_RollsBackTariffAndAuditWhenServiceInsertFails()
    {
        var failureInterceptor = new ChargeServiceInsertFailureInterceptor();
        await using var database = await TestDatabase.CreateAsync(failureInterceptor);
        var fund = CreateFund("Фонд охраны", 10);
        var incomeType = new IncomeType { Name = "Охрана", Code = "membership", DestinationFundId = fund.Id };
        var templateTariff = new Tariff { Name = "Шаблон фиксированного тарифа", CalculationBase = "fixed", Rate = 1200m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.AddRange(fund, incomeType, templateTariff);
        await database.Context.SaveChangesAsync();
        failureInterceptor.Enabled = true;
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateChargeServiceWithTariffAsync(
            new CreateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest("Охрана территории", true, 1, 1, 20, null, 15, false, false, "руб.", incomeType.Id, templateTariff.Id),
                1750m,
                new DateOnly(2026, 7, 23)),
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        database.Context.ChangeTracker.Clear();
        Assert.Single(await database.Context.Tariffs.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.ChargeServiceSettings.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.MeasurementUnits.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_SavesAccountingLinksAndCustomUnit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд членских взносов", 10);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership", DestinationFundId = fund.Id };
        var tariff = new Tariff { Name = "Членский тариф", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var waterTariff = new Tariff { Name = "Вода", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Funds.Add(fund);
        database.Context.IncomeTypes.Add(incomeType);
        database.Context.Tariffs.AddRange(tariff, waterTariff);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос", true, 12, 1, 30, 6, 30, false, false, "руб.", incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);
        var mismatch = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода как членский", true, 1, 1, 30, 6, 30, true, false, "м³", incomeType.Id, waterTariff.Id),
            null,
            CancellationToken.None);
        var incomplete = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Без вида поступления", true, 1, 1, 30, 6, 30, false, false, "руб.", null, tariff.Id),
            null,
            CancellationToken.None);
        var missingIncomeType = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Несуществующий вид поступления", true, 1, 1, 30, 6, 30, false, false, "руб.", Guid.NewGuid(), tariff.Id),
            null,
            CancellationToken.None);
        var customUnit = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос в упаковках", true, 1, 1, 30, null, 30, false, false, " упаковка ", incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);
        var compatibleUnit = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос на гараж", true, 1, 1, 30, null, 30, false, false, "руб./гараж", incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);
        var emptyUnit = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос без единицы", true, 1, 1, 30, null, 30, false, false, " ", incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);
        var longUnit = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос с длинной единицей", true, 1, 1, 30, null, 30, false, false, new string('а', 41), incomeType.Id, tariff.Id),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(incomeType.Id, result.Value!.IncomeTypeId);
        Assert.Equal(tariff.Id, result.Value.TariffId);
        Assert.True(mismatch.Succeeded);
        Assert.Equal(waterTariff.Id, mismatch.Value!.TariffId);
        Assert.Equal("м³", mismatch.Value.UnitName);
        Assert.False(incomplete.Succeeded);
        Assert.Equal("charge_service_regular_link_incomplete", incomplete.ErrorCode);
        Assert.Equal("Для регулярной услуги заполните и вид поступления, и тариф.", incomplete.ErrorMessage);
        Assert.False(missingIncomeType.Succeeded);
        Assert.Equal("charge_service_income_type_not_found", missingIncomeType.ErrorCode);
        Assert.Equal("Вид поступления для услуги не найден.", missingIncomeType.ErrorMessage);
        Assert.True(customUnit.Succeeded);
        Assert.Equal("упаковка", customUnit.Value!.UnitName);
        Assert.True(compatibleUnit.Succeeded);
        Assert.Equal("руб./гараж", compatibleUnit.Value!.UnitName);
        Assert.False(emptyUnit.Succeeded);
        Assert.Equal("charge_service_unit_required", emptyUnit.ErrorCode);
        Assert.Equal("Укажите единицу измерения услуги.", emptyUnit.ErrorMessage);
        Assert.False(longUnit.Succeeded);
        Assert.Equal("charge_service_unit_too_long", longUnit.ErrorCode);
        Assert.Equal("Единица измерения должна содержать не более 40 символов.", longUnit.ErrorMessage);
        Assert.Equal(4, database.Context.ChargeServiceSettings.Count());
        Assert.Equal(4, database.Context.MeasurementUnits.Count());
        Assert.Contains(database.Context.MeasurementUnits, unit => unit.Name == "упаковка");
        Assert.Equal(4, database.Context.AuditEvents.Count(item => item.Action == "dictionary.charge_service_created"));
    }

    [Fact]
    public async Task UpdateChargeServiceSettingAsync_WritesChangedFieldsAndSkipsNoOp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstFund = CreateFund("Фонд воды 2025", 10);
        var secondFund = CreateFund("Фонд воды 2026", 20);
        var firstIncomeType = new IncomeType { Name = "Вода", Code = "water_2025", DestinationFundId = firstFund.Id };
        var secondIncomeType = new IncomeType { Name = "Водоснабжение", Code = "water_2026", DestinationFundId = secondFund.Id };
        var expenseType = new ExpenseType { Name = "Оплата водоснабжения", Code = "water_supply_custom" };
        var firstTariff = new Tariff { Name = "Вода 2025", CalculationBase = "meter_water", Rate = 40m, EffectiveFrom = new DateOnly(2025, 1, 1) };
        var secondTariff = new Tariff { Name = "Вода 2026", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.Funds.AddRange(firstFund, secondFund);
        database.Context.IncomeTypes.AddRange(firstIncomeType, secondIncomeType);
        database.Context.ExpenseTypes.Add(expenseType);
        database.Context.Tariffs.AddRange(firstTariff, secondTariff);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var created = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", true, 1, 1, 30, 6, 30, true, false, "м³", firstIncomeType.Id, firstTariff.Id),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var actorUserId = Guid.NewGuid();
        var result = await service.UpdateChargeServiceSettingAsync(
            created.Value!.Id,
            new UpsertChargeServiceSettingRequest("Водоснабжение", true, 12, 2, 31, 12, 45, true, false, "м³", secondIncomeType.Id, secondTariff.Id),
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
        Assert.Contains("Вид поступления", changedFields, StringComparison.Ordinal);
        Assert.Contains("Тариф", changedFields, StringComparison.Ordinal);

        var noOp = await service.UpdateChargeServiceSettingAsync(
            created.Value.Id,
            new UpsertChargeServiceSettingRequest("Водоснабжение", true, 12, 2, 31, 12, 45, true, false, "м³", secondIncomeType.Id, secondTariff.Id),
            actorUserId,
            CancellationToken.None);

        Assert.True(noOp.Succeeded);
        Assert.Single(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_RejectsInvalidFebruaryDayAndTieredWithoutMeter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var februaryResult = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Членский взнос", true, 12, 1, 29, 2, 30, false, false, "руб."),
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
    public async Task ElectricityTiers_AddPersistDeleteAndAuditReason()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                2m,
                new DateOnly(2026, 7, 1),
                null,
                ElectricityTiers:
                [
                    new(null, "До 50", 50m, 2m),
                    new(null, "До 100", 100m, 3m),
                    new(null, "Свыше 100", null, 5m)
                ]),
            actorUserId,
            CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal(3, created.Value!.ElectricityTiers!.Count);
        Assert.Equal(["0–50 кВт·ч", "50–100 кВт·ч", "100+ кВт·ч"], created.Value.ElectricityTiers.Select(tier => tier.Name));
        Assert.All(created.Value.ElectricityTiers, tier => Assert.True(tier.IsCustom));
        Assert.False(string.IsNullOrWhiteSpace((await database.Context.Tariffs.SingleAsync()).ElectricityTiersJson));

        var original = created.Value.ElectricityTiers;
        var added = await service.UpdateTariffAsync(
            created.Value.Id,
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                2m,
                new DateOnly(2026, 7, 1),
                null,
                ElectricityTiers:
                [
                    new(original[0].Id, original[0].Name, original[0].UpperBound, original[0].Rate),
                    new(original[1].Id, original[1].Name, original[1].UpperBound, original[1].Rate),
                    new(null, "До 150", 150m, 4m),
                    new(original[2].Id, original[2].Name, null, original[2].Rate)
                ]),
            actorUserId,
            CancellationToken.None);

        Assert.True(added.Succeeded);
        Assert.Equal(4, added.Value!.ElectricityTiers!.Count);
        var customTier = Assert.Single(added.Value.ElectricityTiers, tier => tier.Name == "100–150 кВт·ч");
        Assert.True(customTier.IsCustom);

        var deleted = await service.UpdateTariffAsync(
            created.Value.Id,
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                2m,
                new DateOnly(2026, 7, 1),
                null,
                ElectricityTiers: added.Value.ElectricityTiers
                    .Where(tier => tier.Id != customTier.Id)
                    .Select(tier => new UpsertElectricityTariffTierRequest(tier.Id, tier.Name, tier.UpperBound, tier.Rate))
                    .ToArray(),
                ElectricityTierChangeReason: "Порог добавлен ошибочно"),
            actorUserId,
            CancellationToken.None);

        Assert.True(deleted.Succeeded);
        Assert.Equal(3, deleted.Value!.ElectricityTiers!.Count);
        Assert.DoesNotContain(deleted.Value.ElectricityTiers, tier => tier.Id == customTier.Id);
        var deleteAudit = database.Context.AuditEvents
            .Where(item => item.Action == "dictionary.tariff_updated")
            .AsEnumerable()
            .OrderByDescending(item => item.CreatedAtUtc)
            .First();
        using var deleteMetadata = JsonDocument.Parse(deleteAudit.MetadataJson!);
        Assert.Equal("Порог добавлен ошибочно", deleteMetadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsUnorderedVariableElectricityTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Электроэнергия",
                "meter_electricity",
                2m,
                new DateOnly(2026, 7, 1),
                null,
                ElectricityTiers:
                [
                    new(null, "До 100", 100m, 2m),
                    new(null, "До 50", 50m, 3m),
                    new(null, "Свыше", null, 5m)
                ]),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_electricity_tier_upper_bound_invalid", result.ErrorCode);
        Assert.Empty(database.Context.Tariffs);
    }

    [Fact]
    public async Task VariableElectricityTiers_RejectIncompleteLastAndUnknownExistingTiers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var date = new DateOnly(2026, 7, 1);

        var incomplete = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Неполный тариф",
                "meter_electricity",
                2m,
                date,
                null,
                ElectricityTiers:
                [
                    new(null, "Первая", null, 2m),
                    new(null, "Последняя", null, 3m)
                ]),
            null,
            CancellationToken.None);
        var boundedLast = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Ограниченный последний тариф",
                "meter_electricity",
                2m,
                date,
                null,
                ElectricityTiers:
                [
                    new(null, "Первая", 50m, 2m),
                    new(null, "Последняя", 100m, 3m)
                ]),
            null,
            CancellationToken.None);
        var created = await service.CreateTariffAsync(
            new UpsertTariffRequest(
                "Рабочий тариф",
                "meter_electricity",
                2m,
                date,
                null,
                ElectricityTiers:
                [
                    new(null, "Первая", 50m, 2m),
                    new(null, "Последняя", null, 3m)
                ]),
            null,
            CancellationToken.None);
        var unknown = await service.UpdateTariffAsync(
            created.Value!.Id,
            new UpsertTariffRequest(
                "Рабочий тариф",
                "meter_electricity",
                2m,
                date,
                null,
                ElectricityTiers:
                [
                    new(Guid.NewGuid(), "Первая", 50m, 2m),
                    new(created.Value.ElectricityTiers![1].Id, "Последняя", null, 3m)
                ]),
            null,
            CancellationToken.None);

        Assert.Equal("tariff_electricity_tier_upper_bound_required", incomplete.ErrorCode);
        Assert.Equal("tariff_electricity_last_tier_unbounded_required", boundedLast.ErrorCode);
        Assert.Equal("tariff_electricity_tier_not_found", unknown.ErrorCode);
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_RequiresSupportedPeriodicityAndMatchingPaymentDeadline()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var unsupportedPeriodicity = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Раз в полгода", true, 6, 1, 30, 6, 30, false, false, "руб."),
            null,
            CancellationToken.None);
        var monthlyWithoutDay = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Без дня оплаты", true, 1, 1, null, null, 30, false, false, "руб."),
            null,
            CancellationToken.None);
        var annualWithoutMonth = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Без месяца оплаты", true, 12, 1, 30, null, 30, false, false, "руб."),
            null,
            CancellationToken.None);
        var invalidMonthlyDay = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Неверный день", true, 1, 1, 32, null, 30, false, false, "руб."),
            null,
            CancellationToken.None);

        Assert.Equal("charge_service_periodicity_invalid", unsupportedPeriodicity.ErrorCode);
        Assert.Equal("Периодичность регулярной услуги должна быть ежемесячной или ежегодной.", unsupportedPeriodicity.ErrorMessage);
        Assert.Equal("charge_service_payment_day_required", monthlyWithoutDay.ErrorCode);
        Assert.Equal("Для регулярной услуги укажите день оплаты.", monthlyWithoutDay.ErrorMessage);
        Assert.Equal("charge_service_annual_payment_month_required", annualWithoutMonth.ErrorCode);
        Assert.Equal("Для ежегодной услуги укажите месяц оплаты.", annualWithoutMonth.ErrorMessage);
        Assert.Equal("charge_service_payment_day_invalid", invalidMonthlyDay.ErrorCode);
        Assert.Equal("Для ежемесячной услуги укажите день оплаты от 1 до 31.", invalidMonthlyDay.ErrorMessage);
        Assert.Empty(database.Context.ChargeServiceSettings);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task ArchiveChargeServiceSettingAsync_HidesSettingRequiresReasonAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 120m, new DateOnly(2026, 2, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);

        var result = await service.GetTariffsAsync("people", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateOnly(2026, 2, 1), result[0].EffectiveFrom);
        Assert.Equal(120m, result[0].Rate);
    }

    [Fact]
    public async Task GetTariffsPageAsync_ReturnsCurrentAndHistoricalServiceTariffs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var legacyUnlinkedTariff = new Tariff
        {
            Name = "Вода",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var serviceVersion = new Tariff
        {
            Name = "Вода — по счетчику, 05.08.2026, abcdef12",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 5)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Вода",
            IsRegular = true,
            TariffId = serviceVersion.Id,
            Tariff = serviceVersion
        };
        database.Context.Tariffs.AddRange(legacyUnlinkedTariff, serviceVersion);
        database.Context.ChargeServiceSettings.Add(setting);
        database.Context.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
        {
            ChargeServiceSettingId = setting.Id,
            ChargeServiceSetting = setting,
            EffectiveFrom = serviceVersion.EffectiveFrom,
            TariffId = serviceVersion.Id,
            Tariff = serviceVersion
        });
        await database.Context.SaveChangesAsync();

        var page = await service.GetTariffsPageAsync(null, 0, 25, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, tariff => tariff.Id == legacyUnlinkedTariff.Id);
        Assert.Contains(page.Items, tariff => tariff.Id == serviceVersion.Id);
    }

    [Fact]
    public async Task GetTariffsPageAsync_DoesNotHideUnlinkedHistoricalTariff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var legacyTariff = new Tariff
        {
            Name = "Шаблон тарифа воды",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var displacedServiceVersion = new Tariff
        {
            Name = "Вода — по счетчику, 05.08.2026, abcdef12",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 5)
        };
        database.Context.Tariffs.AddRange(legacyTariff, displacedServiceVersion);
        await database.Context.SaveChangesAsync();

        var page = await service.GetTariffsPageAsync(null, 0, 25, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, tariff => tariff.Id == legacyTariff.Id);
        Assert.Contains(page.Items, tariff => tariff.Id == displacedServiceVersion.Id);
    }

    [Fact]
    public async Task IrregularPaymentAsync_SavesStatusAndBlocksArchiveWhenUsed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Вступительный взнос", 1500m), actorUserId, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Вступительный взнос", "entry_custom"), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("1", 1, 1, null, 0m, null, null, null), null, CancellationToken.None);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = garage.Value!.Id,
            IncomeTypeId = incomeType.Value!.Id,
            IrregularPaymentId = created.Value!.Id,
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
    public async Task IrregularPaymentAsync_IgnoresCanceledAccrualAndBlocksActiveLinkedAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var canceledPayment = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Отмененный сбор", 100m), null, CancellationToken.None);
        var activePayment = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Оплаченный сбор", 200m), null, CancellationToken.None);
        var canceledIncomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Отмененный сбор", "canceled_fee"), null, CancellationToken.None);
        var activeIncomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Оплаченный сбор", "paid_fee"), null, CancellationToken.None);
        var garage = await service.CreateGarageAsync(new UpsertGarageRequest("IRR-1", 1, 1, null, 0m, null, null, null), null, CancellationToken.None);
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = garage.Value!.Id,
            IncomeTypeId = canceledIncomeType.Value!.Id,
            IrregularPaymentId = canceledPayment.Value!.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 100m,
            Source = AccrualSources.Manual,
            IsCanceled = true
        });
        database.Context.Accruals.Add(new Accrual
        {
            GarageId = garage.Value.Id,
            IncomeTypeId = activeIncomeType.Value!.Id,
            IrregularPaymentId = activePayment.Value!.Id,
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 200m,
            Source = AccrualSources.Manual
        });
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = garage.Value.Id,
                IncomeTypeId = canceledIncomeType.Value.Id,
                OperationDate = new DateOnly(2026, 7, 10),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 100m,
                IsCanceled = true
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                GarageId = garage.Value.Id,
                IncomeTypeId = activeIncomeType.Value!.Id,
                OperationDate = new DateOnly(2026, 7, 11),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 200m
            });
        await database.Context.SaveChangesAsync();

        var canceledArchive = await service.ArchiveIrregularPaymentAsync(canceledPayment.Value!.Id, "Использование отменено", null, CancellationToken.None);
        var activeArchive = await service.ArchiveIrregularPaymentAsync(activePayment.Value!.Id, "Проверка использования", null, CancellationToken.None);
        var activeList = await service.GetIrregularPaymentsAsync(null, CancellationToken.None);

        Assert.True(canceledArchive.Succeeded);
        Assert.False(canceledArchive.Value!.IsUsed);
        Assert.False(activeArchive.Succeeded);
        Assert.Equal("irregular_payment_used", activeArchive.ErrorCode);
        Assert.True(activeList.Single(payment => payment.Id == activePayment.Value.Id).IsUsed);
    }

    [Fact]
    public async Task ArchiveIrregularPaymentAsync_HidesUnusedPaymentAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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

    [Fact]
    public async Task FeeCampaignAsync_CreatesUpdatesArchivesAndRestoresWithAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Gate fee", "gate_fee"), actorUserId, CancellationToken.None);
        Assert.True(incomeType.Succeeded);
        database.Context.Garages.AddRange(
            new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "3", PeopleCount = 1, FloorCount = 1, IsArchived = true });
        await database.Context.SaveChangesAsync();

        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(" Gate campaign ", otherIncome.Id, "Gate replacement", 500m, 33500m, new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 30), true, 30),
            actorUserId,
            CancellationToken.None);
        Assert.Equal(otherIncome.Id, created.Value!.IncomeTypeId);
        var activeCampaigns = await service.GetFeeCampaignsAsync("gate", CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateFeeCampaignAsync(
            created.Value!.Id,
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, "Gate replacement and wiring", 600m, 34000m, new DateOnly(2026, 5, 4), new DateOnly(2026, 7, 1), true, 45),
            actorUserId,
            CancellationToken.None);
        var noOp = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, "Gate replacement and wiring", 600m, 34000m, new DateOnly(2026, 5, 4), new DateOnly(2026, 7, 1), true, 45),
            actorUserId,
            CancellationToken.None);
        var emptyReason = await service.ArchiveFeeCampaignAsync(created.Value.Id, " ", actorUserId, CancellationToken.None);
        var archived = await service.ArchiveFeeCampaignAsync(created.Value.Id, "No longer used", actorUserId, CancellationToken.None);
        var visibleAfterArchive = await service.GetFeeCampaignsAsync("gate", CancellationToken.None);
        var archivedCampaigns = await service.GetFeeCampaignsAsync("gate", CancellationToken.None, includeArchived: true);
        var restored = await service.RestoreFeeCampaignAsync(created.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(created.Succeeded);
        Assert.Equal("Gate campaign", created.Value.Name);
        Assert.Equal(otherIncome.Id, created.Value.IncomeTypeId);
        Assert.Equal("Прочие доходы", created.Value.IncomeTypeName);
        Assert.Equal(1000m, created.Value.TargetAmount);
        Assert.Single(activeCampaigns);
        Assert.True(updated.Succeeded);
        Assert.Equal(600m, updated.Value!.ContributionAmount);
        Assert.Equal(1200m, updated.Value.TargetAmount);
        Assert.True(noOp.Succeeded);
        Assert.False(emptyReason.Succeeded);
        Assert.Equal("dictionary_archive_reason_required", emptyReason.ErrorCode);
        Assert.True(archived.Succeeded);
        Assert.Empty(visibleAfterArchive);
        Assert.True(Assert.Single(archivedCampaigns).IsArchived);
        Assert.True(restored.Succeeded);
        Assert.False(restored.Value!.IsArchived);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.fee_campaign_updated" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.fee_campaign_archived" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.fee_campaign_restored" && item.ActorUserId == actorUserId);
        Assert.Equal(3, database.Context.AuditEvents.Count());
    }

    [Fact]
    public async Task CloseFeeCampaignAsync_RequiresCommentForEarlyClosureAndAllowsClosureAfterFullPayment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var garage = new Garage { Number = "CLOSE-1", PeopleCount = 1, FloorCount = 1 };
        database.Context.Garages.Add(garage);
        await database.Context.SaveChangesAsync();

        var campaignResult = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Закрываемый сбор", otherIncome.Id, null, 500m, 0m, new DateOnly(2026, 7, 1), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var campaign = await database.Context.FeeCampaigns.SingleAsync(item => item.Id == campaignResult.Value!.Id);
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeTypeId = campaign.IncomeTypeId,
            FeeCampaign = campaign,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 31),
            OverdueFromDate = new DateOnly(2026, 8, 31),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign
        };
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 7, 20),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 500m,
            Garage = garage,
            IncomeTypeId = campaign.IncomeTypeId
        };
        database.Context.AddRange(accrual, payment);
        database.Context.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
        {
            Accrual = accrual,
            FinancialOperation = payment,
            Amount = 500m
        });
        await database.Context.SaveChangesAsync();

        var closed = await service.CloseFeeCampaignAsync(
            campaign.Id,
            new CloseFeeCampaignRequest(null),
            actorUserId,
            CancellationToken.None);
        var repeated = await service.CloseFeeCampaignAsync(
            campaign.Id,
            new CloseFeeCampaignRequest("Повтор"),
            actorUserId,
            CancellationToken.None);
        var update = await service.UpdateFeeCampaignAsync(
            campaign.Id,
            new UpsertFeeCampaignRequest("Изменённый сбор", otherIncome.Id, null, 500m, 0m, new DateOnly(2026, 7, 1), null, true, 30),
            actorUserId,
            CancellationToken.None);

        Assert.True(closed.Succeeded, closed.ErrorMessage);
        Assert.NotNull(closed.Value!.ClosedAtUtc);
        Assert.False(closed.Value.IsClosedEarly);
        Assert.Null(closed.Value.ClosureComment);
        Assert.False(repeated.Succeeded);
        Assert.Equal("fee_campaign_already_closed", repeated.ErrorCode);
        Assert.True(update.Succeeded, update.ErrorMessage);
        Assert.Equal("Изменённый сбор", update.Value!.Name);
        Assert.NotNull(update.Value.ClosedAtUtc);
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.fee_campaign_closed" &&
            item.ActorUserId == actorUserId &&
            item.EntityId == campaign.Id.ToString());
        Assert.Contains(database.Context.AuditEvents, item =>
            item.Action == "dictionary.fee_campaign_updated" &&
            item.ActorUserId == actorUserId &&
            item.EntityId == campaign.Id.ToString());
    }

    [Fact]
    public async Task CloseFeeCampaignAsync_StoresRequiredEarlyClosureComment()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        database.Context.Garages.Add(new Garage { Number = "EARLY-1", PeopleCount = 1, FloorCount = 1 });
        await database.Context.SaveChangesAsync();
        var campaign = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Досрочный сбор", otherIncome.Id, null, 500m, 0m, new DateOnly(2026, 7, 1), null, true, 30),
            null,
            CancellationToken.None);

        var withoutComment = await service.CloseFeeCampaignAsync(
            campaign.Value!.Id,
            new CloseFeeCampaignRequest(" "),
            null,
            CancellationToken.None);
        var closed = await service.CloseFeeCampaignAsync(
            campaign.Value.Id,
            new CloseFeeCampaignRequest("Решение правления"),
            null,
            CancellationToken.None);

        Assert.False(withoutComment.Succeeded);
        Assert.Equal("fee_campaign_closure_comment_required", withoutComment.ErrorCode);
        Assert.True(closed.Succeeded, closed.ErrorMessage);
        Assert.True(closed.Value!.IsClosedEarly);
        Assert.Equal("Решение правления", closed.Value.ClosureComment);
    }

    [Fact]
    public async Task FeeCampaignAsync_RejectsInvalidInputAndDuplicateRestore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Gate fee", "gate_fee"), actorUserId, CancellationToken.None);
        Assert.True(incomeType.Succeeded);
        var incomeTypeWithoutFundDefinition = new IncomeType { Name = "Без фонда", Code = "fee_without_fund" };
        database.Context.Garages.AddRange(
            new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 });
        database.Context.IncomeTypes.Add(incomeTypeWithoutFundDefinition);
        await database.Context.SaveChangesAsync();

        var emptyName = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(" ", incomeType.Value!.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var automaticIncomeType = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("No income type", Guid.Empty, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var incomeTypeWithoutFund = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Income type without fund", incomeTypeWithoutFundDefinition.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var calculatedTarget = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Calculated target", otherIncome.Id, null, 500m, 0m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var invalidPeriod = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Invalid period", otherIncome.Id, null, 500m, 33500m, new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), true, 30),
            actorUserId,
            CancellationToken.None);
        var archived = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        await service.ArchiveFeeCampaignAsync(archived.Value!.Id, "Finished", actorUserId, CancellationToken.None);
        var activeDuplicate = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, null, 700m, 40000m, new DateOnly(2026, 8, 1), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var duplicateRestore = await service.RestoreFeeCampaignAsync(archived.Value.Id, actorUserId, CancellationToken.None);

        Assert.False(emptyName.Succeeded);
        Assert.Equal("fee_campaign_name_required", emptyName.ErrorCode);
        Assert.False(automaticIncomeType.Succeeded);
        Assert.Equal("fee_campaign_income_type_not_found", automaticIncomeType.ErrorCode);
        Assert.False(incomeTypeWithoutFund.Succeeded);
        Assert.Equal("fee_campaign_fund_not_found", incomeTypeWithoutFund.ErrorCode);
        Assert.True(calculatedTarget.Succeeded, calculatedTarget.ErrorMessage);
        Assert.Equal(1000m, calculatedTarget.Value!.TargetAmount);
        Assert.False(invalidPeriod.Succeeded);
        Assert.Equal("fee_campaign_period_invalid", invalidPeriod.ErrorCode);
        Assert.True(activeDuplicate.Succeeded);
        Assert.False(duplicateRestore.Succeeded);
        Assert.Equal("fee_campaign_duplicate", duplicateRestore.ErrorCode);
    }

    [Fact]
    public async Task GetChargeServiceSettingsAsync_FiltersRegularMeteredServicesBeforeLimit()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.ChargeServiceSettings.AddRange(
            new ChargeServiceSetting { Name = "Нерегулярная услуга", IsRegular = false, IsMetered = true },
            new ChargeServiceSetting { Name = "Регулярная без счётчика", IsRegular = true, IsMetered = false },
            new ChargeServiceSetting { Name = "Регулярная по счётчику", IsRegular = true, IsMetered = true });
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var settings = await service.GetChargeServiceSettingsAsync(
            null,
            CancellationToken.None,
            limit: 1,
            isRegular: true,
            isMetered: true);

        var setting = Assert.Single(settings);
        Assert.Equal("Регулярная по счётчику", setting.Name);
        Assert.True(setting.IsRegular);
        Assert.True(setting.IsMetered);
    }

    [Fact]
    public async Task FeeCampaignAsync_CalculatesContributionFromExplicitTargetAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        database.Context.Garages.AddRange(
            new Garage { Number = "1", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "2", PeopleCount = 1, FloorCount = 1 },
            new Garage { Number = "3", PeopleCount = 1, FloorCount = 1 });
        await database.Context.SaveChangesAsync();

        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(
                "Сбор с общей суммой",
                otherIncome.Id,
                null,
                1m,
                1000m,
                new DateOnly(2026, 8, 13),
                null,
                true,
                30,
                null,
                FeeCampaignAmountCalculationModes.Target),
            null,
            CancellationToken.None);

        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(333.34m, created.Value!.ContributionAmount);
        Assert.Equal(1000m, created.Value.TargetAmount);

        var invalidMode = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Неизвестный режим", otherIncome.Id, null, 100m, 300m, new DateOnly(2026, 8, 13), null, true, 30, null, "unknown"),
            null,
            CancellationToken.None);
        var invalidTarget = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Пустая сумма", otherIncome.Id, null, 100m, 0m, new DateOnly(2026, 8, 13), null, true, 30, null, FeeCampaignAmountCalculationModes.Target),
            null,
            CancellationToken.None);

        Assert.False(invalidMode.Succeeded);
        Assert.Equal("fee_campaign_amount_mode_invalid", invalidMode.ErrorCode);
        Assert.False(invalidTarget.Succeeded);
        Assert.Equal("fee_campaign_target_amount_invalid", invalidTarget.ErrorCode);
    }

    [Fact]
    public async Task FeeCampaignAsync_SavesSelectedParticipantGaragesAndWritesDiff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Gate fee", "gate_fee"), actorUserId, CancellationToken.None);
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var garage1 = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var garage2 = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var archivedGarage = new Garage { Number = "99", PeopleCount = 1, FloorCount = 1, Owner = owner, IsArchived = true };
        database.Context.AddRange(owner, garage1, garage2, archivedGarage);
        await database.Context.SaveChangesAsync();

        var invalidArchived = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Invalid participants", otherIncome.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [archivedGarage.Id]),
            actorUserId,
            CancellationToken.None);
        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [garage2.Id, garage1.Id]),
            actorUserId,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        database.Context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 5, 20),
            AccountingMonth = new DateOnly(2026, 5, 1),
            Amount = 275m,
            GarageId = garage2.Id,
            IncomeTypeId = otherIncome.Id,
            FeeCampaignId = created.Value!.Id
        });
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateFeeCampaignAsync(
            created.Value!.Id,
            new UpsertFeeCampaignRequest("Gate campaign", otherIncome.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [garage2.Id]),
            actorUserId,
            CancellationToken.None);
        var loaded = await service.GetFeeCampaignsAsync("Gate", CancellationToken.None);

        Assert.False(invalidArchived.Succeeded);
        Assert.Equal("fee_campaign_participant_garage_not_found", invalidArchived.ErrorCode);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.False(created.Value.AppliesToAllGarages);
        Assert.Equal(1000m, created.Value.TargetAmount);
        Assert.Equal([garage1.Id, garage2.Id], created.Value.ParticipantGarageIds);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal(500m, updated.Value!.TargetAmount);
        Assert.Equal([garage2.Id], updated.Value.ParticipantGarageIds);
        var loadedCampaign = Assert.Single(loaded);
        Assert.Equal([garage2.Id], loadedCampaign.ParticipantGarageIds);
        Assert.Equal(275m, loadedCampaign.CollectedAmount);
        Assert.Equal(otherIncome.DestinationFundId, loadedCampaign.DestinationFundId);
        Assert.Equal(otherIncome.DestinationFund!.Name, loadedCampaign.DestinationFundName);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.fee_campaign_updated" && item.ActorUserId == actorUserId);
        Assert.Contains("participantGarageIds", audit.MetadataJson, StringComparison.Ordinal);
        using var auditMetadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Contains("Сумма сбора", auditMetadata.RootElement.GetProperty("fieldName").GetString(), StringComparison.Ordinal);
        Assert.Contains("500", auditMetadata.RootElement.GetProperty("newValue").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_ClearsMeterFlagsForNonRegularCatalogService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вывоз снега", false, null, null, null, null, 0, true, true, "руб."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsRegular);
        Assert.False(result.Value.IsMetered);
        Assert.False(result.Value.HasTieredTariff);
        var saved = Assert.Single(database.Context.ChargeServiceSettings);
        Assert.False(saved.IsMetered);
        Assert.False(saved.HasTieredTariff);
        Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_created");
    }

    [Fact]
    public async Task CreateChargeServiceSettingAsync_RejectsTariffThatDoesNotMatchMeterMode()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд услуг", 10);
        var incomeType = new IncomeType { Name = "Прочие услуги", Code = "other_income", DestinationFundId = fund.Id };
        var fixedTariff = new Tariff { Name = "Фиксированный", CalculationBase = "fixed", Rate = 300m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var meterTariff = new Tariff { Name = "По воде", CalculationBase = "meter_water", Rate = 50m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        database.Context.AddRange(fund, incomeType, fixedTariff, meterTariff);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var fixedWithMeter = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Неверный фиксированный", true, 1, 1, 30, null, 30, true, false, "руб.", incomeType.Id, fixedTariff.Id),
            null,
            CancellationToken.None);
        var meterWithoutFlag = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Неверный счётчиковый", true, 1, 1, 30, null, 30, false, false, "м³", incomeType.Id, meterTariff.Id),
            null,
            CancellationToken.None);
        var validMeter = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", true, 1, 1, 30, null, 30, true, false, "м³", incomeType.Id, meterTariff.Id),
            null,
            CancellationToken.None);

        Assert.False(fixedWithMeter.Succeeded);
        Assert.Equal("charge_service_meter_mode_mismatch", fixedWithMeter.ErrorCode);
        Assert.Equal("Для расчета по счетчику выберите тариф воды или электроэнергии.", fixedWithMeter.ErrorMessage);
        Assert.False(meterWithoutFlag.Succeeded);
        Assert.Equal("charge_service_meter_mode_mismatch", meterWithoutFlag.ErrorCode);
        Assert.Equal("Для тарифа воды или электроэнергии включите расчет по счетчику.", meterWithoutFlag.ErrorMessage);
        Assert.True(validMeter.Succeeded);
        Assert.Equal(meterTariff.Id, validMeter.Value!.TariffId);
        Assert.True(validMeter.Value.IsMetered);
        Assert.Single(database.Context.ChargeServiceSettings);
        Assert.Equal(2, database.Context.AuditEvents.Count());
        Assert.Contains(database.Context.AuditEvents, audit => audit.Action == "dictionary.measurement_unit_created");
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_SavesServiceAndNumericRateAtomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Водоснабжение", 10);
        var incomeType = new IncomeType { Name = "Вода", Code = "water", DestinationFundId = fund.Id };
        var tariff = new Tariff { Name = "Тариф на воду", CalculationBase = "meter_water", Rate = 100.8m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Вода",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = tariff.Id,
            IsMetered = true,
            UnitName = "м³"
        };
        database.Context.AddRange(fund, incomeType, tariff, setting);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    "Холодная вода",
                    true,
                    1,
                    1,
                    25,
                    null,
                    15,
                    true,
                    false,
                    "м³",
                    incomeType.Id,
                    tariff.Id),
                101.23456m),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Холодная вода", result.Value!.Service.Name);
        Assert.Equal(25, result.Value.Service.PaymentDueDay);
        Assert.Equal(101.2346m, result.Value.Tariff.Rate);
        Assert.Equal(101.2346m, tariff.Rate);
        Assert.Equal(3, database.Context.AuditEvents.Count());
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.measurement_unit_created" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_updated" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_updated" && item.ActorUserId == actorUserId);

        var noOp = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    "Холодная вода",
                    true,
                    1,
                    1,
                    25,
                    null,
                    15,
                    true,
                    false,
                    "м³",
                    incomeType.Id,
                    tariff.Id),
                101.2346m),
            actorUserId,
            CancellationToken.None);

        Assert.True(noOp.Succeeded);
        Assert.Equal(3, database.Context.AuditEvents.Count());
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_VersionsMeteredAndTieredModesAtomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Электроэнергия", 10);
        var selectedFund = CreateFund("Электроэнергия нового периода", 20);
        var incomeType = new IncomeType { Name = "Электроэнергия", Code = "electricity", DestinationFundId = fund.Id };
        var sourceTariff = new Tariff { Name = "Электроэнергия — обычный", CalculationBase = "fixed", Rate = 7.47m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Электроэнергия",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = sourceTariff.Id,
            IsMetered = false,
            HasTieredTariff = false,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, selectedFund, incomeType, sourceTariff, setting);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var tiered = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    true,
                    true,
                    "руб.",
                    incomeType.Id,
                    sourceTariff.Id),
                7.47m,
                "metered_tiered",
                new DateOnly(2026, 8, 1),
                ChangeReason: "Переход на счетчик",
                IncomeFundId: selectedFund.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(tiered.Succeeded);
        Assert.NotEqual(sourceTariff.Id, tiered.Value!.Tariff.Id);
        Assert.Equal("meter_electricity", tiered.Value.Tariff.CalculationBase);
        Assert.Equal(3, tiered.Value.Tariff.ElectricityTiers!.Count);
        Assert.Equal(1100m, tiered.Value.Tariff.ElectricityTiers[0].UpperBound);
        Assert.Equal(1700m, tiered.Value.Tariff.ElectricityTiers[1].UpperBound);
        Assert.Null(tiered.Value.Tariff.ElectricityTiers[2].UpperBound);
        Assert.True(tiered.Value.Service.IsMetered);
        Assert.True(tiered.Value.Service.HasTieredTariff);
        Assert.Equal("руб.", tiered.Value.Service.UnitName);
        Assert.Equal(selectedFund.Id, incomeType.DestinationFundId);
        Assert.Equal("fixed", sourceTariff.CalculationBase);
        Assert.Equal(2, database.Context.Tariffs.Count());

        var regular = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    false,
                    false,
                    "руб.",
                    incomeType.Id,
                    tiered.Value.Tariff.Id),
                7.47m,
                "regular",
                new DateOnly(2026, 8, 2),
                ChangeReason: "Фиксированная ставка"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(regular.Succeeded);
        Assert.Equal("fixed", regular.Value!.Tariff.CalculationBase);
        Assert.False(regular.Value.Service.IsMetered);
        Assert.False(regular.Value.Service.HasTieredTariff);
        Assert.Equal("руб.", regular.Value.Service.UnitName);
        Assert.Equal(3, database.Context.Tariffs.Count());
        Assert.Equal(3, database.Context.ChargeServiceTariffVersions.Count(item => item.ChargeServiceSettingId == setting.Id));
        Assert.Equal(6, database.Context.AuditEvents.Count());
        Assert.Equal(1, database.Context.AuditEvents.Count(item => item.Action == "dictionary.measurement_unit_created"));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_tariff_mode_changed");
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.income_type_destination_fund_updated");

        var repository = new EfChargeServiceSettingRepository(database.Context);
        var july = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 7, 1), CancellationToken.None));
        Assert.Equal(sourceTariff.Id, july.TariffId);
        Assert.Equal("fixed", july.Tariff!.CalculationBase);
        Assert.Equal("руб.", july.UnitName);
        database.Context.ChangeTracker.Clear();
        var firstAugust = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 1), CancellationToken.None));
        Assert.Equal(tiered.Value.Tariff.Id, firstAugust.TariffId);
        Assert.Equal("meter_electricity", firstAugust.Tariff!.CalculationBase);
        Assert.Equal("руб.", firstAugust.UnitName);
        database.Context.ChangeTracker.Clear();
        var secondAugust = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 2), CancellationToken.None));
        Assert.Equal(regular.Value.Tariff.Id, secondAugust.TariffId);
        Assert.Equal("fixed", secondAugust.Tariff!.CalculationBase);
        Assert.Equal("руб.", secondAugust.UnitName);
        database.Context.ChangeTracker.Clear();

        var repeatedSameDate = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    true,
                    false,
                    "кВт·ч",
                    incomeType.Id,
                    regular.Value.Tariff.Id),
                8.25m,
                "metered",
                new DateOnly(2026, 8, 2),
                ChangeReason: "Повторная настройка на ту же дату",
                CalculationBase: "meter_electricity"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(repeatedSameDate.Succeeded, repeatedSameDate.ErrorMessage);
        Assert.Equal(regular.Value.Tariff.Id, repeatedSameDate.Value!.Tariff.Id);
        Assert.Equal("meter_electricity", repeatedSameDate.Value.Tariff.CalculationBase);
        Assert.Equal(8.25m, repeatedSameDate.Value.Tariff.Rate);
        Assert.Equal(3, database.Context.Tariffs.Count());
        Assert.Equal(3, database.Context.ChargeServiceTariffVersions.Count(item => item.ChargeServiceSettingId == setting.Id));
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.tariff_updated");
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_CreatesRateVersionForSelectedPeriod()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Членские взносы", 10);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership", DestinationFundId = fund.Id };
        var sourceTariff = new Tariff { Name = "Членский взнос", CalculationBase = "fixed", Rate = 500m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = sourceTariff.Id,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, sourceTariff, setting);
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    false,
                    false,
                    "руб.",
                    incomeType.Id,
                    sourceTariff.Id),
                650m,
                EffectiveFrom: new DateOnly(2026, 9, 1),
                ChangeReason: "Новая ставка с сентября"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotEqual(sourceTariff.Id, result.Value!.Tariff.Id);
        Assert.Equal(500m, sourceTariff.Rate);
        Assert.Equal(650m, result.Value.Tariff.Rate);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value.Tariff.EffectiveFrom);

        var repository = new EfChargeServiceSettingRepository(database.Context);
        var august = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 1), CancellationToken.None));
        var september = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 9, 1), CancellationToken.None));
        Assert.Equal(sourceTariff.Id, august.TariffId);
        Assert.Equal(500m, august.Tariff!.Rate);
        Assert.Equal(result.Value.Tariff.Id, september.TariffId);
        Assert.Equal(650m, september.Tariff!.Rate);

        var octoberResult = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    false,
                    false,
                    "руб.",
                    incomeType.Id,
                    result.Value.Tariff.Id),
                700m,
                EffectiveFrom: new DateOnly(2026, 10, 1),
                ChangeReason: "Новая ставка с октября"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(octoberResult.Succeeded, octoberResult.ErrorMessage);
        var tariffCountBeforeCorrection = database.Context.Tariffs.Count();

        var correctedSeptember = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    false,
                    false,
                    "руб.",
                    incomeType.Id,
                    octoberResult.Value!.Tariff.Id),
                675m,
                EffectiveFrom: new DateOnly(2026, 9, 1),
                ChangeReason: "Исправленная ставка с сентября"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(correctedSeptember.Succeeded, correctedSeptember.ErrorMessage);
        Assert.Equal(result.Value.Tariff.Id, correctedSeptember.Value!.Tariff.Id);
        Assert.Equal(675m, correctedSeptember.Value.Tariff.Rate);
        Assert.Equal(tariffCountBeforeCorrection, database.Context.Tariffs.Count());
        Assert.Equal(octoberResult.Value.Tariff.Id, setting.TariffId);
        Assert.Equal(
            3,
            database.Context.ChargeServiceTariffVersions.Count(item => item.ChargeServiceSettingId == setting.Id));
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_BackdatedRateKeepsLatestVersionCurrent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Членские взносы", 10);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership", DestinationFundId = fund.Id };
        var currentTariff = new Tariff { Name = "Членский взнос", CalculationBase = "fixed", Rate = 500m, EffectiveFrom = new DateOnly(2026, 8, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = currentTariff.Id,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, currentTariff, setting);
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name, true, 1, 1, 30, null, 30, false, false, "руб.", incomeType.Id, currentTariff.Id),
                400m,
                EffectiveFrom: new DateOnly(2026, 7, 1),
                ChangeReason: "Ставка за прошлый период"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(currentTariff.Id, setting.TariffId);
        Assert.Equal(500m, currentTariff.Rate);

        var repository = new EfChargeServiceSettingRepository(database.Context);
        var july = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 7, 1), CancellationToken.None));
        var august = Assert.Single(await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 1), CancellationToken.None));
        Assert.Equal(400m, july.Tariff!.Rate);
        Assert.Equal(currentTariff.Id, august.TariffId);
        Assert.Equal(500m, august.Tariff!.Rate);
    }

    [Fact]
    public async Task UpdateChargeServiceTariffScheduleAsync_SavesContiguousPeriodsAndSelectsRateByMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд услуг", 10);
        var incomeType = new IncomeType { Name = "Услуги", Code = "services", DestinationFundId = fund.Id };
        var tariff = new Tariff { Name = "Охрана", CalculationBase = "fixed", Rate = 100m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Охрана",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = tariff.Id,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, tariff, setting);
        await database.Context.SaveChangesAsync();

        var service = DictionaryServiceTestFactory.Create(database.Context);
        var result = await service.UpdateChargeServiceTariffScheduleAsync(
            setting.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [
                    new(null, null, new DateOnly(2026, 8, 31), 100m),
                    new(null, new DateOnly(2026, 9, 1), null, 125m)
                ],
                false,
                "Новая ставка с сентября",
                setting.Version),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, result.Value!.Periods.Count);
        Assert.Null(result.Value.Periods[0].EffectiveFrom);
        Assert.Null(result.Value.Periods[1].EffectiveTo);
        Assert.Equal(100m, result.Value.Tariff.Rate);
        Assert.Equal(result.Value.Periods[0].TariffId, result.Value.Service.TariffId);

        var repository = new EfChargeServiceSettingRepository(database.Context);
        Assert.Equal(100m, (await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 1), CancellationToken.None)).Single().Tariff!.Rate);
        Assert.Equal(125m, (await repository.GetActiveRegularAsync(new DateOnly(2026, 9, 1), CancellationToken.None)).Single().Tariff!.Rate);
        var listedInAugust = Assert.Single(await DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 8, 12))
            .GetChargeServiceSettingsAsync(null, CancellationToken.None));
        var listedInSeptember = Assert.Single(await DictionaryServiceTestFactory.Create(database.Context, new DateOnly(2026, 9, 12))
            .GetChargeServiceSettingsAsync(null, CancellationToken.None));
        Assert.Equal(result.Value.Periods[0].TariffId, listedInAugust.TariffId);
        Assert.Equal(result.Value.Periods[1].TariffId, listedInSeptember.TariffId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.charge_service_tariff_schedule_updated");
    }

    [Fact]
    public async Task UpdateChargeServiceTariffScheduleAsync_RejectsGapWithoutExplicitConfirmation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Фонд услуг", 10);
        var incomeType = new IncomeType { Name = "Услуги", Code = "services", DestinationFundId = fund.Id };
        var tariff = new Tariff { Name = "Охрана", CalculationBase = "fixed", Rate = 100m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Охрана",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = tariff.Id,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, tariff, setting);
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceTariffScheduleAsync(
            setting.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [new(null, new DateOnly(2026, 9, 1), null, 125m)],
                false,
                null,
                setting.Version),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_schedule_gap", result.ErrorCode);
        Assert.Empty(database.Context.ChargeServiceTariffVersions);

        var confirmed = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceTariffScheduleAsync(
            setting.Id,
            new UpsertChargeServiceTariffScheduleRequest(
                [new(null, new DateOnly(2026, 9, 1), null, 125m)],
                true,
                "Разрыв подтвержден",
                setting.Version),
            null,
            CancellationToken.None);
        Assert.True(confirmed.Succeeded, confirmed.ErrorMessage);
        var repository = new EfChargeServiceSettingRepository(database.Context);
        Assert.Null((await repository.GetActiveRegularAsync(new DateOnly(2026, 8, 1), CancellationToken.None)).Single().Tariff);
        Assert.Equal(125m, (await repository.GetActiveRegularAsync(new DateOnly(2026, 9, 1), CancellationToken.None)).Single().Tariff!.Rate);
    }

    [Fact]
    public async Task MeteredCatalog_SelectsHistoricalModeAndExcludesLegacyDisabledMeterTariff()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Водоснабжение", 10);
        var incomeType = new IncomeType { Name = "Вода", Code = "water", DestinationFundId = fund.Id };
        var julyMeterTariff = new Tariff { Name = "Вода июль", CalculationBase = "meter_water", Rate = 40m, EffectiveFrom = new DateOnly(2026, 7, 1) };
        var augustFixedTariff = new Tariff { Name = "Вода август", CalculationBase = "fixed", Rate = 500m, EffectiveFrom = new DateOnly(2026, 8, 1) };
        var historicalSetting = new ChargeServiceSetting
        {
            Name = "Вода с историей",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = augustFixedTariff.Id,
            IsMetered = false,
            UnitName = "руб."
        };
        var legacyDisabledSetting = new ChargeServiceSetting
        {
            Name = "Отключенный старый счетчик",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = julyMeterTariff.Id,
            IsMetered = false,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, julyMeterTariff, augustFixedTariff, historicalSetting, legacyDisabledSetting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = historicalSetting.Id,
                TariffId = julyMeterTariff.Id,
                EffectiveFrom = julyMeterTariff.EffectiveFrom
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = historicalSetting.Id,
                TariffId = augustFixedTariff.Id,
                EffectiveFrom = augustFixedTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();

        var repository = new EfChargeServiceSettingRepository(database.Context);
        var july = await repository.GetActiveRegularMeteredAsync(
            TariffCalculationBases.MeterWater,
            new DateOnly(2026, 7, 1),
            50,
            CancellationToken.None);
        var august = await repository.GetActiveRegularMeteredAsync(
            TariffCalculationBases.MeterWater,
            new DateOnly(2026, 8, 1),
            50,
            CancellationToken.None);

        var historicalJuly = Assert.Single(july);
        Assert.Equal(historicalSetting.Id, historicalJuly.Id);
        Assert.True(historicalJuly.IsMetered);
        Assert.Equal(julyMeterTariff.Id, historicalJuly.TariffId);
        Assert.Empty(august);
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_AllowsTieredWaterModeForAnyManagedService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fund = CreateFund("Членские взносы", 10);
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership", DestinationFundId = fund.Id };
        var sourceTariff = new Tariff { Name = "Членский взнос", CalculationBase = "fixed", Rate = 100m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = sourceTariff.Id,
            IsMetered = false,
            HasTieredTariff = false,
            UnitName = "руб."
        };
        database.Context.AddRange(fund, incomeType, sourceTariff, setting);
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(
                new UpsertChargeServiceSettingRequest(
                    setting.Name,
                    true,
                    1,
                    1,
                    30,
                    null,
                    30,
                    true,
                    true,
                    "м³",
                    incomeType.Id,
                    sourceTariff.Id),
                2m,
                "metered_tiered",
                new DateOnly(2026, 8, 1),
                [
                    new(null, "Первый", 10m, 2m),
                    new(null, "Второй", null, 3m)
                ],
                "Настройка порогов воды",
                TariffCalculationBases.MeterWater),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(TariffCalculationBases.MeterWater, result.Value!.Tariff.CalculationBase);
        Assert.Equal("0–10 м³", result.Value.Tariff.ElectricityTiers![0].Name);
        Assert.Equal("10+ м³", result.Value.Tariff.ElectricityTiers[1].Name);
        Assert.True(result.Value.Service.IsMetered);
        Assert.True(result.Value.Service.HasTieredTariff);
        Assert.Equal("м³", result.Value.Service.UnitName);
        Assert.Equal(MeterKinds.ForService(result.Value.Service.Id), result.Value.Service.MeterKind);
    }

    [Fact]
    public async Task UpdateChargeServiceWithTariffAsync_RejectsMissingFundAndInvalidRateWithoutChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incomeType = new IncomeType { Name = "Вода", Code = "water" };
        var tariff = new Tariff { Name = "Тариф на воду", CalculationBase = "meter_water", Rate = 100.8m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Вода",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeTypeId = incomeType.Id,
            TariffId = tariff.Id,
            IsMetered = true,
            UnitName = "м³"
        };
        database.Context.AddRange(incomeType, tariff, setting);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var validServiceRequest = new UpsertChargeServiceSettingRequest(
            "Измененная вода",
            true,
            1,
            1,
            30,
            null,
            30,
            true,
            false,
            "м³",
            incomeType.Id,
            tariff.Id);

        var missingFund = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(validServiceRequest, 110m),
            Guid.NewGuid(),
            CancellationToken.None);
        var invalidRate = await service.UpdateChargeServiceWithTariffAsync(
            setting.Id,
            new UpdateChargeServiceWithTariffRequest(validServiceRequest, 0m),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(missingFund.Succeeded);
        Assert.Equal("charge_service_fund_required", missingFund.ErrorCode);
        Assert.False(invalidRate.Succeeded);
        Assert.Equal("charge_service_rate_invalid", invalidRate.ErrorCode);
        Assert.Equal("Вода", setting.Name);
        Assert.Equal(100.8m, tariff.Rate);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateAndRestoreChargeServiceSettingAsync_RejectMissingOrDeletedDestinationFund()
    {
        await using var database = await TestDatabase.CreateAsync();
        var archivedFund = CreateFund("Удаленный фонд", 10);
        archivedFund.IsArchived = true;
        var withoutFund = new IncomeType { Name = "Поступления без фонда", Code = "membership_without_fund" };
        var withArchivedFund = new IncomeType
        {
            Name = "Поступления удаленного фонда",
            Code = "membership_archived_fund",
            DestinationFundId = archivedFund.Id
        };
        var tariff = new Tariff
        {
            Name = "Членский тариф",
            CalculationBase = "fixed",
            Rate = 300m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var archivedService = new ChargeServiceSetting
        {
            Name = "Архивная услуга",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            UnitName = "руб.",
            IncomeType = withArchivedFund,
            Tariff = tariff,
            IsArchived = true
        };
        database.Context.AddRange(archivedFund, withoutFund, withArchivedFund, tariff, archivedService);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var missingDestination = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest(
                "Услуга без фонда",
                true,
                1,
                1,
                30,
                null,
                30,
                false,
                false,
                "руб.",
                withoutFund.Id,
                tariff.Id),
            null,
            CancellationToken.None);
        var deletedDestination = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest(
                "Услуга удаленного фонда",
                true,
                1,
                1,
                30,
                null,
                30,
                false,
                false,
                "руб.",
                withArchivedFund.Id,
                tariff.Id),
            null,
            CancellationToken.None);
        var restored = await service.RestoreChargeServiceSettingAsync(
            archivedService.Id,
            null,
            CancellationToken.None);

        Assert.False(missingDestination.Succeeded);
        Assert.Equal("charge_service_fund_required", missingDestination.ErrorCode);
        Assert.False(deletedDestination.Succeeded);
        Assert.Equal("charge_service_fund_not_found", deletedDestination.ErrorCode);
        Assert.False(restored.Succeeded);
        Assert.Equal("charge_service_fund_not_found", restored.ErrorCode);
        Assert.True((await database.Context.ChargeServiceSettings.SingleAsync()).IsArchived);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateFeeCampaignAsync_LocksParticipantCompositionAfterFirstAccrual()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
        var firstGarage = new Garage { Number = "1", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var secondGarage = new Garage { Number = "2", PeopleCount = 1, FloorCount = 1, Owner = owner };
        var targetFund = CreateFund("Целевой фонд", 20);
        var targetIncome = new IncomeType
        {
            Name = "Целевые поступления",
            Code = "target_income",
            DestinationFund = targetFund,
            DestinationFundId = targetFund.Id
        };
        database.Context.AddRange(owner, firstGarage, secondGarage, targetFund, targetIncome);
        await database.Context.SaveChangesAsync();

        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Сбор на ворота", otherIncome.Id, null, 500m, 5000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);

        var destinationChange = await service.UpdateFeeCampaignAsync(
            created.Value!.Id,
            new UpsertFeeCampaignRequest("Сбор на ворота", targetIncome.Id, null, 500m, 5000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);
        Assert.True(destinationChange.Succeeded, destinationChange.ErrorMessage);
        Assert.Equal(targetIncome.Id, destinationChange.Value!.IncomeTypeId);
        Assert.Equal(targetIncome.Name, destinationChange.Value.IncomeTypeName);

        database.Context.Accruals.Add(new Accrual
        {
            GarageId = firstGarage.Id,
            IncomeTypeId = targetIncome.Id,
            FeeCampaignId = created.Value!.Id,
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 30),
            OverdueFromDate = new DateOnly(2026, 7, 31),
            Amount = 500m,
            Source = AccrualSources.FeeCampaign
        });
        await database.Context.SaveChangesAsync();

        var participantChange = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Сбор на ворота", targetIncome.Id, null, 500m, 5000m, new DateOnly(2026, 5, 1), null, false, 30, [secondGarage.Id]),
            null,
            CancellationToken.None);
        var detailsChange = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Сбор на ворота", targetIncome.Id, "Уточненная цель", 500m, 6000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);
        var destinationChangeAfterAccrual = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Сбор на ворота", otherIncome.Id, "Уточненная цель", 500m, 6000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);

        Assert.False(participantChange.Succeeded);
        Assert.Equal("fee_campaign_participants_locked", participantChange.ErrorCode);
        Assert.Contains("Исторический состав", participantChange.ErrorMessage, StringComparison.Ordinal);
        Assert.True(detailsChange.Succeeded, detailsChange.ErrorMessage);
        Assert.Equal("Уточненная цель", detailsChange.Value!.Goal);
        Assert.Equal(targetIncome.Id, detailsChange.Value.IncomeTypeId);
        Assert.Equal(500m, detailsChange.Value.TargetAmount);
        Assert.Equal([firstGarage.Id], detailsChange.Value.ParticipantGarageIds);
        Assert.False(destinationChangeAfterAccrual.Succeeded);
        Assert.Equal("fee_campaign_income_type_locked", destinationChangeAfterAccrual.ErrorCode);
    }

    [Fact]
    public async Task CreateFeeCampaignAsync_RejectsUnknownIncomeDestination()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);

        var result = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(
                "Сбор без назначения",
                Guid.NewGuid(),
                null,
                500m,
                5000m,
                new DateOnly(2026, 1, 1),
                null,
                true,
                30),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fee_campaign_income_type_not_found", result.ErrorCode);
        Assert.Empty(database.Context.FeeCampaigns);
    }

    [Fact]
    public async Task GaragePage_LoadsBalancesInThreeSelectsRegardlessOfPageSize()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var incomeType = new IncomeType { Name = "Performance income", Code = "performance_income" };
        database.Context.IncomeTypes.Add(incomeType);
        for (var index = 0; index < 200; index++)
        {
            var garage = new Garage
            {
                Number = $"G-{index:000}",
                PeopleCount = 1,
                FloorCount = 1,
                StartingBalance = 100m
            };
            database.Context.Garages.Add(garage);
            database.Context.Accruals.Add(new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 50m,
                Source = "performance-test"
            });
            database.Context.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 6, 15),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 20m,
                Garage = garage
            });
        }

        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await DictionaryServiceTestFactory.Create(database.Context).GetGaragesPageAsync(
            null,
            0,
            25,
            "number",
            "asc",
            CancellationToken.None);

        Assert.Equal(3, commandCounter.Count);
        Assert.Equal(200, result.TotalCount);
        Assert.Equal(25, result.Items.Count);
        Assert.All(result.Items, garage =>
        {
            Assert.Equal(130m, garage.Balance);
            Assert.Equal(130m, garage.OverdueDebt);
        });
    }

    [Fact]
    public async Task SupplierPage_LoadsDebtsInThreeSelectsRegardlessOfPageSize()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var group = new SupplierGroup { Name = "Performance suppliers" };
        var expenseType = new ExpenseType { Name = "Performance expense", Code = "performance_expense" };
        database.Context.AddRange(group, expenseType);
        for (var index = 0; index < 200; index++)
        {
            var supplier = new Supplier
            {
                Name = $"Supplier {index:000}",
                Group = group,
                StartingBalance = 100m
            };
            database.Context.Suppliers.Add(supplier);
            database.Context.SupplierAccruals.Add(new SupplierAccrual
            {
                Supplier = supplier,
                ExpenseType = expenseType,
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 50m,
                Source = "performance-test"
            });
            database.Context.FinancialOperations.Add(new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 6, 15),
                AccountingMonth = new DateOnly(2026, 6, 1),
                Amount = 20m,
                Supplier = supplier,
                ExpenseType = expenseType
            });
        }

        await database.Context.SaveChangesAsync();
        commandCounter.Reset();

        var result = await DictionaryServiceTestFactory.Create(database.Context).GetSuppliersPageAsync(
            group.Id,
            null,
            0,
            25,
            "name",
            "asc",
            CancellationToken.None);

        Assert.Equal(3, commandCounter.Count);
        Assert.Equal(200, result.TotalCount);
        Assert.Equal(25, result.Items.Count);
        Assert.All(result.Items, supplier => Assert.Equal(130m, supplier.Debt));
    }

    [Fact]
    public async Task ActiveRegularServices_ReuseIncludedTariffVersionsInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var incomeType = new IncomeType { Name = "Регулярные услуги", Code = "regular_services_query_count" };
        var julyTariff = new Tariff
        {
            Name = "Тариф июля",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 100m,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        };
        var augustTariff = new Tariff
        {
            Name = "Тариф августа",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 125m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Регулярная услуга",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = augustTariff,
            UnitName = "руб."
        };
        database.Context.AddRange(incomeType, julyTariff, augustTariff, setting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = julyTariff.Id,
                EffectiveFrom = julyTariff.EffectiveFrom,
                EffectiveTo = new DateOnly(2026, 7, 31)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = augustTariff.Id,
                EffectiveFrom = augustTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        commandCounter.Reset();

        var result = await new EfChargeServiceSettingRepository(database.Context).GetActiveRegularAsync(
            new DateOnly(2026, 7, 1),
            CancellationToken.None);

        var selected = Assert.Single(result);
        Assert.Equal(1, commandCounter.Count);
        Assert.Equal(julyTariff.Id, selected.TariffId);
        Assert.Equal(julyTariff.Id, Assert.Single(selected.TariffVersions).TariffId);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ActiveRegularMeteredServices_ReuseIncludedTariffVersionsInOneSelect()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var incomeType = new IncomeType { Name = "Вода", Code = MeterKinds.Water };
        var tariff = new Tariff
        {
            Name = "Вода по счётчику",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 50m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var futureTariff = new Tariff
        {
            Name = "Вода со следующего месяца",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 60m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Водоснабжение",
            IsRegular = true,
            IsMetered = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = futureTariff,
            UnitName = "м³"
        };
        database.Context.AddRange(incomeType, tariff, futureTariff, setting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = tariff.Id,
                EffectiveFrom = tariff.EffectiveFrom,
                EffectiveTo = new DateOnly(2026, 7, 31)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                TariffId = futureTariff.Id,
                EffectiveFrom = futureTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        commandCounter.Reset();

        var result = await new EfChargeServiceSettingRepository(database.Context).GetActiveRegularMeteredAsync(
            TariffCalculationBases.MeterWater,
            new DateOnly(2026, 7, 1),
            50,
            CancellationToken.None);

        var selected = Assert.Single(result);
        Assert.Equal(setting.Id, selected.Id);
        Assert.Equal(tariff.Id, selected.TariffId);
        Assert.Equal(tariff.Id, Assert.Single(selected.TariffVersions).TariffId);
        Assert.Equal(1, commandCounter.Count);
    }

    [Fact]
    public async Task ChargeServiceList_LoadsOnlyBusinessDateTariffInOneSelectAndPreservesScheduleGaps()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        var julyTariff = new Tariff
        {
            Name = "Тариф списка июля",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 100m,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        };
        var augustTariff = new Tariff
        {
            Name = "Тариф списка августа",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 125m,
            EffectiveFrom = new DateOnly(2026, 8, 1)
        };
        var scheduledSetting = new ChargeServiceSetting
        {
            Name = "A — услуга с текущим периодом",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            Tariff = augustTariff,
            UnitName = "руб."
        };
        var gapTariff = new Tariff
        {
            Name = "Тариф после пробела",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 150m,
            EffectiveFrom = new DateOnly(2026, 9, 1)
        };
        var gapSetting = new ChargeServiceSetting
        {
            Name = "B — услуга с пробелом",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            Tariff = gapTariff,
            UnitName = "руб."
        };
        database.Context.AddRange(julyTariff, augustTariff, gapTariff, scheduledSetting, gapSetting);
        database.Context.ChargeServiceTariffVersions.AddRange(
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = scheduledSetting.Id,
                TariffId = julyTariff.Id,
                EffectiveFrom = julyTariff.EffectiveFrom,
                EffectiveTo = new DateOnly(2026, 7, 31)
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = scheduledSetting.Id,
                TariffId = augustTariff.Id,
                EffectiveFrom = augustTariff.EffectiveFrom
            },
            new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = gapSetting.Id,
                TariffId = gapTariff.Id,
                EffectiveFrom = gapTariff.EffectiveFrom
            });
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        commandCounter.Reset();

        var result = await new EfChargeServiceSettingRepository(database.Context).GetListAsync(
            normalizedSearch: null,
            includeArchived: false,
            isRegular: null,
            isMetered: null,
            limit: 50,
            businessDate: new DateOnly(2026, 7, 15),
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, commandCounter.Count);
        var scheduled = Assert.Single(result, item => item.Id == scheduledSetting.Id);
        Assert.Equal(julyTariff.Id, scheduled.TariffId);
        Assert.Equal(julyTariff.Id, Assert.Single(scheduled.TariffVersions).TariffId);
        var gap = Assert.Single(result, item => item.Id == gapSetting.Id);
        Assert.Null(gap.TariffId);
        Assert.Null(gap.Tariff);
        Assert.Empty(gap.TariffVersions);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BalanceAggregates_WithEmptyIdentifiers_DoNotQueryDatabase()
    {
        var commandCounter = new SelectCommandCounter();
        await using var database = await TestDatabase.CreateAsync(commandCounter);
        commandCounter.Reset();

        var garageTotals = await new EfGarageRepository(database.Context).GetBalanceTotalsAsync([], CancellationToken.None);
        var supplierTotals = await new EfSupplierRepository(database.Context).GetDebtTotalsAsync([], CancellationToken.None);

        Assert.Equal(0, commandCounter.Count);
        Assert.Empty(garageTotals.AccrualTotals);
        Assert.Empty(garageTotals.IncomeTotals);
        Assert.Empty(supplierTotals);
    }

    private static async Task<IncomeType> AddOtherIncomeDestinationAsync(GarageBalanceDbContext context)
    {
        var fund = new Fund
        {
            Name = "Прочее",
            NormalizedName = "ПРОЧЕЕ"
        };
        var incomeType = new IncomeType
        {
            Name = "Прочие доходы",
            Code = "other_income",
            IsSystem = true,
            DestinationFund = fund,
            DestinationFundId = fund.Id
        };
        context.AddRange(fund, incomeType);
        await context.SaveChangesAsync();
        return incomeType;
    }

    private static Fund CreateFund(string name, int sortOrder)
    {
        return new Fund
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            SortOrder = sortOrder,
            IsSystem = false
        };
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

        public static async Task<TestDatabase> CreateAsync(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection);
            if (interceptor is not null)
            {
                optionsBuilder.AddInterceptors(interceptor);
            }

            var options = optionsBuilder.Options;
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

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CountSelect(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CountSelect(command);
            return ValueTask.FromResult(result);
        }

        private void CountSelect(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Count++;
            }
        }
    }

    private sealed class ChargeServiceInsertFailureInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowForChargeServiceInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowForChargeServiceInsert(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowForChargeServiceInsert(DbCommand command)
        {
            if (Enabled &&
                command.CommandText.Contains("INSERT INTO \"charge_service_settings\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Имитирован сбой сохранения услуги.");
            }
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
