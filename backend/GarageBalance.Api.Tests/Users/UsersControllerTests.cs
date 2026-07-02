using System.Security.Claims;
using GarageBalance.Api.Application.Users;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Users;

public sealed class UsersControllerTests
{
    [Fact]
    public async Task GetUsers_PassesLimitToService()
    {
        var service = new FakeUserManagementService();
        var controller = CreateController(service);

        await controller.GetUsers("admin", 50, CancellationToken.None);

        Assert.Equal(("admin", 50), service.LastUserListRequest);
    }

    [Fact]
    public async Task GetUsersPage_PassesPagingToService()
    {
        var service = new FakeUserManagementService();
        var controller = CreateController(service);

        await controller.GetUsersPage("operator", 25, 10, CancellationToken.None);

        Assert.Equal(("operator", 25, 10), service.LastUserPageRequest);
    }

    [Fact]
    public async Task CreateUser_ReturnsConflictForDuplicateEmail()
    {
        var controller = CreateController(new FakeUserManagementService
        {
            CreateResult = UserManagementResult<ManagedUserDto>.Failure("user_email_duplicate", "Пользователь с таким email уже существует.")
        });

        var result = await controller.CreateUser(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("user_email_duplicate", problem.Title);
    }

    [Fact]
    public async Task UpdateUser_ReturnsNotFoundForMissingUser()
    {
        var controller = CreateController(new FakeUserManagementService
        {
            UpdateResult = UserManagementResult<ManagedUserDto>.Failure("user_not_found", "Пользователь не найден.")
        });

        var result = await controller.UpdateUser(
            Guid.NewGuid(),
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], true, null),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("user_not_found", problem.Title);
    }

    [Fact]
    public async Task UpdateUser_ReturnsBadRequestWhenDeactivationReasonIsMissing()
    {
        var controller = CreateController(new FakeUserManagementService
        {
            UpdateResult = UserManagementResult<ManagedUserDto>.Failure("user_deactivation_reason_required", "Reason is required.")
        });

        var result = await controller.UpdateUser(
            Guid.NewGuid(),
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], false, null),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("user_deactivation_reason_required", problem.Title);
    }

    [Fact]
    public async Task UpdateUser_PassesDeactivationReasonToService()
    {
        var user = CreateUserDto();
        var service = new FakeUserManagementService
        {
            UpdateResult = UserManagementResult<ManagedUserDto>.Success(user)
        };
        var controller = CreateController(service);

        var result = await controller.UpdateUser(
            user.Id,
            new UpdateManagedUserRequest("User", [SystemRoles.Operator], false, null, "Access no longer needed"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Access no longer needed", service.LastUpdateRequest?.DeactivationReason);
    }

    [Fact]
    public async Task RestoreUser_ReturnsRestoredUserAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var user = CreateUserDto() with { IsActive = true };
        var service = new FakeUserManagementService
        {
            RestoreResult = UserManagementResult<ManagedUserDto>.Success(user)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.RestoreUser(user.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(user, ok.Value);
        Assert.Equal(user.Id, service.LastRestoreUserId);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task RestoreUser_ReturnsNotFoundForMissingDisabledUser()
    {
        var controller = CreateController(new FakeUserManagementService
        {
            RestoreResult = UserManagementResult<ManagedUserDto>.Failure("user_not_found", "User not found.")
        });

        var result = await controller.RestoreUser(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("user_not_found", problem.Title);
    }

    [Fact]
    public async Task CreateUser_PassesActorUserIdToService()
    {
        var actorUserId = Guid.NewGuid();
        var user = CreateUserDto();
        var service = new FakeUserManagementService
        {
            CreateResult = UserManagementResult<ManagedUserDto>.Success(user)
        };
        var controller = CreateController(service, actorUserId);

        var result = await controller.CreateUser(
            new CreateManagedUserRequest("user@example.com", "User", "StrongPass123", [SystemRoles.Operator]),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    private static UsersController CreateController(FakeUserManagementService service, Guid? actorUserId = null)
    {
        var controller = new UsersController(service);
        var claims = actorUserId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString()) };
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return controller;
    }

    private static ManagedUserDto CreateUserDto()
    {
        return new ManagedUserDto(Guid.NewGuid(), "user@example.com", "User", true, DateTimeOffset.UtcNow, null, [SystemRoles.Operator], [SystemPermissions.DictionariesRead]);
    }

    private sealed class FakeUserManagementService : IUserManagementService
    {
        public Guid? LastActorUserId { get; private set; }
        public Guid? LastRestoreUserId { get; private set; }
        public (string? Search, int? Limit) LastUserListRequest { get; private set; }
        public (string? Search, int Offset, int Limit) LastUserPageRequest { get; private set; }
        public UpdateManagedUserRequest? LastUpdateRequest { get; private set; }
        public UserManagementResult<ManagedUserDto> CreateResult { get; init; } = UserManagementResult<ManagedUserDto>.Failure("not_configured", "Not configured.");
        public UserManagementResult<ManagedUserDto> UpdateResult { get; init; } = UserManagementResult<ManagedUserDto>.Failure("not_configured", "Not configured.");
        public UserManagementResult<ManagedUserDto> RestoreResult { get; init; } = UserManagementResult<ManagedUserDto>.Failure("not_configured", "Not configured.");

        public Task<IReadOnlyList<ManagedRoleDto>> GetRolesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ManagedRoleDto>>([]);
        }

        public Task<IReadOnlyList<ManagedUserDto>> GetUsersAsync(string? search, CancellationToken cancellationToken, int? limit = null)
        {
            LastUserListRequest = (search, limit);
            return Task.FromResult<IReadOnlyList<ManagedUserDto>>([]);
        }

        public Task<ManagedUsersPageDto> GetUsersPageAsync(string? search, int offset, int limit, CancellationToken cancellationToken)
        {
            LastUserPageRequest = (search, offset, limit);
            return Task.FromResult(new ManagedUsersPageDto([], 0, offset, limit));
        }

        public Task<UserManagementResult<ManagedUserDto>> CreateUserAsync(CreateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            return Task.FromResult(CreateResult);
        }

        public Task<UserManagementResult<ManagedUserDto>> UpdateUserAsync(Guid userId, UpdateManagedUserRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastUpdateRequest = request;
            return Task.FromResult(UpdateResult);
        }

        public Task<UserManagementResult<ManagedUserDto>> RestoreUserAsync(Guid userId, Guid? actorUserId, CancellationToken cancellationToken)
        {
            LastActorUserId = actorUserId;
            LastRestoreUserId = userId;
            return Task.FromResult(RestoreResult);
        }
    }
}
