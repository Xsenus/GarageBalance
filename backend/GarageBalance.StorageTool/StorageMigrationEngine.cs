using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.StorageTool;

public sealed record MigrationPlanItem(string FileName, string Action, string? Reason);
public sealed record ReplicaVerification(string DestinationId, string FailureDomain, bool Offsite, bool Verified, string? Error);
public sealed record ObjectVerification(string LogicalKey, long Generation, IReadOnlyList<ReplicaVerification> Replicas,
    bool RequiredProtectionMet, bool LocalRollbackReady);
public sealed record MigrationReport(int SchemaVersion, string Command, bool DryRun, int ExitCode, DateTimeOffset GeneratedAtUtc,
    string Mode, int RequiredIndependentCopies, int MinimumOffsiteCopies, int DesiredCopies,
    int CatalogObjects, int LocalFiles, IReadOnlyList<string> Blockers, object Details);

public delegate Task<bool> ProcessMigrationJob(string owner, IReadOnlyCollection<StorageTransferJobKind> kinds,
    IReadOnlyCollection<Guid> objectIds, CancellationToken cancellationToken);

public sealed class StorageMigrationEngine(IStorageCatalog catalog, IStorageProviderRegistry providers,
    EffectiveStorageConfiguration configuration, LocalBackupInspector inspector, ProcessMigrationJob processJob,
    IStorageMaintenanceLock? maintenanceLock = null, IStorageReconciliationGuard? reconciliationGuard = null)
{
    private StoragePolicyOptions Policy => configuration.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
    private EffectiveStorageDestination Local => configuration.Destinations.Single(item => item.Type == StorageProviderType.LocalFileSystem &&
        configuration.Pools.Single(pool => pool.Id == Policy.PoolId).DestinationIds.Contains(item.Id));
    private string Root => Local.RootPath ?? throw new MigrationToolException("Local backup root is not configured.");

    public async Task<MigrationReport> ExecuteAsync(MigrationCommandOptions options, CancellationToken cancellationToken)
    {
        // The same scope protects API create/retention and CLI backfill across processes.
        var requiresLease = options.Execute && options.Command is "copy" or "resume" or "delta-sync" or "repair";
        await using var lease = requiresLease && maintenanceLock is not null
            ? await maintenanceLock.TryAcquireAsync($"database-backups:{configuration.TenantId}", cancellationToken) : null;
        if (requiresLease && maintenanceLock is not null && lease is null)
            throw new MigrationToolException("Backup maintenance is busy; retry without changing the checkpoint.");

        var manifest = await ReadCatalogAsync(options.PageSize, cancellationToken);
        var local = await inspector.InspectAsync(Root, configuration.TenantId, Policy, cancellationToken);
        var localNames = local.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal);
        var plan = BuildPlan(local, manifest);
        var blockers = new List<string>();
        MigrationCheckpoint? checkpoint = null;
        IReadOnlyList<ObjectVerification> verification = [];
        var mutating = options.Command is "copy" or "resume" or "delta-sync" or "verify" or "repair";
        object details;

        switch (options.Command)
        {
            case "inventory":
                details = local.Select(file => new { file.FileName, file.SizeBytes, file.Sha256, file.Verified, file.ManifestMissing, file.Error }).ToArray();
                break;
            case "plan":
                details = plan;
                break;
            case "diff":
                details = new
                {
                    additions = plan.Where(item => item.Action == "register").Select(item => item.FileName).ToArray(),
                    conflicts = plan.Where(item => item.Action == "blocked").ToArray(),
                    missingLocal = manifest.Where(item => !localNames.Contains(item.LogicalKey) && !IsTombstone(item)).Select(item => item.LogicalKey).ToArray(),
                    tombstones = manifest.Where(IsTombstone).Select(item => item.LogicalKey).ToArray(),
                    pending = manifest.Where(item => !IsTombstone(item) && item.Replicas.Any(replica => replica.State != StorageReplicaState.Available)).Select(item => item.LogicalKey).ToArray()
                };
                break;
            case "status":
                if (options.Checkpoint is not null)
                {
                    await using var store = new MigrationCheckpointStore(options.Checkpoint);
                    checkpoint = await store.ReadAsync(cancellationToken);
                }
                details = new
                {
                    checkpoint,
                    states = manifest.GroupBy(item => item.State.ToString()).ToDictionary(group => group.Key, group => group.Count()),
                    reconciliation = reconciliationGuard is null ? null : await reconciliationGuard.PeekStateAsync(cancellationToken)
                };
                break;
            case "copy":
            case "resume":
            case "delta-sync":
                if (!options.Execute) { details = new { proposed = plan, command = options.Command }; break; }
                checkpoint = await CopyAsync(options, local, manifest, cancellationToken);
                manifest = await ReadCatalogAsync(options.PageSize, cancellationToken);
                if (plan.Any(item => item.Action == "blocked")) blockers.Add("invalid_or_conflicting_local_files");
                details = checkpoint;
                break;
            case "verify":
            case "repair":
            case "cutover-check":
            case "rollback-check":
                if (options.Command == "repair" && options.Execute && reconciliationGuard is not null)
                {
                    var safety = await reconciliationGuard.GetStateAsync(cancellationToken);
                    if (safety.Paused || !safety.PersistenceAvailable)
                        throw new MigrationToolException("Reconciliation is paused or its safety record is unavailable. Investigate and explicitly resume before repair.");
                }
                var before = CatalogFingerprint(manifest);
                verification = await VerifyAsync(manifest, options.Execute, options.Command == "repair", cancellationToken);
                if (options.Command == "repair" && options.Execute)
                {
                    var objectIds = manifest.Where(item => !IsTombstone(item)).Select(item => item.ObjectId).ToArray();
                    for (var count = 0; count < options.MaxJobs && objectIds.Length > 0 &&
                        await processJob("storage-migration-repair", [StorageTransferJobKind.Replicate, StorageTransferJobKind.Repair], objectIds, cancellationToken); count++) { }
                    manifest = await ReadCatalogAsync(options.PageSize, cancellationToken);
                    verification = await VerifyAsync(manifest, false, false, cancellationToken);
                }
                if (options.Command is "cutover-check" or "rollback-check")
                {
                    if (manifest.All(IsTombstone)) blockers.Add("no_retained_catalog_backups");
                    if (plan.Any(item => item.Action is "register" or "blocked")) blockers.Add("unregistered_or_invalid_local_archives");
                    if (options.Command == "cutover-check" && verification.Any(item => !item.RequiredProtectionMet)) blockers.Add("required_physical_coverage_not_met");
                    if (options.Command == "rollback-check" && verification.Any(item => !item.LocalRollbackReady)) blockers.Add("local_rollback_copy_missing_or_invalid");
                    if (options.Command == "rollback-check" && manifest.Any(item => IsTombstone(item) && localNames.Contains(item.LogicalKey)))
                        blockers.Add("tombstoned_local_files_would_reappear_in_old_binary");
                    var after = await ReadCatalogAsync(options.PageSize, cancellationToken);
                    var localAfter = await inspector.InspectAsync(Root, configuration.TenantId, Policy, cancellationToken);
                    if (before != CatalogFingerprint(after) || InventoryFingerprint(local) != InventoryFingerprint(localAfter)) blockers.Add("inventory_changed_during_gate_retry_required");
                }
                else if (verification.Any(item => item.Replicas.Any(replica => !replica.Verified))) blockers.Add("replica_verification_failed");
                details = verification;
                break;
            default:
                details = new
                {
                    inventory = local,
                    plan,
                    catalog = manifest,
                    reconciliation = reconciliationGuard is null ? null : await reconciliationGuard.PeekStateAsync(cancellationToken)
                };
                break;
        }
        return new(2, options.Command, mutating && !options.Execute, blockers.Count > 0 ? 10 : 0, DateTimeOffset.UtcNow,
            configuration.Mode.ToString(), Policy.RequiredIndependentCopies, Policy.MinimumOffsiteCopies, Policy.DesiredCopies,
            manifest.Count, local.Count, blockers, details);
    }

    public async Task<IReadOnlyList<StorageManifestEntry>> ReadCatalogAsync(int pageSize, CancellationToken cancellationToken)
    {
        var result = new List<StorageManifestEntry>();
        string? cursor = null;
        do
        {
            var page = await catalog.ExportManifestPageAsync(configuration.TenantId, StorageDataClass.DatabaseBackup, pageSize, cursor, cancellationToken);
            result.AddRange(page.Items);
            if (result.Count > LocalBackupInspector.MaximumFiles) throw new MigrationToolException("Catalog exceeds the bounded metadata limit; incomplete coverage cannot pass.");
            if (page.NextCursor is not null && string.CompareOrdinal(page.NextCursor, cursor) <= 0)
                throw new MigrationToolException("Catalog cursor did not progress; coverage is incomplete.");
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    private static IReadOnlyList<MigrationPlanItem> BuildPlan(IReadOnlyList<LocalBackupInventory> local, IReadOnlyList<StorageManifestEntry> manifest)
    {
        var byKey = manifest.ToDictionary(item => item.LogicalKey, StringComparer.Ordinal);
        var localPlan = local.Select(file =>
        {
            if (!file.Verified) return new MigrationPlanItem(file.FileName, "blocked", file.Error);
            if (!byKey.TryGetValue(file.FileName, out var entry)) return new MigrationPlanItem(file.FileName, "register", file.ManifestMissing ? "legacy_manifest_backfill" : "catalog_orphan");
            if (IsTombstone(entry)) return new MigrationPlanItem(file.FileName, "preserve_tombstone", "never_resurrect");
            return entry.SizeBytes != file.SizeBytes || !string.Equals(entry.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !file.ManifestMissing && (file.Manifest?.BackupId != entry.OperationId || file.Manifest?.Generation != entry.Generation)
                ? new MigrationPlanItem(file.FileName, "blocked", "catalog_source_conflict")
                : new MigrationPlanItem(file.FileName, "resume_replication", null);
        });
        var names = local.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal);
        return localPlan.Concat(manifest.Where(item => !names.Contains(item.LogicalKey)).Select(item =>
            IsTombstone(item) ? new MigrationPlanItem(item.LogicalKey, "preserve_tombstone", "never_resurrect") :
            item.Replicas.Any(replica => replica.State == StorageReplicaState.Available && replica.Generation == item.Generation)
                ? new MigrationPlanItem(item.LogicalKey, "resume_remote_source", "local_copy_absent")
                : new MigrationPlanItem(item.LogicalKey, "blocked", "no_verified_source"))).ToArray();
    }

    private async Task<MigrationCheckpoint> CopyAsync(MigrationCommandOptions options, IReadOnlyList<LocalBackupInventory> local,
        IReadOnlyList<StorageManifestEntry> manifest, CancellationToken cancellationToken)
    {
        await using var store = new MigrationCheckpointStore(options.Checkpoint!);
        await store.LockAsync(cancellationToken);
        var fingerprint = ConfigurationFingerprint();
        var blockedNames = BuildPlan(local, manifest).Where(item => item.Action == "blocked").Select(item => item.FileName).ToHashSet(StringComparer.Ordinal);
        var manifestByKey = manifest.ToDictionary(item => item.LogicalKey, StringComparer.Ordinal);
        MigrationSnapshotFile Snapshot(LocalBackupInventory file)
        {
            manifestByKey.TryGetValue(file.FileName, out var known);
            var backup = file.Manifest!;
            if (known is not null && !IsTombstone(known) && file.ManifestMissing && known.SizeBytes == file.SizeBytes && known.Sha256 == file.Sha256)
                backup = backup with { BackupId = known.OperationId, Generation = known.Generation, CreatedAtUtc = known.CreatedAtUtc };
            return new(file.FileName, file.SizeBytes, file.Sha256!, backup);
        }
        MigrationCheckpoint checkpoint;
        var now = DateTimeOffset.UtcNow;
        if (options.Command == "copy")
        {
            if (File.Exists(options.Checkpoint)) throw new MigrationToolException("Checkpoint already exists; use resume or delta-sync.");
            checkpoint = new(1, Guid.NewGuid(), fingerprint, now, now, "registering", 0, 0,
                local.Where(file => file.Verified && !blockedNames.Contains(file.FileName)).Select(Snapshot).ToArray(),
                manifest.Where(item => !IsTombstone(item) && !blockedNames.Contains(item.LogicalKey)).Select(item => item.ObjectId).ToArray());
        }
        else
        {
            checkpoint = await store.ReadAsync(cancellationToken);
            if (checkpoint.ConfigurationFingerprint != fingerprint)
                throw new MigrationToolException("Configuration, destination or policy changed since checkpoint; create a reviewed new migration plan.");
            if (options.Command == "delta-sync")
            {
                var known = checkpoint.Files.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal);
                checkpoint = checkpoint with
                {
                    State = "registering",
                    Files = checkpoint.Files.Concat(local.Where(file => file.Verified && !known.Contains(file.FileName) && !blockedNames.Contains(file.FileName))
                    .Select(Snapshot)).ToArray(),
                    ObjectIds = checkpoint.ObjectIds.Concat(manifest.Where(item => !IsTombstone(item) && !blockedNames.Contains(item.LogicalKey)).Select(item => item.ObjectId)).Distinct().ToArray()
                };
            }
        }
        var inventory = local.ToDictionary(file => file.FileName, StringComparer.Ordinal);
        var entries = manifest.ToDictionary(item => item.LogicalKey, StringComparer.Ordinal);
        foreach (var file in checkpoint.Files)
        {
            if (entries.TryGetValue(file.FileName, out var entry) && IsTombstone(entry)) continue;
            if (!inventory.TryGetValue(file.FileName, out var actual) || !actual.Verified || actual.SizeBytes != file.SizeBytes || actual.Sha256 != file.Sha256 ||
                !actual.ManifestMissing && actual.Manifest?.Generation != file.Manifest.Generation)
                throw new MigrationToolException("A checkpoint source is missing, changed or invalid; no unsafe resume was attempted.");
        }
        await store.SaveAsync(checkpoint, cancellationToken);
        var targets = configuration.Pools.Single(pool => pool.Id == Policy.PoolId).DestinationIds
            .Select(id => configuration.Destinations.Single(destination => destination.Id == id))
            .Where(destination => destination.Id != Local.Id && destination.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering && destination.Capabilities.HasFlag(StorageCapability.Write))
            .Select(destination => new StorageReplicationTarget(destination.Id, destination.FailureDomain)).ToArray();
        for (var index = checkpoint.RegisteredCount; index < checkpoint.Files.Count; index++)
        {
            var file = checkpoint.Files[index];
            var current = await catalog.FindByLogicalKeyAsync(configuration.TenantId, StorageDataClass.DatabaseBackup, file.FileName, cancellationToken);
            if (current?.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted))
            {
                if (current is not null && (current.SizeBytes != file.SizeBytes || current.Sha256 != file.Sha256 || current.CommittedGeneration != file.Manifest.Generation))
                    throw new MigrationToolException("Catalog generation conflicts with the migration snapshot; source was preserved.");
                var backup = file.Manifest;
                await catalog.RegisterCommittedObjectAsync(new(current?.OperationId ?? backup.BackupId, configuration.TenantId, StorageDataClass.DatabaseBackup,
                    file.FileName, Policy.Id, Policy.Revision, backup.Generation, file.SizeBytes, file.Sha256, file.FileName,
                    "application/octet-stream", Local.Id, Local.FailureDomain, file.FileName, targets,
                    configuration.Replication.MaximumAttempts, backup.CreatedAtUtc), cancellationToken);
            }
            if (current?.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted) && !File.Exists(Path.Combine(Root, file.FileName + ".manifest.json")))
                await MigrationCheckpointStore.WriteReportAsync(Path.Combine(Root, file.FileName + ".manifest.json"), JsonSerializer.Serialize(file.Manifest, MigrationCheckpointStore.JsonOptions), cancellationToken);
            checkpoint = checkpoint with { RegisteredCount = index + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await store.SaveAsync(checkpoint, cancellationToken);
        }
        var currentManifest = await ReadCatalogAsync(options.PageSize, cancellationToken);
        var names = checkpoint.Files.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal);
        var knownObjectIds = checkpoint.ObjectIds.ToHashSet();
        var objectIds = currentManifest.Where(item => (names.Contains(item.LogicalKey) || knownObjectIds.Contains(item.ObjectId)) && !IsTombstone(item)).Select(item => item.ObjectId).ToArray();
        foreach (var objectId in objectIds)
            await catalog.EnsureTargetsAsync(objectId, Policy.Id, Policy.Revision, targets, configuration.Replication.MaximumAttempts, DateTimeOffset.UtcNow, cancellationToken);
        checkpoint = checkpoint with { State = "replicating", ObjectIds = objectIds };
        await store.SaveAsync(checkpoint, cancellationToken);
        for (var processed = 0; processed < options.MaxJobs && objectIds.Length > 0 &&
            await processJob($"migration-{checkpoint.RunId:N}", [StorageTransferJobKind.Replicate, StorageTransferJobKind.Repair], objectIds, cancellationToken); processed++)
        {
            checkpoint = checkpoint with { ProcessedJobs = checkpoint.ProcessedJobs + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await store.SaveAsync(checkpoint, cancellationToken);
        }
        currentManifest = await ReadCatalogAsync(options.PageSize, cancellationToken);
        var currentObjectIds = objectIds.ToHashSet();
        var pending = currentManifest.Any(item => currentObjectIds.Contains(item.ObjectId) && !IsTombstone(item) &&
            item.Replicas.Any(replica => replica.State != StorageReplicaState.Available));
        checkpoint = checkpoint with { State = pending ? "pending" : "copied_requires_verification", UpdatedAtUtc = DateTimeOffset.UtcNow };
        await store.SaveAsync(checkpoint, cancellationToken);
        return checkpoint;
    }

    private async Task<IReadOnlyList<ObjectVerification>> VerifyAsync(IReadOnlyList<StorageManifestEntry> manifest,
        bool execute, bool repair, CancellationToken cancellationToken)
    {
        var result = new List<ObjectVerification>();
        var pool = configuration.Pools.Single(item => item.Id == Policy.PoolId).DestinationIds;
        foreach (var entry in manifest.Where(item => !IsTombstone(item)))
        {
            var replicas = new List<ReplicaVerification>();
            foreach (var replica in entry.Replicas)
            {
                var destination = configuration.Destinations.SingleOrDefault(item => item.Id == replica.DestinationId);
                string? error = null;
                if (destination is null || destination.State == StorageDestinationState.Disabled || !pool.Contains(destination.Id) ||
                    !destination.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Stat)) error = "destination_not_eligible";
                else if (replica.Generation != entry.Generation || replica.SizeBytes != entry.SizeBytes || replica.Sha256 != entry.Sha256 || replica.State != StorageReplicaState.Available)
                    error = "replica_not_current_available";
                else
                {
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.Replication.OperationDeadlineSeconds));
                        var provider = providers.GetRequired(replica.DestinationId);
                        var stat = await provider.StatAsync(replica.NativeLocator, deadline.Token);
                        if (stat is null || stat.SizeBytes != entry.SizeBytes) error = "missing_or_wrong_size";
                        else
                        {
                            await using var stream = await provider.OpenReadAsync(replica.NativeLocator, deadline.Token);
                            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, deadline.Token));
                            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase)) error = "checksum_mismatch";
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "verification_timeout"; }
                    catch (StorageProviderException exception) { error = exception.Category.ToString(); }
                    catch (IOException) { error = "read_failed"; }
                }
                var verified = error is null;
                replicas.Add(new(replica.DestinationId, destination?.FailureDomain ?? replica.FailureDomain,
                    destination?.Type == StorageProviderType.S3Compatible, verified, error));
            }
            var verifiedReplicas = replicas.Where(item => item.Verified).ToArray();
            var localReplica = entry.Replicas.SingleOrDefault(item => item.DestinationId == Local.Id);
            result.Add(new(entry.LogicalKey, entry.Generation, replicas,
                verifiedReplicas.Select(item => item.FailureDomain).Distinct(StringComparer.Ordinal).Count() >= Policy.RequiredIndependentCopies &&
                verifiedReplicas.Where(item => item.Offsite).Select(item => item.FailureDomain).Distinct(StringComparer.Ordinal).Count() >= Policy.MinimumOffsiteCopies,
                verifiedReplicas.Any(item => item.DestinationId == Local.Id) && localReplica?.NativeLocator == entry.LogicalKey && LocalBackupInspector.IsManagedName(entry.LogicalKey)));
        }
        var anomalies = result.Sum(item => item.Replicas.Count(replica => !replica.Verified &&
            replica.Error is not ("replica_not_current_available" or "destination_not_eligible")));
        if (execute && repair && anomalies > 10)
        {
            if (reconciliationGuard is not null) await reconciliationGuard.PauseAsync(anomalies, cancellationToken);
            throw new MigrationToolException("Mass replica verification failure: automatic repair paused. Inspect the incident and explicitly resume reconciliation before retrying.");
        }
        if (execute)
        {
            var entries = manifest.ToDictionary(item => item.LogicalKey, StringComparer.Ordinal);
            foreach (var item in result)
                foreach (var replica in item.Replicas)
                {
                    var entry = entries[item.LogicalKey];
                    if (replica.Verified)
                        await catalog.RecordReplicaVerifiedAsync(entry.ObjectId, replica.DestinationId, entry.Generation, entry.Sha256, entry.SizeBytes, DateTimeOffset.UtcNow, cancellationToken);
                    else if (repair && configuration.Destinations.Any(destination => destination.Id == replica.DestinationId && destination.State is StorageDestinationState.Enabled or StorageDestinationState.Recovering))
                        await catalog.ScheduleRepairAsync(entry.ObjectId, replica.DestinationId, StorageReplicaState.Corrupted,
                            "MigrationVerification", "Replica requires verification or repair.", Policy.RequiredIndependentCopies, Policy.DesiredCopies,
                            configuration.Replication.MaximumAttempts, DateTimeOffset.UtcNow, cancellationToken);
                }
        }
        return result;
    }

    private string ConfigurationFingerprint() => Hash(JsonSerializer.Serialize(new
    {
        configuration.TenantId,
        configuration.Mode,
        Policy,
        pool = configuration.Pools.Single(item => item.Id == Policy.PoolId),
        destinations = configuration.Destinations.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new
        { item.Id, item.Type, item.State, item.FailureDomain, item.TenantId, item.RootPath, item.Endpoint, item.Bucket, item.Prefix, item.Capabilities })
    }));
    private static bool IsTombstone(StorageManifestEntry item) => item.State is StorageObjectState.Deleting or StorageObjectState.Deleted;
    private static string CatalogFingerprint(IReadOnlyList<StorageManifestEntry> entries) => Hash(JsonSerializer.Serialize(entries.Select(item => new
    { item.ObjectId, item.LogicalKey, item.Generation, item.SizeBytes, item.Sha256, item.State, replicas = item.Replicas.Select(replica => new { replica.DestinationId, replica.NativeLocator, replica.Generation, replica.State, replica.Sha256, replica.SizeBytes }) })));
    private static string InventoryFingerprint(IReadOnlyList<LocalBackupInventory> entries) => Hash(JsonSerializer.Serialize(entries.Select(item => new { item.FileName, item.SizeBytes, item.Sha256, item.Verified })));
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
