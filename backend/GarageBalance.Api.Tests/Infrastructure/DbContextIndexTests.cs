using GarageBalance.Api.Domain.Dictionaries;
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

    private static GarageBalanceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new GarageBalanceDbContext(options);
    }
}
