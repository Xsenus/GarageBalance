using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Storage;

namespace GarageBalance.Api.Application.Storage;

public sealed class StorageReplicationRunner(
    IStorageCatalog catalog,
    IStorageProviderRegistry providerRegistry,
    StorageConfigurationResolver configurationResolver,
    TimeProvider timeProvider,
    ILogger<StorageReplicationRunner> logger)
{
    private readonly EffectiveStorageConfiguration configuration = configurationResolver.Resolve();

    public async Task<bool> ProcessNextAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        if (configuration.Mode != StorageMode.AsyncMirror)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var job = await catalog.ClaimNextJobAsync(
            leaseOwner,
            TimeSpan.FromSeconds(configuration.Replication.LeaseSeconds),
            now,
            cancellationToken);
        if (job is null)
        {
            return false;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.Replication.OperationDeadlineSeconds));
        try
        {
            await ProcessClaimedAsync(job, leaseOwner, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var context = await catalog.GetLeasedJobContextAsync(job.Id, leaseOwner, cancellationToken);
            var policy = configuration.Policies.SingleOrDefault(item =>
                string.Equals(item.Id, context.Object.PolicyId, StringComparison.Ordinal));
            if (policy is null)
            {
                await BlockAsync(context, leaseOwner, null, "StalePolicyOrGeneration", "Storage policy is no longer available.", cancellationToken);
            }
            else
            {
                await RetryAsync(
                    context,
                    leaseOwner,
                    policy,
                    StorageErrorCategory.UnknownOutcome,
                    unknownOutcome: true,
                    cancellationToken);
            }
        }
        return true;
    }

    private async Task ProcessClaimedAsync(
        StorageTransferJob claimedJob,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        StorageTransferContext context;
        try
        {
            context = await catalog.GetLeasedJobContextAsync(claimedJob.Id, leaseOwner, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Storage replication job context could not be loaded. JobId={JobId} ExceptionType={ExceptionType}",
                claimedJob.Id,
                exception.GetType().Name);
            return;
        }

        var policy = configuration.Policies.SingleOrDefault(item =>
            string.Equals(item.Id, context.Object.PolicyId, StringComparison.Ordinal));
        if (policy is null || policy.Revision != context.Object.PolicyRevision ||
            context.Job.PolicyRevision != context.Object.PolicyRevision ||
            context.Job.Generation != context.Object.CommittedGeneration ||
            context.Object.TombstonedAtUtc is not null ||
            context.Job.Kind != StorageTransferJobKind.Replicate)
        {
            await BlockAsync(context, leaseOwner, policy, "StalePolicyOrGeneration", "Storage job no longer matches the committed policy or generation.", cancellationToken);
            return;
        }

        var destination = configuration.Destinations.SingleOrDefault(item =>
            string.Equals(item.Id, context.Job.DestinationId, StringComparison.Ordinal));
        if (destination is null || destination.State is not (StorageDestinationState.Enabled or StorageDestinationState.Recovering) ||
            !destination.Capabilities.HasFlag(StorageCapability.Write) ||
            !destination.Capabilities.HasFlag(StorageCapability.Stat))
        {
            await BlockAsync(context, leaseOwner, policy, "DestinationIneligible", "Storage destination is not eligible for replication writes.", cancellationToken);
            return;
        }

        var target = providerRegistry.GetRequired(destination.Id);
        var request = new StorageWriteRequest(
            context.Object.OperationId,
            context.Object.LogicalKey,
            context.Object.CommittedGeneration,
            context.Object.SizeBytes,
            context.Object.Sha256,
            new Dictionary<string, string>
            {
                ["garagebalance-object-id"] = context.Object.Id.ToString("N"),
                ["garagebalance-data-class"] = context.Object.DataClass.ToString()
            });
        var locator = target.GetWriteLocator(request);
        var writeStarted = false;
        try
        {
            var existing = await target.StatAsync(locator, cancellationToken);
            if (existing is not null)
            {
                if (!MatchesCommittedObject(existing, context.Object))
                {
                    throw new StorageProviderException(
                        StorageErrorCategory.Conflict,
                        "Immutable destination locator already contains different data.");
                }
                await catalog.CompleteReplicationAsync(
                    context.Job.Id,
                    leaseOwner,
                    locator,
                    existing.ProviderVersionId,
                    existing.ProviderChecksum,
                    policy.RequiredIndependentCopies,
                    policy.DesiredCopies,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return;
            }

            await using var source = await OpenFirstAvailableSourceAsync(context, cancellationToken);
            await catalog.MarkReplicationUploadingAsync(
                context.Job.Id,
                leaseOwner,
                locator,
                timeProvider.GetUtcNow(),
                cancellationToken);
            writeStarted = true;
            var result = await target.WriteAsync(request, source, cancellationToken);
            var stat = await target.StatAsync(result.NativeLocator, cancellationToken);
            if (stat is null || !MatchesCommittedObject(stat, context.Object))
            {
                throw new StorageProviderException(
                    StorageErrorCategory.ChecksumOrStale,
                    "Replicated object did not pass destination verification.");
            }
            await catalog.CompleteReplicationAsync(
                context.Job.Id,
                leaseOwner,
                result.NativeLocator,
                result.ProviderVersionId ?? stat.ProviderVersionId,
                stat.ProviderChecksum ?? result.ProviderChecksum,
                policy.RequiredIndependentCopies,
                policy.DesiredCopies,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StorageProviderException exception)
        {
            if (IsBlocking(exception.Category))
            {
                await BlockAsync(context, leaseOwner, policy, exception.Category.ToString(), SafeError(exception.Category), cancellationToken);
                return;
            }
            await RetryAsync(
                context,
                leaseOwner,
                policy,
                exception.Category,
                writeStarted || exception.Category == StorageErrorCategory.UnknownOutcome,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Storage replication attempt failed unexpectedly. JobId={JobId} DestinationId={DestinationId} ExceptionType={ExceptionType}",
                context.Job.Id,
                context.Job.DestinationId,
                exception.GetType().Name);
            await RetryAsync(
                context,
                leaseOwner,
                policy,
                StorageErrorCategory.UnknownOutcome,
                writeStarted,
                cancellationToken);
        }
    }

    private async Task<Stream> OpenFirstAvailableSourceAsync(
        StorageTransferContext context,
        CancellationToken cancellationToken)
    {
        StorageProviderException? lastError = null;
        foreach (var sourceReplica in context.AvailableSources)
        {
            var sourceDestination = configuration.Destinations.SingleOrDefault(item =>
                string.Equals(item.Id, sourceReplica.DestinationId, StringComparison.Ordinal));
            if (sourceDestination?.State == StorageDestinationState.Disabled ||
                sourceDestination is null ||
                !sourceDestination.Capabilities.HasFlag(StorageCapability.Read))
            {
                continue;
            }
            try
            {
                return await providerRegistry.GetRequired(sourceReplica.DestinationId)
                    .OpenReadAsync(sourceReplica.NativeLocator, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StorageProviderException exception)
            {
                lastError = exception;
            }
        }
        throw lastError ?? new StorageProviderException(
            StorageErrorCategory.ObjectMissing,
            "No verified source replica is currently readable.");
    }

    private async Task RetryAsync(
        StorageTransferContext context,
        string leaseOwner,
        StoragePolicyOptions policy,
        StorageErrorCategory category,
        bool unknownOutcome,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var delay = ComputeRetryDelay(context.Job.Id, context.Job.AttemptCount, category);
        await catalog.ScheduleReplicationRetryAsync(
            context.Job.Id,
            leaseOwner,
            now.Add(delay),
            unknownOutcome ? StorageReplicaState.Unknown : StorageReplicaState.Failed,
            category.ToString(),
            SafeError(category),
            policy.RequiredIndependentCopies,
            policy.DesiredCopies,
            now,
            cancellationToken);
        logger.LogWarning(
            "Storage replication scheduled for retry. JobId={JobId} DestinationId={DestinationId} Category={Category} Attempt={Attempt}",
            context.Job.Id,
            context.Job.DestinationId,
            category,
            context.Job.AttemptCount);
    }

    private Task BlockAsync(
        StorageTransferContext context,
        string leaseOwner,
        StoragePolicyOptions? policy,
        string category,
        string safeError,
        CancellationToken cancellationToken) =>
        catalog.BlockReplicationAsync(
            context.Job.Id,
            leaseOwner,
            category,
            safeError,
            policy?.RequiredIndependentCopies ?? 2,
            policy?.DesiredCopies ?? 2,
            timeProvider.GetUtcNow(),
            cancellationToken);

    private static bool MatchesCommittedObject(StorageObjectStat stat, StorageObject storageObject) =>
        stat.SizeBytes == storageObject.SizeBytes &&
        string.Equals(stat.ProviderChecksum, storageObject.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlocking(StorageErrorCategory category) =>
        category is StorageErrorCategory.Conflict or
            StorageErrorCategory.ValidationOrUnsupported or
            StorageErrorCategory.ChecksumOrStale or
            StorageErrorCategory.TlsSecurity;

    private static TimeSpan ComputeRetryDelay(Guid jobId, int attempt, StorageErrorCategory category)
    {
        var baseSeconds = Math.Min(300, Math.Pow(2, Math.Clamp(attempt, 1, 8)));
        if (category is StorageErrorCategory.RateLimited or StorageErrorCategory.CredentialsExpired or StorageErrorCategory.QuotaOrReadOnly)
        {
            baseSeconds = Math.Min(900, baseSeconds * 2);
        }
        var jitterSeconds = jobId.ToByteArray()[0] % 11;
        return TimeSpan.FromSeconds(baseSeconds + jitterSeconds);
    }

    private static string SafeError(StorageErrorCategory category) => category switch
    {
        StorageErrorCategory.TransientNetwork => "Storage endpoint is temporarily unavailable.",
        StorageErrorCategory.RateLimited => "Storage endpoint rate limit was reached.",
        StorageErrorCategory.CredentialsExpired => "Storage credentials are unavailable or expired.",
        StorageErrorCategory.ProviderForbidden => "Storage endpoint denied the operation.",
        StorageErrorCategory.ObjectMissing => "A required storage object is missing.",
        StorageErrorCategory.QuotaOrReadOnly => "Storage destination has no writable capacity.",
        StorageErrorCategory.Conflict => "Immutable storage locator conflicts with different data.",
        StorageErrorCategory.ValidationOrUnsupported => "Storage destination does not support the requested operation.",
        StorageErrorCategory.ChecksumOrStale => "Storage object verification failed.",
        StorageErrorCategory.ArchivePending => "Storage object is not yet available from archive.",
        StorageErrorCategory.TlsSecurity => "Storage endpoint TLS validation failed.",
        StorageErrorCategory.Cancelled => "Storage operation was cancelled.",
        _ => "Storage write outcome is unknown and will be reconciled."
    };
}

public sealed class StorageReplicationWorker(
    IServiceScopeFactory scopeFactory,
    StorageConfigurationResolver configurationResolver,
    ILogger<StorageReplicationWorker> logger) : BackgroundService
{
    private readonly EffectiveStorageConfiguration configuration = configurationResolver.Resolve();
    private readonly string leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.Mode != StorageMode.AsyncMirror)
        {
            logger.LogInformation("Storage replication worker is disabled in Single mode.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;
            try
            {
                for (var processed = 0; processed < 100 && !stoppingToken.IsCancellationRequested; processed++)
                {
                    using var scope = scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<StorageReplicationRunner>();
                    if (!await runner.ProcessNextAsync(leaseOwner, stoppingToken))
                    {
                        break;
                    }
                    processedAny = true;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Storage replication worker cycle failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            if (!processedAny)
            {
                await Task.Delay(TimeSpan.FromSeconds(configuration.Replication.PollSeconds), stoppingToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
