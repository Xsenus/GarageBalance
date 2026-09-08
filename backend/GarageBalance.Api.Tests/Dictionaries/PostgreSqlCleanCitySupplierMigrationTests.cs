using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlCleanCitySupplierMigrationTests
{
    private const string PreviousMigration = "20260908053253_ReconcileGelendzhikTariffCatalog";

    [PostgreSqlFact]
    public async Task Migration_ReconcilesCleanCityServiceAndExpenseTypeWithoutDeletingHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid trashExpenseTypeId;

        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var trashService = await context.SupplierServices.FirstOrDefaultAsync(item =>
                !item.IsArchived && (EF.Functions.ILike(item.Name, "%мусор%") || EF.Functions.ILike(item.Name, "%отход%")))
                ?? new SupplierService { Name = "Вывоз мусора — тест миграции" };
            var waterService = await context.SupplierServices.FirstOrDefaultAsync(item =>
                !item.IsArchived && EF.Functions.ILike(item.Name, "%вод%"))
                ?? new SupplierService { Name = "Водоснабжение — тест миграции" };
            var trashExpenseType = await context.ExpenseTypes.SingleAsync(item => item.Code == "trash_removal");
            var waterExpenseType = await context.ExpenseTypes.SingleAsync(item => item.Code == "water_supply");
            var trashFund = await context.Funds.FirstOrDefaultAsync(item => EF.Functions.ILike(item.Name, "%мусор%"))
                ?? new Fund
                {
                    Name = "Вывоз мусора — тест миграции",
                    NormalizedName = "ВЫВОЗ МУСОРА — ТЕСТ МИГРАЦИИ"
                };
            var group = new SupplierGroup { Name = "Тест миграции Чистого города" };
            var supplier = new Supplier
            {
                Name = "ООО «Чистый город»",
                Group = group,
                SupplierService = waterService,
                ExpenseType = waterExpenseType,
                ExpenseFund = trashFund
            };
            if (context.Entry(trashService).State == EntityState.Detached)
            {
                context.Add(trashService);
            }
            context.Add(supplier);
            await context.SaveChangesAsync();
            supplierId = supplier.Id;
            trashExpenseTypeId = trashExpenseType.Id;

            Assert.Equal(1, await context.Suppliers.CountAsync(item =>
                item.Name.Contains("Чистый город") &&
                item.ExpenseFundId != null &&
                context.Funds.Any(fund => fund.Id == item.ExpenseFundId && fund.Name.Contains("мусор"))));
            Assert.True(await context.SupplierServices.AnyAsync(item =>
                !item.IsArchived && item.Id == trashService.Id));

            await context.Database.MigrateAsync();
            Assert.Contains("20260908094046_ReconcileCleanCitySupplierService", await context.Database.GetAppliedMigrationsAsync());
        }

        await using var verification = database.CreateContext();
        var migrated = await verification.Suppliers.AsNoTracking().SingleAsync(item => item.Id == supplierId);
        var migratedServiceName = await verification.SupplierServices
            .Where(item => item.Id == migrated.SupplierServiceId)
            .Select(item => item.Name)
            .SingleAsync();
        Assert.True(
            migratedServiceName.Contains("мусор", StringComparison.OrdinalIgnoreCase) ||
            migratedServiceName.Contains("отход", StringComparison.OrdinalIgnoreCase),
            $"Unexpected migrated service: {migratedServiceName}; supplier fund: {await verification.Funds.Where(item => item.Id == migrated.ExpenseFundId).Select(item => item.Name).SingleAsync()}");
        Assert.Equal(trashExpenseTypeId, migrated.ExpenseTypeId);
        Assert.Empty(await verification.FinancialOperations.Where(item => item.SupplierId == supplierId).ToListAsync());
        Assert.Contains(await verification.AuditEvents.ToListAsync(), item =>
            item.EntityId == supplierId.ToString() && item.Action == "dictionary.supplier_service_link_reconciled");
    }
}
