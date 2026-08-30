using System.Data.Common;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Users;

public sealed class PostgreSqlUserManagementPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task UserPageLoadsTotalRolesAndOnlyVisibleUserFieldsInOneCommand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var operatorRole = new AppRole
        {
            Code = "operator",
            Name = "Оператор",
            Permissions = ["payments.read"]
        };
        var reportsRole = new AppRole
        {
            Code = "reports",
            Name = "Отчёты",
            Permissions = ["reports.read", "reports.export"]
        };
        var first = CreateUser("a@example.test", "Анна");
        var second = CreateUser("b@example.test", "Борис");
        var third = CreateUser("c@example.test", "Вера");
        second.UserRoles.Add(new AppUserRole { User = second, Role = operatorRole });
        second.UserRoles.Add(new AppUserRole { User = second, Role = reportsRole });
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(first, second, third);
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var page = await new EfUserManagementRepository(context)
            .GetUsersPageAsync(normalizedSearch: null, offset: 1, limit: 1, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        var user = Assert.Single(page.Users);
        Assert.Equal(second.Id, user.Id);
        Assert.Equal("b@example.test", user.Email);
        Assert.Equal("Борис", user.DisplayName);
        Assert.Equal(second.Version, user.Version);
        Assert.Equal(["operator", "reports"], user.UserRoles.Select(item => item.Role.Code).Order().ToArray());
        Assert.Equal(
            ["payments.read", "reports.export", "reports.read"],
            user.UserRoles.SelectMany(item => item.Role.Permissions).Order().ToArray());
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionVersion", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task UserPagePreservesUsersWithoutRolesAndAnEmptySlice()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var userWithoutRoles = CreateUser("solo@example.test", "Без роли");
        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(userWithoutRoles);
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfUserManagementRepository(context);

        var firstPage = await repository.GetUsersPageAsync(null, 0, 1, CancellationToken.None);
        var emptyPage = await repository.GetUsersPageAsync(null, 1, 1, CancellationToken.None);

        Assert.Equal(1, firstPage.TotalCount);
        Assert.Empty(Assert.Single(firstPage.Users).UserRoles);
        Assert.Equal(1, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Users);
        Assert.Equal(2, capture.Commands.Count);
        Assert.All(capture.Commands, command => Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task BoundedUserListLoadsRolesWithoutProtectedUserFields()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var role = new AppRole
        {
            Code = "accounting",
            Name = "Бухгалтерия",
            Permissions = ["payments.read", "reports.read"]
        };
        var first = CreateUser("a@example.test", "Анна");
        var second = CreateUser("b@example.test", "Борис");
        var third = CreateUser("c@example.test", "Вера");
        second.UserRoles.Add(new AppUserRole { User = second, Role = role });
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(first, second, third);
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var users = await new EfUserManagementRepository(context)
            .GetUsersAsync(normalizedSearch: null, limit: 2, CancellationToken.None);

        Assert.Equal([first.Id, second.Id], users.Select(user => user.Id).ToArray());
        Assert.Empty(users[0].UserRoles);
        Assert.Equal("accounting", Assert.Single(users[1].UserRoles).Role.Code);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionVersion", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static AppUser CreateUser(string email, string displayName) =>
        new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            PasswordHash = new string('x', 500),
            SessionVersion = 42
        };

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
