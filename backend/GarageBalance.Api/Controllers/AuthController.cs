using GarageBalance.Api.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("bootstrap-admin")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> BootstrapAdmin(BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.BootstrapAdminAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorCode == "password_policy_violation")
            {
                return BadRequest(ToProblem(result, StatusCodes.Status400BadRequest));
            }

            return Conflict(ToProblem(result, StatusCodes.Status409Conflict));
        }

        return CreatedAtAction(nameof(Me), result.Value);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        if (result.ErrorCode == "user_inactive")
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToProblem(result, StatusCodes.Status403Forbidden));
        }

        if (result.ErrorCode == "too_many_login_attempts")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ToProblem(result, StatusCodes.Status429TooManyRequests));
        }

        return Unauthorized(ToProblem(result, StatusCodes.Status401Unauthorized));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserAsync(User, cancellationToken);
        if (!result.Succeeded)
        {
            return Unauthorized(ToProblem(result, StatusCodes.Status401Unauthorized));
        }

        return Ok(result.Value);
    }

    [Authorize]
    [HttpPut("me/password")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> ChangeOwnPassword(ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ChangeOwnPasswordAsync(User, request, cancellationToken);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        if (result.ErrorCode is "invalid_token" or "user_not_found")
        {
            return Unauthorized(ToProblem(result, StatusCodes.Status401Unauthorized));
        }

        return BadRequest(ToProblem(result, StatusCodes.Status400BadRequest));
    }

    private static ProblemDetails ToProblem<T>(AuthResult<T> result, int statusCode)
    {
        return ApiProblemDetails.Create(result.ErrorCode, result.ErrorMessage, statusCode);
    }
}
