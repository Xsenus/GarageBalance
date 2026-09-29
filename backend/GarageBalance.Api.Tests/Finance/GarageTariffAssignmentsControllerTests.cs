using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageTariffAssignmentsControllerTests
{
    [Theory]
    [InlineData("Get", null, 200)]
    [InlineData("Create", null, 200)]
    [InlineData("Update", null, 200)]
    [InlineData("Archive", null, 200)]
    [InlineData("Get", "charge_service_not_found", 404)]
    [InlineData("Create", "garage_not_found", 404)]
    [InlineData("Update", "garage_tariff_not_found", 404)]
    [InlineData("Archive", "garage_tariff_not_found", 404)]
    [InlineData("Get", "garage_tariff_filter_invalid", 400)]
    [InlineData("Create", "garage_tariff_terms_invalid", 400)]
    [InlineData("Update", "garage_tariff_terms_invalid", 400)]
    [InlineData("Archive", "action_reason_required", 400)]
    [InlineData("Create", "garage_tariff_period_overlap", 409)]
    [InlineData("Update", "garage_tariff_period_overlap", 409)]
    public async Task ActionsForwardArgumentsActorAndCancellationAndMapResults(string action, string? error, int status)
    {
        var fake = new FakeService { Error = error };
        var actor = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var garageId = Guid.NewGuid();
        var controller = new GarageTariffAssignmentsController(fake)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.ToString())])) } }
        };
        using var cancellation = new CancellationTokenSource();
        var version = Guid.NewGuid();
        var create = new CreateGarageTariffAssignmentsRequest([garageId], new(2050, 9, 1), null, 100, null, null, version, "Причина");
        var update = new UpdateGarageTariffAssignmentRequest(create.EffectiveFrom, null, 200, null, null, version, version, "Причина");
        var archive = new ArchiveGarageTariffAssignmentRequest(version, "Причина");
        var result = action switch
        {
            "Get" => (await controller.Get(serviceId, garageId, true, 25, 25, cancellation.Token)).Result,
            "Create" => (await controller.Create(serviceId, create, cancellation.Token)).Result,
            "Update" => (await controller.Update(serviceId, id, update, cancellation.Token)).Result,
            _ => (await controller.Archive(serviceId, id, archive, cancellation.Token)).Result
        };
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(serviceId, fake.ServiceId);
        Assert.Equal(cancellation.Token, fake.Token);
        if (action != "Get") Assert.Equal(actor, fake.Actor);
        if (action is "Update" or "Archive") Assert.Equal(id, fake.Id);
        Assert.Equal(action switch { "Create" => (object)create, "Update" => update, "Archive" => archive, _ => ((Guid?)garageId, true, (int?)25, (int?)25) }, fake.Request);
        if (error is not null)
        {
            var problem = Assert.IsType<ProblemDetails>(response.Value);
            Assert.Equal(status, problem.Status);
        }
        else if (action == "Get") Assert.IsType<PagedResult<GarageTariffAssignmentDto>>(response.Value);
        else if (action == "Create") Assert.IsAssignableFrom<IReadOnlyList<GarageTariffAssignmentDto>>(response.Value);
        else Assert.IsType<GarageTariffAssignmentDto>(response.Value);
    }

    [Theory]
    [InlineData("Create", false)]
    [InlineData("Create", true)]
    [InlineData("Update", false)]
    [InlineData("Update", true)]
    [InlineData("Archive", false)]
    [InlineData("Archive", true)]
    public void MutationFiltersRejectMissingVersions(string action, bool valid)
    {
        var version = valid ? Guid.NewGuid() : Guid.Empty;
        object request = action switch
        {
            "Create" => new CreateGarageTariffAssignmentsRequest([Guid.NewGuid()], new(2050, 9, 1), null, 100, null, null, version, "Причина"),
            "Update" => new UpdateGarageTariffAssignmentRequest(new(2050, 9, 1), null, 100, null, null, version, version, "Причина"),
            _ => new ArchiveGarageTariffAssignmentRequest(version, "Причина")
        };
        var filter = typeof(GarageTariffAssignmentsController).GetMethod(action)!.GetCustomAttributes(false).OfType<ActionFilterAttribute>().Single();
        var controller = new GarageTariffAssignmentsController(new FakeService());
        var context = new ActionExecutingContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?> { ["request"] = request }, controller);
        filter.OnActionExecuting(context);
        if (valid) Assert.Null(context.Result);
        else Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    private sealed class FakeService : IGarageTariffAssignmentService
    {
        public string? Error { get; init; }
        public Guid ServiceId { get; private set; }
        public Guid? Id { get; private set; }
        public object? Request { get; private set; }
        public Guid? Actor { get; private set; }
        public CancellationToken Token { get; private set; }
        private static GarageTariffAssignmentDto Dto => new(Guid.NewGuid(), Guid.NewGuid(), "85", Guid.NewGuid(), Guid.NewGuid(), "fixed", 100, [], new(2050, 9, 1), null, null, false, Guid.NewGuid());
        private Task<DictionaryResult<T>> Respond<T>(Guid serviceId, Guid? id, object request, Guid? actor, CancellationToken token, T value)
        {
            (ServiceId, Id, Request, Actor, Token) = (serviceId, id, request, actor, token);
            return Task.FromResult(Error is null ? DictionaryResult<T>.Success(value) : DictionaryResult<T>.Failure(Error, "Ошибка проверки"));
        }
        public Task<DictionaryResult<PagedResult<GarageTariffAssignmentDto>>> GetPageAsync(Guid serviceId, Guid? garageId, bool includeArchived, int? offset, int? limit, CancellationToken cancellationToken) => Respond(serviceId, null, (garageId, includeArchived, offset, limit), null, cancellationToken, new PagedResult<GarageTariffAssignmentDto>([Dto], 1, offset ?? 0, limit ?? 25));
        public Task<DictionaryResult<IReadOnlyList<GarageTariffAssignmentDto>>> CreateAsync(Guid serviceId, CreateGarageTariffAssignmentsRequest request, Guid? actor, CancellationToken cancellationToken) => Respond<IReadOnlyList<GarageTariffAssignmentDto>>(serviceId, null, request, actor, cancellationToken, [Dto]);
        public Task<DictionaryResult<GarageTariffAssignmentDto>> UpdateAsync(Guid serviceId, Guid id, UpdateGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken) => Respond(serviceId, id, request, actor, cancellationToken, Dto);
        public Task<DictionaryResult<GarageTariffAssignmentDto>> ArchiveAsync(Guid serviceId, Guid id, ArchiveGarageTariffAssignmentRequest request, Guid? actor, CancellationToken cancellationToken) => Respond(serviceId, id, request, actor, cancellationToken, Dto);
    }
}
