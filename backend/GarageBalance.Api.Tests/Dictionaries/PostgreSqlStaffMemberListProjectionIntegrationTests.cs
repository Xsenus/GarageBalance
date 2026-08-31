using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlStaffMemberListProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task GetListAsync_UsesOneBoundedCompactProjectionForDisplayedStaffData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstDepartment = new StaffDepartment
        {
            Name = "Администрация 88422",
            IsArchived = true,
            CreatedAtUtc = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2025-02-01T00:00:00Z")
        };
        var secondDepartment = new StaffDepartment { Name = "Бухгалтерия 88422" };
        var excludedDepartment = new StaffDepartment { Name = "Гаражная служба 88422" };
        var firstMember = new StaffMember
        {
            Department = firstDepartment,
            FullName = "Анна %_ Петрова",
            Rate = 45000.25m
        };
        var secondMember = new StaffMember
        {
            Department = secondDepartment,
            FullName = "Борис Сидоров",
            Rate = 39000m
        };
        var excludedByLimitMember = new StaffMember
        {
            Department = excludedDepartment,
            FullName = "Виктор Иванов",
            Rate = 37000m
        };
        var archivedMember = new StaffMember
        {
            Department = firstDepartment,
            FullName = "Архивный сотрудник",
            Rate = 10000m,
            IsArchived = true
        };
        await using (var setupContext = database.CreateContext())
        {
            setupContext.AddRange(
                firstDepartment,
                secondDepartment,
                excludedDepartment,
                firstMember,
                secondMember,
                excludedByLimitMember,
                archivedMember);
            await setupContext.SaveChangesAsync();
        }

        var capture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var queryContext = new GarageBalanceDbContext(options);
        var repository = new EfStaffMemberRepository(queryContext);

        var result = await repository.GetListAsync(null, null, false, 2, CancellationToken.None);

        Assert.Equal([firstMember.Id, secondMember.Id], result.Select(member => member.Id));
        var actual = result[0];
        Assert.Equal("Анна %_ Петрова", actual.FullName);
        Assert.Equal(45000.25m, actual.Rate);
        Assert.False(actual.IsArchived);
        Assert.Equal(firstDepartment.Id, actual.DepartmentId);
        Assert.Equal("Администрация 88422", actual.Department.Name);
        Assert.Empty(queryContext.ChangeTracker.Entries());

        var command = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", command, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);

        var literalSearch = await repository.GetListAsync(
            firstDepartment.Id,
            "%_",
            true,
            10,
            CancellationToken.None);

        Assert.Equal(firstMember.Id, Assert.Single(literalSearch).Id);
        var searchCommand = Assert.Single(capture.TakeCommandsAndClear());
        Assert.Contains("ILIKE", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT", searchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAtUtc", searchCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", searchCommand, StringComparison.Ordinal);
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
        var repository = new EfStaffMemberRepository(queryContext);
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
