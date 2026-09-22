using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

return await StorageToolProgram.RunAsync(args, CancellationToken.None);

internal static class StorageToolProgram
{
    private static readonly HashSet<string> MutatingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "copy", "verify", "delta-sync", "resume", "repair"
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
        if (command is null or "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }
        var knownCommands = new[] { "inventory", "plan", "copy", "verify", "diff", "delta-sync", "resume", "repair", "status", "report", "cutover-check", "rollback-check" };
        if (!knownCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown command '{command}'. Use --help.");
            return 2;
        }
        var execute = args.Contains("--execute", StringComparer.OrdinalIgnoreCase);
        if (MutatingCommands.Contains(command) && !execute)
        {
            Console.Error.WriteLine($"Command '{command}' is dry-run by default. Re-run with --execute after reviewing inventory and plan.");
            return 3;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("ConnectionStrings__DefaultConnection is required. Its value is never printed.");
            return 4;
        }
        var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var backupOptions = configuration.GetSection(DatabaseBackupOptions.SectionName).Get<DatabaseBackupOptions>() ?? new DatabaseBackupOptions();
        var validator = new StorageOptionsValidator();
        var validation = validator.Validate(null, storageOptions);
        if (validation.Failed)
        {
            Console.Error.WriteLine(string.Join(Environment.NewLine, validation.Failures));
            return 5;
        }

        var dbOptions = new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connectionString).Options;
        await using var dbContext = new GarageBalanceDbContext(dbOptions);
        var catalog = new EfStorageCatalog(dbContext);
        var resolver = new StorageConfigurationResolver(Options.Create(storageOptions), Options.Create(backupOptions));
        var effective = resolver.Resolve();
        using var registry = new StorageProviderRegistry(resolver, new AwsS3ObjectClientFactory(), TimeProvider.System);
        var manifest = await catalog.ExportManifestAsync(StorageDataClass.DatabaseBackup, ReadInt(args, "--limit", 1000, 1, 10000), cancellationToken);
        var localDestination = effective.Destinations.FirstOrDefault(item => item.Type == StorageProviderType.LocalFileSystem);
        var localFiles = InventoryLocal(localDestination?.RootPath);

        if (command is "copy" or "delta-sync" or "resume")
        {
            await RegisterVerifiedLocalFilesAsync(localFiles, catalog, effective, cancellationToken);
            var runner = new StorageReplicationRunner(
                catalog,
                registry,
                resolver,
                TimeProvider.System,
                new StorageOperationHealthTracker(TimeProvider.System),
                NullLogger<StorageReplicationRunner>.Instance);
            var processed = 0;
            var maximum = ReadInt(args, "--max-jobs", 100, 1, 10000);
            while (processed < maximum && await runner.ProcessNextAsync($"storage-tool-{Environment.ProcessId}", cancellationToken))
            {
                processed++;
            }
            Console.WriteLine($"processedJobs={processed}");
            manifest = await catalog.ExportManifestAsync(StorageDataClass.DatabaseBackup, 1000, cancellationToken);
        }
        else if (command is "verify" or "repair")
        {
            var reconciliation = new StorageReconciliationRunner(
                catalog,
                registry,
                resolver,
                new StorageOperationHealthTracker(TimeProvider.System),
                TimeProvider.System,
                NullLogger<StorageReconciliationRunner>.Instance);
            Console.WriteLine($"scheduledRepairs={await reconciliation.ReconcileOnceAsync(cancellationToken)}");
            manifest = await catalog.ExportManifestAsync(StorageDataClass.DatabaseBackup, 1000, cancellationToken);
        }

