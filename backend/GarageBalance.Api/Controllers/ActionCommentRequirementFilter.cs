using System.Reflection;
using GarageBalance.Api.Application.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GarageBalance.Api.Controllers;

public sealed class ActionCommentRequirementFilter(IApplicationSettingsService settingsService) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var mutatingRequest = !HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            !HttpMethods.IsHead(context.HttpContext.Request.Method) &&
            !HttpMethods.IsOptions(context.HttpContext.Request.Method);
        if (mutatingRequest)
        {
            var settings = await settingsService.GetActionCommentSettingsAsync(context.HttpContext.RequestAborted);
            if (settings.Required && FindMissingComment(context.ActionArguments.Values) is { } fieldName)
            {
                var problem = new ValidationProblemDetails(
                    new Dictionary<string, string[]> { [fieldName] = ["Укажите комментарий к действию."] })
                {
                    Title = "action_comment_required",
                    Detail = "Укажите комментарий к действию.",
                    Status = StatusCodes.Status400BadRequest
                };
                problem.Extensions[ApiProblemDetails.CodeExtensionKey] = "action_comment_required";
                context.Result = new BadRequestObjectResult(problem);
                return;
            }

            using var scope = ActionCommentRequirementContext.Push(settings.Required);
            await next();
            return;
        }

        await next();
    }

    private static string? FindMissingComment(IEnumerable<object?> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                continue;
            }

            foreach (var property in argument.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var constructorParameter = argument.GetType()
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                    .SelectMany(constructor => constructor.GetParameters())
                    .FirstOrDefault(parameter => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                if (property.PropertyType != typeof(string) ||
                    property.GetCustomAttribute<ActionCommentAttribute>() is null &&
                    constructorParameter?.GetCustomAttribute<ActionCommentAttribute>() is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(property.GetValue(argument) as string))
                {
                    return property.Name;
                }
            }
        }

        return null;
    }
}
