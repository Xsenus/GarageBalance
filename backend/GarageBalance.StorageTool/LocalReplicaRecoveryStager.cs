using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.StorageTool;

public sealed record LocalRecoverySource(string DestinationId, string FailureDomain, string NativeLocator);
public sealed record LocalRecoveryItem(Guid ObjectId, Guid OperationId, string FileName, long Generation,
    long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc, IReadOnlyList<LocalRecoverySource> VerifiedSources);
public sealed record LocalReplicaRecoveryPlan(int SchemaVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    string ConfigurationHash, string CatalogHash, string InventoryHash, string LocalDestinationId, string LocalRoot,
    string StagingDirectory, IReadOnlyList<LocalRecoveryItem> Items, string Fingerprint);
public sealed record LocalRecoveryStagingReport(bool DryRun, bool StagingOnly, int Files, long RequiredBytes, string? Fingerprint);

// This type has no catalog write interface and never writes to the working root.
// A staged plan is not permission to promote: promotion must recheck its exact
// catalog/configuration under the qualified maintenance barrier.
public sealed class LocalReplicaRecoveryStager(IStorageProviderRegistry registry, EffectiveStorageConfiguration configuration,
    LocalBackupInspector inspector, TimeProvider clock, Func<string, long>? freeBytes = null)
{
    private StoragePolicyOptions Policy => configuration.Policies.Single(p => p.DataClass == StorageDataClass.DatabaseBackup);
    private EffectiveStorageDestination Local => configuration.Destinations.Single(d => d.Type == StorageProviderType.LocalFileSystem &&
        configuration.Pools.Single(p => p.Id == Policy.PoolId).DestinationIds.Contains(d.Id));

    public async Task<LocalRecoveryStagingReport> ExecuteAsync(MigrationCommandOptions options,
        IReadOnlyList<StorageManifestEntry> manifest, IReadOnlyList<LocalBackupInventory> inventory,
        Func<CancellationToken, Task<IReadOnlyList<StorageManifestEntry>>> rereadCatalog,
        Func<CancellationToken, Task<IReadOnlyList<LocalBackupInventory>>> rereadInventory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (options.Command != "local-recovery-stage") throw Error("Invalid local recovery staging command.");
        var root = Path.GetFullPath(Local.RootPath ?? throw Error("Local recovery root is unavailable."));
        var catalogHash = CatalogHash(manifest);
        var inventoryHash = InventoryHash(inventory);
        var items = await CandidatesAsync(manifest, inventory, token);
        var totalBytes = items.Aggregate(0L, (total, item) => checked(total + item.SizeBytes));
        if (!options.Execute) return new(true, true, items.Count, totalBytes, null);
        if (string.IsNullOrWhiteSpace(options.Checkpoint)) throw Error("Local staging requires a new private plan path.");
        if (!Path.IsPathFullyQualified(options.Checkpoint)) throw Error("Local staging requires an absolute private plan path.");
        var planPath = Path.GetFullPath(options.Checkpoint);
        ValidatePrivateDestination(planPath, root);
        var parent = Path.GetDirectoryName(planPath)!;
        var available = freeBytes?.Invoke(parent) ?? new DriveInfo(Path.GetPathRoot(parent)!).AvailableFreeSpace;
        if (available < checked(totalBytes + 64L * 1024 * 1024)) throw Error("Insufficient free space for verified staging and safety reserve.");
        var id = Guid.NewGuid();
        var staging = Path.Combine(parent, "local-recovery-" + id.ToString("N"));
        if (Directory.Exists(staging) || File.Exists(staging)) throw Error("Staging directory already exists.");
        Directory.CreateDirectory(staging);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var owned = new List<string>();
        var planCreated = false;
        var complete = false;
        try
        {
            foreach (var item in items)
            {
                var path = Path.Combine(staging, item.FileName);
                var fileOptions = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                };
                if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var output = new FileStream(path, fileOptions))
                {
                    owned.Add(path);
                    for (var index = 0; index < item.VerifiedSources.Count; index++)
                        await ReadVerifiedAsync(item, item.VerifiedSources[index], index == 0 ? output : null, token);
                    await output.FlushAsync(token);
                    output.Flush(flushToDisk: true);
                }
                File.SetLastWriteTimeUtc(path, item.CreatedAtUtc.UtcDateTime);
            }
            var staged = await inspector.InspectAsync(staging, configuration.TenantId, Policy, token);
            if (staged.Count != items.Count || staged.Any(file => !file.Verified ||
                !items.Any(item => item.FileName == file.FileName && item.Sha256 == file.Sha256 && item.SizeBytes == file.SizeBytes)))
                throw Error("Staged archive failed native validation; working sources were not changed.");
            if (CatalogHash(await rereadCatalog(token)) != catalogHash || InventoryHash(await rereadInventory(token)) != inventoryHash)
                throw Error("Catalog or local inventory changed during staging; promotion is not authorized.");
            SyncDirectory(staging);
            var plan = new LocalReplicaRecoveryPlan(1, id, clock.GetUtcNow(), ConfigurationHash(configuration), catalogHash,
                inventoryHash, Local.Id, root, staging, items, "");
            plan = plan with { Fingerprint = Fingerprint(plan) };
            var outputOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows()) outputOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(planPath, outputOptions))
            {
                planCreated = true;
                await JsonSerializer.SerializeAsync(output, plan, MigrationCheckpointStore.JsonOptions, token);
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }
            SyncDirectory(parent);
            complete = true;
            return new(false, true, items.Count, totalBytes, plan.Fingerprint);
        }
        finally
        {
            if (!complete)
            {
                if (planCreated) File.Delete(planPath);
                if ((File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0)
                    throw Error("Staging cleanup cannot confirm directory identity; retained for inspection.");
                foreach (var path in owned) File.Delete(path);
                Directory.Delete(staging, recursive: false);
            }
        }
    }

    private async Task<IReadOnlyList<LocalRecoveryItem>> CandidatesAsync(IReadOnlyList<StorageManifestEntry> manifest,
        IReadOnlyList<LocalBackupInventory> inventory, CancellationToken token)
    {
        if (manifest.Count > LocalBackupInspector.MaximumFiles || manifest.Select(x => x.ObjectId).Distinct().Count() != manifest.Count ||
            manifest.Select(x => x.LogicalKey).Distinct(StringComparer.Ordinal).Count() != manifest.Count || inventory.Any(x => !x.Verified))
            throw Error("Local recovery inventory is incomplete, ambiguous or unverified.");
        var local = registry.GetRequired(Local.Id);
        var files = inventory.ToDictionary(x => x.FileName, StringComparer.Ordinal);
        var pool = configuration.Pools.Single(p => p.Id == Policy.PoolId).DestinationIds;
        var requiredRemotes = Math.Max(1, Math.Max(Policy.MinimumOffsiteCopies, Policy.RequiredIndependentCopies - 1));
        var result = new List<LocalRecoveryItem>();
        foreach (var entry in manifest.OrderBy(x => x.LogicalKey, StringComparer.Ordinal))
        {
            if (entry.DataClass != StorageDataClass.DatabaseBackup) throw Error("Unexpected storage data class.");
            if (entry.State is StorageObjectState.Deleting or StorageObjectState.Deleted) continue;
            if (entry.State is not (StorageObjectState.Protected or StorageObjectState.ProtectionDegraded) || entry.ObjectId == Guid.Empty ||
                entry.OperationId == Guid.Empty || entry.Generation < 1 || entry.SizeBytes <= 0 || entry.Sha256.Length != 64 ||
                !entry.Sha256.All(Uri.IsHexDigit) || !LocalBackupInspector.IsManagedName(entry.LogicalKey) || Path.GetFileName(entry.LogicalKey) != entry.LogicalKey)
                throw Error("Unreviewed or incomplete backup identity.");
            var replica = entry.Replicas.SingleOrDefault(r => r.DestinationId == Local.Id);
            if (replica is null || replica.NativeLocator != entry.LogicalKey || replica.Generation != entry.Generation ||
                replica.SizeBytes != entry.SizeBytes || replica.Sha256 != entry.Sha256 || replica.State != StorageReplicaState.Available)
                throw Error("Local replica metadata requires separate review.");
            var stat = await local.StatAsync(replica.NativeLocator, token);
            if (stat is not null)
            {
                if (!files.TryGetValue(entry.LogicalKey, out var file) || file.SizeBytes != entry.SizeBytes || file.Sha256 != entry.Sha256)
                    throw Error("Existing local archive conflicts with the catalog; it will not be overwritten.");
                continue;
            }
            if (files.ContainsKey(entry.LogicalKey)) throw Error("Local provider and inventory disagree.");
            var sources = entry.Replicas.Where(r => r.State == StorageReplicaState.Available && r.Generation == entry.Generation &&
                r.SizeBytes == entry.SizeBytes && r.Sha256 == entry.Sha256)
                .Select(r => (Replica: r, Destination: configuration.Destinations.SingleOrDefault(d => d.Id == r.DestinationId)))
                .Where(x => x.Destination is { Type: StorageProviderType.S3Compatible } d && d.State != StorageDestinationState.Disabled &&
                    d.TenantId == configuration.TenantId && pool.Contains(d.Id) && d.FailureDomain != Local.FailureDomain &&
                    d.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Stat) && x.Replica.FailureDomain == d.FailureDomain)
                .OrderBy(x => x.Destination!.Id, StringComparer.Ordinal).GroupBy(x => x.Destination!.FailureDomain, StringComparer.Ordinal)
                .Select(group => group.First()).Take(requiredRemotes)
                .Select(x => new LocalRecoverySource(x.Replica.DestinationId, x.Destination!.FailureDomain, StorageObjectKey.Normalize(x.Replica.NativeLocator))).ToArray();
            if (sources.Length < requiredRemotes) throw Error("Insufficient independent current offsite sources for local recovery.");
            result.Add(new(entry.ObjectId, entry.OperationId, entry.LogicalKey, entry.Generation, entry.SizeBytes, entry.Sha256, entry.CreatedAtUtc, sources));
        }
        return result;
    }

    private async Task ReadVerifiedAsync(LocalRecoveryItem item, LocalRecoverySource source, Stream? output, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.Replication.OperationDeadlineSeconds));
        try
        {
            var provider = registry.GetRequired(source.DestinationId);
            if (!provider.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Stat))
                throw Error("Offsite recovery provider cannot verify this source.");
            var stat = await provider.StatAsync(source.NativeLocator, deadline.Token);
            if (stat is null || stat.SizeBytes != item.SizeBytes) throw Error("Offsite recovery source is missing or has a different size.");
            await using var input = await provider.OpenReadAsync(source.NativeLocator, deadline.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, deadline.Token)) != 0)
            {
                total = checked(total + count);
                if (total > item.SizeBytes) throw Error("Offsite recovery source exceeds its declared size.");
                hash.AppendData(buffer, 0, count);
                if (output is not null) await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
            }
            if (total != item.SizeBytes || !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Error("Offsite recovery source checksum does not match the catalog.");
        }
        catch (StorageProviderException error) { throw Error("Offsite recovery source failed: " + error.Category); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw Error("Offsite recovery source verification timed out."); }
        catch (IOException) { throw Error("Offsite recovery staging IO failed; working sources were not changed."); }
    }

    private static void ValidatePrivateDestination(string plan, string root)
    {
        AccountingCleanupCommands.ValidatePrivateJsonPath(plan);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var parent = Path.GetDirectoryName(plan)!;
        if (File.Exists(plan) || new FileInfo(plan).LinkTarget is not null || !Directory.Exists(parent) ||
            plan.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw Error("Recovery staging requires a new private JSON outside the working backup root.");
        FileSystemInfo? current = new DirectoryInfo(parent);
        while (current is not null)
        {
            if (current.LinkTarget is not null) throw Error("Symlinks are not allowed for recovery staging.");
            current = ((DirectoryInfo)current).Parent;
        }
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(parent) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw Error("Recovery staging directory must be private to its owner.");
    }

    public static string Fingerprint(LocalReplicaRecoveryPlan plan) => Hash(JsonSerializer.Serialize(plan with { Fingerprint = "" }, MigrationCheckpointStore.JsonOptions));
    public static string ConfigurationHash(EffectiveStorageConfiguration value) => Hash(JsonSerializer.Serialize(value, MigrationCheckpointStore.JsonOptions));
    public static string CatalogHash(IReadOnlyList<StorageManifestEntry> value) => Hash(JsonSerializer.Serialize(value.OrderBy(x => x.ObjectId)
        .Select(x => x with { Replicas = x.Replicas.OrderBy(r => r.DestinationId, StringComparer.Ordinal).ToArray() }), MigrationCheckpointStore.JsonOptions));
    public static string InventoryHash(IReadOnlyList<LocalBackupInventory> value) => Hash(JsonSerializer.Serialize(value.OrderBy(x => x.FileName, StringComparer.Ordinal), MigrationCheckpointStore.JsonOptions));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static MigrationToolException Error(string message) => new(message);

    private static void SyncDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var fd = open(path, 0x10000 | 0x20000 | 0x80000);
        if (fd < 0) throw Error("Cannot open recovery staging directory for sync.");
        try { if (fsync(fd) != 0) throw Error("Recovery staging directory sync failed."); }
        finally { close(fd); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
}
