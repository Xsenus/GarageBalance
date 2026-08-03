using GarageBalance.Api.Application.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(IApplicationReadinessService readinessService) : ControllerBase
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(3);

    [HttpGet("live")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> GetLive()
    {
        return Ok(CreateResponse("ok", "not_checked"));
    }

    [HttpGet]
    [HttpGet("ready")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> GetReady(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ReadinessTimeout);

        try
        {
            var isReady = await readinessService.IsReadyAsync(timeoutSource.Token);
            return isReady
                ? Ok(CreateResponse("ok", "ok"))
                : StatusCode(StatusCodes.Status503ServiceUnavailable, CreateResponse("unavailable", "unavailable"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateResponse("unavailable", "timeout"));
        }
    }

    private static HealthResponse CreateResponse(string status, string postgresql) =>
        new(status, "GarageBalance.Api", DateTimeOffset.UtcNow, new HealthDependencies(postgresql));
}

public sealed record HealthResponse(string Status, string Service, DateTimeOffset CheckedAtUtc, HealthDependencies Dependencies);

public sealed record HealthDependencies(string PostgreSql);
