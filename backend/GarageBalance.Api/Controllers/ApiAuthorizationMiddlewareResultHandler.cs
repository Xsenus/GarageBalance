using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

public sealed class ApiAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (context.Response.HasStarted)
        {
            return defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }

        if (authorizeResult.Challenged)
        {
            return WriteProblemAsync(context, ApiProblemDetails.CreateUnauthorized());
        }

        if (authorizeResult.Forbidden)
        {
            return WriteProblemAsync(context, ApiProblemDetails.CreateForbidden());
        }

        return defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(problem);
    }
}
