using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.Api.Application.Storage;

public sealed record RegisterCommittedStorageObjectRequest(
    Guid OperationId,
    string TenantId,
    StorageDataClass DataClass,
    string LogicalKey,
    string PolicyId,
    int PolicyRevision,
    long Generation,
    long SizeBytes,
    string Sha256,
    string OriginalFileName,
    string? ContentType,
    string LocalDestinationId,
    string LocalFailureDomain,
    string LocalNativeLocator,
    IReadOnlyList<StorageReplicationTarget> ReplicationTargets,
    int MaximumAttempts,
    DateTimeOffset CreatedAtUtc);

public sealed record StorageReplicationTarget(string DestinationId, string FailureDomain);

public sealed record StorageCatalogRegistration(
    StorageObject Object,
    StorageObjectReplica LocalReplica,
    IReadOnlyList<StorageTransferJob> Jobs,
    bool AlreadyExisted);

public sealed record StorageTransferContext(
    StorageTransferJob Job,
    StorageObject Object,
    StorageObjectReplica TargetReplica,
    IReadOnlyList<StorageObjectReplica> AvailableSources);

public sealed record StorageManifestEntry(
    Guid ObjectId,
    Guid OperationId,
    StorageDataClass DataClass,
    string LogicalKey,
    long Generation,
    long SizeBytes,
    string Sha256,
    StorageObjectState State,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<StorageManifestReplicaEntry> Replicas);

public sealed record StorageManifestReplicaEntry(
    string DestinationId,
    string FailureDomain,
    string NativeLocator,
    long Generation,
    StorageReplicaState State,
    long? SizeBytes,
    string? Sha256,
    DateTimeOffset? LastVerifiedAtUtc);

public interface IStorageCatalog
{
    Task<StorageCatalogRegistration> RegisterCommittedObjectAsync(
        RegisterCommittedStorageObjectRequest request,
        CancellationToken cancellationToken);
    Task<StorageObject?> FindObjectAsync(Guid objectId, CancellationToken cancellationToken);
    Task<StorageObject?> FindByLogicalKeyAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        CancellationToken cancellationToken);
    Task<StorageTransferJob?> ClaimNextJobAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<StorageTransferContext> GetLeasedJobContextAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken);
    Task MarkReplicationUploadingAsync(
        Guid jobId,
        string leaseOwner,
        string nativeLocator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteReplicationAsync(
        Guid jobId,
        string leaseOwner,
        string nativeLocator,
        string? providerVersionId,
        string? providerChecksum,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task ScheduleReplicationRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        StorageReplicaState replicaState,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task BlockReplicationAsync(
        Guid jobId,
        string leaseOwner,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteJobAsync(Guid jobId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken);
    Task ScheduleJobRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        string category,
        string safeError,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageManifestEntry>> ExportManifestAsync(
        StorageDataClass dataClass,
        int take,
        CancellationToken cancellationToken);
}
