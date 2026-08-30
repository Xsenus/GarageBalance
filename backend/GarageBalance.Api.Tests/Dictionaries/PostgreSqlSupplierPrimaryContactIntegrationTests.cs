using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
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
        var expenseType = new ExpenseType { Name = "Коммунальные расходы" };
        var expenseFund = new Fund { Name = "Основной", NormalizedName = "основной", Balance = 1234m };
        var serviceSettings = Enumerable.Range(1, 3)
            .Select(index => new ChargeServiceSetting { Name = $"Услуга {4 - index}" })
            .ToArray();
        var suppliers = Enumerable.Range(1, 3)
            .Select(index => new Supplier
            {
                Name = $"Поставщик {index}",
                Group = group,
                Inn = $"260000000{index}",
                LegalAddress = $"Адрес {index}",
                ContactPerson = $"Резервный контакт {index}",
                Phone = $"Резервный телефон {index}",
                Email = $"fallback{index}@example.test",
                StartingBalance = index * 100m,
                Comment = $"Комментарий {index}",
                ChargeServiceSetting = serviceSettings[index - 1],
                ExpenseType = expenseType,
                ExpenseFund = expenseFund
            })
            .ToArray();

        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(group, expenseType, expenseFund);
            setupContext.AddRange(serviceSettings);
            setupContext.AddRange(suppliers);
            for (var supplierIndex = 0; supplierIndex < suppliers.Length; supplierIndex++)
            {
                var supplier = suppliers[supplierIndex];
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
                    Phone = $"+7 900 000-00-0{supplierIndex + 1}",
                    Email = $"primary{supplierIndex + 1}@example.test",
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
            Assert.StartsWith("+7 900 000-00-0", item.PrimaryContact.Phone, StringComparison.Ordinal);
            Assert.StartsWith("primary", item.PrimaryContact.Email, StringComparison.Ordinal);
            Assert.NotNull(item.Supplier.ChargeServiceSetting);
            Assert.Equal("Коммунальные расходы", item.Supplier.ExpenseType?.Name);
            Assert.Equal("Основной", item.Supplier.ExpenseFund?.Name);
            Assert.Equal(1234m, item.Supplier.ExpenseFund?.Balance);
            Assert.Equal(item.Supplier.StartingBalance, item.DebtTotal);
        });

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplier_contacts", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ROW_NUMBER() OVER(PARTITION BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(command, "supplier_contacts"));
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);

        var sortingCases = new (string SortBy, bool Descending, string ExpectedName)[]
        {
            ("name", false, "Поставщик 1"),
            ("name", true, "Поставщик 3"),
            ("debt", false, "Поставщик 1"),
            ("debt", true, "Поставщик 3"),
            ("contactPerson", false, "Поставщик 1"),
            ("contactPerson", true, "Поставщик 3"),
            ("phone", false, "Поставщик 1"),
            ("phone", true, "Поставщик 3"),
            ("email", false, "Поставщик 1"),
            ("email", true, "Поставщик 3"),
            ("unsupported", false, "Поставщик 3"),
            ("unsupported", true, "Поставщик 1")
        };
        foreach (var sortingCase in sortingCases)
        {
            var sortedPage = await repository.GetPageAsync(
                null,
                null,
                false,
                0,
                1,
                sortingCase.SortBy,
                sortingCase.Descending,
                CancellationToken.None);

            Assert.Equal(3, sortedPage.TotalCount);
            Assert.Equal(sortingCase.ExpectedName, Assert.Single(sortedPage.Items).Supplier.Name);
            Assert.Single(capture.TakeCommandsAndClear());
        }

        var emptyPage = await repository.GetPageAsync(
            null,
            null,
            false,
            3,
            3,
            "name",
            false,
            CancellationToken.None);
        Assert.Equal(3, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Items);
        Assert.Single(capture.TakeCommandsAndClear());
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var commands = Commands.ToArray();
            Commands.Clear();
            return commands;
        }

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
