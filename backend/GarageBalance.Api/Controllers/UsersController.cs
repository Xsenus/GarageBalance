using System.Security.Claims;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Authorize(Policy = SystemPermissions.UsersManage)]
[Route("api/users")]
public sealed class UsersController(IUserManagementService userManagementService) : ControllerBase
{
    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<ManagedRoleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManagedRoleDto>>> GetRoles(CancellationToken cancellationToken)
    {
        return Ok(await userManagementService.GetRolesAsync(cancellationToken));
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ManagedUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManagedUserDto>>> GetUsers([FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await userManagementService.GetUsersAsync(search, cancellationToken, limit));
    }

    [HttpGet("page")]
    [ProducesResponseType<ManagedUsersPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagedUsersPageDto>> GetUsersPage([FromQuery] string? search, [FromQuery] int offset, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        return Ok(await userManagementService.GetUsersPageAsync(search, offset, limit, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<ManagedUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedUserDto>> CreateUser(CreateManagedUserRequest request, CancellationToken cancellationToken)
    {
        var result = await userManagementService.CreateUserAsync(request, GetActorUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return ToError(result);
        }

        return CreatedAtAction(nameof(GetUsers), new { search = result.Value!.Email }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ManagedUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDto>> UpdateUser(Guid id, UpdateManagedUserRequest request, CancellationToken cancellationToken)
    {
        var result = await userManagementService.UpdateUserAsync(id, request, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType<ManagedUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedUserDto>> RestoreUser(Guid id, CancellationToken cancellationToken)
    {
        var result = await userManagementService.RestoreUserAsync(id, GetActorUserId(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToError(result);
    }

    private Guid? GetActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private ActionResult<T> ToError<T>(UserManagementResult<T> result)
    {
        return result.ErrorCode switch
        {
            "user_not_found" => NotFound(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status404NotFound)),
            "user_email_duplicate" => Conflict(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status409Conflict)),
            _ => BadRequest(ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, StatusCodes.Status400BadRequest))
        };
    }
}
