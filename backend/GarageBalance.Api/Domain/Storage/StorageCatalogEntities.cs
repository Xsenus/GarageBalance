using GarageBalance.Api.Domain.Common;

namespace GarageBalance.Api.Domain.Storage;

public enum StorageDataClass
{
    DatabaseBackup,
    RecoverySecrets
}

public enum StorageObjectState
{
    Creating,
    CreatedLocal,
    ProtectionPending,
    Protected,
    ProtectionDegraded,
    Deleting,
    Deleted,
    Failed
}

public enum StorageReplicaState
{
    Pending,
    Uploading,
    Unknown,
    VerificationPending,
    Available,
    Missing,
    Corrupted,
    Failed,
    Deleting,
    Deleted,
    Disabled
}

public enum StorageTransferJobKind
{
    Replicate,
    Verify,
    Delete,
    Repair
}

public enum StorageTransferJobState
{
    Ready,
    Leased,
    RetryScheduled,
    Completed,
    Blocked,
    DeadLetter
}

public sealed class StorageObject : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public string TenantId { get; set; } = "garagebalance";
    public StorageDataClass DataClass { get; set; }
    public string LogicalKey { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public int PolicyRevision { get; set; } = 1;
    public long CommittedGeneration { get; set; }
    public StorageObjectState State { get; set; } = StorageObjectState.Creating;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TombstonedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<StorageObjectReplica> Replicas { get; set; } = [];
    public List<StorageTransferJob> Jobs { get; set; } = [];

    public void CommitLocal(long generation, long sizeBytes, string sha256, DateTimeOffset now)
    {
        if (State != StorageObjectState.Creating || generation <= CommittedGeneration || sizeBytes <= 0 || sha256.Length != 64)
        {
            throw new InvalidOperationException("Only a complete verified newer generation can be committed.");
        }

        CommittedGeneration = generation;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        State = StorageObjectState.CreatedLocal;
        UpdatedAtUtc = now;
    }

    public void SetProtection(int availableIndependentCopies, int requiredCopies, int desiredCopies, DateTimeOffset now)
    {
        if (State is StorageObjectState.Deleting or StorageObjectState.Deleted or StorageObjectState.Failed)
        {
            throw new InvalidOperationException("Protection state cannot be changed for a terminal storage object.");
        }
        if (requiredCopies < 1 || desiredCopies < requiredCopies || availableIndependentCopies < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableIndependentCopies));
        }

        State = availableIndependentCopies >= requiredCopies
            ? StorageObjectState.Protected
            : availableIndependentCopies > 0
                ? StorageObjectState.ProtectionDegraded
                : StorageObjectState.Failed;
        UpdatedAtUtc = now;
    }

    public void BeginDelete(DateTimeOffset now)
    {
        if (State == StorageObjectState.Deleted)
        {
            return;
        }
        State = StorageObjectState.Deleting;
        TombstonedAtUtc ??= now;
        UpdatedAtUtc = now;
    }
}

