using System.Text.Json;
using System.Runtime.InteropServices;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using var shutdown = new CancellationTokenSource();
using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
    context => { context.Cancel = true; shutdown.Cancel(); });
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };
Console.CancelKeyPress += cancelHandler;
try { return await StorageToolProgram.RunAsync(args, shutdown.Token); }
finally { Console.CancelKeyPress -= cancelHandler; }

namespace GarageBalance.StorageTool
{
    public static class StorageToolProgram
    {
        public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
            if (command is null or "help" or "--help" or "-h")
            {
                Console.WriteLine("""
GarageBalance.StorageTool commands:
  inventory | plan | diff | status | report | cutover-check | rollback-check
  copy | verify | delta-sync | resume | repair [--execute]
  resume-reconciliation --execute --reason <operator-explanation>
  recovery-canary | recovery-publish | recovery-fetch | restore-drill [--execute]
Options: --checkpoint <private-json-path> --page-size N --max-jobs N --output <private-json-path>
Mutating commands are dry-run by default. copy creates a checkpoint; resume continues it;
delta-sync extends it with new files. No command deletes source objects or changes live configuration.
See docs/storage-migration-cli.md for migration gates and docs/disaster-recovery.md for recovery commands.
""");
                return 0;
            }
            try
            {
                var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
                if (DisasterRecoveryCommands.CanHandle(command))
                    return await DisasterRecoveryCommands.RunAsync(args, configuration, cancellationToken);
                var options = MigrationCommandOptions.Parse(args);
                var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new();
                var backups = configuration.GetSection(DatabaseBackupOptions.SectionName).Get<DatabaseBackupOptions>() ?? new();
                if (new StorageOptionsValidator().Validate(null, storage).Failed)
                {
                    Console.Error.WriteLine("Storage configuration is invalid; validate destination and policy settings.");
                    return 5;
                }
                var resolver = new StorageConfigurationResolver(Options.Create(storage), Options.Create(backups));
                var reconciliationGuard = new FileStorageReconciliationGuard(resolver, TimeProvider.System);
                if (command == "resume-reconciliation")
                {
                    var state = options.Execute
                        ? await reconciliationGuard.ResumeAsync(options.Reason!, cancellationToken)
                        : await reconciliationGuard.PeekStateAsync(cancellationToken);
                    Console.WriteLine(JsonSerializer.Serialize(new { dryRun = !options.Execute, state }, MigrationCheckpointStore.JsonOptions));
                    return 0;
                }
                var connection = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connection))
                {
                    Console.Error.WriteLine("ConnectionStrings__DefaultConnection is required. Its value is never printed.");
                    return 4;
                }
                await using var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
                    .UseNpgsql(connection).Options);
                var catalog = new EfStorageCatalog(db, resolver);
                using var registry = new StorageProviderRegistry(resolver, new AwsS3ObjectClientFactory(), TimeProvider.System);
                var runner = new StorageReplicationRunner(catalog, registry, resolver, TimeProvider.System,
                    new StorageOperationHealthTracker(TimeProvider.System), NullLogger<StorageReplicationRunner>.Instance,
                    safetyGuard: reconciliationGuard);
                var engine = new StorageMigrationEngine(catalog, registry, resolver.Resolve(),
                    new LocalBackupInspector(new BackupCommandRunner(), new BackupToolLocator(), backups.PgRestorePath),
                    (owner, kinds, objectIds, token) => runner.ProcessNextAsync(owner, token, kinds, objectIds),
                    new StorageMaintenanceLock(db), reconciliationGuard);
                var result = await engine.ExecuteAsync(options, cancellationToken);
                var json = JsonSerializer.Serialize(result, MigrationCheckpointStore.JsonOptions);
                if (options.Output is not null)
                {
                    await MigrationCheckpointStore.WriteReportAsync(options.Output, json, cancellationToken);
                    Console.WriteLine("Report saved to the requested private output file.");
                }
                else Console.WriteLine(json);
                return result.ExitCode;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Operation cancelled; the saved checkpoint can be resumed.");
                return 130;
            }
            catch (MigrationToolException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
            catch (Exception)
            {
                // Provider/DB/process exception text may contain credentials or private locators.
                Console.Error.WriteLine("Storage operation failed. Check protected operator logs and retry the saved checkpoint; no source cleanup was performed.");
                return 6;
            }
        }
    }
}
