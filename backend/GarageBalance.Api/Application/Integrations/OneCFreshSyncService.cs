using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.Api.Application.Integrations;

public sealed class OneCFreshSyncService(
    GarageBalanceDbContext dbContext,
    IIntegrationSecretSettingsService secretSettingsService,
    IOneCFreshSyncAdapter syncAdapter,
    IAuditEventWriter auditEventWriter) : IOneCFreshSyncService
{
    private const string Provider = "OneCFresh";
    private const string RefreshTokenSettingKey = "RefreshToken";

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

    private sealed record OneCFreshSyncOutcome(
        bool CanRetry,
        bool HasConflict,
        string? ErrorCode,
        string? RecoveryAction);
}
