using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlDockerFirstRunIntegrationTests
{
    [PostgreSqlFact]
    public async Task FirstRun_ContainsInitialCatalogAndCreatesWorkingAdministrator()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var tariffNames = await context.Tariffs
            .Where(item => !item.IsArchived)
            .Select(item => item.Name)
            .ToListAsync();
        Assert.Contains("Тариф на воду", tariffNames);
        Assert.Contains("Электроэнергия", tariffNames);
        Assert.Contains("Сумма членского взноса", tariffNames);
        Assert.Contains("Сумма целевого взноса", tariffNames);
        Assert.Contains("Ставка за вывоз мусора", tariffNames);

        var serviceNames = await context.ChargeServiceSettings
            .Where(item => !item.IsArchived)
            .Select(item => item.Name)
            .ToListAsync();
        Assert.Contains("Вода", serviceNames);
        Assert.Contains("Электроэнергия", serviceNames);
        Assert.Contains("Членский взнос", serviceNames);
        Assert.Contains("Целевой взнос", serviceNames);
        Assert.Contains("Мусор", serviceNames);
        Assert.Contains("Наружное освещение", serviceNames);

        var irregularPayments = await context.IrregularPayments
            .Where(item => item.IsActive && !item.IsArchived)
            .ToDictionaryAsync(item => item.Name, item => item.Amount);
        Assert.Equal(5000m, irregularPayments["Вступительный взнос"]);
        Assert.Equal(10000m, irregularPayments["Подключение канализации"]);
        Assert.Equal(15000m, irregularPayments["Подключение к линии электросети"]);
        Assert.Empty(context.Users);

        const string password = "DockerFirstRunPass123";
        var authService = new AuthService(
            new EfUserRepository(context),
            new Pbkdf2PasswordHasher(),
            new PasswordPolicyValidator(),
            new JwtTokenService(Options.Create(new JwtOptions
            {
                Issuer = "GarageBalance.Tests",
                Audience = "GarageBalance.Tests",
                SigningKey = "test-signing-key-that-is-long-enough-32",
                AccessTokenMinutes = 15
            })),
            new AuditEventWriter(context),
            new NoOpUserSecurityMutationLock());
        using var services = new ServiceCollection()
            .AddSingleton<IAuthService>(authService)
            .BuildServiceProvider();
        var initializer = new InitialAdministratorHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new InitialAdministratorOptions
            {
                Enabled = true,
                Email = "admin@garagebalance.local",
                DisplayName = "Администратор",
                Password = password
            }),
            NullLogger<InitialAdministratorHostedService>.Instance);

        await initializer.StartAsync(CancellationToken.None);
        var login = await authService.LoginAsync(
            new LoginRequest("admin@garagebalance.local", password),
            CancellationToken.None);

        Assert.True(login.Succeeded);
        Assert.Contains(SystemRoles.Administrator, login.Value!.User.Roles);
        Assert.Contains(SystemPermissions.UsersManage, login.Value.User.Permissions);
        Assert.Single(await context.Users.ToListAsync());
        Assert.Contains(
            await context.AuditEvents.ToListAsync(),
            item => item.Action == "auth.bootstrap_admin_created");
    }
}
