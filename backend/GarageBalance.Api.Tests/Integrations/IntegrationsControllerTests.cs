using System.Security.Claims;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class IntegrationsControllerTests
{
    [Fact]
    public async Task GetOneCFreshStatus_ReturnsStatusFromService()
    {
        var expected = new OneCFreshIntegrationStatusDto(
            "OneCFresh",
            "1C Fresh",
            IsConfigured: true,
            CanSynchronize: false,
            "prepared",
            "Токен сохранен.",
            ["RefreshToken"],
            ["RefreshToken"],
            DateTimeOffset.UtcNow);
        var service = new FakeIntegrationStatusService(oneCFreshStatus: expected);
        var controller = new IntegrationsController(service, new FakeIntegrationSecretSettingsService(), new FakeOneCFreshSyncService(), new FakeReceiptPrintingService());

        var result = await controller.GetOneCFreshStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.True(service.OneCFreshCalled);
    }

    [Fact]
    public async Task GetReceiptPrintingStatus_ReturnsStatusFromService()
    {
        var expected = new ReceiptPrintingIntegrationStatusDto(
            "ReceiptPrinting",
            "Печать чеков и квитанций",
            IsConfigured: true,
            CanPrint: false,
            "prepared",
            "Настройки сохранены.",
            ["DeviceConnection", "ReceiptTemplate"],
            ["DeviceConnection", "ReceiptTemplate"],
            ["Печать квитанции", "Отмена печати", "Печать копии квитанции"],
            DateTimeOffset.UtcNow);
        var service = new FakeIntegrationStatusService(receiptPrintingStatus: expected);
        var controller = new IntegrationsController(service, new FakeIntegrationSecretSettingsService(), new FakeOneCFreshSyncService(), new FakeReceiptPrintingService());

        var result = await controller.GetReceiptPrintingStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.True(service.ReceiptPrintingCalled);
    }

    [Fact]
    public async Task RegisterReceiptPrintingAction_ReturnsResultAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var expected = new ReceiptPrintingActionDto(
            Guid.NewGuid(),
            operationId,
            "print",
            "registered",
            "Печать зарегистрирована.",
            "PKO-1",
            IsCopy: false,
            CopyMark: null,
            DateTimeOffset.UtcNow);
        var service = new FakeReceiptPrintingService
        {
            Result = ReceiptPrintingResult<ReceiptPrintingActionDto>.Success(expected)
        };
        var controller = CreateController(service, actorUserId);
        var request = new ReceiptPrintingActionRequest("print", null);

        var result = await controller.RegisterReceiptPrintingAction(operationId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal(operationId, service.LastOperationId);
        Assert.Equal(request, service.LastRequest);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task UpdateProtectedSetting_ReturnsMetadataAndPassesActorWithoutReturningPlaintext()
    {
        var actorUserId = Guid.NewGuid();
        var expected = new IntegrationSecretSettingDto(
            Guid.NewGuid(),
            "OneCFresh",
            "RefreshToken",
            "OneCFresh.RefreshToken",
            DateTimeOffset.UtcNow,
            actorUserId,
            HasProtectedValue: true);
        var secretService = new FakeIntegrationSecretSettingsService
        {
            Result = IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Success(expected)
        };
        var controller = CreateController(new FakeReceiptPrintingService(), actorUserId, integrationSecretSettingsService: secretService);

        var result = await controller.UpdateProtectedSetting(
            "OneCFresh",
            "RefreshToken",
            new UpdateIntegrationSecretRequest("private-refresh-token"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal(actorUserId, secretService.LastActorUserId);
        Assert.Equal("OneCFresh", secretService.LastRequest?.Provider);
        Assert.Equal("RefreshToken", secretService.LastRequest?.SettingKey);
        Assert.Equal("private-refresh-token", secretService.LastRequest?.PlaintextValue);
        Assert.DoesNotContain("private-refresh-token", expected.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProtectedSetting_RequiresBodyAndMapsValidationError()
    {
        var controller = CreateController(new FakeReceiptPrintingService());
        var missingBody = await controller.UpdateProtectedSetting("OneCFresh", "RefreshToken", null, CancellationToken.None);

        var missingBodyResult = Assert.IsType<BadRequestObjectResult>(missingBody.Result);
        Assert.Equal("integration_secret_request_required", Assert.IsType<ProblemDetails>(missingBodyResult.Value).Title);

        var secretService = new FakeIntegrationSecretSettingsService
        {
            Result = IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure(
                "integration_secret_unsupported",
                "Unsupported setting.")
        };
        controller = CreateController(new FakeReceiptPrintingService(), integrationSecretSettingsService: secretService);
        var unsupported = await controller.UpdateProtectedSetting(
            "Unknown",
            "Token",
            new UpdateIntegrationSecretRequest("value"),
            CancellationToken.None);

        var unsupportedResult = Assert.IsType<BadRequestObjectResult>(unsupported.Result);
        Assert.Equal("integration_secret_unsupported", Assert.IsType<ProblemDetails>(unsupportedResult.Value).Title);
    }

    [Fact]
    public async Task StartOneCFreshSync_ReturnsResultAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var expected = new OneCFreshSyncDto(
            Guid.NewGuid(),
            "OneCFresh",
            "pending_adapter",
            "Синхронизация ожидает адаптер.",
            DateTimeOffset.UtcNow);
        var service = new FakeOneCFreshSyncService
        {
            Result = OneCFreshSyncResult<OneCFreshSyncDto>.Success(expected)
        };
        var controller = CreateController(new FakeReceiptPrintingService(), actorUserId, service);
        var request = new OneCFreshSyncRequest("Проверочный запуск");

        var result = await controller.StartOneCFreshSync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal(request, service.LastRequest);
        Assert.Equal(actorUserId, service.LastActorUserId);
    }

    [Fact]
    public async Task PreviewOneCFreshSync_ReturnsResultAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var expected = new OneCFreshSyncPreviewDto(
            Guid.NewGuid(),
            "OneCFresh",
            "preview",
            "pending_decision",
            "draft_preview",
            "Предпросмотр подготовлен.",
            DateTimeOffset.UtcNow,
            "Период не выбран.",
            "snapshot",
            CanApply: false,
            [new OneCFreshSyncPreviewCountDto("payment", "export", 0)],
            [new OneCFreshSyncPreviewNoticeDto("decision_required", "Нужно решение.")],
            []);
        var service = new FakeOneCFreshSyncService
        {
            PreviewResult = OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Success(expected)
        };
        var controller = CreateController(new FakeReceiptPrintingService(), actorUserId, service);
        var request = new OneCFreshSyncRequest("Проверочный предпросмотр");

        var result = await controller.PreviewOneCFreshSync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal(request, service.LastPreviewRequest);
        Assert.Equal(actorUserId, service.LastPreviewActorUserId);
    }

    [Fact]
    public async Task RetryOneCFreshSync_ReturnsResultAndPassesActorUserId()
    {
        var actorUserId = Guid.NewGuid();
        var expected = new OneCFreshSyncDto(
            Guid.NewGuid(),
            "OneCFresh",
            "pending_adapter",
            "Повтор ожидает адаптер.",
            DateTimeOffset.UtcNow);
        var service = new FakeOneCFreshSyncService
        {
            RetryResult = OneCFreshSyncResult<OneCFreshSyncDto>.Success(expected)
        };
        var controller = CreateController(new FakeReceiptPrintingService(), actorUserId, service);
        var request = new OneCFreshSyncRequest("Повтор после ошибки");

        var result = await controller.RetryOneCFreshSync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal(request, service.LastRetryRequest);
        Assert.Equal(actorUserId, service.LastRetryActorUserId);
    }

    [Fact]
    public async Task RetryOneCFreshSync_MapsNotConfiguredToConflict()
    {
        var service = new FakeOneCFreshSyncService
        {
            RetryResult = OneCFreshSyncResult<OneCFreshSyncDto>.Failure("one_c_fresh_not_configured", "Нет токена.")
        };
        var controller = CreateController(new FakeReceiptPrintingService(), oneCFreshSyncService: service);

        var result = await controller.RetryOneCFreshSync(null, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("one_c_fresh_not_configured", problem.Title);
        Assert.NotNull(service.LastRetryRequest);
    }

    [Fact]
    public async Task PreviewOneCFreshSync_MapsNotConfiguredToConflict()
    {
        var service = new FakeOneCFreshSyncService
        {
            PreviewResult = OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Failure("one_c_fresh_not_configured", "Нет токена.")
        };
        var controller = CreateController(new FakeReceiptPrintingService(), oneCFreshSyncService: service);

        var result = await controller.PreviewOneCFreshSync(null, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("one_c_fresh_not_configured", problem.Title);
        Assert.NotNull(service.LastPreviewRequest);
    }

    [Fact]
    public async Task StartOneCFreshSync_MapsNotConfiguredToConflict()
    {
        var service = new FakeOneCFreshSyncService
        {
            Result = OneCFreshSyncResult<OneCFreshSyncDto>.Failure("one_c_fresh_not_configured", "Нет токена.")
        };
        var controller = CreateController(new FakeReceiptPrintingService(), oneCFreshSyncService: service);

        var result = await controller.StartOneCFreshSync(null, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("one_c_fresh_not_configured", problem.Title);
        Assert.NotNull(service.LastRequest);
    }

    [Fact]
    public async Task RegisterReceiptPrintingAction_RequiresRequestBody()
    {
        var controller = CreateController(new FakeReceiptPrintingService());

        var result = await controller.RegisterReceiptPrintingAction(Guid.NewGuid(), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("receipt_print_request_required", problem.Title);
    }

    [Theory]
    [InlineData("financial_operation_not_found", typeof(NotFoundObjectResult))]
    [InlineData("receipt_print_income_required", typeof(ConflictObjectResult))]
    [InlineData("receipt_print_action_invalid", typeof(BadRequestObjectResult))]
    public async Task RegisterReceiptPrintingAction_MapsServiceErrors(string errorCode, Type resultType)
    {
        var service = new FakeReceiptPrintingService
        {
            Result = ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure(errorCode, "Ошибка печати.")
        };
        var controller = CreateController(service);

        var result = await controller.RegisterReceiptPrintingAction(Guid.NewGuid(), new ReceiptPrintingActionRequest("print", null), CancellationToken.None);

        Assert.IsType(resultType, result.Result);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(((ObjectResult)objectResult).Value);
        Assert.Equal(errorCode, problem.Title);
    }

    private static IntegrationsController CreateController(
        FakeReceiptPrintingService receiptPrintingService,
        Guid? actorUserId = null,
        FakeOneCFreshSyncService? oneCFreshSyncService = null,
        FakeIntegrationSecretSettingsService? integrationSecretSettingsService = null)
    {
        var controller = new IntegrationsController(
            new FakeIntegrationStatusService(),
            integrationSecretSettingsService ?? new FakeIntegrationSecretSettingsService(),
            oneCFreshSyncService ?? new FakeOneCFreshSyncService(),
            receiptPrintingService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        if (actorUserId is not null)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actorUserId.Value.ToString())],
                "Test"));
        }

        return controller;
    }

    private sealed class FakeIntegrationSecretSettingsService : IIntegrationSecretSettingsService
    {
        public UpsertIntegrationSecretRequest? LastRequest { get; private set; }

        public Guid? LastActorUserId { get; private set; }

        public IntegrationSecretSettingResult<IntegrationSecretSettingDto> Result { get; init; } =
            IntegrationSecretSettingResult<IntegrationSecretSettingDto>.Failure("not_configured", "Not configured.");

        public Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
            UpsertIntegrationSecretRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastActorUserId = actorUserId;
            return Task.FromResult(Result);
        }

        public Task<IntegrationSecretSettingResult<string>> GetSecretAsync(string provider, string settingKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(IntegrationSecretSettingResult<string>.Failure("not_configured", "Not configured."));
        }

        public Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(string? provider, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<IntegrationSecretSettingDto>>([]);
        }
    }

    private sealed class FakeOneCFreshSyncService : IOneCFreshSyncService
    {
        public OneCFreshSyncRequest? LastPreviewRequest { get; private set; }

        public Guid? LastPreviewActorUserId { get; private set; }

        public OneCFreshSyncRequest? LastRequest { get; private set; }

        public Guid? LastActorUserId { get; private set; }

        public OneCFreshSyncRequest? LastRetryRequest { get; private set; }

        public Guid? LastRetryActorUserId { get; private set; }

        public OneCFreshSyncResult<OneCFreshSyncDto> Result { get; init; } =
            OneCFreshSyncResult<OneCFreshSyncDto>.Failure("not_configured", "Not configured.");

        public OneCFreshSyncResult<OneCFreshSyncPreviewDto> PreviewResult { get; init; } =
            OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Failure("not_configured", "Not configured.");

        public OneCFreshSyncResult<OneCFreshSyncDto> RetryResult { get; init; } =
            OneCFreshSyncResult<OneCFreshSyncDto>.Failure("not_configured", "Not configured.");

        public Task<OneCFreshSyncResult<OneCFreshSyncPreviewDto>> PreviewSyncAsync(
            OneCFreshSyncRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            LastPreviewRequest = request;
            LastPreviewActorUserId = actorUserId;
            return Task.FromResult(PreviewResult);
        }

        public Task<OneCFreshSyncResult<OneCFreshSyncDto>> StartSyncAsync(
            OneCFreshSyncRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastActorUserId = actorUserId;
            return Task.FromResult(Result);
        }

        public Task<OneCFreshSyncResult<OneCFreshSyncDto>> RetrySyncAsync(
            OneCFreshSyncRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            LastRetryRequest = request;
            LastRetryActorUserId = actorUserId;
            return Task.FromResult(RetryResult);
        }
    }

    private sealed class FakeIntegrationStatusService(
        OneCFreshIntegrationStatusDto? oneCFreshStatus = null,
        ReceiptPrintingIntegrationStatusDto? receiptPrintingStatus = null) : IIntegrationStatusService
    {
        public bool OneCFreshCalled { get; private set; }

        public bool ReceiptPrintingCalled { get; private set; }

        public Task<OneCFreshIntegrationStatusDto> GetOneCFreshStatusAsync(CancellationToken cancellationToken)
        {
            OneCFreshCalled = true;
            return Task.FromResult(oneCFreshStatus!);
        }

        public Task<ReceiptPrintingIntegrationStatusDto> GetReceiptPrintingStatusAsync(CancellationToken cancellationToken)
        {
            ReceiptPrintingCalled = true;
            return Task.FromResult(receiptPrintingStatus!);
        }
    }

    private sealed class FakeReceiptPrintingService : IReceiptPrintingService
    {
        public Guid? LastOperationId { get; private set; }

        public ReceiptPrintingActionRequest? LastRequest { get; private set; }

        public Guid? LastActorUserId { get; private set; }

        public ReceiptPrintingResult<ReceiptPrintingActionDto> Result { get; init; } =
            ReceiptPrintingResult<ReceiptPrintingActionDto>.Failure("not_configured", "Not configured.");

        public Task<ReceiptPrintingResult<ReceiptPrintingActionDto>> RegisterActionAsync(
            Guid financialOperationId,
            ReceiptPrintingActionRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            LastOperationId = financialOperationId;
            LastRequest = request;
            LastActorUserId = actorUserId;
            return Task.FromResult(Result);
        }
    }
}