public sealed class StorageObjectReplica : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StorageObjectId { get; set; }
    public StorageObject? StorageObject { get; set; }
    public string DestinationId { get; set; } = string.Empty;
    public string FailureDomain { get; set; } = string.Empty;
    public string NativeLocator { get; set; } = string.Empty;
    public string? ProviderVersionId { get; set; }
    public long Generation { get; set; }
    public StorageReplicaState State { get; set; } = StorageReplicaState.Pending;
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? ProviderChecksum { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastVerifiedAtUtc { get; set; }
    public string? LastErrorCategory { get; set; }
    public string? LastError { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<StorageTransferJob> Jobs { get; set; } = [];

    public void MarkAvailable(long sizeBytes, string sha256, string? providerChecksum, DateTimeOffset now)
    {
        if (State is StorageReplicaState.Deleting or StorageReplicaState.Deleted or StorageReplicaState.Disabled)
        {
            throw new InvalidOperationException("A deleting, deleted, or disabled replica cannot become available.");
        }
        if (sizeBytes <= 0 || sha256.Length != 64)
        {
            throw new InvalidOperationException("A replica can be available only after size and SHA-256 verification.");
        }

        SizeBytes = sizeBytes;
        Sha256 = sha256;
        ProviderChecksum = providerChecksum;
        State = StorageReplicaState.Available;
        LastVerifiedAtUtc = now;
        LastErrorCategory = null;
        LastError = null;
        UpdatedAtUtc = now;
    }

    public void MarkUnknown(string safeError, DateTimeOffset now)
    {
        if (State is StorageReplicaState.Deleted or StorageReplicaState.Disabled)
        {
            throw new InvalidOperationException("A terminal replica cannot return to unknown state.");
        }
        State = StorageReplicaState.Unknown;
        LastErrorCategory = "UnknownOutcome";
        LastError = LimitError(safeError);
        UpdatedAtUtc = now;
    }

    public void MarkUploading(string nativeLocator, DateTimeOffset now)
    {
        if (State is StorageReplicaState.Deleting or StorageReplicaState.Deleted or StorageReplicaState.Disabled ||
            string.IsNullOrWhiteSpace(nativeLocator))
        {
            throw new InvalidOperationException("A terminal replica cannot start uploading and a locator is required.");
        }
        NativeLocator = nativeLocator;
        State = StorageReplicaState.Uploading;
        LastErrorCategory = null;
        LastError = null;
        UpdatedAtUtc = now;
    }

    public void MarkFailed(string category, string safeError, DateTimeOffset now)
    {
        if (State is StorageReplicaState.Deleted or StorageReplicaState.Disabled)
        {
            throw new InvalidOperationException("A terminal replica cannot fail again.");
        }
        State = StorageReplicaState.Failed;
        LastErrorCategory = LimitError(category);
        LastError = LimitError(safeError);
        UpdatedAtUtc = now;
    }

    public void BeginDelete(DateTimeOffset now)
    {
        State = StorageReplicaState.Deleting;
        UpdatedAtUtc = now;
    }

    internal static string LimitError(string value) => value.Length <= 1000 ? value : value[..1000];
}

public sealed class StorageTransferJob : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StorageObjectId { get; set; }
    public StorageObject? StorageObject { get; set; }
    public Guid? StorageObjectReplicaId { get; set; }
    public StorageObjectReplica? StorageObjectReplica { get; set; }
    public string DestinationId { get; set; } = string.Empty;
    public long Generation { get; set; }
    public StorageTransferJobKind Kind { get; set; }
    public StorageTransferJobState State { get; set; } = StorageTransferJobState.Ready;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int PolicyRevision { get; set; } = 1;
    public int AttemptCount { get; set; }
    public int MaximumAttempts { get; set; } = 12;
    public DateTimeOffset DueAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public string? LastErrorCategory { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();

    public bool TryLease(string owner, DateTimeOffset now, TimeSpan duration)
    {
        var eligible = State is StorageTransferJobState.Ready or StorageTransferJobState.RetryScheduled ||
            State == StorageTransferJobState.Leased && LeaseExpiresAtUtc <= now;
        if (!eligible || DueAtUtc > now || AttemptCount >= MaximumAttempts || string.IsNullOrWhiteSpace(owner) || duration <= TimeSpan.Zero)
        {
            return false;
        }

        State = StorageTransferJobState.Leased;
        LeaseOwner = owner;
        LeaseExpiresAtUtc = now.Add(duration);
        AttemptCount++;
        UpdatedAtUtc = now;
        return true;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureLeased();
        State = StorageTransferJobState.Completed;
        ClearLease();
        LastErrorCategory = null;
        LastError = null;
        UpdatedAtUtc = now;
    }

    public void ScheduleRetry(DateTimeOffset dueAtUtc, string category, string safeError, DateTimeOffset now)
    {
        EnsureLeased();
        State = AttemptCount >= MaximumAttempts ? StorageTransferJobState.DeadLetter : StorageTransferJobState.RetryScheduled;
        DueAtUtc = dueAtUtc;
        LastErrorCategory = category;
        LastError = StorageObjectReplica.LimitError(safeError);
        ClearLease();
        UpdatedAtUtc = now;
    }

    public void Block(string category, string safeError, DateTimeOffset now)
    {
        EnsureLeased();
        State = StorageTransferJobState.Blocked;
        LastErrorCategory = category;
        LastError = StorageObjectReplica.LimitError(safeError);
        ClearLease();
        UpdatedAtUtc = now;
    }

    private void EnsureLeased()
    {
        if (State != StorageTransferJobState.Leased)
        {
            throw new InvalidOperationException("Only a leased storage job can be completed or rescheduled.");
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }
}
