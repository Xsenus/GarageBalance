using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Contracts.Settings;
using GarageBalance.Api.Controllers;
using GarageBalance.Api.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Settings;

public sealed class SettingsControllerTests
{
    [Fact]
    public void PaymentDisplayActions_RequireReadAndAdminPermissions()
    {
        var getAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetPaymentDisplaySettings));
        var updateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.UpdatePaymentDisplaySettings));
        var backupStatusAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetDatabaseBackups));
        var backupCreateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.CreateDatabaseBackup));

        Assert.Equal(SystemPermissions.PaymentsRead, Assert.Single(getAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(updateAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupStatusAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupCreateAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsServiceValue()
    {
        var service = new FakeService { Current = new PaymentDisplaySettingsDto(false) };
        var controller = new SettingsController(service, new FakeBackupService());

        var result = await controller.GetPaymentDisplaySettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(service.Current, ok.Value);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_PassesActorAndReturnsUpdatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeService();
        var controller = new SettingsController(service, new FakeBackupService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
                }
            }
        };
        var request = new UpdatePaymentDisplaySettingsRequest(true);

        var result = await controller.UpdatePaymentDisplaySettings(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<PaymentDisplaySettingsDto>(ok.Value);
        Assert.True(dto.ShowAllGarageOperationsByDefault);
        Assert.Same(request, service.ReceivedRequest);
        Assert.Equal(actorUserId, service.ReceivedActorUserId);
    }

    [Fact]
    public async Task GetDatabaseBackups_ReturnsBoundedStatusFromService()
    {
        var backupService = new FakeBackupService();
        var controller = new SettingsController(new FakeService(), backupService);

        var result = await controller.GetDatabaseBackups(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(backupService.Status, ok.Value);
    }

    [Fact]
    public async Task CreateDatabaseBackup_PassesActorReasonAndReturnsCreatedFile()
    {
        var actorUserId = Guid.NewGuid();
        var backupService = new FakeBackupService();
        var controller = new SettingsController(new FakeService(), backupService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
                }
            }
        };

        var result = await controller.CreateDatabaseBackup(new CreateDatabaseBackupRequest("Перед обновлением"), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(backupService.CreatedFile, created.Value);
        Assert.Equal(DatabaseBackupKind.Manual, backupService.ReceivedKind);
        Assert.Equal("Перед обновлением", backupService.ReceivedReason);
        Assert.Equal(actorUserId, backupService.ReceivedActorUserId);
    }

    [Theory]
    [InlineData("database_backup_in_progress", StatusCodes.Status409Conflict)]
    [InlineData("database_backup_disabled", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("database_backup_tools_unavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("database_backup_reason_required", StatusCodes.Status400BadRequest)]
    public async Task CreateDatabaseBackup_MapsServiceFailuresToSafeProblemDetails(string errorCode, int expectedStatus)
    {
        var backupService = new FakeBackupService
        {
            CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure(errorCode, "Безопасное сообщение.")
        };
        var controller = new SettingsController(new FakeService(), backupService);

        var result = await controller.CreateDatabaseBackup(new CreateDatabaseBackupRequest("Причина"), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal(errorCode, details.Title);
        Assert.Equal("Безопасное сообщение.", details.Detail);
    }

    private sealed class FakeService : IApplicationSettingsService
    {
        public PaymentDisplaySettingsDto Current { get; set; } = new(false);
        public UpdatePaymentDisplaySettingsRequest? ReceivedRequest { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }

        public Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(UpdatePaymentDisplaySettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedRequest = request;
            ReceivedActorUserId = actorUserId;
            Current = new PaymentDisplaySettingsDto(request.ShowAllGarageOperationsByDefault);
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeBackupService : IDatabaseBackupService
    {
        public DatabaseBackupFileDto CreatedFile { get; } = new("garagebalance_manual_20260715_120000_000.pgdump", 1024, DateTimeOffset.UtcNow, "manual");
        public DatabaseBackupStatusDto Status { get; } = new(true, true, 24, 30, "/backups", false, null, null, []);
        public DatabaseBackupResult<DatabaseBackupFileDto>? CreateResult { get; set; }
        public DatabaseBackupKind? ReceivedKind { get; private set; }
        public string? ReceivedReason { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }

        public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(DatabaseBackupKind kind, string? reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedKind = kind;
            ReceivedReason = reason;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(CreateResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(CreatedFile));
        }
    }
}
