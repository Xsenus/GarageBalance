using System.Security.Claims;
using GarageBalance.Api.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class InitialAdministratorHostedServiceTests
{
    [Fact]
    public void OptionsValidator_AllowsDisabledBootstrapWithoutCredentials()
    {
        var result = new InitialAdministratorOptionsValidator().Validate(
            null,
            new InitialAdministratorOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OptionsValidator_AcceptsCompleteEnabledConfiguration()
    {
        var result = new InitialAdministratorOptionsValidator().Validate(
            null,
            CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not-an-email", "Администратор", "StrongPass123")]
    [InlineData("admin@example.test", "", "StrongPass123")]
    [InlineData("admin@example.test", "Администратор", "short")]
    [InlineData("admin@example.test", "Администратор", "__GENERATE__")]
    public void OptionsValidator_RejectsInvalidEnabledConfiguration(
        string email,
        string displayName,
        string password)
    {
        var result = new InitialAdministratorOptionsValidator().Validate(
            null,
            new InitialAdministratorOptions
            {
                Enabled = true,
                Email = email,
                DisplayName = displayName,
                Password = password
            });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task StartAndStop_DoNotCreateScopeWhenBootstrapIsDisabled()
    {
        var scopeFactory = new ThrowingScopeFactory();
        var service = new InitialAdministratorHostedService(
            scopeFactory,
            Options.Create(new InitialAdministratorOptions { Enabled = false }),
            NullLogger<InitialAdministratorHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopeFactory.CreateScopeCount);
    }

    [Fact]
    public async Task Start_CreatesConfiguredAdministrator()
    {
        var authService = new StubAuthService(AuthResult<AuthResponse>.Success(CreateResponse()));
        using var services = new ServiceCollection()
            .AddSingleton<IAuthService>(authService)
            .BuildServiceProvider();
        var service = CreateService(services, CreateOptions());

        await service.StartAsync(CancellationToken.None);

        var request = Assert.IsType<BootstrapAdminRequest>(authService.BootstrapRequest);
        Assert.Equal("admin@garagebalance.local", request.Email);
        Assert.Equal("StrongPass123", request.Password);
        Assert.Equal("Администратор", request.DisplayName);
    }

    [Fact]
    public async Task Start_SkipsAnAlreadyInitializedDatabase()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAuthService>(new StubAuthService(
                AuthResult<AuthResponse>.Failure("bootstrap_closed", "Первый администратор уже создан.")))
            .BuildServiceProvider();
        var service = CreateService(services, CreateOptions());

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_StopsApplicationForUnexpectedBootstrapFailure()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IAuthService>(new StubAuthService(
                AuthResult<AuthResponse>.Failure("role_missing", "Системная роль не создана.")))
            .BuildServiceProvider();
        var service = CreateService(services, CreateOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("role_missing", exception.Message, StringComparison.Ordinal);
    }

    private static InitialAdministratorHostedService CreateService(
        ServiceProvider services,
        InitialAdministratorOptions options) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<InitialAdministratorHostedService>.Instance);

    private static InitialAdministratorOptions CreateOptions() => new()
    {
        Enabled = true,
        Email = "admin@garagebalance.local",
        DisplayName = "Администратор",
        Password = "StrongPass123"
    };

    private static AuthResponse CreateResponse() => new(
        "token",
        DateTimeOffset.UtcNow.AddMinutes(15),
        new CurrentUserDto(Guid.NewGuid(), "admin@garagebalance.local", "Администратор", ["administrator"], []));

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCount++;
            throw new InvalidOperationException("A scope must not be created when bootstrap is disabled.");
        }
    }

    private sealed class StubAuthService(AuthResult<AuthResponse> bootstrapResult) : IAuthService
    {
        public BootstrapAdminRequest? BootstrapRequest { get; private set; }

        public Task<AuthResult<AuthResponse>> BootstrapAdminAsync(
            BootstrapAdminRequest request,
            CancellationToken cancellationToken)
        {
            BootstrapRequest = request;
            return Task.FromResult(bootstrapResult);
        }

        public Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResult<CurrentUserDto>> GetCurrentUserAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResult<CurrentUserDto>> ChangeOwnPasswordAsync(
            ClaimsPrincipal principal,
            ChangeOwnPasswordRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
