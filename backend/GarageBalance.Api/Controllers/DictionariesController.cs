using System.Security.Claims;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.DictionariesRead)]
[Route("api/dictionaries")]
public sealed class DictionariesController(IDictionaryService dictionaryService) : ControllerBase
{
    [HttpGet("owners")]
    [ProducesResponseType<IReadOnlyList<OwnerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> GetOwners([FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetOwnersAsync(search, cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("owners")]
    [ProducesResponseType<OwnerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OwnerDto>> CreateOwner(UpsertOwnerRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateOwnerAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetOwners), new { search = result.Value!.LastName }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("owners/{id:guid}")]
    [ProducesResponseType<OwnerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OwnerDto>> UpdateOwner(Guid id, UpsertOwnerRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateOwnerAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("owners/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveOwner(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveOwnerAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("garages")]
    [ProducesResponseType<IReadOnlyList<GarageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GarageDto>>> GetGarages([FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetGaragesAsync(search, cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("garages")]
    [ProducesResponseType<GarageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageDto>> CreateGarage(UpsertGarageRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateGarageAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetGarages), new { search = result.Value!.Number }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("garages/{id:guid}")]
    [ProducesResponseType<GarageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageDto>> UpdateGarage(Guid id, UpsertGarageRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateGarageAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("garages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveGarage(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveGarageAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("supplier-groups")]
    [ProducesResponseType<IReadOnlyList<SupplierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierGroupDto>>> GetSupplierGroups(CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierGroupsAsync(cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("supplier-groups")]
    [ProducesResponseType<SupplierGroupDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierGroupDto>> CreateSupplierGroup(UpsertSupplierGroupRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateSupplierGroupAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetSupplierGroups), result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("supplier-groups/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveSupplierGroup(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveSupplierGroupAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("suppliers")]
    [ProducesResponseType<IReadOnlyList<SupplierDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetSuppliers([FromQuery] Guid? groupId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSuppliersAsync(groupId, search, cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("suppliers")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(UpsertSupplierRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateSupplierAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetSuppliers), new { groupId = result.Value!.GroupId, search = result.Value.Name }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("suppliers/{id:guid}")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(Guid id, UpsertSupplierRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateSupplierAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("suppliers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveSupplier(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveSupplierAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("income-types")]
    [ProducesResponseType<IReadOnlyList<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetIncomeTypes(CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIncomeTypesAsync(cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("income-types")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> CreateIncomeType(UpsertAccountingTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateIncomeTypeAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetIncomeTypes), result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("income-types/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveIncomeType(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveIncomeTypeAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("expense-types")]
    [ProducesResponseType<IReadOnlyList<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetExpenseTypes(CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetExpenseTypesAsync(cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("expense-types")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> CreateExpenseType(UpsertAccountingTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateExpenseTypeAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetExpenseTypes), result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("expense-types/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveExpenseType(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveExpenseTypeAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [HttpGet("tariffs")]
    [ProducesResponseType<IReadOnlyList<TariffDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TariffDto>>> GetTariffs([FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetTariffsAsync(search, cancellationToken));
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("tariffs")]
    [ProducesResponseType<TariffDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TariffDto>> CreateTariff(UpsertTariffRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateTariffAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetTariffs), new { search = result.Value!.Name }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPut("tariffs/{id:guid}")]
    [ProducesResponseType<TariffDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TariffDto>> UpdateTariff(Guid id, UpsertTariffRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateTariffAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpDelete("tariffs/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveTariff(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.ArchiveTariffAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(DictionaryResult<T> result)
    {
        return result.ErrorCode switch
        {
            "owner_not_found" or "garage_not_found" or "supplier_group_not_found" or "supplier_not_found" or "income_type_not_found" or "expense_type_not_found" or "tariff_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "garage_number_duplicate" or "supplier_group_duplicate" or "supplier_group_system" or "income_type_duplicate" or "income_type_system" or "expense_type_duplicate" or "expense_type_system" or "tariff_duplicate" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
