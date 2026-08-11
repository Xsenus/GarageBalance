using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlChargeServiceExpenseTypeMigrationIntegrationTests
{
    private const string PreviousMigration = "20260811151027_AddServiceMeterKinds";

    [PostgreSqlFact]
    public async Task Migration_MovesServiceExpenseSettingsToSupplierAndProtectsReferences()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid serviceId;
        Guid expenseTypeId;
        Guid expenseFundId;

        await using (var initialContext = database.CreateContext())
        {
            var source = await initialContext.Suppliers
                .AsNoTracking()
                .Where(supplier =>
                    supplier.ChargeServiceSettingId != null &&
                    supplier.ExpenseTypeId != null &&
                    supplier.ExpenseFundId != null)
                .Select(supplier => new
                {
                    supplier.Id,
                    ServiceId = supplier.ChargeServiceSettingId!.Value,
                    ExpenseTypeId = supplier.ExpenseTypeId!.Value,
                    ExpenseFundId = supplier.ExpenseFundId!.Value
                })
                .FirstAsync();
            supplierId = source.Id;
            serviceId = source.ServiceId;
            expenseTypeId = source.ExpenseTypeId;
            expenseFundId = source.ExpenseFundId;

            await initialContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await initialContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE charge_service_settings
                SET "ExpenseTypeId" = {expenseTypeId}, "ExpenseFundId" = {expenseFundId}
                WHERE "Id" = {serviceId};

                UPDATE suppliers
                SET "ExpenseFundId" = NULL
                WHERE "Id" = {supplierId};
                """);
            await initialContext.Database.MigrateAsync();
        }

        await using var context = database.CreateContext();

        var migratedSupplier = await context.Suppliers
            .AsNoTracking()
            .Include(supplier => supplier.ChargeServiceSetting)
            .Include(supplier => supplier.ExpenseType)
            .Include(supplier => supplier.ExpenseFund)
            .SingleAsync(supplier => supplier.Id == supplierId);
        Assert.Equal(serviceId, migratedSupplier.ChargeServiceSettingId);
        Assert.Equal(expenseTypeId, migratedSupplier.ExpenseTypeId);
        Assert.Equal(expenseFundId, migratedSupplier.ExpenseFundId);
        Assert.NotNull(migratedSupplier.ChargeServiceSetting);
        Assert.NotNull(migratedSupplier.ExpenseType);
        Assert.NotNull(migratedSupplier.ExpenseFund);

        var referencedExpenseType = await context.ExpenseTypes
            .SingleAsync(item => item.Id == expenseTypeId);
        context.ExpenseTypes.Remove(referencedExpenseType);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
