using GarageBalance.Api.Application.Integrations;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class OneCFreshSyncAdapterTests
{
    [Fact]
    public async Task DisabledAdapter_ReturnsPendingStatusWithoutLeakingRequestData()
    {
        var adapter = new DisabledOneCFreshSyncAdapter();
        var request = new OneCFreshSyncAdapterRequest(
            "super-secret-token",
            "Комментарий администратора",
            DateTimeOffset.UtcNow,
            IsRetry: true);

        var result = await adapter.StartAsync(request, CancellationToken.None);

        Assert.Equal("pending_adapter", result.Status);
        Assert.Contains("адаптера 1C Fresh", result.StatusMessage, StringComparison.Ordinal);
        Assert.Null(result.ExternalRunId);
        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain(request.RefreshToken, result.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Comment!, result.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AdapterResultFactories_PreserveExternalRunConflictAndErrorMappingData()
    {
        var pending = OneCFreshSyncAdapterResult.Pending("Ожидает подключения.");
        var started = OneCFreshSyncAdapterResult.Started("Запущено.", "fresh-run-42");
        var failed = OneCFreshSyncAdapterResult.Failed("rate_limited", "Лимит 1C Fresh.", "too_many_requests");
        var conflict = OneCFreshSyncAdapterResult.Conflict("Найдены дубли.", "duplicate_document");

        Assert.Equal("pending_adapter", pending.Status);
        Assert.Null(pending.ExternalRunId);
        Assert.Null(pending.ErrorCode);
        Assert.Equal("started", started.Status);
        Assert.Equal("fresh-run-42", started.ExternalRunId);
        Assert.Null(started.ErrorCode);
        Assert.Equal("rate_limited", failed.Status);
        Assert.Null(failed.ExternalRunId);
        Assert.Equal("too_many_requests", failed.ErrorCode);
        Assert.Equal("conflict", conflict.Status);
        Assert.Null(conflict.ExternalRunId);
        Assert.Equal("duplicate_document", conflict.ErrorCode);
    }
}
