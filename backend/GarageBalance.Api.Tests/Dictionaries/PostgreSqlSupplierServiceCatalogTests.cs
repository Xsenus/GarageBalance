using System.Data.Common;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierServiceCatalogTests
{
    [PostgreSqlFact]
    public async Task Search_UsesBoundedSqlAndTreatsCyrillicAndWildcardCharactersCorrectly()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commands = new QueryRecorder();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(commands).Options);
        context.SupplierServices.AddRange(Enumerable.Range(0, 12).Select(index => new SupplierService { Name = $"Вывоз {index:D2}" }));
        context.SupplierServices.AddRange(new SupplierService { Name = "Вывоз %_ 99" }, new SupplierService { Name = "Вывоз архив", IsArchived = true });
        await context.SaveChangesAsync();
        commands.Queries.Clear();
        var catalog = CreateCatalog(context);
        var page = await catalog.GetPageAsync(" ВЫВОЗ ", 2, 3, false, CancellationToken.None);
        Assert.Equal(13, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.All(page.Items, item => Assert.False(item.IsArchived));
        Assert.Equal(2, commands.Queries.Count);
        Assert.Contains(commands.Queries, sql => sql.Contains("LIMIT", StringComparison.Ordinal) && sql.Contains("OFFSET", StringComparison.Ordinal));
        Assert.Equal("Вывоз %_ 99", Assert.Single((await catalog.GetPageAsync("%_", 0, 25, false, CancellationToken.None)).Items).Name);
        var pastEnd = await catalog.GetPageAsync("вывоз", 100, 25, true, CancellationToken.None);
        Assert.Equal(14, pastEnd.TotalCount);
        Assert.Empty(pastEnd.Items);
    }

    [PostgreSqlFact]
    public async Task ConcurrentCreate_ProducesOneServiceAndOneAuditWithADomainConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var results = await Task.WhenAll(
            CreateCatalog(first).CreateAsync(new("Одновременное создание"), null, CancellationToken.None),
            CreateCatalog(second).CreateAsync(new("Одновременное создание"), null, CancellationToken.None));
        Assert.Single(results, result => result.Succeeded);
        Assert.Equal("supplier_service_duplicate", Assert.Single(results, result => !result.Succeeded).ErrorCode);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.SupplierServices.Where(item => item.Name == "Одновременное создание").ToListAsync());
        Assert.Single(await verification.AuditEvents.Where(item => item.Action == "dictionary.supplier_service_created").ToListAsync());
    }

    private static SupplierServiceCatalog CreateCatalog(GarageBalanceDbContext context) =>
        new(new EfSupplierServiceRepository(context), new EfFundRepository(context), new EfApplicationUnitOfWork(context), new AuditEventWriter(context));

    private sealed class QueryRecorder : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
