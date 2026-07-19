using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierPrimaryContactIntegrationTests
{
    [PostgreSqlFact]
    public async Task SupplierPage_ProjectsOneRankedContactPerSupplierInPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Коммунальные услуги" };
        var suppliers = Enumerable.Range(1, 3)
            .Select(index => new Supplier
            {
                Name = $"Поставщик {index}",
                Group = group
            })
            .ToArray();

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(group);
            setupContext.AddRange(suppliers);
            foreach (var supplier in suppliers)
            {
                setupContext.SupplierContacts.Add(new SupplierContact
                {
                    Supplier = supplier,
                    FullName = "А Архивный",
                    Status = "Работает",
                    IsArchived = true
                });
                setupContext.SupplierContacts.Add(new SupplierContact
                {
                    Supplier = supplier,
                    FullName = "А Бывший",
                    Status = "Не работает"
                });
                setupContext.SupplierContacts.Add(new SupplierContact
                {
                    Supplier = supplier,
                    FullName = $"Б Основной {supplier.Name}",
                    Phone = "+7 900 000-00-00",
                    Email = "primary@example.test",
                    Status = "Работает"
                });
                for (var index = 0; index < 40; index++)
                {
                    setupContext.SupplierContacts.Add(new SupplierContact
                    {
                        Supplier = supplier,
                        FullName = $"Я Дополнительный {index:D2}",
                        Status = "Работает"
                    });
                }
            }

            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierRepository(queryContext);

        var page = await repository.GetPageAsync(
            null,
            null,
            false,
            0,
            10,
            "name",
            false,
            CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
        Assert.All(page.Items, item =>
        {
            Assert.NotNull(item.PrimaryContact);
            Assert.StartsWith("Б Основной", item.PrimaryContact.FullName, StringComparison.Ordinal);
            Assert.Equal("+7 900 000-00-00", item.PrimaryContact.Phone);
            Assert.Equal("primary@example.test", item.PrimaryContact.Email);
        });

        var contactQuery = Assert.Single(
            capture.Commands,
            command => command.Contains("supplier_contacts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY", contactQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("row <= 1", contactQuery, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
