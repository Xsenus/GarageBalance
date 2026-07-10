using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

namespace GarageBalance.Api.Application.Integrations;

public sealed class OneCFreshSyncService(
    GarageBalanceDbContext dbContext,
    IIntegrationSecretSettingsService secretSettingsService,
    IOneCFreshSyncAdapter syncAdapter,
    IAuditEventWriter auditEventWriter) : IOneCFreshSyncService
{
    private const string Provider = "OneCFresh";
    private const string RefreshTokenSettingKey = "RefreshToken";
    private const string PreviewDirection = "pending_decision";
    private const string PreviewStatus = "draft_preview";

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
        const string periodSummary = "Период и документы не выбраны: требуется решение по направлению обмена и составу документов 1C Fresh.";
        var snapshotHash = BuildPreviewSnapshotHash(comment, periodSummary);
        IReadOnlyList<OneCFreshSyncPreviewCountDto> counts =
        [
            new("counterparty", "match", 0),
            new("payment", "export", 0),
            new("accrual", "export", 0)
        ];
        IReadOnlyList<OneCFreshSyncPreviewNoticeDto> warnings =
        [
            new(
                "one_c_fresh_exchange_decisions_required",
                "Предпросмотр не отправлял данные в 1C Fresh: направление обмена, документы и тестовый контур еще требуют решения.")
        ];
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
                ["direction"] = PreviewDirection,
                ["syncStatus"] = PreviewStatus,
                ["periodSummary"] = periodSummary,
                ["snapshotHash"] = snapshotHash,
                ["canApply"] = false,
                ["plannedObjectTypes"] = "counterparty,payment,accrual",
                ["warningCodes"] = "one_c_fresh_exchange_decisions_required",
                ["conflictCount"] = conflicts.Count,
                ["protectedCredentialConfigured"] = true
            }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return OneCFreshSyncResult<OneCFreshSyncPreviewDto>.Success(new OneCFreshSyncPreviewDto(
            auditEvent!.Id,
            Provider,
            "preview",
            PreviewDirection,
            PreviewStatus,
            "Предпросмотр синхронизации подготовлен без отправки данных в 1C Fresh.",
            auditEvent.CreatedAtUtc,
            periodSummary,
            snapshotHash,
            CanApply: false,
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
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var refreshToken = await secretSettingsService.GetSecretAsync(Provider, RefreshTokenSettingKey, cancellationToken);
        if (!refreshToken.Succeeded || string.IsNullOrWhiteSpace(refreshToken.Value))
        {
            return OneCFreshSyncResult<OneCFreshSyncDto>.Failure(
                "one_c_fresh_not_configured",
                "Для запуска синхронизации сохраните защищенную настройку OneCFresh:RefreshToken.");
        }

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var adapterResult = await syncAdapter.StartAsync(
            new OneCFreshSyncAdapterRequest(refreshToken.Value, comment, requestedAtUtc, isRetry),
            cancellationToken);
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
        await dbContext.SaveChangesAsync(cancellationToken);

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

    private static string BuildPreviewSnapshotHash(string? comment, string periodSummary)
    {
        var source = string.Join('|', Provider, "preview", PreviewDirection, periodSummary, comment ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record OneCFreshSyncOutcome(
        bool CanRetry,
        bool HasConflict,
        string? ErrorCode,
        string? RecoveryAction);
}
