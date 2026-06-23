using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class ApiProblemDetailsTests
{
    [Fact]
    public void Create_ReturnsUnifiedProblemContract()
    {
        var problem = ApiProblemDetails.Create("validation_failed", "Поле заполнено неверно.", StatusCodes.Status400BadRequest);

        Assert.Equal("validation_failed", problem.Title);
        Assert.Equal("Поле заполнено неверно.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("validation_failed", problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
    }

    [Fact]
    public void Create_NormalizesBlankCode()
    {
        var problem = ApiProblemDetails.Create(" ", "Ошибка API.", StatusCodes.Status500InternalServerError);

        Assert.Equal("api_error", problem.Title);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("api_error", problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
    }
}
