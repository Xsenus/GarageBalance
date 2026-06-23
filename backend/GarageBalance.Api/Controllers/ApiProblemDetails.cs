using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

public static class ApiProblemDetails
{
    public const string CodeExtensionKey = "code";

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
}
