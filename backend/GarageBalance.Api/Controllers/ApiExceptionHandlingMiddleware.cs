using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

public sealed class ApiExceptionHandlingMiddleware(RequestDelegate next, ILogger<ApiExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled API exception.");
            await WriteProblemAsync(context, ApiProblemDetails.CreateInternalError());
        }
    }

    private static Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.Clear();
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
