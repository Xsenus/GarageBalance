using System.Reflection;
using System.Security.Claims;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Finance;
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
        var backupDownloadAction = typeof(SettingsController).GetMethod(nameof(SettingsController.DownloadDatabaseBackup));
        var backupDeleteAction = typeof(SettingsController).GetMethod(nameof(SettingsController.DeleteDatabaseBackup));
        var getBusinessDateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetBusinessDateSettings));
        var previewBusinessDateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.PreviewBusinessDateChange));
        var updateBusinessDateAction = typeof(SettingsController).GetMethod(nameof(SettingsController.UpdateBusinessDateSettings));
        var getSalaryAccrualAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetSalaryAccrualSettings));
        var updateSalaryAccrualAction = typeof(SettingsController).GetMethod(nameof(SettingsController.UpdateSalaryAccrualSettings));
        var getCashBankBalancesAction = typeof(SettingsController).GetMethod(nameof(SettingsController.GetCashBankBalances));
        var updateOpeningBalancesAction = typeof(SettingsController).GetMethod(nameof(SettingsController.UpdateCashBankOpeningBalances));
        var createAdjustmentAction = typeof(SettingsController).GetMethod(nameof(SettingsController.CreateCashBankBalanceAdjustment));

        Assert.Equal(SystemPermissions.PaymentsRead, Assert.Single(getAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(updateAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupStatusAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupCreateAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupDownloadAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(backupDeleteAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(getBusinessDateAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(previewBusinessDateAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(updateBusinessDateAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Equal(SystemPermissions.PaymentsRead, Assert.Single(getSalaryAccrualAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemPermissions.UsersManage, Assert.Single(updateSalaryAccrualAction!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(getCashBankBalancesAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(updateOpeningBalancesAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Equal(SystemRoles.Administrator, Assert.Single(createAdjustmentAction!.GetCustomAttributes<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task GetPaymentDisplaySettings_ReturnsServiceValue()
    {
        var service = new FakeService { Current = new PaymentDisplaySettingsDto(false) };
        var controller = CreateController(service);

        var result = await controller.GetPaymentDisplaySettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(service.Current, ok.Value);
    }

    [Fact]
    public async Task GetSalaryAccrualSettings_ReturnsServiceValue()
    {
        var service = new FakeService();
        var controller = CreateController(service);

        var result = await controller.GetSalaryAccrualSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(10, Assert.IsType<SalaryAccrualSettingsDto>(ok.Value).AccrualDay);
    }

    [Fact]
    public async Task UpdatePaymentDisplaySettings_PassesActorAndReturnsUpdatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeService();
        var controller = CreateController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
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
        var controller = CreateController(backupService: backupService);

        var result = await controller.GetDatabaseBackups(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(backupService.Status, ok.Value);
    }

    [Fact]
    public async Task UpdateBusinessDate_PassesActorAndReturnsUpdatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeService();
        var controller = CreateController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
            }
        };
        var request = new UpdateBusinessDateRequest(new DateOnly(2026, 8, 5));

        var result = await controller.UpdateBusinessDateSettings(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BusinessDateSettingsDto>(ok.Value);
        Assert.Equal(new DateOnly(2026, 8, 5), dto.EffectiveDate);
        Assert.Same(request, service.ReceivedBusinessDateRequest);
        Assert.Equal(actorUserId, service.ReceivedActorUserId);
    }

    [Fact]
    public async Task PreviewBusinessDate_ReturnsScopeWithoutPassingActor()
    {
        var service = new FakeService();
        var controller = CreateController(service);
        var request = new PreviewBusinessDateRequest(new DateOnly(2026, 8, 5), Guid.NewGuid());

        var result = await controller.PreviewBusinessDateChange(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var preview = Assert.IsType<BusinessDateChangePreviewDto>(ok.Value);
        Assert.Equal(new DateOnly(2026, 8, 5), preview.ProposedEffectiveDate);
        Assert.Equal(new DateOnly(2026, 8, 1), preview.Automation.AccountingMonth);
        Assert.Same(request, service.ReceivedBusinessDatePreviewRequest);
    }

    [Fact]
    public async Task UpdateSalaryAccrualSettings_PassesActorAndReturnsUpdatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var service = new FakeService();
        var controller = CreateController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
            }
        };
        var request = new UpdateSalaryAccrualSettingsRequest(15);

        var result = await controller.UpdateSalaryAccrualSettings(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(15, Assert.IsType<SalaryAccrualSettingsDto>(ok.Value).AccrualDay);
        Assert.Same(request, service.ReceivedSalaryAccrualRequest);
        Assert.Equal(actorUserId, service.ReceivedActorUserId);
    }

    [Fact]
    public async Task UpdateSalaryAccrualSettings_MapsInvalidDayToBadRequest()
    {
        var service = new FakeService { RejectSalaryAccrualUpdate = true };
        var controller = CreateController(service);

        var result = await controller.UpdateSalaryAccrualSettings(
            new UpdateSalaryAccrualSettingsRequest(29),
            CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("salary_accrual_day_invalid", problem.Title);
    }

    [Fact]
    public async Task CreateDatabaseBackup_PassesActorReasonAndReturnsCreatedFile()
    {
        var actorUserId = Guid.NewGuid();
        var backupService = new FakeBackupService();
        var controller = CreateController(backupService: backupService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
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
        var controller = CreateController(backupService: backupService);

        var result = await controller.CreateDatabaseBackup(new CreateDatabaseBackupRequest("Причина"), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal(errorCode, details.Title);
        Assert.Equal("Безопасное сообщение.", details.Detail);
    }

    [Fact]
    public async Task DownloadDatabaseBackup_ReturnsProtectedStreamAndPassesActor()
    {
        var actorUserId = Guid.NewGuid();
        var backupService = new FakeBackupService();
        var controller = CreateController(backupService: backupService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())], "Test"))
            }
        };

        var result = await controller.DownloadDatabaseBackup(backupService.CreatedFile.FileName, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.Equal(backupService.CreatedFile.FileName, file.FileDownloadName);
        Assert.True(file.EnableRangeProcessing);
        Assert.Equal(actorUserId, backupService.ReceivedActorUserId);
        Assert.Equal(backupService.CreatedFile.FileName, backupService.ReceivedFileName);
        await file.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task DeleteDatabaseBackup_PassesReasonAndReturnsDeletedFile()
    {
        var backupService = new FakeBackupService();
        var controller = CreateController(backupService: backupService);

        var result = await controller.DeleteDatabaseBackup(
            backupService.CreatedFile.FileName,
            new DeleteDatabaseBackupRequest("Копия больше не нужна"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(backupService.CreatedFile, ok.Value);
        Assert.Equal(backupService.CreatedFile.FileName, backupService.ReceivedFileName);
        Assert.Equal("Копия больше не нужна", backupService.ReceivedReason);
    }

    [Theory]
    [InlineData("database_backup_file_invalid", StatusCodes.Status400BadRequest)]
    [InlineData("database_backup_not_found", StatusCodes.Status404NotFound)]
    [InlineData("database_backup_in_progress", StatusCodes.Status409Conflict)]
    [InlineData("database_backup_delete_failed", StatusCodes.Status503ServiceUnavailable)]
    public async Task BackupFileActions_MapServiceFailures(string errorCode, int expectedStatus)
    {
        var backupService = new FakeBackupService
        {
            FileResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure(errorCode, "Безопасное сообщение.")
        };
        var controller = CreateController(backupService: backupService);

        var delete = await controller.DeleteDatabaseBackup(
            backupService.CreatedFile.FileName,
            new DeleteDatabaseBackupRequest("Причина удаления"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(delete.Result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(errorCode, Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Theory]
    [InlineData("database_backup_file_invalid", StatusCodes.Status400BadRequest)]
    [InlineData("database_backup_not_found", StatusCodes.Status404NotFound)]
    [InlineData("database_backup_download_failed", StatusCodes.Status503ServiceUnavailable)]
    public async Task DownloadDatabaseBackup_MapsServiceFailures(string errorCode, int expectedStatus)
    {
        var backupService = new FakeBackupService
        {
            FileResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure(errorCode, "Безопасное сообщение.")
        };
        var controller = CreateController(backupService: backupService);

        var result = await controller.DownloadDatabaseBackup(backupService.CreatedFile.FileName, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(errorCode, Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    [Fact]
    public async Task CreateCashBankBalanceAdjustment_PassesActorAndReturnsCreatedValue()
    {
        var actorUserId = Guid.NewGuid();
        var balanceService = new FakeCashBankBalanceService();
        var controller = CreateController(balanceService: balanceService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())],
                    "Test"))
            }
        };
        var request = new CreateCashBankBalanceAdjustmentRequest(
            "cash",
            "increase",
            new DateOnly(2026, 7, 27),
            500m,
            "Размен кассы");

        var result = await controller.CreateCashBankBalanceAdjustment(request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(balanceService.Current, created.Value);
        Assert.Same(request, balanceService.ReceivedAdjustment);
        Assert.Equal(actorUserId, balanceService.ReceivedActorUserId);
    }

    [Theory]
    [InlineData("amount_invalid", StatusCodes.Status400BadRequest)]
    [InlineData("insufficient_balance", StatusCodes.Status409Conflict)]
    public async Task CashBankBalanceActions_MapFailuresToProblemDetails(
        string errorCode,
        int expectedStatus)
    {
        var balanceService = new FakeCashBankBalanceService
        {
            AdjustmentResult = FinanceResult<CashBankBalanceSettingsDto>.Failure(
                errorCode,
                "Проверяемая ошибка.")
        };
        var controller = CreateController(balanceService: balanceService);

        var result = await controller.CreateCashBankBalanceAdjustment(
            new CreateCashBankBalanceAdjustmentRequest(
                "cash",
                "decrease",
                new DateOnly(2026, 7, 27),
                10m,
                "Причина"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(errorCode, Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    private static SettingsController CreateController(
        FakeService? service = null,
        FakeCashBankBalanceService? balanceService = null,
        FakeBackupService? backupService = null) =>
        new(
            service ?? new FakeService(),
            balanceService ?? new FakeCashBankBalanceService(),
            backupService ?? new FakeBackupService());

    private sealed class FakeService : IApplicationSettingsService
    {
        public PaymentDisplaySettingsDto Current { get; set; } = new(false);
        public UpdatePaymentDisplaySettingsRequest? ReceivedRequest { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }
        public UpdateBusinessDateRequest? ReceivedBusinessDateRequest { get; private set; }
        public PreviewBusinessDateRequest? ReceivedBusinessDatePreviewRequest { get; private set; }
        public UpdateSalaryAccrualSettingsRequest? ReceivedSalaryAccrualRequest { get; private set; }
        public bool RejectSalaryAccrualUpdate { get; set; }

        public Task<PaymentDisplaySettingsDto> GetPaymentDisplaySettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<PaymentDisplaySettingsDto> UpdatePaymentDisplaySettingsAsync(UpdatePaymentDisplaySettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedRequest = request;
            ReceivedActorUserId = actorUserId;
            Current = new PaymentDisplaySettingsDto(request.ShowAllGarageOperationsByDefault);
            return Task.FromResult(Current);
        }

        public Task<SalaryAccrualSettingsDto> GetSalaryAccrualSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SalaryAccrualSettingsDto(10));

        public Task<SalaryAccrualSettingsDto> UpdateSalaryAccrualSettingsAsync(UpdateSalaryAccrualSettingsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            if (RejectSalaryAccrualUpdate)
            {
                throw new SalaryAccrualSettingsValidationException("День начисления зарплаты должен быть от 1 до 28.");
            }

            ReceivedSalaryAccrualRequest = request;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(new SalaryAccrualSettingsDto(request.AccrualDay));
        }

        public Task<BusinessDateSettingsDto> GetBusinessDateSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BusinessDateSettingsDto(new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 21), null, false, null, null));

        public Task<BusinessDateChangePreviewDto> PreviewBusinessDateChangeAsync(PreviewBusinessDateRequest request, CancellationToken cancellationToken)
        {
            ReceivedBusinessDatePreviewRequest = request;
            var proposedDate = request.OverrideDate ?? new DateOnly(2026, 7, 21);
            return Task.FromResult(new BusinessDateChangePreviewDto(
                new DateOnly(2026, 7, 21),
                new DateOnly(2026, 7, 21),
                proposedDate,
                request.OverrideDate,
                true,
                new RegularAccrualAutomationPreviewDto(
                    new DateOnly(proposedDate.Year, proposedDate.Month, 1),
                    12,
                    4,
                    3,
                    1,
                    48,
                    []),
                request.Version ?? Guid.NewGuid()));
        }

        public Task<BusinessDateSettingsDto> UpdateBusinessDateSettingsAsync(UpdateBusinessDateRequest request, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedBusinessDateRequest = request;
            ReceivedActorUserId = actorUserId;
            var effectiveDate = request.OverrideDate ?? new DateOnly(2026, 7, 21);
            return Task.FromResult(new BusinessDateSettingsDto(
                new DateOnly(2026, 7, 21),
                effectiveDate,
                request.OverrideDate,
                request.OverrideDate.HasValue,
                DateTimeOffset.UtcNow,
                new RegularAccrualAutomationSummaryDto(true, 1, 0, "Готово")));
        }
    }

    private sealed class FakeBackupService : IDatabaseBackupService
    {
        public DatabaseBackupFileDto CreatedFile { get; } = new("garagebalance_manual_20260715_120000_000.pgdump", 1024, DateTimeOffset.UtcNow, "manual");
        public DatabaseBackupStatusDto Status { get; } = new(true, true, 24, 30, "/backups", false, null, null, []);
        public DatabaseBackupResult<DatabaseBackupFileDto>? CreateResult { get; set; }
        public DatabaseBackupResult<DatabaseBackupFileDto>? FileResult { get; set; }
        public DatabaseBackupKind? ReceivedKind { get; private set; }
        public string? ReceivedReason { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }
        public string? ReceivedFileName { get; private set; }

        public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

        public Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(DatabaseBackupKind kind, string? reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedKind = kind;
            ReceivedReason = reason;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(CreateResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(CreatedFile));
        }

        public Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(string fileName, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedFileName = fileName;
            ReceivedActorUserId = actorUserId;
            if (FileResult is { Succeeded: false })
            {
                return Task.FromResult(DatabaseBackupResult<DatabaseBackupDownloadDto>.Failure(FileResult.ErrorCode!, FileResult.ErrorMessage!));
            }

            return Task.FromResult(DatabaseBackupResult<DatabaseBackupDownloadDto>.Success(
                new DatabaseBackupDownloadDto(fileName, CreatedFile.SizeBytes, new MemoryStream([1, 2, 3]))));
        }

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(string fileName, string? reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedFileName = fileName;
            ReceivedReason = reason;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(FileResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(CreatedFile));
        }
    }

    private sealed class FakeCashBankBalanceService : ICashBankBalanceSettingsService
    {
        public CashBankBalanceSettingsDto Current { get; } = new(100m, 200m, 150m, 250m, []);
        public CreateCashBankBalanceAdjustmentRequest? ReceivedAdjustment { get; private set; }
        public Guid? ReceivedActorUserId { get; private set; }
        public FinanceResult<CashBankBalanceSettingsDto>? AdjustmentResult { get; set; }

        public Task<CashBankBalanceSettingsDto> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<FinanceResult<CashBankBalanceSettingsDto>> UpdateOpeningBalancesAsync(
            UpdateCashBankOpeningBalancesRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(FinanceResult<CashBankBalanceSettingsDto>.Success(Current));

        public Task<FinanceResult<CashBankBalanceSettingsDto>> CreateAdjustmentAsync(
            CreateCashBankBalanceAdjustmentRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            ReceivedAdjustment = request;
            ReceivedActorUserId = actorUserId;
            return Task.FromResult(
                AdjustmentResult ?? FinanceResult<CashBankBalanceSettingsDto>.Success(Current));
        }
    }
}
