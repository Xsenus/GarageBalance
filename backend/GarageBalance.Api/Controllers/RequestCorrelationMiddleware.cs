using System.Security.Cryptography;

namespace GarageBalance.Api.Controllers;

public sealed class RequestCorrelationMiddleware(RequestDelegate next, ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Error-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestedId = context.Request.Headers[HeaderName].ToString();
        var errorId = IsSafeIdentifier(requestedId) ? requestedId : Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        context.TraceIdentifier = errorId;
        context.Response.Headers[HeaderName] = errorId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["ErrorId"] = errorId,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path.Value
        }))
        {
            await next(context);
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length is >= 8 and <= 80 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
