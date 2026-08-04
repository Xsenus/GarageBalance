using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PostgreSqlJwtSessionValidationTests
{
    [PostgreSqlFact]
    public async Task SessionValidation_RejectsStaleVersionAndInactiveUser()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var user = new AppUser
        {
            Email = "session@example.test",
            NormalizedEmail = "SESSION@EXAMPLE.TEST",
            DisplayName = "Проверка сессии",
            PasswordHash = "hash"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var repository = new EfUserRepository(context);

        Assert.True(await repository.IsSessionValidAsync(user.Id, 1, CancellationToken.None));

        user.SessionVersion++;
        await context.SaveChangesAsync();
        Assert.False(await repository.IsSessionValidAsync(user.Id, 1, CancellationToken.None));
        Assert.True(await repository.IsSessionValidAsync(user.Id, 2, CancellationToken.None));

        user.IsActive = false;
        await context.SaveChangesAsync();
        Assert.False(await repository.IsSessionValidAsync(user.Id, 2, CancellationToken.None));
    }
}
