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
    DateTimeOffset CreatedAtUtc,
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

public sealed record StorageManifestPage(IReadOnlyList<StorageManifestEntry> Items, string? NextCursor);

public interface IStorageMaintenanceLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string scope, CancellationToken cancellationToken);
}

public sealed record StorageReconciliationSafetyState(
    bool Paused,
    bool PersistenceAvailable,
    int AnomalyCount,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? ResumedAtUtc,
    string? Category);

public interface IStorageReconciliationGuard
{
    Task<StorageReconciliationSafetyState> PeekStateAsync(CancellationToken cancellationToken) => GetStateAsync(cancellationToken);
    Task<StorageReconciliationSafetyState> GetStateAsync(CancellationToken cancellationToken);
    Task<StorageReconciliationSafetyState> PauseAsync(int anomalyCount, CancellationToken cancellationToken);
    Task<StorageReconciliationSafetyState> ResumeAsync(string reason, CancellationToken cancellationToken);
}

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
    Task<StorageTransferJob?> ClaimNextJobAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        IReadOnlyCollection<StorageTransferJobKind> allowedKinds,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? objectIds = null) => throw new NotSupportedException();
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
    Task<bool> ScheduleRepairAsync(
        Guid objectId,
        string destinationId,
        StorageReplicaState observedState,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> RetryProtectionAsync(
        Guid objectId,
        int requiredCopies,
        int desiredCopies,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken) => Task.FromResult(false);
    Task<StorageObject?> TombstoneAndScheduleDeleteAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteReplicaDeleteAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task ScheduleDeleteRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        string category,
        string safeError,
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
    Task<StorageManifestPage> ExportManifestPageAsync(
        string tenantId,
        StorageDataClass dataClass,
        int take,
        string? afterLogicalKey,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<bool> RecordReplicaVerifiedAsync(
        Guid objectId,
        string destinationId,
        long generation,
        string sha256,
        long sizeBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> EnsureTargetsAsync(
        Guid objectId,
        string policyId,
        int policyRevision,
        IReadOnlyList<StorageReplicationTarget> targets,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
