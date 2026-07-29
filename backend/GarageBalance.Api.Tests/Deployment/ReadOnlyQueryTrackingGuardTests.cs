using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ReadOnlyQueryTrackingGuardTests
{
    [Fact]
    public void InfrastructureQueryClasses_ExplicitlyDisableTracking()
    {
        var dataDirectory = FindProjectFile("Infrastructure/Data/GarageBalanceDbContext.cs").Directory!;
        var queryFiles = dataDirectory.EnumerateFiles("*Query.cs", SearchOption.TopDirectoryOnly).ToArray();
        var missingNoTracking = queryFiles
            .Where(file => !File.ReadAllText(file.FullName).Contains(".AsNoTracking()", StringComparison.Ordinal))
            .Select(file => file.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var forcedTracking = queryFiles
            .Where(file => File.ReadAllText(file.FullName).Contains(".AsTracking()", StringComparison.Ordinal))
            .Select(file => file.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(queryFiles);
        Assert.True(
            missingNoTracking.Length == 0,
            $"Read-only query classes without AsNoTracking:{Environment.NewLine}{string.Join(Environment.NewLine, missingNoTracking)}");
        Assert.True(
            forcedTracking.Length == 0,
            $"Read-only query classes with AsTracking:{Environment.NewLine}{string.Join(Environment.NewLine, forcedTracking)}");
    }

    [Fact]
    public async Task GetUserRolesAsync_DoesNotPopulateChangeTracker()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var user = new AppUser
        {
            Email = "reader@example.test",
            NormalizedEmail = "READER@EXAMPLE.TEST",
            DisplayName = "Читатель",
            PasswordHash = "test-hash"
        };
        var role = new AppRole
        {
            Code = "reader",
            Name = "Читатель"
        };
        database.Context.AddRange(user, role);
        database.Context.UserRoles.Add(new AppUserRole { User = user, Role = role });
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var roles = await new EfUserRepository(database.Context)
            .GetUserRolesAsync(user.Id, CancellationToken.None);

        Assert.Single(roles);
        Assert.Equal(role.Id, roles[0].Id);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    private static FileInfo FindProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = new FileInfo(Path.Combine(directory.FullName, "GarageBalance.Api", relativePath));
            if (candidate.Exists)
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate GarageBalance.Api/{relativePath}.");
    }
}
