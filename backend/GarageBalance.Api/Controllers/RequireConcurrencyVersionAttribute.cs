using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GarageBalance.Api.Controllers;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequireConcurrencyVersionAttribute(params string[] propertyPaths) : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var propertyPath in propertyPaths)
        {
            if (ResolveValue(context.ActionArguments, propertyPath) is Guid version && version != Guid.Empty)
            {
                continue;
            }

            context.ModelState.AddModelError(
                propertyPath,
                "Перед сохранением обновите карточку: отсутствует актуальная версия данных.");
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
            {
                Title = "concurrency_version_required",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Карточка загружена без версии. Обновите страницу и повторите изменение."
            });
        }
    }

    private static object? ResolveValue(IDictionary<string, object?> arguments, string propertyPath)
    {
        var segments = propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !arguments.TryGetValue(segments[0], out var current))
        {
            return null;
        }

        for (var index = 1; index < segments.Length && current is not null; index++)
        {
            current = current.GetType()
                .GetProperty(segments[index], BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                ?.GetValue(current);
        }

        return current;
    }
}
