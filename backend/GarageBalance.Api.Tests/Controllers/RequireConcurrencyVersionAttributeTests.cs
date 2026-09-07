using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace GarageBalance.Api.Tests.Controllers;

public sealed class RequireConcurrencyVersionAttributeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("981fbaed-e292-4a4b-ae1b-5529d47c9460")]
    public void FundDeletion_RequiresNonemptyVersionAtHttpBoundary(string? version)
    {
        var attribute = Assert.Single(typeof(FundsController).GetMethod(nameof(FundsController.DeleteFund))!
            .GetCustomAttributes(typeof(RequireConcurrencyVersionAttribute), true).Cast<RequireConcurrencyVersionAttribute>());
        var request = new GarageBalance.Api.Application.Funds.DeleteFundRequest(
            "Закрытие", version is null ? null : Guid.Parse(version));
        var context = CreateContext(request);
        attribute.OnActionExecuting(context);
        if (request.Version is null || request.Version == Guid.Empty)
        {
            var result = Assert.IsType<BadRequestObjectResult>(context.Result);
            Assert.Equal("concurrency_version_required", Assert.IsType<ValidationProblemDetails>(result.Value).Title);
        }
        else
        {
            Assert.Null(context.Result);
        }
    }

    [Fact]
    public void OnActionExecuting_RejectsMissingAndEmptyVersions()
    {
        var attribute = new RequireConcurrencyVersionAttribute("request.Version");
        var request = new UpsertGarageRequest("1", 1, 1, null, 0, null, null, null);
        var context = CreateContext(request);

        attribute.OnActionExecuting(context);

        var badRequest = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal("concurrency_version_required", problem.Title);
        Assert.Contains("request.Version", problem.Errors.Keys);
    }

    [Fact]
    public void OnActionExecuting_AcceptsCurrentVersion()
    {
        var attribute = new RequireConcurrencyVersionAttribute("request.Version");
        var request = new UpsertGarageRequest("1", 1, 1, null, 0, null, null, null, Guid.NewGuid());
        var context = CreateContext(request);

        attribute.OnActionExecuting(context);

        Assert.Null(context.Result);
        Assert.True(context.ModelState.IsValid);
    }

    [Fact]
    public void OnActionExecuting_ValidatesNestedCompositeVersionPaths()
    {
        var attribute = new RequireConcurrencyVersionAttribute("request.Service.Version", "request.TariffVersion");
        var request = new UpdateChargeServiceWithTariffRequest(
            new UpsertChargeServiceSettingRequest("Вода", true, 1, 1, 30, null, 30, true, false, "м³", Version: Guid.NewGuid()),
            100m,
            TariffVersion: null);
        var context = CreateContext(request);

        attribute.OnActionExecuting(context);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(context.Result).Value);
        Assert.DoesNotContain("request.Service.Version", problem.Errors.Keys);
        Assert.Contains("request.TariffVersion", problem.Errors.Keys);
    }

    private static ActionExecutingContext CreateContext(object request)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());
        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["request"] = request },
            new object());
    }
}
