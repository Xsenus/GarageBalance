using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GarageBalance.StorageTool;

public sealed record StagingBackupPreparationResult(LocalRecoveryPromotionResult? LocalRecovery, int CatalogObjects,
    int CheckpointSources, int TombstonedSources, bool PhysicalCoverageVerified);

public static class StagingBackupPreparation
{
    // The adapter never commits a database transaction. A crash here can leave
    // some immutable backup files restored, but cannot delete financial rows.
    public static async Task<StagingBackupPreparationResult> VerifyAndRestoreAsync(GarageBalanceDbContext db,
        IConfiguration configuration, StagingPreparationPermit permit, string? planPath, string? confirmation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(permit);
        await permit.VerifyAsync(token);
        await StagingTargetBinding.VerifyAsync(db, token);
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new();
        var backups = configuration.GetSection(DatabaseBackupOptions.SectionName).Get<DatabaseBackupOptions>() ?? new();
        if (new StorageOptionsValidator().Validate(null, storage).Failed) throw Error("cleanup_storage_configuration_invalid");
        var resolver = new StorageConfigurationResolver(Options.Create(storage), Options.Create(backups));
        var effective = resolver.Resolve();
        if (effective.Mode != StorageMode.AsyncMirror || effective.TenantId != "garagebalance") throw Error("cleanup_storage_configuration_changed");
        var checkpoint = effective.Destinations.Any(d => d.Id == "hostkey-nl")
            ? "/var/lib/garagebalance-staging/storage-migration/multi-s3-backfill.json"
            : "/var/lib/garagebalance-staging/storage-migration/cloudru-backfill.json";
        var catalog = new EfStorageCatalog(db, resolver);
        using var providers = new StorageProviderRegistry(resolver, new AwsS3ObjectClientFactory(), TimeProvider.System);
        var inspector = new LocalBackupInspector(new BackupCommandRunner(), new BackupToolLocator(), backups.PgRestorePath);
        var engine = new StorageMigrationEngine(catalog, providers, effective, inspector,
            (_, _, _, _) => throw new InvalidOperationException("maintenance_storage_readonly_job_forbidden"));
        LocalRecoveryPromotionResult? restored = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='60s'; LOCK TABLE storage_objects,storage_object_replicas IN SHARE MODE", token);
            var initial = await engine.ExecuteAsync(new("checkpoint-check", false, checkpoint, 200, 1, null), token);
            var compatibility = initial.Details as CheckpointCompatibility ?? throw Error("cleanup_storage_checkpoint_unverified");
            if (!compatibility.ConfigurationMatches || compatibility.UncataloguedMissingSources != 0 || compatibility.ChangedOrInvalidLocalSources != 0 ||
                compatibility.CatalogMismatches != 0 || initial.Blockers.Any(b => b != "checkpoint_missing_active_local_sources"))
                throw Error("cleanup_storage_checkpoint_unverified");
            if (planPath is not null)
            {
                using var handle = LinuxMaintenanceBarrierHost.OpenVerifiedRootFile(planPath);
                if (RandomAccess.GetLength(handle) is <= 0 or > 32 * 1024 * 1024) throw Error("cleanup_storage_plan_invalid");
                await using var input = new FileStream(handle, FileAccess.Read);
                var plan = await JsonSerializer.DeserializeAsync<LocalReplicaRecoveryPlan>(input, MigrationCheckpointStore.JsonOptions, token)
                    ?? throw Error("cleanup_storage_plan_invalid");
                var expectedDirectory = Path.Combine(Path.GetDirectoryName(planPath)!, "local-recovery-" + plan.PlanId.ToString("N"));
                if (Path.GetFullPath(plan.StagingDirectory) != Path.GetFullPath(expectedDirectory)) throw Error("cleanup_storage_plan_invalid");
                // Source files must be the root-owned sealed artifacts, not an
                // app-writable directory substituted after the plan was approved.
                foreach (var item in plan.Items)
                {
                    if (Path.GetFileName(item.FileName) != item.FileName || !LocalBackupInspector.IsManagedName(item.FileName)) throw Error("cleanup_storage_plan_invalid");
                    using var source = LinuxMaintenanceBarrierHost.OpenVerifiedRootFile(Path.Combine(plan.StagingDirectory, item.FileName));
                }
                var group = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/id", ["-g", "garagebalance"], token);
                if (group.ExitCode != 0 || !int.TryParse(group.Output.Trim(), out var gid) || gid < 0) throw Error("cleanup_storage_group_unknown");
                var manifest = await engine.ReadCatalogAsync(200, token);
                restored = await new LocalReplicaRecoveryFiles(effective, inspector, permit.VerifyAsync, gid)
                    .PromoteAsync(plan, confirmation ?? "", manifest, token);
            }
            var after = await engine.ExecuteAsync(new("checkpoint-check", false, checkpoint, 200, 1, null), token);
            if (after.ExitCode != 0) throw Error("cleanup_storage_checkpoint_unverified");
            // Unlike cutover-check, this does not register legitimate deployment
            // archives outside the old checkpoint. No repair or pause reset exists.
            var physical = await engine.ExecuteAsync(new("verify", false, null, 200, 1, null), token);
            if (physical.ExitCode != 0 || physical.Details is not IReadOnlyList<ObjectVerification> verified || verified.Count == 0 ||
                verified.Any(item => !item.RequiredProtectionMet || !item.LocalRollbackReady))
                throw Error("cleanup_storage_physical_coverage_unverified");
            await permit.VerifyAsync(token);
            await transaction.RollbackAsync(token);
            var checkedSources = (CheckpointCompatibility)after.Details;
            return new(restored, physical.CatalogObjects, checkedSources.Sources, checkedSources.TombstonedSources, true);
        }
    }
    private static AccountingCleanupException Error(string code) => new(code, "Staging backup recovery is not verified; financial cleanup remains blocked.");
}
