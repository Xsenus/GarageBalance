using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.Api.Application.Storage;

public sealed record StorageReadResult(
    Guid ObjectId,
    string DestinationId,
    string NativeLocator,
    long SizeBytes,
    string Sha256,
    Stream Content);

public interface IStorageReadRouter
{
    Task<StorageReadResult> OpenByLogicalKeyAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        CancellationToken cancellationToken);
}

public sealed class StorageReadRouter(
    IStorageCatalog catalog,
    IStorageProviderRegistry providerRegistry,
    StorageConfigurationResolver configurationResolver,
    StorageOperationHealthTracker healthTracker,
    ILogger<StorageReadRouter> logger) : IStorageReadRouter
{
    private readonly EffectiveStorageConfiguration configuration = configurationResolver.Resolve();

    public async Task<StorageReadResult> OpenByLogicalKeyAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        CancellationToken cancellationToken)
    {
        var storageObject = await catalog.FindByLogicalKeyAsync(
            tenantId,
            dataClass,
            logicalKey,
            cancellationToken) ?? throw new StorageProviderException(
                StorageErrorCategory.ObjectMissing,
                "Storage object was not found in the catalog.");
        if (storageObject.TombstonedAtUtc is not null ||
            storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            throw new StorageProviderException(StorageErrorCategory.ObjectMissing, "Storage object is deleted.");
        }

        var destinationOrder = configuration.Destinations
            .Select((destination, index) => (destination, index))
            .ToDictionary(item => item.destination.Id, item => (item.destination, item.index), StringComparer.Ordinal);
        var replicas = storageObject.Replicas
            .Where(replica => replica.State == StorageReplicaState.Available &&
                replica.Generation == storageObject.CommittedGeneration &&
                replica.SizeBytes == storageObject.SizeBytes &&
                string.Equals(replica.Sha256, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
            .Where(replica => destinationOrder.TryGetValue(replica.DestinationId, out var item) &&
                item.destination.State != StorageDestinationState.Disabled &&
                item.destination.Capabilities.HasFlag(StorageCapability.Read) &&
                item.destination.Capabilities.HasFlag(StorageCapability.Stat))
            .OrderBy(replica => destinationOrder[replica.DestinationId].index)
            .ToArray();

        StorageProviderException? lastError = null;
        foreach (var replica in replicas)
        {
            if (!healthTracker.TryBeginAttempt(replica.DestinationId, StorageOperationKind.Read))
            {
                lastError = new StorageProviderException(StorageErrorCategory.TransientNetwork, "Storage read circuit is cooling down.");
                continue;
            }
            var provider = providerRegistry.GetRequired(replica.DestinationId);
            try
            {
                var stat = await provider.StatAsync(replica.NativeLocator, cancellationToken);
                if (stat is null || stat.SizeBytes != storageObject.SizeBytes ||
                    !string.Equals(stat.ProviderChecksum, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    lastError = new StorageProviderException(
                        stat is null ? StorageErrorCategory.ObjectMissing : StorageErrorCategory.ChecksumOrStale,
                        "Storage replica is missing or does not match the committed object.");
                    continue;
                }
                var content = await provider.OpenReadAsync(replica.NativeLocator, cancellationToken);
                healthTracker.RecordSuccess(replica.DestinationId, StorageOperationKind.Read);
                return new StorageReadResult(
                    storageObject.Id,
                    replica.DestinationId,
                    replica.NativeLocator,
                    storageObject.SizeBytes,
                    storageObject.Sha256,
                    content);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StorageProviderException exception)
            {
                lastError = exception;
                healthTracker.RecordFailure(replica.DestinationId, StorageOperationKind.Read, exception.Category);
                logger.LogWarning(
                    "Storage read replica failed; another verified replica will be attempted. ObjectId={ObjectId} DestinationId={DestinationId} Category={Category}",
                    storageObject.Id,
                    replica.DestinationId,
                    exception.Category);
            }
        }

        throw new StorageProviderException(
            lastError?.Category ?? StorageErrorCategory.ObjectMissing,
            "No verified current storage replica is readable.",
            lastError);
    }
}
