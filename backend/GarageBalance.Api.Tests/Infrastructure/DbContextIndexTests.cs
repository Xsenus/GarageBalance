using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Infrastructure;

public sealed class DbContextIndexTests
{
    [Theory]
    [InlineData(typeof(Garage), nameof(Garage.Number))]
    [InlineData(typeof(Garage), nameof(Garage.OwnerId))]
    [InlineData(typeof(Owner), nameof(Owner.LastName), nameof(Owner.FirstName), nameof(Owner.MiddleName))]
    [InlineData(typeof(Owner), nameof(Owner.Phone))]
    [InlineData(typeof(Supplier), nameof(Supplier.Name))]
    [InlineData(typeof(Supplier), nameof(Supplier.GroupId))]
    [InlineData(typeof(Supplier), nameof(Supplier.Inn))]
    [InlineData(typeof(Supplier), nameof(Supplier.ContactPerson))]
    public void SearchFields_HaveEfIndexes(Type entityType, params string[] propertyNames)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Contains(
            entity!.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    [Theory]
    [InlineData(typeof(Garage), "\"IsArchived\" = false", nameof(Garage.Number))]
    [InlineData(typeof(SupplierGroup), "\"IsArchived\" = false", nameof(SupplierGroup.Name))]
    [InlineData(typeof(IncomeType), "\"IsArchived\" = false", nameof(IncomeType.Name))]
    [InlineData(typeof(ExpenseType), "\"IsArchived\" = false", nameof(ExpenseType.Name))]
    [InlineData(typeof(Tariff), "\"IsArchived\" = false", nameof(Tariff.Name), nameof(Tariff.EffectiveFrom))]
    public void ActiveDictionaryEntries_HavePartialUniqueIndexes(Type entityType, string filter, params string[] propertyNames)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Contains(
            entity!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() == filter &&
                index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    [Theory]
    [InlineData(typeof(FinancialOperation), "\"IsCanceled\" = false AND \"DocumentNumber\" IS NOT NULL", nameof(FinancialOperation.OperationKind), nameof(FinancialOperation.OperationDate), nameof(FinancialOperation.DocumentNumber))]
    [InlineData(typeof(Accrual), "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL", nameof(Accrual.GarageId), nameof(Accrual.IncomeTypeId), nameof(Accrual.AccountingMonth), nameof(Accrual.Source))]
    [InlineData(typeof(Accrual), "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NOT NULL", nameof(Accrual.GarageId), nameof(Accrual.IrregularPaymentId), nameof(Accrual.AccountingMonth))]
    [InlineData(typeof(SupplierAccrual), "\"IsCanceled\" = false", nameof(SupplierAccrual.SupplierId), nameof(SupplierAccrual.ExpenseTypeId), nameof(SupplierAccrual.AccountingMonth), nameof(SupplierAccrual.Source), nameof(SupplierAccrual.DocumentNumber))]
    [InlineData(typeof(MeterReading), "\"IsCanceled\" = false", nameof(MeterReading.GarageId), nameof(MeterReading.MeterKind), nameof(MeterReading.AccountingMonth))]
    public void ActiveFinanceEntries_HavePartialUniqueIndexes(Type entityType, string filter, params string[] propertyNames)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Contains(
            entity!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() == filter &&
                index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static GarageBalanceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new GarageBalanceDbContext(options);
    }
}
