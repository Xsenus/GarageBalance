using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class HealthControllerTests
{
    [Fact]
    public void Live_ReturnsProcessStatusWithoutCheckingPostgreSql()
    {
        var readiness = new FakeReadinessService(true);
        var controller = new HealthController(readiness);

        var result = Assert.IsType<OkObjectResult>(controller.GetLive().Result);
        var response = Assert.IsType<HealthResponse>(result.Value);

        Assert.Equal("ok", response.Status);
        Assert.Equal("not_checked", response.Dependencies.PostgreSql);
        Assert.Equal(0, readiness.CallCount);
    }

    [Fact]
    public async Task Ready_ReturnsOkWhenPostgreSqlIsAvailable()
    {
        var readiness = new FakeReadinessService(true);
        var controller = new HealthController(readiness);

        var result = Assert.IsType<OkObjectResult>((await controller.GetReady(CancellationToken.None)).Result);
        var response = Assert.IsType<HealthResponse>(result.Value);

        Assert.Equal("ok", response.Status);
        Assert.Equal("ok", response.Dependencies.PostgreSql);
        Assert.Equal(1, readiness.CallCount);
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailableWhenPostgreSqlIsUnavailable()
    {
        var controller = new HealthController(new FakeReadinessService(false));

        var result = Assert.IsType<ObjectResult>((await controller.GetReady(CancellationToken.None)).Result);
        var response = Assert.IsType<HealthResponse>(result.Value);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("unavailable", response.Dependencies.PostgreSql);
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailableWhenPostgreSqlCheckTimesOut()
    {
        var controller = new HealthController(new FakeReadinessService(new OperationCanceledException()));

        var result = Assert.IsType<ObjectResult>((await controller.GetReady(CancellationToken.None)).Result);
        var response = Assert.IsType<HealthResponse>(result.Value);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("timeout", response.Dependencies.PostgreSql);
    }

    private sealed class FakeReadinessService : IApplicationReadinessService
    {
        private readonly bool result;
        private readonly Exception? exception;

        public FakeReadinessService(bool result) => this.result = result;

        public FakeReadinessService(Exception exception) => this.exception = exception;

        public int CallCount { get; private set; }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is null ? Task.FromResult(result) : Task.FromException<bool>(exception);
        }
    }
}
