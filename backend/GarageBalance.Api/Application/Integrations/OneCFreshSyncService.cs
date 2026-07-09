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
            new OneCFreshSyncAdapterRequest(refreshToken.Value, comment, requestedAtUtc),
            cancellationToken);

        var auditEvent = auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "one_c_fresh.sync_requested",
            "integration_sync",
            Provider,
            Summary: "Запрошен запуск синхронизации 1C Fresh.",
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
                ["adapterErrorCode"] = adapterResult.ErrorCode,
                ["protectedCredentialConfigured"] = true
            }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return OneCFreshSyncResult<OneCFreshSyncDto>.Success(new OneCFreshSyncDto(
            auditEvent!.Id,
            Provider,
            adapterResult.Status,
            adapterResult.StatusMessage,
            auditEvent.CreatedAtUtc));
    }
}
