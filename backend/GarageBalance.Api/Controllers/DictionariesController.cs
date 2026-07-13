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
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> GetOwners([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetOwnersAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("owners/page")]
    [ProducesResponseType<PagedResult<OwnerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OwnerDto>>> GetOwnersPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetOwnersPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveOwner(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveOwnerAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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
    public async Task<ActionResult<IReadOnlyList<GarageDto>>> GetGarages([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetGaragesAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("garages/page")]
    [ProducesResponseType<PagedResult<GarageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<GarageDto>>> GetGaragesPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetGaragesPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveGarage(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveGarageAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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
    public async Task<ActionResult<IReadOnlyList<SupplierGroupDto>>> GetSupplierGroups([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierGroupsAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("supplier-groups/page")]
    [ProducesResponseType<PagedResult<SupplierGroupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierGroupDto>>> GetSupplierGroupsPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierGroupsPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveSupplierGroup(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveSupplierGroupAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetSuppliers([FromQuery] Guid? groupId, [FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSuppliersAsync(groupId, search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("suppliers/page")]
    [ProducesResponseType<PagedResult<SupplierDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetSuppliersPage([FromQuery] Guid? groupId, [FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSuppliersPageAsync(groupId, search, offset, limit, cancellationToken, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("suppliers")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(Guid id, UpsertSupplierRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateSupplierAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("suppliers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveSupplier(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveSupplierAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("suppliers/{id:guid}/restore")]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> RestoreSupplier(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreSupplierAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("supplier-contacts")]
    [ProducesResponseType<IReadOnlyList<SupplierContactDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierContactDto>>> GetSupplierContacts([FromQuery] Guid? supplierId, [FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetSupplierContactsAsync(supplierId, search, cancellationToken, limit, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("supplier-contacts")]
    [ProducesResponseType<SupplierContactDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierContactDto>> CreateSupplierContact(UpsertSupplierContactRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateSupplierContactAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetSupplierContacts), new { supplierId = result.Value!.SupplierId }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("supplier-contacts/{id:guid}")]
    [ProducesResponseType<SupplierContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierContactDto>> UpdateSupplierContact(Guid id, UpsertSupplierContactRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateSupplierContactAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("supplier-contacts/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveSupplierContact(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveSupplierContactAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("supplier-contacts/{id:guid}/restore")]
    [ProducesResponseType<SupplierContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierContactDto>> RestoreSupplierContact(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreSupplierContactAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("staff-departments")]
    [ProducesResponseType<IReadOnlyList<StaffDepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffDepartmentDto>>> GetStaffDepartments([FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetStaffDepartmentsAsync(cancellationToken, limit, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("staff-departments")]
    [ProducesResponseType<StaffDepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffDepartmentDto>> CreateStaffDepartment(UpsertStaffDepartmentRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateStaffDepartmentAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetStaffDepartments), result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("staff-departments/{id:guid}")]
    [ProducesResponseType<StaffDepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffDepartmentDto>> UpdateStaffDepartment(Guid id, UpsertStaffDepartmentRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateStaffDepartmentAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("staff-departments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveStaffDepartment(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveStaffDepartmentAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("staff-departments/{id:guid}/restore")]
    [ProducesResponseType<StaffDepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffDepartmentDto>> RestoreStaffDepartment(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreStaffDepartmentAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("staff-members")]
    [ProducesResponseType<IReadOnlyList<StaffMemberDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffMemberDto>>> GetStaffMembers([FromQuery] Guid? departmentId, [FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetStaffMembersAsync(departmentId, search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("staff-members/page")]
    [ProducesResponseType<PagedResult<StaffMemberDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StaffMemberDto>>> GetStaffMembersPage([FromQuery] Guid? departmentId, [FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] string? sortBy, [FromQuery] string? sortDirection, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetStaffMembersPageAsync(departmentId, search, offset, limit, sortBy, sortDirection, cancellationToken, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("staff-members")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffMemberDto>> CreateStaffMember(UpsertStaffMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateStaffMemberAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetStaffMembers), new { departmentId = result.Value!.DepartmentId }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPut("staff-members/{id:guid}")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffMemberDto>> UpdateStaffMember(Guid id, UpsertStaffMemberRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateStaffMemberAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpDelete("staff-members/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveStaffMember(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveStaffMemberAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.DictionariesWrite)]
    [HttpPost("staff-members/{id:guid}/restore")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffMemberDto>> RestoreStaffMember(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreStaffMemberAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("income-types")]
    [ProducesResponseType<IReadOnlyList<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetIncomeTypes([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIncomeTypesAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("income-types/page")]
    [ProducesResponseType<PagedResult<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountingTypeDto>>> GetIncomeTypesPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIncomeTypesPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveIncomeType(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveIncomeTypeAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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
    public async Task<ActionResult<IReadOnlyList<AccountingTypeDto>>> GetExpenseTypes([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetExpenseTypesAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("expense-types/page")]
    [ProducesResponseType<PagedResult<AccountingTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountingTypeDto>>> GetExpenseTypesPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetExpenseTypesPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveExpenseType(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveExpenseTypeAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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
    public async Task<ActionResult<IReadOnlyList<TariffDto>>> GetTariffs([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetTariffsAsync(search, cancellationToken, limit, includeArchived));
    }

    [HttpGet("tariffs/page")]
    [ProducesResponseType<PagedResult<TariffDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TariffDto>>> GetTariffsPage([FromQuery] string? search, [FromQuery] int? offset, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetTariffsPageAsync(search, offset, limit, cancellationToken, includeArchived));
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveTariff(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveTariffAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
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

    [HttpGet("charge-services")]
    [ProducesResponseType<IReadOnlyList<ChargeServiceSettingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChargeServiceSettingDto>>> GetChargeServiceSettings([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetChargeServiceSettingsAsync(search, cancellationToken, limit, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("charge-services")]
    [ProducesResponseType<ChargeServiceSettingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChargeServiceSettingDto>> CreateChargeServiceSetting(UpsertChargeServiceSettingRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateChargeServiceSettingAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetChargeServiceSettings), new { search = result.Value!.Name }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPut("charge-services/{id:guid}")]
    [ProducesResponseType<ChargeServiceSettingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChargeServiceSettingDto>> UpdateChargeServiceSetting(Guid id, UpsertChargeServiceSettingRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateChargeServiceSettingAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpDelete("charge-services/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveChargeServiceSetting(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveChargeServiceSettingAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("charge-services/{id:guid}/restore")]
    [ProducesResponseType<ChargeServiceSettingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChargeServiceSettingDto>> RestoreChargeServiceSetting(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreChargeServiceSettingAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("irregular-payments")]
    [ProducesResponseType<IReadOnlyList<IrregularPaymentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IrregularPaymentDto>>> GetIrregularPayments([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetIrregularPaymentsAsync(search, cancellationToken, limit, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("irregular-payments")]
    [ProducesResponseType<IrregularPaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IrregularPaymentDto>> CreateIrregularPayment(UpsertIrregularPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateIrregularPaymentAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetIrregularPayments), new { search = result.Value!.Name }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPut("irregular-payments/{id:guid}")]
    [ProducesResponseType<IrregularPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IrregularPaymentDto>> UpdateIrregularPayment(Guid id, UpsertIrregularPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateIrregularPaymentAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("irregular-payments/{id:guid}/status")]
    [ProducesResponseType<IrregularPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IrregularPaymentDto>> SetIrregularPaymentStatus(Guid id, UpdateIrregularPaymentStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.SetIrregularPaymentStatusAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpDelete("irregular-payments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArchiveIrregularPayment(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveIrregularPaymentAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("irregular-payments/{id:guid}/restore")]
    [ProducesResponseType<IrregularPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IrregularPaymentDto>> RestoreIrregularPayment(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreIrregularPaymentAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("fee-campaigns")]
    [ProducesResponseType<IReadOnlyList<FeeCampaignDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FeeCampaignDto>>> GetFeeCampaigns([FromQuery] string? search, [FromQuery] int? limit, [FromQuery] bool includeArchived, CancellationToken cancellationToken)
    {
        return Ok(await dictionaryService.GetFeeCampaignsAsync(search, cancellationToken, limit, includeArchived));
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("fee-campaigns")]
    [ProducesResponseType<FeeCampaignDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeeCampaignDto>> CreateFeeCampaign(UpsertFeeCampaignRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.CreateFeeCampaignAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetFeeCampaigns), new { search = result.Value!.Name }, result.Value);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPut("fee-campaigns/{id:guid}")]
    [ProducesResponseType<FeeCampaignDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeeCampaignDto>> UpdateFeeCampaign(Guid id, UpsertFeeCampaignRequest request, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.UpdateFeeCampaignAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpDelete("fee-campaigns/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveFeeCampaign(Guid id, [FromBody] ArchiveDictionaryEntryRequest? request, CancellationToken cancellationToken)
    {
        if (ValidateArchiveRequest(request) is { } validationError)
        {
            return validationError;
        }

        var result = await dictionaryService.ArchiveFeeCampaignAsync(id, request!.Reason, GetActorUserId(), cancellationToken);
        return result.Succeeded ? NoContent() : ToError(result).Result!;
    }

    [Authorize(Policy = SystemPermissions.TariffsManage)]
    [HttpPost("fee-campaigns/{id:guid}/restore")]
    [ProducesResponseType<FeeCampaignDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeeCampaignDto>> RestoreFeeCampaign(Guid id, CancellationToken cancellationToken)
    {
        var result = await dictionaryService.RestoreFeeCampaignAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult? ValidateArchiveRequest(ArchiveDictionaryEntryRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest(ApiProblemDetails.Create("dictionary_archive_reason_required", "Укажите причину удаления записи.", StatusCodes.Status400BadRequest));
        }

        return null;
    }

    private ActionResult<T> ToError<T>(DictionaryResult<T> result)
    {
        return result.ErrorCode switch
        {
            "owner_not_found" or "garage_not_found" or "supplier_group_not_found" or "supplier_not_found" or "supplier_contact_not_found" or "staff_department_not_found" or "staff_member_not_found" or "income_type_not_found" or "expense_type_not_found" or "tariff_not_found" or "charge_service_not_found" or "irregular_payment_not_found" or "fee_campaign_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "garage_number_duplicate" or "supplier_group_duplicate" or "supplier_duplicate" or "supplier_group_system" or "staff_department_duplicate" or "staff_department_used" or "income_type_duplicate" or "income_type_system" or "expense_type_duplicate" or "expense_type_system" or "tariff_duplicate" or "tariff_effective_from_after_accrual" or "charge_service_duplicate" or "irregular_payment_duplicate" or "irregular_payment_used" or "fee_campaign_duplicate" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
