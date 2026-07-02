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
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> GetOwners([FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetOwnersAsync(search, cancellationToken, limit));
    }

    [HttpGet("owners/page")]
    [ProducesResponseType<PagedResult<OwnerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OwnerDto>>> GetOwnersPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetOwnersPageAsync(search, offset, limit, cancellationToken));
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("owners/{id:guid}/restore")]
    [ProducesResponseType<OwnerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OwnerDto>> RestoreOwner(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreOwnerAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("garages")]
    [ProducesResponseType<IReadOnlyList<GarageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GarageDto>>> GetGarages([FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetGaragesAsync(search, cancellationToken, limit));
    }

    [HttpGet("garages/page")]
    [ProducesResponseType<PagedResult<GarageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<GarageDto>>> GetGaragesPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetGaragesPageAsync(search, offset, limit, cancellationToken));
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("garages/{id:guid}/restore")]
    [ProducesResponseType<GarageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GarageDto>> RestoreGarage(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreGarageAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("supplier-groups")]
    [ProducesResponseType<IReadOnlyList<SupplierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierGroupDto>>> GetSupplierGroups([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierGroupsAsync(cancellationToken, limit));
    }

    [HttpGet("supplier-groups/page")]
    [ProducesResponseType<PagedResult<SupplierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierGroupDto>>> GetSupplierGroupsPage([FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierGroupsPageAsync(offset, limit, cancellationToken));
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
    [HttpPut("supplier-groups/{id:guid}")]
    [ProducesResponseType<SupplierGroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierGroupDto>> UpdateSupplierGroup(Guid id, UpsertSupplierGroupRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateSupplierGroupAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("supplier-groups/{id:guid}/restore")]
    [ProducesResponseType<SupplierGroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierGroupDto>> RestoreSupplierGroup(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreSupplierGroupAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("suppliers")]
    [ProducesResponseType<IReadOnlyList<SupplierDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetSuppliers([FromQuery] Guid? groupId, [FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSuppliersAsync(groupId, search, cancellationToken, limit));
    }

    [HttpGet("suppliers/page")]
    [ProducesResponseType<PagedResult<SupplierDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetSuppliersPage([FromQuery] Guid? groupId, [FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSuppliersPageAsync(groupId, search, offset, limit, cancellationToken));
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("suppliers/{id:guid}/restore")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> RestoreSupplier(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreSupplierAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("income-types")]
    [ProducesResponseType<IReadOnlyList<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetIncomeTypes([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIncomeTypesAsync(cancellationToken, limit));
    }

    [HttpGet("income-types/page")]
    [ProducesResponseType<PagedResult<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountingTypeDto>>> GetIncomeTypesPage([FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIncomeTypesPageAsync(offset, limit, cancellationToken));
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
    [HttpPut("income-types/{id:guid}")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> UpdateIncomeType(Guid id, UpsertAccountingTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateIncomeTypeAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("income-types/{id:guid}/restore")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> RestoreIncomeType(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreIncomeTypeAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("expense-types")]
    [ProducesResponseType<IReadOnlyList<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetExpenseTypes([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetExpenseTypesAsync(cancellationToken, limit));
    }

    [HttpGet("expense-types/page")]
    [ProducesResponseType<PagedResult<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountingTypeDto>>> GetExpenseTypesPage([FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetExpenseTypesPageAsync(offset, limit, cancellationToken));
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
    [HttpPut("expense-types/{id:guid}")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> UpdateExpenseType(Guid id, UpsertAccountingTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateExpenseTypeAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
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

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("expense-types/{id:guid}/restore")]
    [ProducesResponseType<AccountingTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountingTypeDto>> RestoreExpenseType(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreExpenseTypeAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("tariffs")]
    [ProducesResponseType<IReadOnlyList<TariffDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TariffDto>>> GetTariffs([FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetTariffsAsync(search, cancellationToken, limit));
    }

    [HttpGet("tariffs/page")]
    [ProducesResponseType<PagedResult<TariffDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TariffDto>>> GetTariffsPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetTariffsPageAsync(search, offset, limit, cancellationToken));
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

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("tariffs/{id:guid}/restore")]
    [ProducesResponseType<TariffDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TariffDto>> RestoreTariff(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreTariffAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
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
            "garage_number_duplicate" or "supplier_group_duplicate" or "supplier_group_system" or "income_type_duplicate" or "income_type_system" or "expense_type_duplicate" or "expense_type_system" or "tariff_duplicate" or "tariff_effective_from_after_accrual" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
