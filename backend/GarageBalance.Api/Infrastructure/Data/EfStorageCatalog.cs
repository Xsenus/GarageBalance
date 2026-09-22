using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStorageCatalog(GarageBalanceDbContext dbContext) : IStorageCatalog
{
    public async Task<StorageCatalogRegistration> RegisterCommittedObjectAsync(
        RegisterCommittedStorageObjectRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRegistration(request);
        var normalizedKey = StorageObjectKey.Normalize(request.LogicalKey);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await dbContext.StorageObjects
            .Include(item => item.Replicas)
            .Include(item => item.Jobs)
            .SingleOrDefaultAsync(
                item => item.TenantId == request.TenantId &&
                    item.DataClass == request.DataClass &&
                    item.OperationId == request.OperationId,
                cancellationToken);
        if (existing is not null)
        {
            EnsureIdempotent(existing, request, normalizedKey);
            await transaction.CommitAsync(cancellationToken);
            return new StorageCatalogRegistration(
                existing,
                existing.Replicas.Single(replica => replica.DestinationId == request.LocalDestinationId),
                existing.Jobs.OrderBy(job => job.DestinationId, StringComparer.Ordinal).ToArray(),
                true);
        }

        var storageObject = new StorageObject
        {
            OperationId = request.OperationId,
            TenantId = request.TenantId,
            DataClass = request.DataClass,
            LogicalKey = normalizedKey,
            PolicyId = request.PolicyId,
            PolicyRevision = request.PolicyRevision,
            CommittedGeneration = request.Generation,
            State = StorageObjectState.CreatedLocal,
            SizeBytes = request.SizeBytes,
            Sha256 = request.Sha256.ToLowerInvariant(),
            OriginalFileName = request.OriginalFileName,
            ContentType = request.ContentType,
            CreatedAtUtc = request.CreatedAtUtc,
            UpdatedAtUtc = request.CreatedAtUtc
        };
        var localReplica = new StorageObjectReplica
        {
            StorageObject = storageObject,
            DestinationId = request.LocalDestinationId,
            FailureDomain = request.LocalFailureDomain,
            NativeLocator = request.LocalNativeLocator,
            Generation = request.Generation,
            State = StorageReplicaState.Available,
            SizeBytes = request.SizeBytes,
            Sha256 = request.Sha256.ToLowerInvariant(),
            CreatedAtUtc = request.CreatedAtUtc,
            UpdatedAtUtc = request.CreatedAtUtc,
            LastVerifiedAtUtc = request.CreatedAtUtc
        };
        storageObject.Replicas.Add(localReplica);

        var remoteReplicas = request.ReplicationTargets
            .Where(target => !string.Equals(target.DestinationId, request.LocalDestinationId, StringComparison.Ordinal))
            .GroupBy(target => target.DestinationId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(target => new StorageObjectReplica
            {
                StorageObject = storageObject,
                DestinationId = target.DestinationId,
                FailureDomain = target.FailureDomain,
                NativeLocator = normalizedKey,
                Generation = request.Generation,
                State = StorageReplicaState.Pending,
                CreatedAtUtc = request.CreatedAtUtc,
                UpdatedAtUtc = request.CreatedAtUtc
            })
            .ToArray();
        storageObject.Replicas.AddRange(remoteReplicas);
        if (remoteReplicas.Length > 0)
        {
            storageObject.State = StorageObjectState.ProtectionPending;
        }
        var jobs = remoteReplicas
            .Select(replica => new StorageTransferJob
            {
                StorageObject = storageObject,
                StorageObjectReplica = replica,
                DestinationId = replica.DestinationId,
                Generation = request.Generation,
                Kind = StorageTransferJobKind.Replicate,
                State = StorageTransferJobState.Ready,
                IdempotencyKey = $"{request.OperationId:N}:{request.Generation}:{replica.DestinationId}:replicate",
                PolicyRevision = request.PolicyRevision,
                MaximumAttempts = request.MaximumAttempts,
                DueAtUtc = request.CreatedAtUtc,
                CreatedAtUtc = request.CreatedAtUtc,
                UpdatedAtUtc = request.CreatedAtUtc
            })
            .ToArray();
        storageObject.Jobs.AddRange(jobs);
        dbContext.StorageObjects.Add(storageObject);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StorageCatalogRegistration(storageObject, localReplica, jobs, false);
    }

    public Task<StorageObject?> FindObjectAsync(Guid objectId, CancellationToken cancellationToken) =>
        dbContext.StorageObjects.AsNoTracking()
            .Include(item => item.Replicas)
            .Include(item => item.Jobs)
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken);

    public Task<StorageObject?> FindByLogicalKeyAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        CancellationToken cancellationToken)
    {
        var normalizedKey = StorageObjectKey.Normalize(logicalKey);
        return dbContext.StorageObjects.AsNoTracking()
            .Include(item => item.Replicas)
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId && item.DataClass == dataClass && item.LogicalKey == normalizedKey,
                cancellationToken);
    }

    public async Task<StorageTransferJob?> ClaimNextJobAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 160 ||
            leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentException("A bounded lease owner and duration are required.", nameof(leaseOwner));
        }

        Guid[] candidateIds;
        if (IsPostgreSql())
        {
            candidateIds = await dbContext.StorageTransferJobs.AsNoTracking()
                .Where(job =>
                    job.DueAtUtc <= now &&
                    job.AttemptCount < job.MaximumAttempts &&
                    (job.State == StorageTransferJobState.Ready ||
                     job.State == StorageTransferJobState.RetryScheduled ||
                     (job.State == StorageTransferJobState.Leased && job.LeaseExpiresAtUtc <= now)))
                .OrderBy(job => job.DueAtUtc)
                .ThenBy(job => job.CreatedAtUtc)
                .Select(job => job.Id)
                .Take(16)
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            var boundedCandidates = await dbContext.StorageTransferJobs.AsNoTracking()
                .Where(job =>
                    job.AttemptCount < job.MaximumAttempts &&
                    (job.State == StorageTransferJobState.Ready ||
                     job.State == StorageTransferJobState.RetryScheduled ||
                     job.State == StorageTransferJobState.Leased))
                .Take(256)
                .ToArrayAsync(cancellationToken);
            candidateIds = boundedCandidates
                .Where(job => job.DueAtUtc <= now &&
                    (job.State != StorageTransferJobState.Leased || job.LeaseExpiresAtUtc <= now))
                .OrderBy(job => job.DueAtUtc)
                .ThenBy(job => job.CreatedAtUtc)
                .Select(job => job.Id)
                .Take(16)
                .ToArray();
        }

        foreach (var candidateId in candidateIds)
        {
            var leaseExpiresAtUtc = now.Add(leaseDuration);
            var version = Guid.NewGuid();
            var claimable = dbContext.StorageTransferJobs.Where(job =>
                job.Id == candidateId &&
                job.AttemptCount < job.MaximumAttempts &&
                (job.State == StorageTransferJobState.Ready ||
                 job.State == StorageTransferJobState.RetryScheduled ||
                 job.State == StorageTransferJobState.Leased));
            if (IsPostgreSql())
            {
                claimable = claimable.Where(job =>
                    job.DueAtUtc <= now &&
                    (job.State != StorageTransferJobState.Leased || job.LeaseExpiresAtUtc <= now));
            }
            var updated = await claimable
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.State, StorageTransferJobState.Leased)
                    .SetProperty(job => job.LeaseOwner, leaseOwner)
                    .SetProperty(job => job.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                    .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                    .SetProperty(job => job.UpdatedAtUtc, now)
                    .SetProperty(job => job.Version, version),
                    cancellationToken);
            if (updated == 1)
            {
                dbContext.ChangeTracker.Clear();
                return await dbContext.StorageTransferJobs.AsNoTracking()
                    .SingleAsync(job => job.Id == candidateId, cancellationToken);
            }
        }

        return null;
    }

    public async Task CompleteJobAsync(Guid jobId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobAsync(jobId, leaseOwner, cancellationToken);
        job.Complete(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageTransferContext> GetLeasedJobContextAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.StorageTransferJobs.AsNoTracking()
            .Include(item => item.StorageObject)
                .ThenInclude(item => item!.Replicas)
            .Include(item => item.StorageObjectReplica)
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException("Storage job was not found.");
        EnsureLeaseOwner(job, leaseOwner);
        var storageObject = job.StorageObject
            ?? throw new InvalidOperationException("Storage job has no logical object.");
        var target = job.StorageObjectReplica
            ?? throw new InvalidOperationException("Storage job has no target replica.");
        var sources = storageObject.Replicas
            .Where(replica => replica.Id != target.Id &&
                replica.State == StorageReplicaState.Available &&
                replica.Generation == storageObject.CommittedGeneration &&
                replica.SizeBytes == storageObject.SizeBytes &&
                string.Equals(replica.Sha256, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
            .OrderBy(replica => replica.CreatedAtUtc)
            .ToArray();
        return new StorageTransferContext(job, storageObject, target, sources);
    }

    public async Task MarkReplicationUploadingAsync(
        Guid jobId,
        string leaseOwner,
        string nativeLocator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        var replica = job.StorageObjectReplica
            ?? throw new InvalidOperationException("Storage job has no target replica.");
        replica.MarkUploading(nativeLocator, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteReplicationAsync(
        Guid jobId,
        string leaseOwner,
        string nativeLocator,
        string? providerVersionId,
        string? providerChecksum,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        var storageObject = job.StorageObject
            ?? throw new InvalidOperationException("Storage job has no logical object.");
        var replica = job.StorageObjectReplica
            ?? throw new InvalidOperationException("Storage job has no target replica.");
        EnsureCurrentGeneration(job, storageObject, replica);
        replica.NativeLocator = StorageObjectKey.Normalize(nativeLocator);
        replica.ProviderVersionId = providerVersionId;
        replica.MarkAvailable(storageObject.SizeBytes, storageObject.Sha256, providerChecksum, now);
        job.Complete(now);
        RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ScheduleReplicationRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        StorageReplicaState replicaState,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        var storageObject = job.StorageObject
            ?? throw new InvalidOperationException("Storage job has no logical object.");
        var replica = job.StorageObjectReplica
            ?? throw new InvalidOperationException("Storage job has no target replica.");
        if (replicaState == StorageReplicaState.Unknown)
        {
            replica.MarkUnknown(safeError, now);
        }
        else if (replicaState == StorageReplicaState.Failed)
        {
            replica.MarkFailed(category, safeError, now);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(replicaState));
        }
        job.ScheduleRetry(dueAtUtc, category, safeError, now);
        RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BlockReplicationAsync(
        Guid jobId,
        string leaseOwner,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        var storageObject = job.StorageObject
            ?? throw new InvalidOperationException("Storage job has no logical object.");
        var replica = job.StorageObjectReplica
            ?? throw new InvalidOperationException("Storage job has no target replica.");
        replica.MarkFailed(category, safeError, now);
        job.Block(category, safeError, now);
        RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ScheduleJobRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        string category,
        string safeError,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobAsync(jobId, leaseOwner, cancellationToken);
        job.ScheduleRetry(dueAtUtc, category, safeError, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageManifestEntry>> ExportManifestAsync(
        StorageDataClass dataClass,
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        StorageObject[] objects;
        if (IsPostgreSql())
        {
            objects = await dbContext.StorageObjects.AsNoTracking()
                .Where(item => item.DataClass == dataClass)
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(take)
                .Include(item => item.Replicas)
                .ToArrayAsync(cancellationToken);
        }
        else
        {
            var boundedObjects = await dbContext.StorageObjects.AsNoTracking()
                .Where(item => item.DataClass == dataClass)
                .Take(take)
                .Include(item => item.Replicas)
                .ToArrayAsync(cancellationToken);
            objects = boundedObjects.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
        }
        return objects.Select(item => new StorageManifestEntry(
            item.Id,
            item.OperationId,
            item.DataClass,
            item.LogicalKey,
            item.CommittedGeneration,
            item.SizeBytes,
            item.Sha256,
            item.State,
            item.UpdatedAtUtc,
            item.Replicas.OrderBy(replica => replica.DestinationId, StringComparer.Ordinal)
                .Select(replica => new StorageManifestReplicaEntry(
                    replica.DestinationId,
                    replica.FailureDomain,
                    replica.NativeLocator,
                    replica.Generation,
                    replica.State,
                    replica.SizeBytes,
                    replica.Sha256,
                    replica.LastVerifiedAtUtc))
                .ToArray())).ToArray();
    }

    private async Task<StorageTransferJob> GetOwnedLeasedJobAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.StorageTransferJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException("Storage job was not found.");
        if (job.State != StorageTransferJobState.Leased || !string.Equals(job.LeaseOwner, leaseOwner, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage job lease is not owned by this worker.");
        }
        return job;
    }

    private async Task<StorageTransferJob> GetOwnedLeasedJobWithGraphAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.StorageTransferJobs
            .Include(item => item.StorageObject)
                .ThenInclude(item => item!.Replicas)
            .Include(item => item.StorageObjectReplica)
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException("Storage job was not found.");
        EnsureLeaseOwner(job, leaseOwner);
        return job;
    }

    private static void EnsureLeaseOwner(StorageTransferJob job, string leaseOwner)
    {
        if (job.State != StorageTransferJobState.Leased ||
            !string.Equals(job.LeaseOwner, leaseOwner, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage job lease is not owned by this worker.");
        }
    }

    private static void EnsureCurrentGeneration(
        StorageTransferJob job,
        StorageObject storageObject,
        StorageObjectReplica replica)
    {
        if (storageObject.TombstonedAtUtc is not null ||
            job.Generation != storageObject.CommittedGeneration ||
            replica.Generation != storageObject.CommittedGeneration ||
            job.PolicyRevision != storageObject.PolicyRevision)
        {
            throw new InvalidOperationException("Storage replication job no longer targets the committed object generation and policy.");
        }
    }

    private static void RefreshProtection(
        StorageObject storageObject,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now)
    {
        var availableFailureDomains = storageObject.Replicas
            .Where(replica => replica.State == StorageReplicaState.Available &&
                replica.Generation == storageObject.CommittedGeneration &&
                replica.SizeBytes == storageObject.SizeBytes &&
                string.Equals(replica.Sha256, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(replica => replica.FailureDomain)
            .Distinct(StringComparer.Ordinal)
            .Count();
        storageObject.SetProtection(availableFailureDomains, requiredCopies, desiredCopies, now);
    }

    private static void ValidateRegistration(RegisterCommittedStorageObjectRequest request)
    {
        if (request.OperationId == Guid.Empty || request.Generation < 1 || request.SizeBytes < 1 ||
            request.PolicyRevision < 1 || request.MaximumAttempts < 1 ||
            request.Sha256.Length != 64 || !request.Sha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(request.LocalDestinationId) || string.IsNullOrWhiteSpace(request.LocalFailureDomain) ||
            string.IsNullOrWhiteSpace(request.LocalNativeLocator) ||
            request.ReplicationTargets.Any(target =>
                string.IsNullOrWhiteSpace(target.DestinationId) || string.IsNullOrWhiteSpace(target.FailureDomain)) ||
            request.ReplicationTargets
                .GroupBy(target => target.DestinationId, StringComparer.Ordinal)
                .Any(group => group.Select(target => target.FailureDomain).Distinct(StringComparer.Ordinal).Count() != 1))
        {
            throw new ArgumentException("Committed storage object registration is incomplete or invalid.", nameof(request));
        }
    }

    private static void EnsureIdempotent(
        StorageObject existing,
        RegisterCommittedStorageObjectRequest request,
        string normalizedKey)
    {
        if (!string.Equals(existing.LogicalKey, normalizedKey, StringComparison.Ordinal) ||
            existing.CommittedGeneration != request.Generation ||
            existing.SizeBytes != request.SizeBytes ||
            !string.Equals(existing.Sha256, request.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.PolicyId, request.PolicyId, StringComparison.Ordinal) ||
            existing.PolicyRevision != request.PolicyRevision)
        {
            throw new InvalidOperationException("Storage operation id was reused with different immutable object data.");
        }
    }

    private bool IsPostgreSql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
}
