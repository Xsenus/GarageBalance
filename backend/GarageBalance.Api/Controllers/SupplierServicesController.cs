using System.Security.Claims;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dictionaries/supplier-services")]
public sealed class SupplierServicesController(ISupplierServiceCatalog catalog) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SystemPermissions.DictionariesRead)]
    [ProducesResponseType<PagedResult<SupplierServiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierServiceDto>>> GetPage(
        CancellationToken cancellationToken, string? search = null, int offset = 0, int limit = 25, bool includeArchived = false) =>
        Ok(await catalog.GetPageAsync(search, offset, limit, includeArchived, cancellationToken));

    [HttpPost]
    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [ProducesResponseType<SupplierServiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierServiceDto>> Create(UpsertSupplierServiceRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.CreateAsync(request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? CreatedAtAction(nameof(GetPage), result.Value) : ToProblem(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [RequireConcurrencyVersion("request.Version")]
    [ProducesResponseType<SupplierServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierServiceDto>> Update(Guid id, UpsertSupplierServiceRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.UpdateAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToProblem(result);
    }

    private ObjectResult ToProblem(DictionaryResult<SupplierServiceDto> result)
    {
        var status = result.ErrorCode switch
        {
            "supplier_service_not_found" => StatusCodes.Status404NotFound,
            "supplier_service_duplicate" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(status, ApiProblemDetails.Create(result.ErrorCode!, result.ErrorMessage!, status));
    }

    private Guid? GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
