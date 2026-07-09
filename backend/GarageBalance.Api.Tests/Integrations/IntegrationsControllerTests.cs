using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class IntegrationsControllerTests
{
    [Fact]
    public async Task GetOneCFreshStatus_ReturnsStatusFromService()
    {
        var expected = new OneCFreshIntegrationStatusDto(
            "OneCFresh",
            "1C Fresh",
            IsConfigured: true,
            CanSynchronize: false,
            "prepared",
            "Токен сохранен.",
            ["RefreshToken"],
            ["RefreshToken"],
            DateTimeOffset.UtcNow);
        var service = new FakeIntegrationStatusService(expected);
        var controller = new IntegrationsController(service);

        var result = await controller.GetOneCFreshStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.True(service.Called);
    }

    private sealed class FakeIntegrationStatusService(OneCFreshIntegrationStatusDto status) : IIntegrationStatusService
    {
        public bool Called { get; private set; }

        public Task<OneCFreshIntegrationStatusDto> GetOneCFreshStatusAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(status);
        }
    }
}
