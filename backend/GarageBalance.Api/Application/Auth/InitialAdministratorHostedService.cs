using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Auth;

public sealed class InitialAdministratorHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<InitialAdministratorOptions> options,
    ILogger<InitialAdministratorHostedService> logger) : IHostedService
{
    private readonly InitialAdministratorOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Initial administrator bootstrap is disabled.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var result = await authService.BootstrapAdminAsync(
            new BootstrapAdminRequest(_options.Email, _options.Password, _options.DisplayName),
            cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation("Initial administrator account was created.");
            return;
        }

        if (string.Equals(result.ErrorCode, "bootstrap_closed", StringComparison.Ordinal))
        {
            logger.LogInformation("Initial administrator bootstrap was skipped because the database already contains users.");
            return;
        }

        throw new InvalidOperationException($"Initial administrator bootstrap failed: {result.ErrorCode}.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
