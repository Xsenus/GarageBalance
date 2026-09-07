using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class SupplierServiceCatalogTests
{
    [Fact]
    public async Task CreateAndRename_OnlyChangeIndependentNameAndWriteAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        context.ChargeServiceSettings.Add(new ChargeServiceSetting { Name = "Уборка" });
        await context.SaveChangesAsync();
        var catalog = CreateCatalog(context);
        var created = await catalog.CreateAsync(new(" Уборка "), null, CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.Equal("Уборка", created.Value!.Name);
        Assert.False(created.Value.IsArchived);
        Assert.NotEqual(Guid.Empty, created.Value.Version);
        var unchanged = await catalog.UpdateAsync(created.Value.Id, new("Уборка", created.Value.Version), null, CancellationToken.None);
        Assert.Equal(created.Value, unchanged.Value);
        Assert.Single(await context.AuditEvents.ToListAsync());
        var updated = await catalog.UpdateAsync(created.Value.Id, new("Уборка территории", created.Value.Version), null, CancellationToken.None);
        Assert.True(updated.Succeeded);
        Assert.NotEqual(created.Value.Version, updated.Value!.Version);
        Assert.Equal("Уборка", (await context.ChargeServiceSettings.SingleAsync()).Name);
        Assert.Empty(await context.Tariffs.ToListAsync());
        var audit = (await context.AuditEvents.ToListAsync()).OrderBy(item => item.CreatedAtUtc).ToArray();
        Assert.Equal(2, audit.Length);
        Assert.Equal("dictionary.supplier_service_created", audit[0].Action);
        Assert.Equal("dictionary.supplier_service_updated", audit[1].Action);
        Assert.Contains("Уборка территории", audit[1].Summary);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => catalog.UpdateAsync(created.Value.Id, new("Старая версия", created.Value.Version), null, CancellationToken.None));
        Assert.Equal("Уборка территории", (await context.SupplierServices.SingleAsync()).Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvalidNames_AreRejectedBeforeCreatingOrChangingRecords(int length)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var catalog = CreateCatalog(database.Context);
        var request = new UpsertSupplierServiceRequest(length < 0 ? null! : length == 0 ? "  " : new string('Я', length));
        Assert.Equal("supplier_service_name_invalid", (await catalog.CreateAsync(request, null, CancellationToken.None)).ErrorCode);
        Assert.Equal("supplier_service_name_invalid", (await catalog.UpdateAsync(Guid.NewGuid(), request, null, CancellationToken.None)).ErrorCode);
        Assert.Empty(await database.Context.SupplierServices.ToListAsync());
        Assert.Empty(await database.Context.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task DuplicateMissingAndArchivedRecords_ReturnDomainErrorsWithoutMutation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var catalog = CreateCatalog(database.Context);
        var first = (await catalog.CreateAsync(new("Первое"), null, CancellationToken.None)).Value!;
        var second = (await catalog.CreateAsync(new("Второе"), null, CancellationToken.None)).Value!;
        Assert.Equal("supplier_service_duplicate", (await catalog.CreateAsync(new(" Первое "), null, CancellationToken.None)).ErrorCode);
        Assert.Equal("supplier_service_duplicate", (await catalog.UpdateAsync(second.Id, new("Первое", second.Version), null, CancellationToken.None)).ErrorCode);
        Assert.Equal("supplier_service_not_found", (await catalog.UpdateAsync(Guid.NewGuid(), new("Не найдено"), null, CancellationToken.None)).ErrorCode);
        var archived = await database.Context.SupplierServices.SingleAsync(item => item.Id == first.Id);
        archived.IsArchived = true;
        await database.Context.SaveChangesAsync();
        Assert.Equal("supplier_service_not_found", (await catalog.UpdateAsync(first.Id, new("Архив"), null, CancellationToken.None)).ErrorCode);
        Assert.True((await catalog.CreateAsync(new("Первое"), null, CancellationToken.None)).Succeeded);
        Assert.Equal("Второе", (await database.Context.SupplierServices.SingleAsync(item => item.Id == second.Id)).Name);
        Assert.Equal(3, await database.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task SearchAndPaging_AreBoundedAndSupportEmptyAndArchivedResults()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        database.Context.SupplierServices.AddRange(Enumerable.Range(0, 30).Select(index => new SupplierService { Name = $"Service {index:D2}", IsArchived = index == 29 }));
        await database.Context.SaveChangesAsync();
        var catalog = CreateCatalog(database.Context);
        var first = await catalog.GetPageAsync("  ", -5, 0, false, CancellationToken.None);
        Assert.Equal(29, first.TotalCount);
        Assert.Equal(0, first.Offset);
        Assert.Equal(25, first.Limit);
        Assert.Equal(25, first.Items.Count);
        var second = await catalog.GetPageAsync(" SERVICE ", 25, 5000, true, CancellationToken.None);
        Assert.Equal(30, second.TotalCount);
        Assert.Equal(500, second.Limit);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal("Service 25", second.Items[0].Name);
        Assert.True(second.Items[^1].IsArchived);
        Assert.Empty((await catalog.GetPageAsync("absent", 0, 25, false, CancellationToken.None)).Items);
        var pastEnd = await catalog.GetPageAsync(null, 100, 25, false, CancellationToken.None);
        Assert.Empty(pastEnd.Items);
        Assert.Equal(29, pastEnd.TotalCount);
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutSavedData()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var catalog = CreateCatalog(database.Context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetPageAsync(null, 0, 25, false, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.CreateAsync(new("Отменённая"), null, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.UpdateAsync(Guid.NewGuid(), new("Отменённая"), null, cancellation.Token));
        Assert.Empty(await database.Context.SupplierServices.ToListAsync());
    }

    [Fact]
    public async Task SaveFailure_DoesNotReportSuccessOrPersistAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var catalog = new SupplierServiceCatalog(new EfSupplierServiceRepository(database.Context), new EfFundRepository(database.Context), new FailingUnitOfWork(), new AuditEventWriter(database.Context));
        await Assert.ThrowsAsync<IOException>(() => catalog.CreateAsync(new("Несохранённая"), null, CancellationToken.None));
        Assert.Empty(await database.Context.SupplierServices.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RenameSaveFailure_DoesNotPersistTheNewNameOrAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var created = (await CreateCatalog(database.Context).CreateAsync(new("Сохранённая услуга"), null, CancellationToken.None)).Value!;
        var failing = new SupplierServiceCatalog(new EfSupplierServiceRepository(database.Context), new EfFundRepository(database.Context), new FailingUnitOfWork(), new AuditEventWriter(database.Context));
        await Assert.ThrowsAsync<IOException>(() => failing.UpdateAsync(created.Id, new("Несохранённое имя", created.Version), null, CancellationToken.None));
        Assert.Equal(created.Name, (await database.Context.SupplierServices.AsNoTracking().SingleAsync()).Name);
        Assert.Single(await database.Context.AuditEvents.AsNoTracking().ToListAsync());
    }

    private static SupplierServiceCatalog CreateCatalog(GarageBalanceDbContext context) =>
        new(new EfSupplierServiceRepository(context), new EfFundRepository(context), new EfApplicationUnitOfWork(context), new AuditEventWriter(context));

    private sealed class FailingUnitOfWork : IApplicationUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new IOException("Test persistence failure");
    }
}
