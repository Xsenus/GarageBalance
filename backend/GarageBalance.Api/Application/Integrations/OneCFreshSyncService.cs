using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Integrations;

public sealed class OneCFreshSyncService(
    IApplicationUnitOfWork unitOfWork,
    IIntegrationSecretSettingsService secretSettingsService,
    IOneCFreshSyncAdapter syncAdapter,
    IAuditEventWriter auditEventWriter,
    IOneCFreshSyncBackgroundQueue? backgroundQueue = null,
    IOptions<OneCFreshSyncBackgroundOptions>? backgroundOptions = null) : IOneCFreshSyncService
{
    private const string Provider = IntegrationSecretCatalog.OneCFreshProvider;
    private const string RefreshTokenSettingKey = IntegrationSecretCatalog.OneCFreshRefreshToken;
    private const string PreviewDirection = "pending_decision";
    private const string PreviewStatus = "draft_preview";
    private readonly TimeSpan _adapterTimeout = TimeSpan.FromSeconds(
        backgroundOptions?.Value.AdapterTimeoutSeconds ?? 30);

    public async Task<OneCFreshSyncResult<OneCFreshSyncPreviewDto>> PreviewSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var refreshToken = await secretSettingsService.GetSecretAsync(Provider, RefreshTokenSettingKey, cancellationToken);
        if (!refreshToken.Succeeded || string.IsNullOrWhiteSpace(refreshToken.Value))
        {
            return OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Failure(
                "one_c_fresh_not_configured",
                "Для предпросмотра синхронизации сохраните защищенную настройку OneCFresh:RefreshToken.");
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var adapterAvailable = syncAdapter.Availability.IsAvailable;
        var previewDirection = adapterAvailable ? "configured_bridge" : PreviewDirection;
        var previewStatus = adapterAvailable ? "ready_preview" : PreviewStatus;
        var periodSummary = adapterAvailable
            ? "Состав и период обмена определяет настроенный шлюз 1C Fresh; GarageBalance передает подтвержденное задание."
            : "Период и документы не выбраны: требуется включить и настроить шлюз 1C Fresh.";
        var snapshotHash = BuildPreviewSnapshotHash(comment, periodSummary, previewDirection);
        IReadOnlyList<OneCFreshSyncPreviewCountDto> counts =
        [
            new("counterparty", "match", 0),
            new("payment", "export", 0),
            new("accrual", "export", 0)
        ];
        IReadOnlyList<OneCFreshSyncPreviewNoticeDto> warnings = adapterAvailable
            ? [new("one_c_fresh_bridge_scope", "Предпросмотр не отправляет данные: состав документов проверит настроенный шлюз после подтверждения запуска.")]
            : [new("one_c_fresh_exchange_decisions_required", "Предпросмотр не отправлял данные в 1C Fresh: сначала настройте HTTPS endpoint адаптера и тестовый контур.")];
        IReadOnlyList<OneCFreshSyncPreviewNoticeDto> conflicts = [];

        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "one_c_fresh.sync_preview_requested",
            "integration_sync",
            Provider,
            Summary: "Подготовлен предпросмотр синхронизации 1C Fresh без отправки данных.",
            Section: "integrations",
            ActionKind: "sync",
            EntityDisplayName: "1C Fresh",
            Reason: comment,
            Metadata: new Dictionary<string, object?>
            {
                ["provider"] = Provider,
                ["mode"] = "preview",
                ["direction"] = previewDirection,
                ["syncStatus"] = previewStatus,
                ["periodSummary"] = periodSummary,
                ["snapshotHash"] = snapshotHash,
                ["canApply"] = adapterAvailable,
                ["plannedObjectTypes"] = "counterparty,payment,accrual",
                ["warningCodes"] = string.Join(',', warnings.Select(item => item.Code)),
                ["conflictCount"] = conflicts.Count,
                ["protectedCredentialConfigured"] = true
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Success(new OneCFreshSyncPreviewDto(
            auditEvent!.Id,
            Provider,
            "preview",
            previewDirection,
            previewStatus,
            adapterAvailable
                ? "Предпросмотр готов: после подтверждения задание будет передано настроенному шлюзу 1C Fresh."
                : "Предпросмотр подготовлен без отправки данных; адаптер 1C Fresh пока недоступен.",
            auditEvent.CreatedAtUtc,
            periodSummary,
            snapshotHash,
            CanApply: adapterAvailable,
            counts,
            warnings,
            conflicts));
    }

    public async Task<OneCFreshSyncResult<OneCFreshSyncDto>> StartSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        return await RunSyncAsync(
            request,
            actorUserId,
            isRetry: false,
            action: "one_c_fresh.sync_requested",
            summary: "Запрошен запуск синхронизации 1C Fresh.",
            cancellationToken);
    }

    public async Task<OneCFreshSyncResult<OneCFreshSyncDto>> RetrySyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        return await RunSyncAsync(
            request,
            actorUserId,
            isRetry: true,
            action: "one_c_fresh.sync_retry_requested",
            summary: "Запрошен повтор синхронизации 1C Fresh.",
            cancellationToken);
    }

    private async Task<OneCFreshSyncResult<OneCFreshSyncDto>> RunSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        bool isRetry,
        string action,
        string summary,
        CancellationToken cancellationToken)
    {
        if (backgroundQueue is not null)
        {
            return await QueueSyncAsync(request, actorUserId, isRetry, action, summary, cancellationToken);
        }

        return await RunSyncInlineAsync(request, actorUserId, isRetry, action, summary, cancellationToken);
    }

    internal Task<OneCFreshSyncResult<OneCFreshSyncDto>> ExecuteQueuedSyncAsync(
        OneCFreshSyncBackgroundJob job,
        CancellationToken cancellationToken)
    {
        return RunSyncInlineAsync(
            job.Request,
            job.ActorUserId,
            job.IsRetry,
            job.IsRetry ? "one_c_fresh.sync_retry_completed" : "one_c_fresh.sync_completed",
            job.IsRetry ? "Завершён фоновый повтор синхронизации 1C Fresh." : "Завершена фоновая синхронизация 1C Fresh.",
            cancellationToken);
    }

    private async Task<OneCFreshSyncResult<OneCFreshSyncDto>> QueueSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        bool isRetry,
        string action,
        string summary,
        CancellationToken cancellationToken)
    {
        var refreshToken = await secretSettingsService.GetSecretAsync(Provider, RefreshTokenSettingKey, cancellationToken);
        if (!refreshToken.Succeeded || string.IsNullOrWhiteSpace(refreshToken.Value))
        {
            return OneCFreshSyncResult<OneCFreshSyncDto>.Failure(
                "one_c_fresh_not_configured",
                "Для запуска синхронизации сохраните защищенную настройку OneCFresh:RefreshToken.");
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var job = new OneCFreshSyncBackgroundJob(new OneCFreshSyncRequest(comment), actorUserId, isRetry);
        if (!backgroundQueue!.TryQueue(job))
        {
            return OneCFreshSyncResult<OneCFreshSyncDto>.Failure(
                "one_c_fresh_queue_busy",
                "Очередь синхронизации занята. Повторите запуск позже.");
        }

        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            "integration_sync",
            Provider,
            Summary: summary,
            Section: "integrations",
            ActionKind: "sync",
            EntityDisplayName: "1C Fresh",
            Reason: comment,
            Metadata: new Dictionary<string, object?>
            {
                ["provider"] = Provider,
                ["syncStatus"] = "queued",
                ["isRetry"] = isRetry,
                ["protectedCredentialConfigured"] = true
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OneCFreshSyncResult<OneCFreshSyncDto>.Success(new OneCFreshSyncDto(
            auditEvent!.Id,
            Provider,
            "queued",
            "Синхронизация поставлена в фоновую очередь. Раздел можно продолжать использовать.",
            auditEvent.CreatedAtUtc,
            isRetry,
            CanRetry: false,
            HasConflict: false,
            ErrorCode: null,
            ExternalRunId: null,
            RecoveryAction: "watch_status"));
    }

    private async Task<OneCFreshSyncResult<OneCFreshSyncDto>> RunSyncInlineAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        bool isRetry,
        string action,
        string summary,
        CancellationToken cancellationToken)
    {
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var refreshToken = await secretSettingsService.GetSecretAsync(Provider, RefreshTokenSettingKey, cancellationToken);
        if (!refreshToken.Succeeded || string.IsNullOrWhiteSpace(refreshToken.Value))
        {
            return OneCFreshSyncResult<OneCFreshSyncDto>.Failure(
                "one_c_fresh_not_configured",
                "Для запуска синхронизации сохраните защищенную настройку OneCFresh:RefreshToken.");
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        OneCFreshSyncAdapterResult adapterResult;
        try
        {
            adapterResult = await syncAdapter.StartAsync(
                    new OneCFreshSyncAdapterRequest(refreshToken.Value, comment, requestedAtUtc, isRetry),
                    cancellationToken)
                .WaitAsync(_adapterTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            adapterResult = OneCFreshSyncAdapterResult.Failed(
                "timeout",
                "Адаптер 1C Fresh не ответил вовремя. Операцию можно безопасно повторить вручную.",
                "one_c_fresh_timeout");
        }
        var outcome = ClassifyAdapterResult(adapterResult);

        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            "integration_sync",
            Provider,
            Summary: summary,
            Section: "integrations",
            ActionKind: "sync",
            EntityDisplayName: "1C Fresh",
            Reason: comment,
            Metadata: new Dictionary<string, object?>
            {
                ["provider"] = Provider,
                ["syncStatus"] = adapterResult.Status,
                ["syncMessage"] = adapterResult.StatusMessage,
                ["externalRunId"] = adapterResult.ExternalRunId,
                ["adapterErrorCode"] = outcome.ErrorCode,
                ["isRetry"] = isRetry,
                ["canRetry"] = outcome.CanRetry,
                ["hasConflict"] = outcome.HasConflict,
                ["recoveryAction"] = outcome.RecoveryAction,
                ["protectedCredentialConfigured"] = true
            }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OneCFreshSyncResult<OneCFreshSyncDto>.Success(new OneCFreshSyncDto(
            auditEvent!.Id,
            Provider,
            adapterResult.Status,
            adapterResult.StatusMessage,
            auditEvent.CreatedAtUtc,
            isRetry,
            outcome.CanRetry,
            outcome.HasConflict,
            outcome.ErrorCode,
            adapterResult.ExternalRunId,
            outcome.RecoveryAction));
    }

    private static OneCFreshSyncOutcome ClassifyAdapterResult(OneCFreshSyncAdapterResult adapterResult)
    {
        var normalizedStatus = adapterResult.Status.Trim().ToLowerInvariant();
        var hasConflict =
            normalizedStatus == "conflict" ||
            normalizedStatus.StartsWith("conflict_", StringComparison.Ordinal) ||
            normalizedStatus.Contains("_conflict", StringComparison.Ordinal);
        var canRetry =
            !hasConflict &&
            (normalizedStatus == "pending_adapter" ||
             normalizedStatus == "adapter_error" ||
             normalizedStatus == "rate_limited" ||
             normalizedStatus == "timeout" ||
             normalizedStatus == "failed" ||
             normalizedStatus.EndsWith("_failed", StringComparison.Ordinal) ||
             normalizedStatus.EndsWith("_error", StringComparison.Ordinal) ||
             !string.IsNullOrWhiteSpace(adapterResult.ErrorCode));
        var recoveryAction = hasConflict
            ? "resolve_conflict"
            : canRetry
                ? "retry"
                : normalizedStatus is "started" or "running"
                    ? "watch_status"
                    : null;
        var errorCode = string.IsNullOrWhiteSpace(adapterResult.ErrorCode)
            ? hasConflict
                ? "one_c_fresh_conflict"
                : canRetry && normalizedStatus != "pending_adapter"
                    ? "one_c_fresh_adapter_error"
                    : null
            : adapterResult.ErrorCode.Trim();

        return new OneCFreshSyncOutcome(canRetry, hasConflict, errorCode, recoveryAction);
    }

    private static string BuildPreviewSnapshotHash(string? comment, string periodSummary, string direction)
    {
        var source = string.Join('|', Provider, "preview", direction, periodSummary, comment ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record OneCFreshSyncOutcome(
        bool CanRetry,
        bool HasConflict,
        string? ErrorCode,
        string? RecoveryAction);
}
