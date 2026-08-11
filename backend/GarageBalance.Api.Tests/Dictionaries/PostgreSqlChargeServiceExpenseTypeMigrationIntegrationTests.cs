using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
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
            var fund = new Fund
            {
                Name = "Миграционный фонд",
                NormalizedName = "МИГРАЦИОННЫЙ ФОНД",
                IsSystem = false
            };
            var expenseType = new ExpenseType { Name = "Миграционная статья расходов" };
            var group = new SupplierGroup { Name = "Миграционные поставщики" };
            var incomeType = new IncomeType { Name = "Миграционные поступления" };
            var tariff = new Tariff
            {
                Name = "Миграционный тариф",
                CalculationBase = "fixed",
                Rate = 100m,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            };
            var service = new ChargeServiceSetting
            {
                Name = "Миграционная услуга",
                IncomeType = incomeType,
                Tariff = tariff,
                IsRegular = true
            };
            var supplier = new Supplier
            {
                Name = "Миграционный поставщик",
                Group = group,
                ChargeServiceSetting = service,
                ExpenseType = expenseType,
                ExpenseFund = fund
            };
            initialContext.AddRange(fund, expenseType, group, incomeType, tariff, service, supplier);
            await initialContext.SaveChangesAsync();
            supplierId = supplier.Id;
            serviceId = service.Id;
            expenseTypeId = expenseType.Id;
            expenseFundId = fund.Id;

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
