using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GarageBalance.Api.Controllers;

public static class ApiProblemDetails
{
    public const string CodeExtensionKey = "code";
    public const string ForbiddenCode = "forbidden";
    public const string UnauthorizedCode = "unauthorized";
    public const string ValidationFailedCode = "validation_failed";

    public static ProblemDetails Create(string? code, string? detail, int statusCode)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? "api_error" : code;
        var problem = new ProblemDetails
        {
            Title = normalizedCode,
            Detail = detail,
            Status = statusCode
        };

        problem.Extensions[CodeExtensionKey] = normalizedCode;
        return problem;
    }

    public static ValidationProblemDetails CreateValidation(ModelStateDictionary modelState)
    {
        var problem = new ValidationProblemDetails(modelState)
        {
            Title = ValidationFailedCode,
            Detail = "Проверьте обязательные поля и формат данных.",
            Status = StatusCodes.Status400BadRequest
        };

        problem.Extensions[CodeExtensionKey] = ValidationFailedCode;
        return problem;
    }

    public static IActionResult CreateInvalidModelStateResponse(ActionContext context)
    {
        return new BadRequestObjectResult(CreateValidation(context.ModelState));
    }

    public static ProblemDetails CreateUnauthorized()
    {
        return Create(UnauthorizedCode, "Необходима авторизация.", StatusCodes.Status401Unauthorized);
    }

    public static ProblemDetails CreateForbidden()
    {
        return Create(ForbiddenCode, "Недостаточно прав для выполнения действия.", StatusCodes.Status403Forbidden);
    }
}
