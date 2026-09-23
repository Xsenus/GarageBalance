using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStorageCatalog(GarageBalanceDbContext dbContext, StorageConfigurationResolver? configurationResolver = null) : IStorageCatalog
{
    private readonly EffectiveStorageConfiguration? configuration = configurationResolver?.Resolve();
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
            if (existing.TombstonedAtUtc is null && existing.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted))
            {
                AddMissingTargets(existing, request.ReplicationTargets, request.MaximumAttempts, request.CreatedAtUtc);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
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

    public Task<StorageTransferJob?> ClaimNextJobAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ClaimNextJobAsync(leaseOwner, leaseDuration, now, Enum.GetValues<StorageTransferJobKind>(), cancellationToken);

    public async Task<StorageTransferJob?> ClaimNextJobAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        IReadOnlyCollection<StorageTransferJobKind> allowedKinds,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? objectIds = null)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 160 ||
            leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)))
        {
            throw new ArgumentException("A bounded lease owner and duration are required.", nameof(leaseOwner));
        }
        if (allowedKinds.Count == 0)
        {
            return null;
        }
        var eligibleKinds = allowedKinds.Distinct().ToArray();
        var eligibleObjects = objectIds?.Distinct().ToArray();
        var eligibleJobs = dbContext.StorageTransferJobs.AsNoTracking().Where(job => eligibleKinds.Contains(job.Kind));
        if (eligibleObjects is not null)
        {
            eligibleJobs = eligibleJobs.Where(job => eligibleObjects.Contains(job.StorageObjectId));
        }

        Guid[] candidateIds;
        if (IsPostgreSql())
        {
            candidateIds = await eligibleJobs
                .Where(job =>
                    eligibleKinds.Contains(job.Kind) &&
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
            var boundedCandidates = await eligibleJobs
                .Where(job =>
                    eligibleKinds.Contains(job.Kind) &&
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
                eligibleKinds.Contains(job.Kind) &&
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
        EnsureCurrentGeneration(job, job.StorageObject ?? throw new InvalidOperationException("Storage job has no logical object."), replica);
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
        if (storageObject.TombstonedAtUtc is not null || storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            job.Block("Tombstoned", "Storage object was deleted while replication was in progress.", now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
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
        job.Block(category, safeError, now);
        if (storageObject.TombstonedAtUtc is null && storageObject.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted))
        {
            replica.MarkFailed(category, safeError, now);
            RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ScheduleRepairAsync(
        Guid objectId,
        string destinationId,
        StorageReplicaState observedState,
        string category,
        string safeError,
        int requiredCopies,
        int desiredCopies,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (observedState is not (StorageReplicaState.Missing or StorageReplicaState.Corrupted) ||
            maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(observedState));
        }
        var storageObject = await dbContext.StorageObjects
            .Include(item => item.Replicas)
            .Include(item => item.Jobs)
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken)
            ?? throw new InvalidOperationException("Storage object was not found.");
        if (storageObject.TombstonedAtUtc is not null || storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            return false;
        }
        var replica = storageObject.Replicas.SingleOrDefault(item =>
            string.Equals(item.DestinationId, destinationId, StringComparison.Ordinal) &&
            item.Generation == storageObject.CommittedGeneration)
            ?? throw new InvalidOperationException("Storage replica was not found.");
        if (storageObject.Jobs.Any(item => item.DestinationId == destinationId && item.Generation == storageObject.CommittedGeneration &&
                item.State == StorageTransferJobState.Leased && item.LeaseExpiresAtUtc > now))
        {
            return false;
        }
        replica.State = observedState;
        replica.LastErrorCategory = StorageObjectReplica.LimitError(category);
        replica.LastError = StorageObjectReplica.LimitError(safeError);
        replica.UpdatedAtUtc = now;
        var idempotencyKey = $"{storageObject.OperationId:N}:{storageObject.CommittedGeneration}:{destinationId}:repair";
        var job = storageObject.Jobs.Where(item => item.Kind == StorageTransferJobKind.Repair &&
                item.DestinationId == destinationId && item.Generation == storageObject.CommittedGeneration &&
                item.State is not (StorageTransferJobState.Completed or StorageTransferJobState.Blocked or StorageTransferJobState.DeadLetter))
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
        if (job is null)
        {
            job = new StorageTransferJob
            {
                StorageObject = storageObject,
                StorageObjectReplica = replica,
                DestinationId = destinationId,
                Generation = storageObject.CommittedGeneration,
                Kind = StorageTransferJobKind.Repair,
                State = StorageTransferJobState.Ready,
                IdempotencyKey = idempotencyKey + ":" + Guid.NewGuid().ToString("N"),
                PolicyRevision = storageObject.PolicyRevision,
                MaximumAttempts = maximumAttempts,
                DueAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.StorageTransferJobs.Add(job);
        }
        RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<StorageObject?> TombstoneAndScheduleDeleteAsync(
        string tenantId,
        StorageDataClass dataClass,
        string logicalKey,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedKey = StorageObjectKey.Normalize(logicalKey);
        var storageObject = await dbContext.StorageObjects
            .Include(item => item.Replicas)
            .Include(item => item.Jobs)
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.DataClass == dataClass && item.LogicalKey == normalizedKey, cancellationToken);
        if (storageObject is null)
        {
            return null;
        }
        storageObject.BeginDelete(now);
        // An already-running immutable upload may still be finishing; delete only after its bounded lease.
        var deleteNotBefore = storageObject.Jobs
            .Where(job => job.State == StorageTransferJobState.Leased && job.Kind != StorageTransferJobKind.Delete)
            .Select(job => job.LeaseExpiresAtUtc ?? now)
            .Append(now)
            .Max();
        foreach (var obsolete in storageObject.Jobs.Where(job => job.Kind != StorageTransferJobKind.Delete &&
                     job.State is StorageTransferJobState.Ready or StorageTransferJobState.RetryScheduled))
        {
            obsolete.State = StorageTransferJobState.Blocked;
            obsolete.LastErrorCategory = "Tombstoned";
            obsolete.LastError = "Storage object was deleted before delivery completed.";
            obsolete.UpdatedAtUtc = now;
        }
        foreach (var replica in storageObject.Replicas.Where(item => item.State != StorageReplicaState.Deleted))
        {
            replica.BeginDelete(now);
            var idempotencyKey = $"{storageObject.OperationId:N}:{storageObject.CommittedGeneration}:{replica.DestinationId}:delete";
            if (storageObject.Jobs.Any(item => string.Equals(item.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
            {
                continue;
            }
            dbContext.StorageTransferJobs.Add(new StorageTransferJob
            {
                StorageObject = storageObject,
                StorageObjectReplica = replica,
                DestinationId = replica.DestinationId,
                Generation = storageObject.CommittedGeneration,
                Kind = StorageTransferJobKind.Delete,
                State = StorageTransferJobState.Ready,
                IdempotencyKey = idempotencyKey,
                PolicyRevision = storageObject.PolicyRevision,
                MaximumAttempts = maximumAttempts,
                DueAtUtc = deleteNotBefore,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        if (storageObject.Replicas.All(item => item.State == StorageReplicaState.Deleted))
        {
            storageObject.State = StorageObjectState.Deleted;
            storageObject.UpdatedAtUtc = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return storageObject;
    }

    public async Task<bool> RetryProtectionAsync(
        Guid objectId,
        int requiredCopies,
        int desiredCopies,
        int maximumAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }
        var storageObject = await dbContext.StorageObjects
            .Include(item => item.Replicas)
            .Include(item => item.Jobs)
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken)
            ?? throw new InvalidOperationException("Storage object was not found.");
        if (storageObject.TombstonedAtUtc is not null || storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            return false;
        }

        var scheduled = false;
        foreach (var replica in storageObject.Replicas.Where(item =>
                     item.Generation == storageObject.CommittedGeneration &&
                     item.State is not (StorageReplicaState.Available or StorageReplicaState.Deleting or StorageReplicaState.Deleted or StorageReplicaState.Disabled)))
        {
            if (storageObject.Jobs.Any(item => item.DestinationId == replica.DestinationId && item.Generation == storageObject.CommittedGeneration &&
                    item.State == StorageTransferJobState.Leased && item.LeaseExpiresAtUtc > now))
            {
                continue;
            }
            replica.State = StorageReplicaState.Missing;
            replica.LastErrorCategory = null;
            replica.LastError = null;
            replica.UpdatedAtUtc = now;
            var key = $"{storageObject.OperationId:N}:{storageObject.CommittedGeneration}:{replica.DestinationId}:repair";
            var job = storageObject.Jobs.Where(item => item.Kind is StorageTransferJobKind.Replicate or StorageTransferJobKind.Repair &&
                    item.DestinationId == replica.DestinationId && item.Generation == storageObject.CommittedGeneration &&
                    item.State is not (StorageTransferJobState.Completed or StorageTransferJobState.Blocked or StorageTransferJobState.DeadLetter))
                .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
            if (job is null)
            {
                dbContext.StorageTransferJobs.Add(new StorageTransferJob
                {
                    StorageObject = storageObject,
                    StorageObjectReplica = replica,
                    DestinationId = replica.DestinationId,
                    Generation = storageObject.CommittedGeneration,
                    Kind = StorageTransferJobKind.Repair,
                    State = StorageTransferJobState.Ready,
                    IdempotencyKey = key + ":" + Guid.NewGuid().ToString("N"),
                    PolicyRevision = storageObject.PolicyRevision,
                    MaximumAttempts = maximumAttempts,
                    DueAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                job.State = StorageTransferJobState.Ready;
                job.AttemptCount = 0;
                job.MaximumAttempts = maximumAttempts;
                job.DueAtUtc = now;
                job.LeaseOwner = null;
                job.LeaseExpiresAtUtc = null;
                job.LastErrorCategory = null;
                job.LastError = null;
                job.UpdatedAtUtc = now;
            }
            scheduled = true;
        }
        if (!scheduled)
        {
            return false;
        }
        RefreshProtection(storageObject, requiredCopies, desiredCopies, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task CompleteReplicaDeleteAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        if (job.Kind != StorageTransferJobKind.Delete)
        {
            throw new InvalidOperationException("Storage job is not a delete job.");
        }
        var storageObject = job.StorageObject ?? throw new InvalidOperationException("Storage job has no logical object.");
        var replica = job.StorageObjectReplica ?? throw new InvalidOperationException("Storage job has no target replica.");
        replica.State = StorageReplicaState.Deleted;
        replica.UpdatedAtUtc = now;
        replica.LastError = null;
        replica.LastErrorCategory = null;
        job.Complete(now);
        if (storageObject.Replicas.All(item => item.State == StorageReplicaState.Deleted))
        {
            storageObject.State = StorageObjectState.Deleted;
            storageObject.UpdatedAtUtc = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ScheduleDeleteRetryAsync(
        Guid jobId,
        string leaseOwner,
        DateTimeOffset dueAtUtc,
        string category,
        string safeError,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var job = await GetOwnedLeasedJobWithGraphAsync(jobId, leaseOwner, cancellationToken);
        var replica = job.StorageObjectReplica ?? throw new InvalidOperationException("Storage job has no target replica.");
        replica.LastErrorCategory = StorageObjectReplica.LimitError(category);
        replica.LastError = StorageObjectReplica.LimitError(safeError);
        replica.UpdatedAtUtc = now;
        job.ScheduleRetry(dueAtUtc, category, safeError, now);
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
        return objects.Select(ToManifestEntry).ToArray();
    }

    public async Task<StorageManifestPage> ExportManifestPageAsync(
        string tenantId,
        StorageDataClass dataClass,
        int take,
        string? afterLogicalKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || take is < 1 or > 10000)
        {
            throw new ArgumentException("A tenant and a page size between 1 and 10000 are required.");
        }
        var query = dbContext.StorageObjects.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.DataClass == dataClass);
        if (afterLogicalKey is not null)
        {
            query = query.Where(item => string.Compare(item.LogicalKey, afterLogicalKey) > 0);
        }
        // Immutable logical keys keep pages stable while verification updates timestamps.
        var objects = await query.OrderBy(item => item.LogicalKey)
            .Take(take + 1)
            .Include(item => item.Replicas)
            .ToArrayAsync(cancellationToken);
        var items = objects.Take(take).Select(ToManifestEntry).ToArray();
        return new StorageManifestPage(items, objects.Length > take ? items[^1].LogicalKey : null);
    }

    public async Task<bool> RecordReplicaVerifiedAsync(
        Guid objectId,
        string destinationId,
        long generation,
        string sha256,
        long sizeBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var storageObject = await dbContext.StorageObjects.Include(item => item.Replicas)
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken);
        if (storageObject is null || storageObject.TombstonedAtUtc is not null ||
            storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted ||
            storageObject.CommittedGeneration != generation || storageObject.SizeBytes != sizeBytes ||
            !string.Equals(storageObject.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var replica = storageObject.Replicas.SingleOrDefault(item => item.DestinationId == destinationId && item.Generation == generation);
        if (replica is null || replica.State is StorageReplicaState.Deleting or StorageReplicaState.Deleted or StorageReplicaState.Disabled)
        {
            return false;
        }
        replica.MarkAvailable(sizeBytes, sha256, sha256, now);
        var policy = configuration?.Policies.SingleOrDefault(item => item.Id == storageObject.PolicyId);
        if (policy is not null)
        {
            RefreshProtection(storageObject, policy.RequiredIndependentCopies, policy.DesiredCopies, now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> EnsureTargetsAsync(Guid objectId, string policyId, int policyRevision,
        IReadOnlyList<StorageReplicationTarget> targets, int maximumAttempts, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!StorageObjectKey.IsValidId(policyId) || policyRevision < 1 || maximumAttempts < 1 ||
            targets.Any(target => !StorageObjectKey.IsValidId(target.DestinationId) || !StorageObjectKey.IsValidId(target.FailureDomain)))
        {
            throw new ArgumentException("A valid policy and replication targets are required.");
        }
        var storageObject = await dbContext.StorageObjects.Include(item => item.Replicas).Include(item => item.Jobs)
            .SingleOrDefaultAsync(item => item.Id == objectId, cancellationToken)
            ?? throw new InvalidOperationException("Storage object was not found.");
        if (storageObject.TombstonedAtUtc is not null || storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
        {
            return false;
        }
        if (storageObject.PolicyId != policyId || policyRevision < storageObject.PolicyRevision)
        {
            throw new InvalidOperationException("A storage policy cannot be replaced or rolled back during target reconciliation.");
        }
        storageObject.PolicyRevision = policyRevision;
        var changed = AddMissingTargets(storageObject, targets, maximumAttempts, now);
        foreach (var job in storageObject.Jobs.Where(job => job.Kind is StorageTransferJobKind.Replicate or StorageTransferJobKind.Repair &&
                     job.State is not (StorageTransferJobState.Leased or StorageTransferJobState.Completed) &&
                     targets.Any(target => target.DestinationId == job.DestinationId)))
        {
            if (job.PolicyRevision != policyRevision)
            {
                job.PolicyRevision = policyRevision;
                job.State = StorageTransferJobState.Ready;
                job.AttemptCount = 0;
                job.DueAtUtc = now;
                job.LastErrorCategory = null;
                job.LastError = null;
                changed = true;
            }
        }
        var policy = configuration?.Policies.SingleOrDefault(item => item.Id == policyId);
        if (policy is not null)
        {
            RefreshProtection(storageObject, policy.RequiredIndependentCopies, policy.DesiredCopies, now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private bool AddMissingTargets(StorageObject storageObject, IReadOnlyList<StorageReplicationTarget> targets,
        int maximumAttempts, DateTimeOffset now)
    {
        var changed = false;
        foreach (var target in targets.DistinctBy(target => target.DestinationId))
        {
            if (configuration is not null && !configuration.Destinations.Any(destination => destination.Id == target.DestinationId &&
                    destination.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering &&
                    destination.Capabilities.HasFlag(StorageCapability.Write)))
            {
                continue;
            }
            var existing = storageObject.Replicas.SingleOrDefault(replica => replica.DestinationId == target.DestinationId &&
                replica.Generation == storageObject.CommittedGeneration);
            if (existing is not null)
            {
                if (existing.FailureDomain != target.FailureDomain)
                {
                    throw new InvalidOperationException("Existing replica failure domain cannot be reassigned without re-verification.");
                }
                continue;
            }
            var replica = new StorageObjectReplica
            {
                StorageObject = storageObject,
                DestinationId = target.DestinationId,
                FailureDomain = target.FailureDomain,
                NativeLocator = storageObject.LogicalKey,
                Generation = storageObject.CommittedGeneration,
                State = StorageReplicaState.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.StorageObjectReplicas.Add(replica);
            dbContext.StorageTransferJobs.Add(new StorageTransferJob
            {
                StorageObject = storageObject,
                StorageObjectReplica = replica,
                DestinationId = target.DestinationId,
                Generation = storageObject.CommittedGeneration,
                Kind = StorageTransferJobKind.Replicate,
                State = StorageTransferJobState.Ready,
                IdempotencyKey = $"{storageObject.OperationId:N}:{storageObject.CommittedGeneration}:{target.DestinationId}:replicate",
                PolicyRevision = storageObject.PolicyRevision,
                MaximumAttempts = maximumAttempts,
                DueAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            changed = true;
        }
        if (changed && storageObject.State == StorageObjectState.CreatedLocal)
        {
            storageObject.State = StorageObjectState.ProtectionPending;
        }
        return changed;
    }

    private static StorageManifestEntry ToManifestEntry(StorageObject item) => new(
            item.Id,
            item.OperationId,
            item.DataClass,
            item.LogicalKey,
            item.CommittedGeneration,
            item.SizeBytes,
            item.Sha256,
            item.State,
            item.CreatedAtUtc,
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
                .ToArray());

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

    private void RefreshProtection(
        StorageObject storageObject,
        int requiredCopies,
        int desiredCopies,
        DateTimeOffset now)
    {
        var policy = configuration?.Policies.SingleOrDefault(item => item.DataClass == storageObject.DataClass);
        var pool = configuration?.Pools.SingleOrDefault(item => item.Id == policy?.PoolId);
        var available = storageObject.Replicas
            .Where(replica => replica.State == StorageReplicaState.Available &&
                replica.Generation == storageObject.CommittedGeneration &&
                replica.SizeBytes == storageObject.SizeBytes &&
                string.Equals(replica.Sha256, storageObject.Sha256, StringComparison.OrdinalIgnoreCase))
            .Where(replica => configuration is null || configuration.Destinations.Any(destination =>
                destination.Id == replica.DestinationId && destination.State != StorageDestinationState.Disabled &&
                destination.TenantId == storageObject.TenantId && configuration.TenantId == storageObject.TenantId &&
                destination.FailureDomain == replica.FailureDomain &&
                destination.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Stat) &&
                pool?.DestinationIds.Contains(destination.Id, StringComparer.Ordinal) == true))
            .ToArray();
        var availableFailureDomains = available.Select(replica => replica.FailureDomain)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var offsiteCopies = available.Where(replica => configuration?.Destinations.Any(destination =>
                destination.Id == replica.DestinationId && destination.Type != StorageProviderType.LocalFileSystem) == true)
            .Select(replica => replica.FailureDomain).Distinct(StringComparer.Ordinal).Count();
        storageObject.SetProtection(availableFailureDomains, policy?.RequiredIndependentCopies ?? requiredCopies,
            policy?.DesiredCopies ?? desiredCopies, now,
            offsiteCopies, policy?.MinimumOffsiteCopies ?? 0);
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
