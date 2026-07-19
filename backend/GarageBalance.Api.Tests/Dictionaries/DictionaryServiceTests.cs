using System.Data.Common;
using System.Text.Json;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionaryServiceTests
{
    [Fact]
    public async Task CreateOwnerAsync_TrimsFieldsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        await AddOtherIncomeDestinationAsync(database.Context);
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
            Assert.True((await service.CreateFeeCampaignAsync(new UpsertFeeCampaignRequest($"Сбор {index}", incomeType.Value!.Id, null, 100 + index, 1000 + index, new DateOnly(2026, 1, 1).AddMonths(index), null, true, 30), null, CancellationToken.None)).Succeeded);
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
    public async Task CurrentExtendedUpdateMethods_DoNotWriteAuditWhenNormalizedValuesAreUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();

        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var supplier = await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", group.Value!.Id, null, null, null, null, null, 0m, null), null, CancellationToken.None);
        var contact = await service.CreateSupplierContactAsync(
            new UpsertSupplierContactRequest(supplier.Value!.Id, "Петров И.А.", "Директор", "+7 901", "contact@example.com", "Работает", "Основной"),
            null,
            CancellationToken.None);
        var department = await service.CreateStaffDepartmentAsync(new UpsertStaffDepartmentRequest("Бухгалтерия"), null, CancellationToken.None);
        var staffMember = await service.CreateStaffMemberAsync(new UpsertStaffMemberRequest("Петрова Ольга", department.Value!.Id, 40000.005m), null, CancellationToken.None);
        var irregularPayment = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Вступительный взнос", 1500.005m), null, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Целевой сбор", "target_fee"), null, CancellationToken.None);
        var feeCampaign = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Ремонт ворот", incomeType.Value!.Id, "Замена механизма", 100.005m, 1000.005m, new DateOnly(2026, 7, 1), null, true, 30),
            null,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        Assert.True((await service.UpdateSupplierContactAsync(
            contact.Value!.Id,
            new UpsertSupplierContactRequest(supplier.Value.Id, " Петров И.А. ", " Директор ", " +7 901 ", " contact@example.com ", " Работает ", " Основной "),
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
            new UpsertFeeCampaignRequest(" Ремонт ворот ", incomeType.Value.Id, " Замена механизма ", 100.005m, 1000.005m, new DateOnly(2026, 7, 1), null, true, 30),
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
        Assert.Equal("+7 900 111-22-33", garageByNumber.OwnerPhone);
        var garageByOwner = Assert.Single(byOwner);
        Assert.Equal("21", garageByOwner.Number);
        Assert.Null(garageByOwner.OwnerPhone);
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

        var beforeGrace = await new EfGarageRepository(
                database.Context,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)))
            .GetBalanceTotalsAsync([garage.Id], CancellationToken.None);
        var afterGrace = await new EfGarageRepository(
                database.Context,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)))
            .GetBalanceTotalsAsync([garage.Id], CancellationToken.None);

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
            new UpsertOwnerRequest("Zed", "Owner", null, "+7 900 200", null, null),
            null,
            CancellationToken.None);
        var alphaOwner = await service.CreateOwnerAsync(
            new UpsertOwnerRequest("Alpha", "Owner", null, "+7 900 100", null, null),
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
        var water = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Вода", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);
        var electricity = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Электроэнергия", false, null, null, null, null, 0, false, false, "руб."),
            null,
            CancellationToken.None);

        var created = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Ресурсоснабжающая организация", group.Value!.Id, null, null, null, null, null, 0, null, water.Value!.Id),
            null,
            CancellationToken.None);
        var updated = await service.UpdateSupplierAsync(
            created.Value!.Id,
            new UpsertSupplierRequest(created.Value.Name, group.Value.Id, null, null, null, null, null, 0, null, electricity.Value!.Id),
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
        Assert.True(updated.Succeeded);
        Assert.Equal(electricity.Value.Id, updated.Value!.ChargeServiceSettingId);
        Assert.Equal("Электроэнергия", updated.Value.ChargeServiceSettingName);
        var listedSupplier = Assert.Single(serviceSortedPage.Items);
        Assert.Equal(created.Value.Id, listedSupplier.Id);
        Assert.Equal(electricity.Value.Id, listedSupplier.ChargeServiceSettingId);
        Assert.Equal("Электроэнергия", listedSupplier.ChargeServiceSettingName);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.supplier_updated");
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingOrArchivedChargeService()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var group = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var archived = await service.CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest("Архивная услуга", false, null, null, null, null, 0, false, false, null),
            null,
            CancellationToken.None);
        var existingSupplier = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Существующий поставщик", group.Value!.Id, null, null, null, null, null, 0, null, archived.Value!.Id),
            null,
            CancellationToken.None);
        await service.ArchiveChargeServiceSettingAsync(archived.Value!.Id, "Услуга больше не используется", null, CancellationToken.None);

        var missingResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Первый поставщик", group.Value!.Id, null, null, null, null, null, 0, null, Guid.NewGuid()),
            null,
            CancellationToken.None);
        var archivedResult = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Второй поставщик", group.Value.Id, null, null, null, null, null, 0, null, archived.Value.Id),
            null,
            CancellationToken.None);
        var existingSupplierUpdate = await service.UpdateSupplierAsync(
            existingSupplier.Value!.Id,
            new UpsertSupplierRequest(existingSupplier.Value.Name, group.Value.Id, null, null, null, "+7 900 000-00-00", null, 0, null, archived.Value.Id),
            null,
            CancellationToken.None);

        Assert.False(missingResult.Succeeded);
        Assert.Equal("charge_service_not_found", missingResult.ErrorCode);
        Assert.False(archivedResult.Succeeded);
        Assert.Equal("charge_service_not_found", archivedResult.ErrorCode);
        Assert.True(existingSupplierUpdate.Succeeded);
        Assert.Equal(archived.Value.Id, existingSupplierUpdate.Value!.ChargeServiceSettingId);
        Assert.Equal("+7 900 000-00-00", existingSupplierUpdate.Value.Phone);
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
        await service.CreateSupplierContactAsync(new UpsertSupplierContactRequest(waterSupplier.Id, "Яковлев Яков", null, "+7 999", "z@example.test", "Работает", null), null, CancellationToken.None);
        await service.CreateSupplierContactAsync(new UpsertSupplierContactRequest(bankSupplier.Value!.Id, "Анна Алексеева", null, "+7 111", "a@example.test", "Работает", null), null, CancellationToken.None);

        var result = await service.GetSuppliersAsync(utilityGroup.Value.Id, "5401", CancellationToken.None);

        var supplier = Assert.Single(result);
        Assert.Equal("Водоканал", supplier.Name);
        Assert.Equal("Коммунальные услуги", supplier.GroupName);

        var highestDebt = await service.GetSuppliersPageAsync(null, null, 0, 1, "debt", "desc", CancellationToken.None);
        var safeFallback = await service.GetSuppliersPageAsync(null, null, 0, 1, "unsupported", "asc", CancellationToken.None);
        var primaryContact = await service.GetSuppliersPageAsync(null, null, 0, 1, "contactPerson", "asc", CancellationToken.None);
        Assert.Equal("Водоканал", highestDebt.Items[0].Name);
        Assert.Equal("Альфа-Банк", safeFallback.Items[0].Name);
        Assert.Equal("Альфа-Банк", primaryContact.Items[0].Name);
        Assert.Equal("Анна Алексеева", primaryContact.Items[0].ContactPerson);
        Assert.Equal("+7 111", primaryContact.Items[0].Phone);
        Assert.Equal("a@example.test", primaryContact.Items[0].Email);
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
        await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership"), null, CancellationToken.None);

        var result = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership2"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_duplicate", result.ErrorCode);
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

        var result = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электроэнергия", "electricity"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.expense_type_created" && item.ActorUserId == actorUserId);
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
        Assert.Contains("электричество: От 0 кВт до 50.556 кВт по 3.11", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("От 50.556 кВт до 100.778 кВт по 4.22", audit.Summary, StringComparison.Ordinal);
        Assert.Contains("От 100.778 кВт по 5.33", audit.Summary, StringComparison.Ordinal);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
        var service = DictionaryServiceTestFactory.Create(database.Context);
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
        var service = DictionaryServiceTestFactory.Create(database.Context);

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
    public async Task IrregularPaymentAsync_SavesStatusAndBlocksArchiveWhenUsed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var created = await service.CreateIrregularPaymentAsync(new UpsertIrregularPaymentRequest("Вступительный взнос", 1500m), actorUserId, CancellationToken.None);
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Вступительный взнос", "entry"), null, CancellationToken.None);
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

        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(" Gate campaign ", incomeType.Value!.Id, "Gate replacement", 500m, 33500m, new DateOnly(2026, 5, 4), new DateOnly(2026, 6, 30), true, 30),
            actorUserId,
            CancellationToken.None);
        Assert.Equal(otherIncome.Id, created.Value!.IncomeTypeId);
        var activeCampaigns = await service.GetFeeCampaignsAsync("gate", CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateFeeCampaignAsync(
            created.Value!.Id,
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, "Gate replacement and wiring", 600m, 34000m, new DateOnly(2026, 5, 4), new DateOnly(2026, 7, 1), true, 45),
            actorUserId,
            CancellationToken.None);
        var noOp = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, "Gate replacement and wiring", 600m, 34000m, new DateOnly(2026, 5, 4), new DateOnly(2026, 7, 1), true, 45),
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
        Assert.Single(activeCampaigns);
        Assert.True(updated.Succeeded);
        Assert.Equal(600m, updated.Value!.ContributionAmount);
        Assert.Equal(34000m, updated.Value.TargetAmount);
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
    public async Task FeeCampaignAsync_RejectsInvalidInputAndDuplicateRestore()
    {
        await using var database = await TestDatabase.CreateAsync();
        var otherIncome = await AddOtherIncomeDestinationAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var actorUserId = Guid.NewGuid();
        var incomeType = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Gate fee", "gate_fee"), actorUserId, CancellationToken.None);
        Assert.True(incomeType.Succeeded);

        var emptyName = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest(" ", incomeType.Value!.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var automaticIncomeType = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("No income type", Guid.Empty, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var invalidTarget = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Invalid target", incomeType.Value.Id, null, 500m, 0m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var invalidPeriod = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Invalid period", incomeType.Value.Id, null, 500m, 33500m, new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30), true, 30),
            actorUserId,
            CancellationToken.None);
        var archived = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, true, 30),
            actorUserId,
            CancellationToken.None);
        await service.ArchiveFeeCampaignAsync(archived.Value!.Id, "Finished", actorUserId, CancellationToken.None);
        var activeDuplicate = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, null, 700m, 40000m, new DateOnly(2026, 8, 1), null, true, 30),
            actorUserId,
            CancellationToken.None);
        var duplicateRestore = await service.RestoreFeeCampaignAsync(archived.Value.Id, actorUserId, CancellationToken.None);

        Assert.False(emptyName.Succeeded);
        Assert.Equal("fee_campaign_name_required", emptyName.ErrorCode);
        Assert.True(automaticIncomeType.Succeeded);
        Assert.Equal(otherIncome.Id, automaticIncomeType.Value!.IncomeTypeId);
        Assert.False(invalidTarget.Succeeded);
        Assert.Equal("fee_campaign_target_amount_invalid", invalidTarget.ErrorCode);
        Assert.False(invalidPeriod.Succeeded);
        Assert.Equal("fee_campaign_period_invalid", invalidPeriod.ErrorCode);
        Assert.True(activeDuplicate.Succeeded);
        Assert.False(duplicateRestore.Succeeded);
        Assert.Equal("fee_campaign_duplicate", duplicateRestore.ErrorCode);
    }

    [Fact]
    public async Task FeeCampaignAsync_SavesSelectedParticipantGaragesAndWritesDiff()
    {
        await using var database = await TestDatabase.CreateAsync();
        await AddOtherIncomeDestinationAsync(database.Context);
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
            new UpsertFeeCampaignRequest("Invalid participants", incomeType.Value!.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [archivedGarage.Id]),
            actorUserId,
            CancellationToken.None);
        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [garage2.Id, garage1.Id]),
            actorUserId,
            CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateFeeCampaignAsync(
            created.Value!.Id,
            new UpsertFeeCampaignRequest("Gate campaign", incomeType.Value.Id, null, 500m, 33500m, new DateOnly(2026, 5, 4), null, false, 30, [garage2.Id]),
            actorUserId,
            CancellationToken.None);
        var loaded = await service.GetFeeCampaignsAsync("Gate", CancellationToken.None);

        Assert.False(invalidArchived.Succeeded);
        Assert.Equal("fee_campaign_participant_garage_not_found", invalidArchived.ErrorCode);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.False(created.Value.AppliesToAllGarages);
        Assert.Equal([garage1.Id, garage2.Id], created.Value.ParticipantGarageIds);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.Equal([garage2.Id], updated.Value!.ParticipantGarageIds);
        Assert.Equal([garage2.Id], Assert.Single(loaded).ParticipantGarageIds);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "dictionary.fee_campaign_updated" && item.ActorUserId == actorUserId);
        Assert.Contains("participantGarageIds", audit.MetadataJson, StringComparison.Ordinal);
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
        database.Context.AddRange(owner, firstGarage, secondGarage);
        await database.Context.SaveChangesAsync();

        var created = await service.CreateFeeCampaignAsync(
            new UpsertFeeCampaignRequest("Сбор на ворота", otherIncome.Id, null, 500m, 5000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);

        database.Context.Accruals.Add(new Accrual
        {
            GarageId = firstGarage.Id,
            IncomeTypeId = otherIncome.Id,
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
            new UpsertFeeCampaignRequest("Сбор на ворота", otherIncome.Id, null, 500m, 5000m, new DateOnly(2026, 5, 1), null, false, 30, [secondGarage.Id]),
            null,
            CancellationToken.None);
        var detailsChange = await service.UpdateFeeCampaignAsync(
            created.Value.Id,
            new UpsertFeeCampaignRequest("Сбор на ворота", otherIncome.Id, "Уточненная цель", 500m, 6000m, new DateOnly(2026, 5, 1), null, false, 30, [firstGarage.Id]),
            null,
            CancellationToken.None);

        Assert.False(participantChange.Succeeded);
        Assert.Equal("fee_campaign_participants_locked", participantChange.ErrorCode);
        Assert.Contains("Исторический состав", participantChange.ErrorMessage, StringComparison.Ordinal);
        Assert.True(detailsChange.Succeeded, detailsChange.ErrorMessage);
        Assert.Equal("Уточненная цель", detailsChange.Value!.Goal);
        Assert.Equal(6000m, detailsChange.Value.TargetAmount);
        Assert.Equal([firstGarage.Id], detailsChange.Value.ParticipantGarageIds);
    }

    [Fact]
    public async Task CreateFeeCampaignAsync_RejectsMissingOtherIncomeDestination()
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
        Assert.Equal("other_income_destination_not_configured", result.ErrorCode);
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
    public async Task SupplierPage_LoadsDebtsInFourSelectsRegardlessOfPageSize()
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

        Assert.Equal(4, commandCounter.Count);
        Assert.Equal(200, result.TotalCount);
        Assert.Equal(25, result.Items.Count);
        Assert.All(result.Items, supplier => Assert.Equal(130m, supplier.Debt));
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
