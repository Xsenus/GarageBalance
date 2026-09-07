using System.Security.Claims;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Security;
using GarageBalance.Api.Tests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class SupplierServicesControllerTests
{
    [Theory]
    [InlineData(nameof(SupplierServicesController.GetPage), SystemPermissions.DictionariesRead)]
    [InlineData(nameof(SupplierServicesController.Create), SystemPermissions.DictionariesWrite)]
    [InlineData(nameof(SupplierServicesController.Update), SystemPermissions.DictionariesWrite)]
    public async Task Actions_RequireTheirPermissionAndDenyOtherPermissions(string methodName, string expectedPermission)
    {
        var method = typeof(SupplierServicesController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(expectedPermission, attribute.Policy);
        var requirement = new PermissionRequirement(attribute.Policy!);
        var denied = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", SystemPermissions.ReportsRead)], "test")), null);
        await new PermissionAuthorizationHandler().HandleAsync(denied);
        Assert.False(denied.HasSucceeded);
        var allowed = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", expectedPermission)], "test")), null);
        await new PermissionAuthorizationHandler().HandleAsync(allowed);
        Assert.True(allowed.HasSucceeded);
    }

    [Fact]
    public void Contract_ExposesOnlyNameAndConcurrencyTokenAndHasADistinctRoute()
    {
        Assert.Equal(["Name", "Version"], typeof(UpsertSupplierServiceRequest).GetProperties().Select(item => item.Name).Order().ToArray());
        Assert.Equal("api/dictionaries/supplier-services", Assert.Single(typeof(SupplierServicesController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Single(typeof(SupplierServicesController).GetMethod(nameof(SupplierServicesController.Update))!.GetCustomAttributes(typeof(RequireConcurrencyVersionAttribute), true));
    }

    [Fact]
    public async Task CreateListAndUpdate_ReturnDtoAndPersistActorInAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var actor = Guid.NewGuid();
        // Audit storage intentionally has no dependency on an HTTP principal or a tariff.
        var controller = CreateController(database.Context, actor.ToString());
        var initial = Assert.IsType<OkObjectResult>((await controller.GetPage(CancellationToken.None)).Result);
        Assert.Empty(Assert.IsType<PagedResult<SupplierServiceDto>>(initial.Value).Items);
        var created = Assert.IsType<CreatedAtActionResult>((await controller.Create(new("Вывоз отходов"), CancellationToken.None)).Result);
        Assert.Equal(nameof(SupplierServicesController.GetPage), created.ActionName);
        var dto = Assert.IsType<SupplierServiceDto>(created.Value);
        var page = Assert.IsType<PagedResult<SupplierServiceDto>>(Assert.IsType<OkObjectResult>((await controller.GetPage(CancellationToken.None, "Вывоз", 0, 10)).Result).Value);
        Assert.Equal(dto, Assert.Single(page.Items));
        var updated = Assert.IsType<SupplierServiceDto>(Assert.IsType<OkObjectResult>((await controller.Update(dto.Id, new("Вывоз по договору", dto.Version), CancellationToken.None)).Result).Value);
        Assert.Equal(dto.Id, updated.Id);
        Assert.Equal("Вывоз по договору", updated.Name);
        Assert.NotEqual(dto.Version, updated.Version);
        Assert.All(database.Context.AuditEvents, item => Assert.Equal(actor, item.ActorUserId));
    }

    [Fact]
    public async Task Errors_MapInvalidDuplicateAndMissingToProblemDetails()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var controller = CreateController(database.Context, "invalid-id");
        var invalidCreate = await controller.Create(new(" "), CancellationToken.None);
        AssertProblem(invalidCreate.Result, 400, "supplier_service_name_invalid");
        var created = Assert.IsType<SupplierServiceDto>(Assert.IsType<CreatedAtActionResult>((await controller.Create(new("Услуга"), CancellationToken.None)).Result).Value);
        AssertProblem((await controller.Create(new("Услуга"), CancellationToken.None)).Result, 409, "supplier_service_duplicate");
        AssertProblem((await controller.Update(created.Id, new(" ", created.Version), CancellationToken.None)).Result, 400, "supplier_service_name_invalid");
        AssertProblem((await controller.Update(Guid.NewGuid(), new("Не найдена"), CancellationToken.None)).Result, 404, "supplier_service_not_found");
        var second = Assert.IsType<SupplierServiceDto>(Assert.IsType<CreatedAtActionResult>((await controller.Create(new("Вторая"), CancellationToken.None)).Result).Value);
        AssertProblem((await controller.Update(second.Id, new("Услуга", second.Version), CancellationToken.None)).Result, 409, "supplier_service_duplicate");
        Assert.All(database.Context.AuditEvents, item => Assert.Null(item.ActorUserId));
    }

    private static void AssertProblem(ActionResult? result, int status, string code)
    {
        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, Assert.IsType<ProblemDetails>(error.Value).Extensions["code"]);
    }

    private static SupplierServicesController CreateController(GarageBalanceDbContext context, string actor) =>
        new(new SupplierServiceCatalog(new EfSupplierServiceRepository(context), new EfFundRepository(context), new EfApplicationUnitOfWork(context), new AuditEventWriter(context)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor)], "test")) }
            }
        };
}
