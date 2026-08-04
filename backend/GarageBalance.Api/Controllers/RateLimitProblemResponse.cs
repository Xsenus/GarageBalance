using System.Globalization;
using System.Threading.RateLimiting;

namespace GarageBalance.Api.Controllers;

public static class RateLimitProblemResponse
{
    public static async Task WriteAsync(
        HttpContext context,
        RateLimitLease lease,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        var problem = ApiProblemDetails.Create(
            "ip_rate_limited",
            "Слишком много запросов с этого адреса. Повторите попытку позже.",
            StatusCodes.Status429TooManyRequests);
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }
}
