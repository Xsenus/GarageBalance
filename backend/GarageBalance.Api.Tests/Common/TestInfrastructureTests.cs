using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Common;

public sealed class TestInfrastructureTests
{
    [Fact]
    public async Task SqliteTestDatabase_CreatesSchemaAndPersistsBuiltEntities()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var builder = new AccountingTestDataBuilder();
        var owner = builder.BuildOwner(lastName: "Ivanov", firstName: "Ivan");
        var garage = builder.BuildGarage(owner, number: "42", startingBalance: 125.50m);
        var group = builder.BuildSupplierGroup("Utilities");
        var supplier = builder.BuildSupplier(group, "Water supplier", 300m);
        var incomeType = builder.BuildIncomeType("Membership", "membership", isSystem: true);
        var expenseType = builder.BuildExpenseType("Water", "water", isSystem: true);

        database.Context.AddRange(owner, garage, group, supplier, incomeType, expenseType);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var storedGarage = await database.Context.Set<Garage>()
            .Include(item => item.Owner)
            .SingleAsync();
        var storedSupplier = await database.Context.Set<Supplier>()
            .Include(item => item.Group)
            .SingleAsync();

        Assert.Equal("42", storedGarage.Number);
        Assert.Equal("Ivanov Ivan", storedGarage.Owner!.FullName);
        Assert.Equal(125.50m, storedGarage.StartingBalance);
        Assert.Equal("Utilities", storedSupplier.Group.Name);
        Assert.Equal(300m, storedSupplier.StartingBalance);
        Assert.True(await database.Context.Set<IncomeType>().AnyAsync(item => item.Code == "membership" && item.IsSystem));
        Assert.True(await database.Context.Set<ExpenseType>().AnyAsync(item => item.Code == "water" && item.IsSystem));
    }

    [Fact]
    public void AccountingTestDataBuilder_UsesUniqueDefaultsAndRespectsOverrides()
    {
        var builder = new AccountingTestDataBuilder();

        var firstOwner = builder.BuildOwner();
        var secondOwner = builder.BuildOwner(isArchived: true);
        var garage = builder.BuildGarage(firstOwner, number: "A-7", peopleCount: 3, floorCount: 2);
        var supplier = builder.BuildSupplier(name: "Custom supplier", startingBalance: -25m);

        Assert.NotEqual(firstOwner.LastName, secondOwner.LastName);
        Assert.True(secondOwner.IsArchived);
        Assert.Equal(firstOwner.Id, garage.OwnerId);
        Assert.Equal("A-7", garage.Number);
        Assert.Equal(3, garage.PeopleCount);
        Assert.Equal(2, garage.FloorCount);
        Assert.Equal("Custom supplier", supplier.Name);
        Assert.Equal(-25m, supplier.StartingBalance);
        Assert.NotEqual(Guid.Empty, supplier.GroupId);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), supplier.CreatedAtUtc);
    }

    [Fact]
    public void SqliteTestDatabase_CreateSupportsSynchronousTests()
    {
        using var database = SqliteTestDatabase.Create();

        Assert.True(database.Context.Database.CanConnect());
        Assert.NotEmpty(database.Context.Model.GetEntityTypes());
    }
}
