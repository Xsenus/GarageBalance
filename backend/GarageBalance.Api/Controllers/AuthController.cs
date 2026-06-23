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
            return Conflict(ToProblem(result));
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
            return StatusCode(StatusCodes.Status403Forbidden, ToProblem(result));
        }

        if (result.ErrorCode == "too_many_login_attempts")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ToProblem(result));
        }

        return Unauthorized(ToProblem(result));
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
            return Unauthorized(ToProblem(result));
        }

        return Ok(result.Value);
    }

    private static ProblemDetails ToProblem<T>(AuthResult<T> result)
    {
        return new ProblemDetails
        {
            Title = result.ErrorCode,
            Detail = result.ErrorMessage
        };
    }
}
