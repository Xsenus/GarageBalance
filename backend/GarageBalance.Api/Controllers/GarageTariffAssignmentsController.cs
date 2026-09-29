using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
[Route("api/dictionaries/charge-services/{serviceId:guid}/garage-tariffs")]
public sealed class GarageTariffAssignmentsController(IGarageTariffAssignmentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<GarageTariffAssignmentDto>>> Get(Guid serviceId,
        [FromQuery] Guid? garageId, [FromQuery] bool includeArchived, [FromQuery] int? offset,
        [FromQuery] int? limit, CancellationToken cancellationToken) =>
        Respond(await service.GetPageAsync(serviceId, garageId, includeArchived, offset, limit, cancellationToken));

    [HttpPost]
    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [RequireConcurrencyVersion("request.ServiceVersion")]
    public async Task<ActionResult<IReadOnlyList<GarageTariffAssignmentDto>>> Create(Guid serviceId,
        CreateGarageTariffAssignmentsRequest request, CancellationToken cancellationToken) =>
        Respond(await service.CreateAsync(serviceId, request, Actor(), cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [RequireConcurrencyVersion("request.Version", "request.ServiceVersion")]
    public async Task<ActionResult<GarageTariffAssignmentDto>> Update(Guid serviceId, Guid id,
        UpdateGarageTariffAssignmentRequest request, CancellationToken cancellationToken) =>
        Respond(await service.UpdateAsync(serviceId, id, request, Actor(), cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [RequireConcurrencyVersion("request.Version")]
    public async Task<ActionResult<GarageTariffAssignmentDto>> Archive(Guid serviceId, Guid id,
        [FromBody] ArchiveGarageTariffAssignmentRequest request, CancellationToken cancellationToken) =>
        Respond(await service.ArchiveAsync(serviceId, id, request, Actor(), cancellationToken));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null;

    private ActionResult<T> Respond<T>(DictionaryResult<T> result)
    {
        if (result.Succeeded) return Ok(result.Value);
        var status = result.ErrorCode?.EndsWith("_not_found", StringComparison.Ordinal) == true ? 404
            : result.ErrorCode == "garage_tariff_period_overlap" ? 409 : 400;
        return StatusCode(status, ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, status));
    }
}
