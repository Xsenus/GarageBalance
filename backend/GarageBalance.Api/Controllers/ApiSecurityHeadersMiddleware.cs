namespace GarageBalance.Api.Controllers;

public sealed class ApiSecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "no-referrer");

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "no-store, no-cache, max-age=0";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }

        await next(context);
    }
}
