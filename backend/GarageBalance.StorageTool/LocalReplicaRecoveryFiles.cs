using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.Win32.SafeHandles;

namespace GarageBalance.StorageTool;

public sealed record LocalRecoveryPromotionResult(int Created, int AlreadyPresent, long Bytes, string Fingerprint);

// Filesystem-only primitive: no catalog write interface, deletion of a backup,
// overwrite or checkpoint mutation exists. The production adapter must hold
// the qualified preparation permit and a SHARE catalog lock while invoking it.
public sealed class LocalReplicaRecoveryFiles(EffectiveStorageConfiguration configuration, LocalBackupInspector inspector,
    Func<CancellationToken, Task> verifyAuthority, int? targetUnixGroup = null, Func<string, long>? freeBytes = null)
{
    public async Task<LocalRecoveryPromotionResult> PromoteAsync(LocalReplicaRecoveryPlan plan, string confirmation,
        IReadOnlyList<StorageManifestEntry> manifest, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifyAuthority);
        await verifyAuthority(token);
        Validate(plan, confirmation, manifest);
        var policy = configuration.Policies.Single(p => p.DataClass == StorageDataClass.DatabaseBackup);
        var original = await inspector.InspectAsync(plan.LocalRoot, configuration.TenantId, policy, token);
        var names = plan.Items.Select(i => i.FileName).ToHashSet(StringComparer.Ordinal);
        if (original.Any(i => !i.Verified) || LocalReplicaRecoveryStager.InventoryHash(original.Where(i => !names.Contains(i.FileName)).ToArray()) != plan.InventoryHash)
            throw Error("original_inventory_changed");
        var staged = await inspector.InspectAsync(plan.StagingDirectory, configuration.TenantId, policy, token);
        if (staged.Count != plan.Items.Count || staged.Any(file => !file.Verified ||
            !plan.Items.Any(i => i.FileName == file.FileName && i.SizeBytes == file.SizeBytes && i.Sha256 == file.Sha256)))
            throw Error("staging_archive_changed");
        var existing = original.Where(i => names.Contains(i.FileName)).ToDictionary(i => i.FileName, StringComparer.Ordinal);
        foreach (var item in plan.Items)
            if (existing.TryGetValue(item.FileName, out var file) && (file.SizeBytes != item.SizeBytes || file.Sha256 != item.Sha256 ||
                !file.ManifestMissing && (file.Manifest?.BackupId != item.OperationId || file.Manifest?.Generation != item.Generation)))
                throw Error("existing_target_conflict");
        var needed = plan.Items.Where(i => !existing.ContainsKey(i.FileName)).Sum(i => i.SizeBytes);
        var available = freeBytes?.Invoke(plan.LocalRoot) ?? new DriveInfo(Path.GetPathRoot(plan.LocalRoot)!).AvailableFreeSpace;
        if (available < checked(needed + 64L * 1024 * 1024)) throw Error("insufficient_space");
        if (OperatingSystem.IsLinux() && (targetUnixGroup is null || targetUnixGroup < 0)) throw Error("target_group_missing");
        var created = 0;
        foreach (var item in plan.Items.Where(i => !existing.ContainsKey(i.FileName)))
        {
            await verifyAuthority(token);
            var source = Path.Combine(plan.StagingDirectory, item.FileName);
            var destination = Path.Combine(plan.LocalRoot, item.FileName);
            var temporary = Path.Combine(plan.LocalRoot, ".local-recovery-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using var input = OpenSource(source);
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var output = new FileStream(temporary, options))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[64 * 1024];
                    long size = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, token)) != 0)
                    {
                        size = checked(size + count);
                        if (size > item.SizeBytes) throw Error("staging_archive_changed");
                        hash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                    }
                    if (size != item.SizeBytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != item.Sha256)
                        throw Error("staging_archive_changed");
                    if (OperatingSystem.IsLinux() && (fchown(Fd(output.SafeFileHandle), uint.MaxValue, checked((uint)targetUnixGroup!.Value)) != 0 ||
                        fchmod(Fd(output.SafeFileHandle), 0x1a0) != 0)) throw Error("target_permissions_failed");
                    await output.FlushAsync(token);
                    output.Flush(flushToDisk: true);
                }
                File.SetLastWriteTimeUtc(temporary, item.CreatedAtUtc.UtcDateTime);
                using (var metadata = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) metadata.Flush(flushToDisk: true);
                await verifyAuthority(token);
                if (OperatingSystem.IsLinux())
                {
                    if (renameat2(-100, temporary, -100, destination, 1) != 0) throw Error("publish_failed_target_untouched");
                }
                else File.Move(temporary, destination, overwrite: false);
                SyncDirectory(plan.LocalRoot);
                created++;
            }
            finally
            {
                // A published destination is never removed, including after an
                // uncertain filesystem result. Only this invocation's temp is owned.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        var after = await inspector.InspectAsync(plan.LocalRoot, configuration.TenantId, policy, token);
        foreach (var item in plan.Items)
        {
            var file = after.SingleOrDefault(f => f.FileName == item.FileName);
            if (file is null || !file.Verified || file.SizeBytes != item.SizeBytes || file.Sha256 != item.Sha256) throw Error("post_copy_verification_failed");
        }
        if (LocalReplicaRecoveryStager.InventoryHash(after.Where(i => !names.Contains(i.FileName)).ToArray()) != plan.InventoryHash)
            throw Error("original_inventory_changed");
        await verifyAuthority(token);
        return new(created, existing.Count, plan.Items.Sum(i => i.SizeBytes), plan.Fingerprint);
    }

    private void Validate(LocalReplicaRecoveryPlan plan, string confirmation, IReadOnlyList<StorageManifestEntry> manifest)
    {
        if (plan.SchemaVersion != 1 || plan.PlanId == Guid.Empty || plan.Fingerprint != confirmation ||
            plan.Fingerprint != LocalReplicaRecoveryStager.Fingerprint(plan) || plan.ConfigurationHash != LocalReplicaRecoveryStager.ConfigurationHash(configuration) ||
            plan.CatalogHash != LocalReplicaRecoveryStager.CatalogHash(manifest) || plan.Items.Count is < 1 or > LocalBackupInspector.MaximumFiles ||
            plan.Items.Select(i => i.ObjectId).Distinct().Count() != plan.Items.Count || plan.Items.Select(i => i.FileName).Distinct(StringComparer.Ordinal).Count() != plan.Items.Count)
            throw Error("plan_or_catalog_changed");
        var policy = configuration.Policies.Single(p => p.DataClass == StorageDataClass.DatabaseBackup);
        var pool = configuration.Pools.Single(p => p.Id == policy.PoolId).DestinationIds;
        var local = configuration.Destinations.Single(d => pool.Contains(d.Id) && d.Type == StorageProviderType.LocalFileSystem);
        if (local.State is not (StorageDestinationState.Enabled or StorageDestinationState.Recovering) ||
            !local.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat)) throw Error("local_destination_not_writable");
        if (!Path.IsPathFullyQualified(plan.LocalRoot) || !Path.IsPathFullyQualified(plan.StagingDirectory) || plan.LocalDestinationId != local.Id ||
            !string.Equals(Path.GetFullPath(plan.LocalRoot), Path.GetFullPath(local.RootPath!), PathComparison) || !Directory.Exists(plan.LocalRoot) || !Directory.Exists(plan.StagingDirectory) ||
            string.Equals(Path.GetFullPath(plan.StagingDirectory), Path.GetFullPath(plan.LocalRoot), PathComparison) ||
            Path.GetFullPath(plan.StagingDirectory).StartsWith(Path.GetFullPath(plan.LocalRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison))
            throw Error("directory_invalid");
        foreach (var path in new[] { plan.LocalRoot, plan.StagingDirectory })
            for (DirectoryInfo? dir = new(path); dir is not null; dir = dir.Parent)
                if (dir.LinkTarget is not null) throw Error("directory_link_forbidden");
        foreach (var item in plan.Items)
        {
            var entry = manifest.SingleOrDefault(e => e.ObjectId == item.ObjectId);
            if (!LocalBackupInspector.IsManagedName(item.FileName) || Path.GetFileName(item.FileName) != item.FileName ||
                item.OperationId == Guid.Empty || item.Generation < 1 || item.SizeBytes <= 0 || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit) ||
                entry is null || entry.DataClass != StorageDataClass.DatabaseBackup || entry.State is not (StorageObjectState.Protected or StorageObjectState.ProtectionDegraded) ||
                entry.LogicalKey != item.FileName || entry.OperationId != item.OperationId || entry.Generation != item.Generation || entry.SizeBytes != item.SizeBytes || entry.Sha256 != item.Sha256 || entry.CreatedAtUtc != item.CreatedAtUtc ||
                !entry.Replicas.Any(r => r.DestinationId == local.Id && r.NativeLocator == item.FileName && r.State == StorageReplicaState.Available &&
                    r.Generation == item.Generation && r.SizeBytes == item.SizeBytes && r.Sha256 == item.Sha256)) throw Error("item_identity_changed");
            var sources = item.VerifiedSources;
            if (sources.Count < Math.Max(1, Math.Max(policy.MinimumOffsiteCopies, policy.RequiredIndependentCopies - 1)) ||
                sources.Select(s => s.FailureDomain).Distinct(StringComparer.Ordinal).Count() != sources.Count || sources.Any(s =>
                !configuration.Destinations.Any(d => d.Id == s.DestinationId && pool.Contains(d.Id) && d.Type == StorageProviderType.S3Compatible &&
                    d.State != StorageDestinationState.Disabled && d.FailureDomain == s.FailureDomain && d.FailureDomain != local.FailureDomain && d.TenantId == configuration.TenantId &&
                    d.Capabilities.HasFlag(StorageCapability.Read | StorageCapability.Stat)) ||
                !entry.Replicas.Any(r => r.DestinationId == s.DestinationId && r.FailureDomain == s.FailureDomain && r.NativeLocator == s.NativeLocator &&
                    r.State == StorageReplicaState.Available && r.Generation == item.Generation && r.SizeBytes == item.SizeBytes && r.Sha256 == item.Sha256)))
                throw Error("offsite_evidence_changed");
        }
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static FileStream OpenSource(string path)
    {
        if (!OperatingSystem.IsLinux()) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fd = open(path, 0x20000 | 0x80000);
        if (fd < 0) throw Error("source_open_failed");
        return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Read);
    }
    private static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    private static MigrationToolException Error(string code) => new("Local recovery blocked: " + code);
    private static void SyncDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var fd = open(path, 0x10000 | 0x20000 | 0x80000);
        if (fd < 0) throw Error("directory_sync_failed");
        try { if (fsync(fd) != 0) throw Error("directory_sync_failed"); }
        finally { close(fd); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int fchown(int fd, uint owner, uint group);
    [DllImport("libc", SetLastError = true)] private static extern int fchmod(int fd, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int renameat2(int oldDirectory, string oldPath, int newDirectory, string newPath, uint flags);
}
