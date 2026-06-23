using System.Security.Claims;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Auth;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task BootstrapAdmin_ReturnsCreatedForSuccessfulBootstrap()
    {
        var response = CreateResponse();
        var controller = new AuthController(new FakeAuthService
        {
            BootstrapResult = AuthResult<AuthResponse>.Success(response)
        });

        var result = await controller.BootstrapAdmin(new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Admin"), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AuthController.Me), created.ActionName);
        Assert.Same(response, created.Value);
    }

    [Fact]
    public async Task BootstrapAdmin_ReturnsConflictWhenBootstrapIsClosed()
    {
        var controller = new AuthController(new FakeAuthService
        {
            BootstrapResult = AuthResult<AuthResponse>.Failure("bootstrap_closed", "Первый администратор уже создан.")
        });

        var result = await controller.BootstrapAdmin(new BootstrapAdminRequest("admin@example.com", "StrongPass123", "Admin"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("bootstrap_closed", problem.Title);
    }

    [Fact]
    public async Task BootstrapAdmin_ReturnsBadRequestForWeakPassword()
    {
        var controller = new AuthController(new FakeAuthService
        {
            BootstrapResult = AuthResult<AuthResponse>.Failure("password_policy_violation", "Пароль должен содержать хотя бы одну цифру.")
        });

        var result = await controller.BootstrapAdmin(new BootstrapAdminRequest("admin@example.com", "password", "Admin"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("password_policy_violation", problem.Title);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorizedForInvalidCredentials()
    {
        var controller = new AuthController(new FakeAuthService
        {
            LoginResult = AuthResult<AuthResponse>.Failure("invalid_credentials", "Неверный email или пароль.")
        });

        var result = await controller.Login(new LoginRequest("admin@example.com", "wrong"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal("invalid_credentials", problem.Title);
    }

    [Fact]
    public async Task Login_ReturnsForbiddenForInactiveUser()
    {
        var controller = new AuthController(new FakeAuthService
        {
            LoginResult = AuthResult<AuthResponse>.Failure("user_inactive", "Пользователь отключен.")
        });

        var result = await controller.Login(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTooManyRequestsForRateLimitedLogin()
    {
        var controller = new AuthController(new FakeAuthService
        {
            LoginResult = AuthResult<AuthResponse>.Failure("too_many_login_attempts", "Слишком много неуспешных попыток входа. Повторите позже.")
        });

        var result = await controller.Login(new LoginRequest("admin@example.com", "StrongPass123"), CancellationToken.None);

        var tooManyRequests = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, tooManyRequests.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(tooManyRequests.Value);
        Assert.Equal("too_many_login_attempts", problem.Title);
    }

    [Fact]
    public async Task Me_ReturnsCurrentUser()
    {
        var user = CreateResponse().User;
        var controller = new AuthController(new FakeAuthService
        {
            MeResult = AuthResult<CurrentUserDto>.Success(user)
        });
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())]))
        };

        var result = await controller.Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(user, ok.Value);
    }

    [Fact]
    public async Task ChangeOwnPassword_ReturnsCurrentUserForSuccessfulChange()
    {
        var user = CreateResponse().User;
        var controller = new AuthController(new FakeAuthService
        {
            ChangePasswordResult = AuthResult<CurrentUserDto>.Success(user)
        });
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())]))
        };

        var result = await controller.ChangeOwnPassword(
            new ChangeOwnPasswordRequest("StrongPass123", "NewStrongPass123"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(user, ok.Value);
    }

    [Fact]
    public async Task ChangeOwnPassword_ReturnsBadRequestForInvalidCurrentPassword()
    {
        var controller = new AuthController(new FakeAuthService
        {
            ChangePasswordResult = AuthResult<CurrentUserDto>.Failure("invalid_current_password", "Текущий пароль указан неверно.")
        });

        var result = await controller.ChangeOwnPassword(
            new ChangeOwnPasswordRequest("WrongPass123", "NewStrongPass123"),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("invalid_current_password", problem.Title);
    }

    [Fact]
    public async Task ChangeOwnPassword_ReturnsUnauthorizedForInvalidToken()
    {
        var controller = new AuthController(new FakeAuthService
        {
            ChangePasswordResult = AuthResult<CurrentUserDto>.Failure("invalid_token", "Не удалось определить пользователя.")
        });

        var result = await controller.ChangeOwnPassword(
            new ChangeOwnPasswordRequest("StrongPass123", "NewStrongPass123"),
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal("invalid_token", problem.Title);
    }

    private static AuthResponse CreateResponse()
    {
        var user = new CurrentUserDto(Guid.NewGuid(), "admin@example.com", "Admin", ["administrator"], ["users.manage"]);
        return new AuthResponse("token", DateTimeOffset.UtcNow.AddMinutes(15), user);
    }

    private sealed class FakeAuthService : IAuthService
    {
        public AuthResult<AuthResponse> BootstrapResult { get; init; } = AuthResult<AuthResponse>.Failure("not_configured", "Not configured.");
        public AuthResult<AuthResponse> LoginResult { get; init; } = AuthResult<AuthResponse>.Failure("not_configured", "Not configured.");
        public AuthResult<CurrentUserDto> MeResult { get; init; } = AuthResult<CurrentUserDto>.Failure("not_configured", "Not configured.");
        public AuthResult<CurrentUserDto> ChangePasswordResult { get; init; } = AuthResult<CurrentUserDto>.Failure("not_configured", "Not configured.");

        public Task<AuthResult<AuthResponse>> BootstrapAdminAsync(BootstrapAdminRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(BootstrapResult);
        }

        public Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(LoginResult);
        }

        public Task<AuthResult<CurrentUserDto>> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            return Task.FromResult(MeResult);
        }

        public Task<AuthResult<CurrentUserDto>> ChangeOwnPasswordAsync(ClaimsPrincipal principal, ChangeOwnPasswordRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ChangePasswordResult);
        }
    }
}