        var report = BuildReport(command, effective, localFiles, manifest);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var output = ReadValue(args, "--output");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var fullPath = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, json, cancellationToken);
            Console.WriteLine($"reportPath={fullPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        var required = effective.Policies.SingleOrDefault(item => item.DataClass == StorageDataClass.DatabaseBackup)?.RequiredIndependentCopies ?? 1;
        var insufficient = manifest.Count(item => item.State is not StorageObjectState.Deleted &&
            item.Replicas.Where(replica => replica.State == StorageReplicaState.Available)
                .Select(replica => replica.FailureDomain).Distinct(StringComparer.Ordinal).Count() < required);
        return command is "cutover-check" or "rollback-check" && insufficient > 0 ? 10 : 0;
    }

    private static async Task RegisterVerifiedLocalFilesAsync(
        IReadOnlyList<LocalBackupInventory> files,
        IStorageCatalog catalog,
        EffectiveStorageConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.Mode != StorageMode.AsyncMirror)
        {
            return;
        }
        var local = configuration.Destinations.Single(item => item.Type == StorageProviderType.LocalFileSystem);
        var policy = configuration.Policies.Single(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var remoteTargets = configuration.Pools.Single(item => item.Id == policy.PoolId).DestinationIds
            .Where(id => !string.Equals(id, local.Id, StringComparison.Ordinal))
            .Select(id => configuration.Destinations.Single(item => item.Id == id))
            .Where(item => item.State != StorageDestinationState.Disabled && item.Capabilities.HasFlag(StorageCapability.Write))
            .Select(item => new StorageReplicationTarget(item.Id, item.FailureDomain))
            .ToArray();
        foreach (var file in files.Where(item => item.Verified && item.Manifest is not null))
        {
            var manifest = file.Manifest!;
            await catalog.RegisterCommittedObjectAsync(new RegisterCommittedStorageObjectRequest(
                manifest.BackupId,
                configuration.TenantId,
                StorageDataClass.DatabaseBackup,
                manifest.FileName,
                policy.Id,
                policy.Revision,
                Math.Max(1, manifest.Generation),
                manifest.SizeBytes,
                manifest.Sha256,
                manifest.FileName,
                "application/octet-stream",
                local.Id,
                local.FailureDomain,
                manifest.FileName,
                remoteTargets,
                configuration.Replication.MaximumAttempts,
                manifest.CreatedAtUtc), cancellationToken);
        }
    }

    private static IReadOnlyList<LocalBackupInventory> InventoryLocal(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }
        var result = new List<LocalBackupInventory>();
        foreach (var path in Directory.EnumerateFiles(root, "garagebalance_*.pgdump", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            DatabaseBackupManifest? manifest = null;
            var manifestPath = path + ".manifest.json";
            try
            {
                if (File.Exists(manifestPath))
                {
                    manifest = JsonSerializer.Deserialize<DatabaseBackupManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                var hash = Convert.ToHexStringLower(SHA256.HashData(source));
                result.Add(new LocalBackupInventory(info.Name, info.Length, manifest is not null && manifest.SizeBytes == info.Length && string.Equals(manifest.Sha256, hash, StringComparison.OrdinalIgnoreCase), manifest));
            }
            catch
            {
                result.Add(new LocalBackupInventory(info.Name, info.Length, false, manifest));
            }
        }
        return result.OrderBy(item => item.FileName, StringComparer.Ordinal).ToArray();
    }

    private static object BuildReport(string command, EffectiveStorageConfiguration configuration, IReadOnlyList<LocalBackupInventory> local, IReadOnlyList<StorageManifestEntry> manifest)
    {
        var policy = configuration.Policies.SingleOrDefault(item => item.DataClass == StorageDataClass.DatabaseBackup);
        var required = policy?.RequiredIndependentCopies ?? 1;
        return new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            command,
            mode = configuration.Mode.ToString(),
            policyRevision = policy?.Revision,
            requiredIndependentCopies = required,
            local = new { total = local.Count, verified = local.Count(item => item.Verified), invalid = local.Count(item => !item.Verified) },
            catalog = new
            {
                total = manifest.Count,
                protectedCount = manifest.Count(item => item.State == StorageObjectState.Protected),
                deletingCount = manifest.Count(item => item.State == StorageObjectState.Deleting),
                insufficientCount = manifest.Count(item => item.State is not StorageObjectState.Deleted && item.Replicas.Where(replica => replica.State == StorageReplicaState.Available).Select(replica => replica.FailureDomain).Distinct(StringComparer.Ordinal).Count() < required),
                objects = manifest.Select(item => new
                {
                    item.LogicalKey,
                    item.Generation,
                    item.SizeBytes,
                    sha256 = item.Sha256,
                    state = item.State.ToString(),
                    availableCopies = item.Replicas.Count(replica => replica.State == StorageReplicaState.Available),
                    independentCopies = item.Replicas.Where(replica => replica.State == StorageReplicaState.Available).Select(replica => replica.FailureDomain).Distinct(StringComparer.Ordinal).Count()
                })
            }
        };
    }

    private static int ReadInt(string[] args, string name, int fallback, int minimum, int maximum)
    {
        var value = ReadValue(args, name);
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
    }

    private static string? ReadValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void PrintHelp() => Console.WriteLine("""
GarageBalance.StorageTool commands:
  inventory | plan | diff | status | report | cutover-check | rollback-check
  copy | verify | delta-sync | resume | repair --execute
Options: --limit N --max-jobs N --output <json-path>
Mutating commands are dry-run guarded and never delete source objects.
""");

    private sealed record LocalBackupInventory(string FileName, long SizeBytes, bool Verified, DatabaseBackupManifest? Manifest);
}
