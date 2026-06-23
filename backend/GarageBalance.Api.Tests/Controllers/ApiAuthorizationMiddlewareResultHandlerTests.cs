using System.Text.Json;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ApiAuthorizationMiddlewareResultHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsProblemDetailsForChallenge()
    {
        var context = CreateHttpContext();
        var handler = new ApiAuthorizationMiddlewareResultHandler();

        await handler.HandleAsync(_ => throw new InvalidOperationException("Next should not be called."), context, CreatePolicy(), PolicyAuthorizationResult.Challenge());

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(ApiProblemDetails.UnauthorizedCode, problem.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.GetProperty("status").GetInt32());
        Assert.Equal(ApiProblemDetails.UnauthorizedCode, problem.GetProperty(ApiProblemDetails.CodeExtensionKey).GetString());
    }

    [Fact]
    public async Task HandleAsync_ReturnsProblemDetailsForForbidden()
    {
        var context = CreateHttpContext();
        var handler = new ApiAuthorizationMiddlewareResultHandler();

        await handler.HandleAsync(_ => throw new InvalidOperationException("Next should not be called."), context, CreatePolicy(), PolicyAuthorizationResult.Forbid());

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(ApiProblemDetails.ForbiddenCode, problem.GetProperty("title").GetString());
        Assert.Equal(StatusCodes.Status403Forbidden, problem.GetProperty("status").GetInt32());
        Assert.Equal(ApiProblemDetails.ForbiddenCode, problem.GetProperty(ApiProblemDetails.CodeExtensionKey).GetString());
    }

    [Fact]
    public async Task HandleAsync_CallsNextForSuccessfulAuthorization()
    {
        var nextCalled = false;
        var context = CreateHttpContext();
        var handler = new ApiAuthorizationMiddlewareResultHandler();

        await handler.HandleAsync(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, context, CreatePolicy(), PolicyAuthorizationResult.Success());

        Assert.True(nextCalled);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static AuthorizationPolicy CreatePolicy()
    {
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
