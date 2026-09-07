using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class SupplierServiceAssignmentTests
{
    [PostgreSqlFact]
    public async Task RenameAndEdit_PreserveGroupMoneyAndHistoricalLinksAcrossFreshContexts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid serviceId;
        Guid groupId;
        Guid expenseId;
        Guid fundId;
        Guid ownerServiceId;
        await using (var context = database.CreateContext())
        {
            var group = new SupplierGroup { Name = "Существующая классификация" };
            var ownerService = new ChargeServiceSetting { Name = "Тариф владельцев" };
            var independent = new SupplierService { Name = "Уборка" };
            var expense = new ExpenseType { Name = "Расход по старой категории" };
            var fund = new Fund { Name = "Расходы", NormalizedName = "РАСХОДЫ", Balance = 1000m };
            var supplier = new Supplier
            {
                Name = "Подрядчик",
                Group = group,
                SupplierService = independent,
                ChargeServiceSetting = ownerService,
                ExpenseType = expense,
                ExpenseFund = fund,
                StartingBalance = 125m,
                StartingDebt = 125m
            };
            context.Add(supplier);
            await context.SaveChangesAsync();
            supplierId = supplier.Id; serviceId = independent.Id; groupId = group.Id;
            expenseId = expense.Id; fundId = fund.Id; ownerServiceId = ownerService.Id;
        }

        await using (var context = database.CreateContext())
        {
            var catalog = DictionaryServiceTestFactory.CreateSupplierCatalog(context);
            var current = await context.SupplierServices.SingleAsync(item => item.Id == serviceId);
            var renamed = await catalog.UpdateAsync(serviceId, new("Уборка территории", current.Version), null, CancellationToken.None);
            Assert.True(renamed.Succeeded);
        }

        await using (var context = database.CreateContext())
        {
            var service = DictionaryServiceTestFactory.Create(context);
            var current = Assert.Single(await service.GetSuppliersAsync(groupId, "Уборка территории", CancellationToken.None));
            Assert.Equal(serviceId, current.SupplierServiceId);
            Assert.Equal(125m, current.Debt);
            var request = new UpsertSupplierRequest(current.Name, groupId, null, null, null, null, null, 125m, "Изменён комментарий",
                serviceId, current.Version, ExpenseFundId: fundId);
            var updated = await service.UpdateSupplierAsync(supplierId, request, null, CancellationToken.None);
            Assert.True(updated.Succeeded, updated.ErrorMessage);
            Assert.Equal(expenseId, updated.Value!.ExpenseTypeId);
            Assert.Equal(groupId, updated.Value.GroupId);
            await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => service.UpdateSupplierAsync(supplierId, request, null, CancellationToken.None));
        }

        await using var verification = database.CreateContext();
        var stored = await verification.Suppliers.SingleAsync(item => item.Id == supplierId);
        Assert.Equal(ownerServiceId, stored.ChargeServiceSettingId);
        Assert.Equal("Тариф владельцев", (await verification.ChargeServiceSettings.SingleAsync(item => item.Id == ownerServiceId)).Name);
        Assert.Equal(125m, stored.StartingBalance);
        Assert.Equal(125m, stored.StartingDebt);
        Assert.Equal(1000m, (await verification.Funds.SingleAsync(item => item.Id == fundId)).Balance);
        Assert.Equal("Расход по старой категории", (await verification.ExpenseTypes.SingleAsync(item => item.Id == expenseId)).Name);
        Assert.Contains(await verification.AuditEvents.ToListAsync(), item => item.Action == "dictionary.supplier_updated");
    }

    [Fact]
    public async Task OwnerTariffCannotBeAssignedAsSupplierService()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Поставщики" };
        var tariffService = new ChargeServiceSetting { Name = "Только владельцы" };
        database.Context.AddRange(group, tariffService);
        await database.Context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(database.Context).CreateSupplierAsync(
            new("Новый", group.Id, null, null, null, null, null, 0m, null, tariffService.Id), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("supplier_service_not_found", result.ErrorCode);
        Assert.Empty(await database.Context.Suppliers.ToListAsync());
        Assert.Empty(await database.Context.ExpenseTypes.ToListAsync());
        Assert.Empty(await database.Context.AuditEvents.ToListAsync());
    }
}
