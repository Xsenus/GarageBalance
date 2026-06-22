using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class DictionaryServiceTests
{
    [Fact]
    public async Task CreateOwnerAsync_TrimsFieldsAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
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
    public async Task GetOwnersAsync_SearchesByNameAndPhone()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, "+7 111", null, null), null, CancellationToken.None);
        await service.CreateOwnerAsync(new UpsertOwnerRequest("Сидоров", "Сергей", null, "+7 222", null, null), null, CancellationToken.None);

        var result = await service.GetOwnersAsync("222", CancellationToken.None);

        var owner = Assert.Single(result);
        Assert.Equal("Сидоров", owner.LastName);
    }

    [Fact]
    public async Task UpdateOwnerAsync_ReturnsNotFoundForMissingOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

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
        var service = new DictionaryService(database.Context);

        var result = await service.CreateGarageAsync(
            new UpsertGarageRequest("A-1", 1, 1, Guid.NewGuid(), 10, 20, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("owner_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task CreateGarageAsync_RejectsDuplicateNumber()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, null, null, null), null, CancellationToken.None);

        var result = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, null, null, null, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateGarageAsync_ChangesOwnerAndKeepsDtoOwnerName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Кузнецов", "Олег", null, null, null, null), null, CancellationToken.None);
        var garageResult = await service.CreateGarageAsync(new UpsertGarageRequest("15", 1, 1, null, null, null, null), null, CancellationToken.None);

        var result = await service.UpdateGarageAsync(
            garageResult.Value!.Id,
            new UpsertGarageRequest("15A", 3, 2, ownerResult.Value!.Id, 1.5m, 9.75m, "угловой"),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("15A", result.Value!.Number);
        Assert.Equal("Кузнецов Олег", result.Value.OwnerName);
        Assert.Equal(3, result.Value.PeopleCount);
    }

    [Fact]
    public async Task CreateSupplierGroupAsync_RejectsDuplicateName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        var result = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);

        var result = await service.CreateSupplierAsync(
            new UpsertSupplierRequest("Водоканал", Guid.NewGuid(), "5400000000", null, null, null, null, 0, null),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("supplier_group_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task GetSuppliersAsync_FiltersByGroupAndSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var utilityGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Коммунальные услуги"), null, CancellationToken.None);
        var bankGroup = await service.CreateSupplierGroupAsync(new UpsertSupplierGroupRequest("Банки"), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Водоканал", utilityGroup.Value!.Id, "5401", null, "Мария", null, null, 100, null), null, CancellationToken.None);
        await service.CreateSupplierAsync(new UpsertSupplierRequest("Альфа-Банк", bankGroup.Value!.Id, "7728", null, "Ольга", null, null, 0, null), null, CancellationToken.None);

        var result = await service.GetSuppliersAsync(utilityGroup.Value.Id, "5401", CancellationToken.None);

        var supplier = Assert.Single(result);
        Assert.Equal("Водоканал", supplier.Name);
        Assert.Equal("Коммунальные услуги", supplier.GroupName);
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

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
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
}
