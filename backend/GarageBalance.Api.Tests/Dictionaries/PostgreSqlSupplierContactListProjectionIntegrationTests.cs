using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierContactListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesOneBoundedCompactProjectionForDisplayedContactData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = new SupplierGroup { Name = "Коммунальные услуги" };
        var firstSupplier = new Supplier
        {
            Name = "Альфа поставщик",
            Inn = "7700000001",
            LegalAddress = "Не должен загружаться",
            ContactPerson = "Не должен загружаться",
            Phone = "+7 900 000-00-01",
            Email = "supplier@example.test",
            StartingBalance = 1234m,
            Comment = "Не должен загружаться",
            Group = group
        };
        var secondSupplier = new Supplier { Name = "Бета поставщик", Group = group };
        var excludedSupplier = new Supplier { Name = "Вега поставщик", Group = group };
        var firstContact = new SupplierContact
        {
            Supplier = firstSupplier,
            FullName = "Анна %_ 88421",
            Position = "Диспетчер",
            Phone = "+7 900 111-22-33",
            Email = "contact@example.test",
            Status = "Работает",
            Comment = "Отображаемая заметка"
        };
        var secondContact = new SupplierContact
        {
            Supplier = secondSupplier,
            FullName = "Борис 88421",
            Status = "В отпуске"
        };
        var excludedByLimitContact = new SupplierContact
        {
            Supplier = excludedSupplier,
            FullName = "Виктор 88421",
            Status = "Работает"
        };
        var archivedContact = new SupplierContact
        {
            Supplier = firstSupplier,
            FullName = "Архивный 88421",
            Status = "Не работает",
            IsArchived = true
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                group,
                firstSupplier,
                secondSupplier,
                excludedSupplier,
                firstContact,
                secondContact,
                excludedByLimitContact,
                archivedContact);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierContactRepository(queryContext);

        var result = await repository.GetListAsync(null, null, false, 2, CancellationToken.None);

        Assert.Equal([firstContact.Id, secondContact.Id], result.Select(contact => contact.Id));
        var actual = result[0];
        Assert.Equal(firstSupplier.Id, actual.SupplierId);
        Assert.Equal("Альфа поставщик", actual.Supplier.Name);
        Assert.Equal("Анна %_ 88421", actual.FullName);
        Assert.Equal("Диспетчер", actual.Position);
        Assert.Equal("+7 900 111-22-33", actual.Phone);
        Assert.Equal("contact@example.test", actual.Email);
        Assert.Equal("Работает", actual.Status);
        Assert.Equal("Отображаемая заметка", actual.Comment);
        Assert.False(actual.IsArchived);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Inn", command, StringComparison.Ordinal);
        Assert.DoesNotContain("LegalAddress", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContactPerson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("StartingBalance", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", command, StringComparison.Ordinal);

        var literalSearch = await repository.GetListAsync(firstSupplier.Id, "%_", true, 10, CancellationToken.None);

        Assert.Equal(firstContact.Id, Assert.Single(literalSearch).Id);
        var searchCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StartingBalance", searchCommand, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task GetListAsync_PropagatesCancellationBeforeDatabaseRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfSupplierContactRepository(queryContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.GetListAsync(null, null, false, 10, cancellation.Token));

        Assert.Empty(capture.TakeCommandsAndClear());
        Assert.Empty(queryContext.ChangeTracker.Entries());
    }

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        private readonly List<string> commands = [];

        public IReadOnlyList<string> TakeCommandsAndClear()
        {
            var result = commands.ToArray();
            commands.Clear();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
