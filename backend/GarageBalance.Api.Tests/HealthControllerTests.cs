using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests;

public sealed class HealthControllerTests
{
    [Fact]
    public void Get_ReturnsHealthyServiceStatus()
    {
        var controller = new HealthController();

        var result = controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(ok.Value);
        Assert.Equal("ok", response.Status);
        Assert.Equal("GarageBalance.Api", response.Service);
        Assert.True(response.CheckedAtUtc <= DateTimeOffset.UtcNow);
    }
}
