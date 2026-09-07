using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierServiceMigrationTests
{
    private const string PreviousMigration = "20260904064206_AddFinancialOperationConcurrencyVersion";

    [PostgreSqlFact]
    public async Task Migration_CopiesNamesAndArchivedLinksWithoutChangingGroupsTariffsOrFinancialHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var group = new SupplierGroup { Name = "Контрольная группа поставщиков" };
        var fallbackGroup = new SupplierGroup { Name = "Услуга только из старой группы" };
        var tariff = new Tariff { Name = "Тариф владельцев для проверки разделения", Rate = 123.45m, CalculationBase = "fixed", EffectiveFrom = new DateOnly(2026, 1, 1) };
        var ownerService = new ChargeServiceSetting { Name = "Обслуживание по договору", IsRegular = true, Tariff = tariff };
        var archivedService = new ChargeServiceSetting { Name = "Архивное обслуживание по договору", IsArchived = true };
        var expenseType = new ExpenseType { Name = "Расход по контрольному договору" };
        var fund = new Fund { Name = "Фонд по контрольному договору", NormalizedName = "ФОНД ПО КОНТРОЛЬНОМУ ДОГОВОРУ", Balance = 500m };
        var supplier = new Supplier { Name = "Контрольный поставщик", Group = group, ChargeServiceSetting = ownerService, ExpenseType = expenseType, ExpenseFund = fund, StartingBalance = 45m, StartingDebt = 45m };
        var archivedSupplier = new Supplier { Name = "Архивный поставщик", Group = group, ChargeServiceSetting = archivedService, IsArchived = true };
        var fallbackSupplier = new Supplier { Name = "Поставщик прежнего справочника", Group = fallbackGroup };
        var anotherFallbackSupplier = new Supplier { Name = "Второй поставщик прежнего справочника", Group = fallbackGroup };
        var accrual = new SupplierAccrual { Supplier = supplier, ExpenseType = expenseType, ExpenseFund = fund, AccountingMonth = new DateOnly(2026, 8, 1), Amount = 300m, Source = "manual", DocumentNumber = "TEST-SERVICE-1" };
        var payment = new FinancialOperation { Supplier = supplier, OperationKind = "expense", OperationDate = new DateOnly(2026, 8, 31), AccountingMonth = new DateOnly(2026, 8, 1), Amount = 200m, ExpenseType = expenseType, ExpenseFund = fund };
        context.AddRange(supplier, archivedSupplier, fallbackSupplier, anotherFallbackSupplier, accrual, payment);
        await context.SaveChangesAsync();
        var supplierVersion = supplier.Version;
        var tariffCount = await context.Tariffs.CountAsync();
        var serviceCount = await context.ChargeServiceSettings.CountAsync();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();

        var migrated = await context.Suppliers.Include(item => item.SupplierService).SingleAsync(item => item.Id == supplier.Id);
        Assert.Equal(ownerService.Id, migrated.SupplierServiceId);
        Assert.Equal(ownerService.Name, migrated.SupplierService!.Name);
        Assert.Equal(ownerService.Id, migrated.ChargeServiceSettingId);
        Assert.Equal(group.Id, migrated.GroupId);
        Assert.Equal(supplierVersion, migrated.Version);
        Assert.Equal(45m, migrated.StartingBalance);
        Assert.Equal(45m, migrated.StartingDebt);
        Assert.Equal(expenseType.Id, migrated.ExpenseTypeId);
        Assert.Equal(fund.Id, migrated.ExpenseFundId);
        Assert.Equal(300m, (await context.SupplierAccruals.SingleAsync(item => item.Id == accrual.Id)).Amount);
        Assert.Equal(200m, (await context.FinancialOperations.SingleAsync(item => item.Id == payment.Id)).Amount);
        Assert.Equal(500m, (await context.Funds.SingleAsync(item => item.Id == fund.Id)).Balance);
        Assert.Equal(tariffCount, await context.Tariffs.CountAsync());
        Assert.Equal(serviceCount, await context.ChargeServiceSettings.CountAsync());
        Assert.Equal(123.45m, (await context.Tariffs.SingleAsync(item => item.Id == tariff.Id)).Rate);
        Assert.True((await context.SupplierServices.SingleAsync(item => item.Id == archivedService.Id)).IsArchived);
        Assert.Equal(archivedService.Id, (await context.Suppliers.SingleAsync(item => item.Id == archivedSupplier.Id)).SupplierServiceId);
        var fallback = await context.Suppliers.Include(item => item.SupplierService).SingleAsync(item => item.Id == fallbackSupplier.Id);
        Assert.Equal(fallbackGroup.Name, fallback.SupplierService!.Name);
        Assert.Equal(fallbackGroup.Id, fallback.GroupId);
        Assert.Equal(fallback.SupplierServiceId, (await context.Suppliers.SingleAsync(item => item.Id == anotherFallbackSupplier.Id)).SupplierServiceId);
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "dictionary.supplier_services_prepared").ToListAsync());

        migrated.SupplierService.Name = "Независимое новое наименование";
        await context.SaveChangesAsync();
        Assert.Equal(ownerService.Name, (await context.ChargeServiceSettings.SingleAsync(item => item.Id == ownerService.Id)).Name);
        var independentCount = await context.SupplierServices.CountAsync();
        await context.Database.MigrateAsync();
        Assert.Equal(independentCount, await context.SupplierServices.CountAsync());
        Assert.Equal("Независимое новое наименование", (await context.SupplierServices.SingleAsync(item => item.Id == ownerService.Id)).Name);
    }

    [PostgreSqlFact]
    public async Task Catalog_EnforcesUniqueActiveNamesAndAllowsArchivedNameReuse()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var service = new SupplierService { Name = "Контроль уникальности" };
        context.Add(service);
        await context.SaveChangesAsync();
        Assert.NotEqual(Guid.Empty, service.Version);
        context.Add(new SupplierService { Name = service.Name });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Add(new SupplierService { Name = service.Name, IsArchived = true });
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.SupplierServices.CountAsync(item => item.Name == service.Name));
        context.Add(new SupplierService { Name = new string('Я', 201) });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task Catalog_RejectsStaleRenameWithoutChangingTheSavedName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var service = new SupplierService { Name = "Исходное название" };
        first.Add(service);
        await first.SaveChangesAsync();
        await using var second = database.CreateContext();
        var stale = await second.SupplierServices.SingleAsync(item => item.Id == service.Id);
        var originalVersion = service.Version;
        service.Name = "Сохранённое название";
        await first.SaveChangesAsync();
        Assert.NotEqual(originalVersion, service.Version);
        stale.Name = "Устаревшее название";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verification = database.CreateContext();
        Assert.Equal(service.Name, (await verification.SupplierServices.SingleAsync(item => item.Id == service.Id)).Name);
    }

    [PostgreSqlFact]
    public async Task Catalog_ProtectsReferencedServicesAndAllowsSuppliersWithoutAService()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var group = new SupplierGroup { Name = "Проверка ссылок услуги" };
        var service = new SupplierService { Name = "Ссылочная услуга" };
        var supplier = new Supplier { Name = "Поставщик независимой услуги", Group = group, SupplierService = service };
        context.AddRange(supplier, new Supplier { Name = "Поставщик без услуги", Group = group });
        await context.SaveChangesAsync();
        Assert.Null(supplier.ChargeServiceSettingId);
        context.ChangeTracker.Clear();
        context.SupplierServices.Remove(await context.SupplierServices.SingleAsync(item => item.Id == service.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
