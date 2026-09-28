using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.StorageTool;

public sealed record BackupLayoutMove(Guid ObjectId, string DestinationId, long Generation, long SizeBytes,
    string Sha256, string OldLocator, string NewLocator, bool CatalogCommitted = false, bool OldDeleted = false);
public sealed record BackupLayoutCheckpoint(int SchemaVersion, string ConfigurationHash, IReadOnlyList<BackupLayoutMove> Moves);
public sealed record BackupLayoutReport(bool DryRun, int Planned, int CatalogCommitted, int OldDeleted);

/// <summary>Two-phase physical relocation. Logical backup identities and local archives never change.</summary>
public sealed class BackupLayoutMigration(IStorageCatalog catalog, IStorageProviderRegistry registry,
    EffectiveStorageConfiguration configuration, IStorageMaintenanceLock maintenanceLock)
{
    private const int MaximumObjects = 20000;

    public async Task<BackupLayoutReport> ExecuteAsync(MigrationCommandOptions options, CancellationToken cancellationToken)
    {
        await using var maintenance = await maintenanceLock.TryAcquireAsync("database-backups:" + configuration.TenantId, cancellationToken)
            ?? throw new MigrationToolException("Backup maintenance is busy; retry later.");
        await using var store = options.Checkpoint is null ? null : new MigrationCheckpointStore(options.Checkpoint);
        if (store is not null) await store.LockAsync(cancellationToken);
        var entries = await ReadCatalogAsync(cancellationToken);
        if (entries.Any(entry => entry.State is not (StorageObjectState.Protected or StorageObjectState.Deleted)))
            throw new MigrationToolException("All live backups must be protected before relocation.");
        var fingerprint = Hash(JsonSerializer.Serialize(configuration.Destinations.Select(destination => new
        { destination.Id, destination.Type, destination.Endpoint, destination.Bucket, destination.Prefix, destination.BackupPrefix, destination.BackupTimeZoneId }), MigrationCheckpointStore.JsonOptions));
        BackupLayoutCheckpoint checkpoint;
        if (options.Checkpoint is not null && File.Exists(options.Checkpoint))
        {
            checkpoint = await ReadCheckpointAsync(options.Checkpoint, cancellationToken);
            if (checkpoint.ConfigurationHash != fingerprint) throw new MigrationToolException("Layout configuration changed; use a reviewed new checkpoint.");
        }
        else
        {
            if (options.Command == "backup-layout-prune") throw new MigrationToolException("Pruning requires the existing completed layout checkpoint.");
            var moves = new List<BackupLayoutMove>();
            foreach (var entry in entries.Where(IsLive))
            {
                foreach (var destination in configuration.Destinations.Where(destination => destination.Type == StorageProviderType.S3Compatible && destination.BackupPrefix is not null))
                {
                    if (destination.State != StorageDestinationState.Enabled) throw new MigrationToolException("Layout destinations must be enabled.");
                    var replica = entry.Replicas.SingleOrDefault(replica => replica.DestinationId == destination.Id && replica.Generation == entry.Generation);
                    if (replica is null || replica.State != StorageReplicaState.Available) throw new MigrationToolException("Every source cloud replica must be available before relocation.");
                    var target = registry.GetRequired(destination.Id).GetWriteLocator(Request(entry));
                    if (target != replica.NativeLocator) moves.Add(new(entry.ObjectId, destination.Id, entry.Generation, entry.SizeBytes, entry.Sha256, replica.NativeLocator, target));
                }
            }
            if (moves.GroupBy(move => (move.DestinationId, move.NewLocator)).Any(group => group.Count() != 1))
                throw new MigrationToolException("Layout target collision; no copies were changed.");
            checkpoint = new(1, fingerprint, moves);
        }
        ValidatePlan(checkpoint, entries);
        if (!options.Execute) return Report(checkpoint, true);
        if (options.Checkpoint is null) throw new MigrationToolException("A private checkpoint is required.");
        await SaveAsync(options.Checkpoint, checkpoint, cancellationToken);
        if (options.Command == "backup-layout-prune")
        {
            var recovery = await catalog.ExportManifestPageAsync(configuration.TenantId, StorageDataClass.RecoverySecrets, 1, null, cancellationToken);
            if (recovery.Items.Count != 0) throw new MigrationToolException("Recovery manifests may reference old paths; retain sources until a recovery-aware migration is reviewed.");
            if (checkpoint.Moves.Any(move => !move.CatalogCommitted)) throw new MigrationToolException("Complete all catalog relocations before pruning.");
            // Verify every live replica, including both clouds and the local copy, before deleting anything.
            foreach (var entry in entries.Where(IsLive))
            {
                var policy = configuration.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
                var pool = configuration.Pools.Single(item => item.Id == policy.PoolId);
                if (pool.DestinationIds.Any(id => !entry.Replicas.Any(replica => replica.DestinationId == id && replica.State == StorageReplicaState.Available && replica.Generation == entry.Generation)))
                    throw new MigrationToolException("A required replica is not available; old objects retained.");
                foreach (var replica in entry.Replicas.Where(replica => replica.State == StorageReplicaState.Available && replica.Generation == entry.Generation))
                    await VerifyAsync(registry.GetRequired(replica.DestinationId), replica.NativeLocator, entry.SizeBytes, entry.Sha256, cancellationToken);
            }
        }
        var updated = checkpoint.Moves.ToArray();
        for (var index = 0; index < updated.Length; index++)
        {
            var move = updated[index];
            var entry = entries.Single(entry => entry.ObjectId == move.ObjectId);
            var provider = registry.GetRequired(move.DestinationId);
            if (options.Command == "backup-layout-prune")
            {
                if (move.OldDeleted) continue;
                // Recheck the catalog immediately before deletion; never follow a checkpoint's arbitrary path.
                var current = await catalog.FindObjectAsync(move.ObjectId, cancellationToken);
                if (current is null || current.TombstonedAtUtc is not null || current.CommittedGeneration != move.Generation ||
                    !current.Replicas.Any(replica => replica.DestinationId == move.DestinationId && replica.State == StorageReplicaState.Available && replica.NativeLocator == move.NewLocator))
                    throw new MigrationToolException("Catalog changed during pruning; old objects retained.");
                await VerifyAsync(provider, move.NewLocator, move.SizeBytes, move.Sha256, cancellationToken);
                if (await provider.StatAsync(move.OldLocator, cancellationToken) is not null)
                {
                    await VerifyAsync(provider, move.OldLocator, move.SizeBytes, move.Sha256, cancellationToken);
                    await provider.DeleteAsync(move.OldLocator, cancellationToken);
                    if (await provider.StatAsync(move.OldLocator, cancellationToken) is not null) throw new MigrationToolException("Old cloud path still exists; retry pruning.");
                }
                updated[index] = move with { OldDeleted = true };
            }
            else
            {
                if (move.CatalogCommitted)
                {
                    await VerifyAsync(provider, move.NewLocator, move.SizeBytes, move.Sha256, cancellationToken);
                    continue;
                }
                StorageWriteResult result;
                var existingTarget = await provider.StatAsync(move.NewLocator, cancellationToken);
                if (existingTarget is not null)
                {
                    // Also supports rebinding a catalog restored from a pre-move database snapshot.
                    await VerifyAsync(provider, move.NewLocator, move.SizeBytes, move.Sha256, cancellationToken);
                    result = new(move.NewLocator, existingTarget.ProviderVersionId, existingTarget.ProviderChecksum);
                }
                else
                {
                    await VerifyAsync(provider, move.OldLocator, move.SizeBytes, move.Sha256, cancellationToken);
                    await using var source = await provider.OpenReadAsync(move.OldLocator, cancellationToken);
                    result = await provider.WriteAsync(Request(entry), source, cancellationToken);
                }
                if (result.NativeLocator != move.NewLocator) throw new MigrationToolException("Unexpected destination path; source retained.");
                await VerifyAsync(provider, result.NativeLocator, move.SizeBytes, move.Sha256, cancellationToken);
                var alreadyCommitted = entry.Replicas.Any(replica => replica.DestinationId == move.DestinationId && replica.NativeLocator == move.NewLocator);
                if (!alreadyCommitted && !await catalog.RelocateReplicaAsync(move.ObjectId, move.DestinationId, move.Generation, move.Sha256,
                        move.SizeBytes, move.OldLocator, result, DateTimeOffset.UtcNow, cancellationToken))
                    throw new MigrationToolException("Concurrent catalog change; both cloud objects retained.");
                updated[index] = move with { CatalogCommitted = true };
            }
            checkpoint = checkpoint with { Moves = updated.ToArray() };
            await SaveAsync(options.Checkpoint, checkpoint, cancellationToken);
        }
        return Report(checkpoint, false);
    }

    private void ValidatePlan(BackupLayoutCheckpoint checkpoint, IReadOnlyList<StorageManifestEntry> entries)
    {
        if (checkpoint.SchemaVersion != 1 || checkpoint.Moves.Count > MaximumObjects * configuration.Destinations.Count ||
            checkpoint.Moves.GroupBy(move => (move.ObjectId, move.DestinationId)).Any(group => group.Count() != 1))
            throw new MigrationToolException("Invalid layout checkpoint.");
        foreach (var move in checkpoint.Moves)
        {
            var entry = entries.SingleOrDefault(entry => entry.ObjectId == move.ObjectId);
            var destination = configuration.Destinations.SingleOrDefault(destination => destination.Id == move.DestinationId);
            var replica = entry?.Replicas.SingleOrDefault(replica => replica.DestinationId == move.DestinationId);
            if (entry is null || !IsLive(entry) || entry.Generation != move.Generation || entry.SizeBytes != move.SizeBytes || entry.Sha256 != move.Sha256 ||
                destination is null || destination.Type != StorageProviderType.S3Compatible || destination.BackupPrefix is null ||
                replica is null || replica.State != StorageReplicaState.Available || replica.Generation != move.Generation ||
                (replica.NativeLocator != move.OldLocator && replica.NativeLocator != move.NewLocator) ||
                (move.CatalogCommitted && replica.NativeLocator != move.NewLocator) ||
                move.NewLocator != registry.GetRequired(move.DestinationId).GetWriteLocator(Request(entry)) ||
                !move.OldLocator.StartsWith(destination.Prefix.TrimEnd('/') + "/", StringComparison.Ordinal) || move.OldLocator == move.NewLocator)
                throw new MigrationToolException("Layout checkpoint no longer matches the live catalog; sources retained.");
            _ = StorageObjectKey.Normalize(move.OldLocator);
        }
    }

    private async Task<IReadOnlyList<StorageManifestEntry>> ReadCatalogAsync(CancellationToken token)
    {
        var entries = new List<StorageManifestEntry>();
        string? cursor = null;
        do
        {
            var page = await catalog.ExportManifestPageAsync(configuration.TenantId, StorageDataClass.DatabaseBackup, 200, cursor, token);
            entries.AddRange(page.Items);
            if (entries.Count > MaximumObjects) throw new MigrationToolException("Layout catalog exceeds its operator safety limit.");
            if (page.NextCursor is not null && (page.Items.Count == 0 || page.NextCursor == cursor)) throw new MigrationToolException("Catalog pagination did not advance.");
            cursor = page.NextCursor;
        } while (cursor is not null);
        return entries;
    }

    public static async Task VerifyAsync(IStorageProvider provider, string locator, long size, string sha256, CancellationToken token)
    {
        await using var stream = await provider.OpenReadAsync(locator, token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0)
        {
            readTotal += read;
            if (readTotal > size) throw new MigrationToolException("Cloud archive size mismatch; source retained.");
            hash.AppendData(buffer, 0, read);
        }
        if (readTotal != size || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase))
            throw new MigrationToolException("Cloud archive checksum mismatch; source retained.");
    }

    private static bool IsLive(StorageManifestEntry entry) => entry.State == StorageObjectState.Protected;
    private static StorageWriteRequest Request(StorageManifestEntry entry) => new(entry.OperationId, entry.LogicalKey, entry.Generation,
        entry.SizeBytes, entry.Sha256, new Dictionary<string, string>
        {
            ["garagebalance-data-class"] = "DatabaseBackup",
            [ReadableBackupLayout.CreatedAtMetadataKey] = entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["garagebalance-object-id"] = entry.ObjectId.ToString("N")
        });
    private static BackupLayoutReport Report(BackupLayoutCheckpoint checkpoint, bool dryRun) =>
        new(dryRun, checkpoint.Moves.Count, checkpoint.Moves.Count(move => move.CatalogCommitted), checkpoint.Moves.Count(move => move.OldDeleted));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static Task SaveAsync(string path, BackupLayoutCheckpoint checkpoint, CancellationToken token) =>
        MigrationCheckpointStore.WriteReportAsync(path, JsonSerializer.Serialize(new Envelope(Hash(JsonSerializer.Serialize(checkpoint, MigrationCheckpointStore.JsonOptions)), checkpoint), MigrationCheckpointStore.JsonOptions), token);
    private static async Task<BackupLayoutCheckpoint> ReadCheckpointAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new MigrationToolException("Layout checkpoint exceeds its safe size.");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, token), MigrationCheckpointStore.JsonOptions);
            if (envelope is null || envelope.Payload is null || envelope.Sha256 != Hash(JsonSerializer.Serialize(envelope.Payload, MigrationCheckpointStore.JsonOptions)))
                throw new MigrationToolException("Layout checkpoint integrity check failed.");
            return envelope.Payload;
        }
        catch (JsonException) { throw new MigrationToolException("Layout checkpoint is damaged."); }
    }
    private sealed record Envelope(string Sha256, BackupLayoutCheckpoint Payload);
}
