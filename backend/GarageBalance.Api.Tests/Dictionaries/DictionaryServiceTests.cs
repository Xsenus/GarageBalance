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
    public async Task ArchiveOwnerAsync_HidesOwnerFromListAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();
        var ownerResult = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveOwnerAsync(ownerResult.Value!.Id, actorUserId, CancellationToken.None);
        var owners = await service.GetOwnersAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsArchived);
        Assert.Empty(owners);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.owner_archived" && item.ActorUserId == actorUserId);
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
        var service = new DictionaryService(database.Context);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.CreateGarageAsync(new UpsertGarageRequest("12", 2, 1, null, 0, null, null, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("garage_number_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateGarageAsync_ChangesOwnerAndKeepsDtoOwnerName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
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
        var service = new DictionaryService(database.Context);
        var garageResult = await service.CreateGarageAsync(new UpsertGarageRequest("15", 1, 1, null, 0, null, null, null), null, CancellationToken.None);

        var result = await service.ArchiveGarageAsync(garageResult.Value!.Id, null, CancellationToken.None);
        var garages = await service.GetGaragesAsync(null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsArchived);
        Assert.Empty(garages);
    }

    [Fact]
    public async Task GetGaragesAsync_SearchesByNumberAndOwnerName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var firstOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Иванов", "Иван", null, null, null, null), null, CancellationToken.None);
        var secondOwner = await service.CreateOwnerAsync(new UpsertOwnerRequest("Петров", "Петр", null, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("12", 1, 1, firstOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);
        await service.CreateGarageAsync(new UpsertGarageRequest("21", 1, 1, secondOwner.Value!.Id, 0, null, null, null), null, CancellationToken.None);

        var byNumber = await service.GetGaragesAsync("12", CancellationToken.None);
        var byOwner = await service.GetGaragesAsync("петров", CancellationToken.None);

        var garageByNumber = Assert.Single(byNumber);
        Assert.Equal("Иванов Иван", garageByNumber.OwnerName);
        var garageByOwner = Assert.Single(byOwner);
        Assert.Equal("21", garageByOwner.Number);
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

    [Fact]
    public async Task CreateIncomeTypeAsync_RejectsDuplicateName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership"), null, CancellationToken.None);

        var result = await service.CreateIncomeTypeAsync(new UpsertAccountingTypeRequest("Членский взнос", "membership2"), null, CancellationToken.None);

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
        var service = new DictionaryService(database.Context);

        var result = await service.ArchiveIncomeTypeAsync(systemType.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("income_type_system", result.ErrorCode);
        Assert.False(systemType.IsArchived);
    }

    [Fact]
    public async Task CreateExpenseTypeAsync_WritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateExpenseTypeAsync(new UpsertAccountingTypeRequest("Электроэнергия", "electricity"), actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Электроэнергия", result.Value!.Name);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.expense_type_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateTariffAsync_RejectsDuplicateNameAndDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        var effectiveFrom = new DateOnly(2026, 7, 1);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50.25m, effectiveFrom, null), null, CancellationToken.None);

        var result = await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 60m, effectiveFrom, null), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("tariff_duplicate", result.ErrorCode);
    }

    [Fact]
    public async Task GetTariffsAsync_SearchesAndOrdersByEffectiveDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new DictionaryService(database.Context);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 100m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Мусор", "people", 120m, new DateOnly(2026, 2, 1), null), null, CancellationToken.None);
        await service.CreateTariffAsync(new UpsertTariffRequest("Вода", "meter_water", 50m, new DateOnly(2026, 1, 1), null), null, CancellationToken.None);

        var result = await service.GetTariffsAsync("people", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateOnly(2026, 2, 1), result[0].EffectiveFrom);
        Assert.Equal(120m, result[0].Rate);
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
