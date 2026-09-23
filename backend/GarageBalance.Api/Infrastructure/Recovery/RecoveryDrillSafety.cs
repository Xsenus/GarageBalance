using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Recovery;

/// <summary>Fail-closed sandbox for the operator's disposable restore verifier, never a production mode.</summary>
public static class RecoveryDrillSafety
{
    public static bool Configure(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("RecoveryDrill:Enabled"))
        {
            return false;
        }

        Validate(builder.Configuration);
        // All application hosted services can write or call external systems. None may run in a drill.
        for (var index = builder.Services.Count - 1; index >= 0; index--)
        {
            var descriptor = builder.Services[index];
            if (descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType?.Assembly == typeof(RecoveryDrillSafety).Assembly)
            {
                builder.Services.RemoveAt(index);
            }
        }
        builder.Services.RemoveAll<Microsoft.AspNetCore.Hosting.IStartupFilter>();
        return true;
    }

    public static void Validate(IConfiguration configuration)
    {
        var runId = configuration["RecoveryDrill:RunId"];
        if (!Guid.TryParseExact(runId, "N", out _))
        {
            throw new InvalidOperationException("Recovery drill requires an explicit generated run identifier.");
        }
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (connection.Database != $"gb_restore_{runId}" ||
            !IPAddress.TryParse(connection.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException("Recovery drill requires a dedicated generated database on loopback.");
        }
        var urls = configuration["urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (urls is not { Length: 1 } || !Uri.TryCreate(urls[0], UriKind.Absolute, out var url) ||
            url.Scheme != "http" || !IPAddress.TryParse(url.Host, out var listenAddress) || !IPAddress.IsLoopback(listenAddress) ||
            !string.IsNullOrEmpty(configuration["Kestrel:Endpoints:Http:Url"]))
        {
            throw new InvalidOperationException("Recovery drill must listen only on a single explicit loopback HTTP endpoint.");
        }
        if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            throw new InvalidOperationException("Additional Kestrel endpoints are forbidden during recovery verification.");
        }
    }

    public static bool IsAllowedRequest(string method, PathString path) =>
        HttpMethods.IsPost(method) && path == "/api/auth/login" ||
        HttpMethods.IsGet(method) && (path == "/health" || path == "/api/auth/me" || path == "/api/reports/consolidated");

    public static void UseReadOnlyBoundary(WebApplication app) => app.Use(async (context, next) =>
    {
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
            !IsAllowedRequest(context.Request.Method, context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context);
    });
}
