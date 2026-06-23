using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    [Fact]
    public void CreateValidation_ReturnsValidationProblemContractWithErrors()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "Email обязателен.");
        modelState.AddModelError("Password", "Пароль слишком короткий.");

        var problem = ApiProblemDetails.CreateValidation(modelState);

        Assert.Equal(ApiProblemDetails.ValidationFailedCode, problem.Title);
        Assert.Equal("Проверьте обязательные поля и формат данных.", problem.Detail);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(ApiProblemDetails.ValidationFailedCode, problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
        Assert.Equal("Email обязателен.", Assert.Single(problem.Errors["Email"]));
        Assert.Equal("Пароль слишком короткий.", Assert.Single(problem.Errors["Password"]));
    }

    [Fact]
    public void CreateValidation_ReturnsValidationProblemDetails()
    {
        var problem = ApiProblemDetails.CreateValidation(new ModelStateDictionary());

        Assert.IsType<ValidationProblemDetails>(problem);
    }

    [Fact]
    public void CreateInvalidModelStateResponse_ReturnsBadRequestWithValidationProblem()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("DisplayName", "ФИО обязательно.");
        var context = new ActionContext(
            new DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
            modelState);

        var result = ApiProblemDetails.CreateInvalidModelStateResponse(context);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(ApiProblemDetails.ValidationFailedCode, problem.Extensions[ApiProblemDetails.CodeExtensionKey]);
        Assert.Equal("ФИО обязательно.", Assert.Single(problem.Errors["DisplayName"]));
    }
}
