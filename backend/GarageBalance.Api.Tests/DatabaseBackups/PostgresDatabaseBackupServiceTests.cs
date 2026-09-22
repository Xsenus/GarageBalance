using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Backups;

public sealed class PostgresDatabaseBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"garagebalance-backup-tests-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 7, 15, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateManualBackup_WritesTemporaryDumpVerifiesMovesAuditsAndReturnsStatus()
    {
        var runner = new FakeCommandRunner();
        var audit = new CaptureAuditWriter();
        var unitOfWork = new CaptureUnitOfWork();
        var service = CreateService(runner, audit, unitOfWork);

        var result = await service.CreateAsync(DatabaseBackupKind.Manual, "Перед обновлением", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("garagebalance_manual_20260715_093000_000.pgdump", result.Value.FileName);
        Assert.Equal(4, result.Value.SizeBytes);
        var backupPath = Path.Combine(_directory, result.Value.FileName);
        Assert.True(File.Exists(backupPath));
        Assert.False(File.Exists(Path.Combine(_directory, result.Value.FileName + ".tmp")));
        var manifestPath = backupPath + ".manifest.json";
        Assert.True(File.Exists(manifestPath));
        var manifest = JsonSerializer.Deserialize<DatabaseBackupManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal(1, manifest.Generation);
        Assert.Equal("database-backups", manifest.PolicyId);
        Assert.Equal(1, manifest.PolicyRevision);
        Assert.Equal(result.Value.FileName, manifest.FileName);
        Assert.Equal(4, manifest.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3, 4])), manifest.Sha256);
        Assert.Equal(manifest.Sha256, result.Value.Sha256);
        Assert.Equal("local_verified", result.Value.ProtectionState);
        Assert.Equal(2, runner.Commands.Count);
        var dump = runner.Commands[0];
        Assert.Equal("pg_dump", dump.FileName);
        Assert.Contains("--format=custom", dump.Arguments);
        Assert.Contains("postgres", dump.Arguments);
        Assert.DoesNotContain("secret-password", dump.Arguments);
        Assert.Equal("secret-password", dump.Environment["PGPASSWORD"]);
        Assert.Equal("pg_restore", runner.Commands[1].FileName);
        Assert.Equal(["--list", Path.Combine(_directory, result.Value.FileName + ".tmp")], runner.Commands[1].Arguments);
        var auditRequest = Assert.Single(audit.Requests);
        Assert.Equal("database.backup_created", auditRequest.Action);
        Assert.Equal("Перед обновлением", auditRequest.Reason);
        Assert.Equal(1, unitOfWork.SaveCount);

        var status = await service.GetStatusAsync(CancellationToken.None);
        Assert.True(status.Enabled);
        Assert.True(status.AutomaticEnabled);
        Assert.False(status.IsRunning);
        Assert.Null(status.LastError);
        Assert.Equal(result.Value.FileName, Assert.Single(status.Backups).FileName);
        Assert.Equal(result.Value.Sha256, status.Backups[0].Sha256);
    }

    [Fact]
    public async Task CreateBackup_RegistersCommittedGenerationInStorageCatalog()
    {
        var catalog = new CaptureStorageCatalog();
        var service = CreateService(new FakeCommandRunner(), storageCatalog: catalog);

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.Single(catalog.Registrations);
        Assert.Equal(StorageDataClass.DatabaseBackup, request.DataClass);
        Assert.Equal(1, request.Generation);
        Assert.Equal(4, request.SizeBytes);
        Assert.Equal(result.Value!.Sha256, request.Sha256);
        Assert.Equal(result.Value.FileName, request.LogicalKey);
        Assert.Equal("local-hot", request.LocalDestinationId);
        Assert.Empty(request.ReplicationTargets);
    }

    [Fact]
    public async Task AsyncMirror_CreateAcknowledgesLocalCommitAndExposesProtectionDebt()
    {
        var catalog = new CaptureStorageCatalog();
        var resolver = CreateAsyncMirrorResolver();
        var registry = new LocalOnlyProviderRegistry(new LocalFileStorageProvider("local-hot", _directory));
        var service = CreateService(
            new FakeCommandRunner(),
            storageCatalog: catalog,
            storageConfigurationResolver: resolver,
            storageProviderRegistry: registry);

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("protection_pending", result.Value!.ProtectionState);
        var registration = catalog.Registrations[0];
        Assert.Equal("offsite-a", Assert.Single(registration.ReplicationTargets).DestinationId);
        Assert.Equal("protection_pending", Assert.Single(status.Backups).ProtectionState);
        Assert.Equal("Локальное и удалённое хранилища", status.StorageLocation);
    }

    [Fact]
    public async Task AsyncMirror_CapacityLimitBlocksAnotherDumpBeforeStartingPostgresTool()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(
            Path.Combine(_directory, "garagebalance_automatic_20260701_020000_000.pgdump"),
            new byte[1024 * 1024]);
        var runner = new FakeCommandRunner();
        var service = CreateService(
            runner,
            storageConfigurationResolver: CreateAsyncMirrorResolver(1024 * 1024));

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("database_backup_storage_capacity", result.ErrorCode);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task GetStatus_MarksBackupFailedWhenBytesNoLongerMatchManifest()
    {
        var service = CreateService(new FakeCommandRunner());
        var created = await service.CreateAsync(
            DatabaseBackupKind.Automatic,
            null,
            null,
            CancellationToken.None);
        Assert.True(created.Succeeded);
        var path = Path.Combine(_directory, created.Value!.FileName);
        await File.WriteAllBytesAsync(path, [4, 3, 2, 1]);

        var status = await service.GetStatusAsync(CancellationToken.None);

        var backup = Assert.Single(status.Backups);
        Assert.Equal("failed", backup.ProtectionState);
        Assert.Null(backup.LastVerifiedAtUtc);
        Assert.Equal(created.Value.Sha256, backup.Sha256);
        var download = await service.OpenDownloadAsync(backup.FileName, null, CancellationToken.None);
        Assert.False(download.Succeeded);
        Assert.Equal("database_backup_integrity_failed", download.ErrorCode);
    }

    [Fact]
    public async Task CreateBackup_RejectsDisabledConfigurationAndInvalidManualReasonWithoutStartingProcess()
    {
        var runner = new FakeCommandRunner();
        var disabled = CreateService(runner, enabled: false);
        var enabled = CreateService(runner);

        var disabledResult = await disabled.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);
        var missingReason = await enabled.CreateAsync(DatabaseBackupKind.Manual, " ", null, CancellationToken.None);
        var shortReason = await enabled.CreateAsync(DatabaseBackupKind.Manual, "ab", null, CancellationToken.None);
        var longReason = await enabled.CreateAsync(DatabaseBackupKind.Manual, new string('a', 501), null, CancellationToken.None);

        Assert.Equal("database_backup_disabled", disabledResult.ErrorCode);
        Assert.Equal("database_backup_reason_required", missingReason.ErrorCode);
        Assert.Equal("database_backup_reason_invalid", shortReason.ErrorCode);
        Assert.Equal("database_backup_reason_invalid", longReason.ErrorCode);
        Assert.Empty(runner.Commands);
    }

    [Theory]
    [InlineData(true, false, "database_backup_dump_failed")]
    [InlineData(false, true, "database_backup_verification_failed")]
    public async Task CreateBackup_RejectsCommandFailuresAndRemovesTemporaryFile(bool failDump, bool failVerification, string expectedCode)
    {
        var runner = new FakeCommandRunner { FailDump = failDump, FailVerification = failVerification };
        var service = CreateService(runner);

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : []);
        var status = await service.GetStatusAsync(CancellationToken.None);
        Assert.NotNull(status.LastError);
    }

    [Fact]
    public async Task CreateBackup_RejectsEmptyDumpAndDoesNotRunVerification()
    {
        var runner = new FakeCommandRunner { WriteEmptyDump = true };
        var service = CreateService(runner);

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);

        Assert.Equal("database_backup_empty", result.ErrorCode);
        Assert.Single(runner.Commands);
        Assert.Empty(Directory.Exists(_directory) ? Directory.GetFiles(_directory) : []);
    }

    [Fact]
    public async Task BackupCommandRunner_StartsProcessWithoutShellAndReturnsItsExitCode()
    {
        var runner = new BackupCommandRunner();

        var result = await runner.RunAsync(
            new BackupCommand("dotnet", ["--version"], new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public void BackupToolLocator_ResolvesConfiguredExecutableAndRejectsMissingAbsolutePath()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");
        var locator = new BackupToolLocator();

        Assert.Equal(Path.GetFullPath(executable), locator.Resolve(executable));
        Assert.Null(locator.Resolve(Path.Combine(_directory, "missing-pg-dump")));
    }

    [Fact]
    public void BackupToolLocator_PrefersNewestInstalledPostgresVersion()
    {
        var installations = new[]
        {
            Path.Combine(_directory, "PostgreSQL", "9.6"),
            Path.Combine(_directory, "PostgreSQL", "17"),
            Path.Combine(_directory, "PostgreSQL", "10")
        };

        Assert.Equal(
            [installations[1], installations[2], installations[0]],
            BackupToolLocator.OrderPostgresInstallations(installations));
    }

    [Fact]
    public async Task GetStatus_DoesNotExposePersistentOperatingSystemDirectoryForAutoMode()
    {
        var service = CreateService(new FakeCommandRunner(), directory: "auto");

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Empty(status.Directory);
        Assert.Equal(30, status.FreshnessThresholdHours);
        Assert.Equal("Локальное хранилище", status.StorageLocation);
    }

    [Fact]
    public async Task LastAutomaticBackup_SearchesBeyondTwentyMostRecentManualFiles()
    {
        Directory.CreateDirectory(_directory);
        var automaticPath = Path.Combine(_directory, "garagebalance_automatic_20260701_020000_000.pgdump");
        await File.WriteAllBytesAsync(automaticPath, [1]);
        File.SetLastWriteTimeUtc(automaticPath, _now.AddDays(-1).UtcDateTime);
        for (var index = 0; index < 25; index++)
        {
            var manualPath = Path.Combine(
                _directory,
                $"garagebalance_manual_20260702_03{index:D2}00_000.pgdump");
            await File.WriteAllBytesAsync(manualPath, [1]);
            File.SetLastWriteTimeUtc(manualPath, _now.AddMinutes(index).UtcDateTime);
        }
        var service = CreateService(new FakeCommandRunner());

        var status = await service.GetStatusAsync(CancellationToken.None);
        var lastAutomatic = await service.GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken.None);

        Assert.Equal(20, status.Backups.Count);
        Assert.DoesNotContain(status.Backups, backup => backup.Kind == "automatic");
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(automaticPath), TimeSpan.Zero), lastAutomatic);
        Assert.All(status.Backups, backup => Assert.Equal("manifest_missing", backup.ProtectionState));
    }

    [Fact]
    public async Task CreateBackup_ReportsMissingPostgresClientToolsWithoutStartingProcess()
    {
        var runner = new FakeCommandRunner();
        var service = CreateService(runner, toolLocator: new FakeToolLocator(available: false));

        var status = await service.GetStatusAsync(CancellationToken.None);
        var result = await service.CreateAsync(DatabaseBackupKind.Manual, "Проверка локальной копии", null, CancellationToken.None);

        Assert.Contains("pg_dump", status.LastError, StringComparison.Ordinal);
        Assert.Equal("database_backup_tools_unavailable", result.ErrorCode);
        Assert.Contains("POSTGRESQL_BIN", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task CreateBackup_KeepsOnlyConfiguredNumberOfManagedFilesAndNeverDeletesForeignFiles()
    {
        Directory.CreateDirectory(_directory);
        var oldest = Path.Combine(_directory, "garagebalance_automatic_20260712_010000_000.pgdump");
        var newer = Path.Combine(_directory, "garagebalance_manual_20260713_010000_000.pgdump");
        var foreign = Path.Combine(_directory, "customer-copy.pgdump");
        await File.WriteAllBytesAsync(oldest, [1]);
        await File.WriteAllBytesAsync(newer, [1]);
        await File.WriteAllBytesAsync(foreign, [1]);
        File.SetLastWriteTimeUtc(oldest, _now.AddDays(-3).UtcDateTime);
        File.SetLastWriteTimeUtc(newer, _now.AddDays(-2).UtcDateTime);
        var service = CreateService(new FakeCommandRunner(), retentionCount: 2);

        var result = await service.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(oldest + ".manifest.json"));
        Assert.True(File.Exists(newer));
        Assert.True(File.Exists(foreign));
        Assert.Equal(2, (await service.GetStatusAsync(CancellationToken.None)).Backups.Count);
    }

    [Fact]
    public async Task CreateBackup_ReturnsConflictWhileAnotherBackupOwnsTheGlobalLock()
    {
        var runner = new FakeCommandRunner { HoldDump = true };
        var firstService = CreateService(runner);
        var secondService = CreateService(new FakeCommandRunner());
        var first = firstService.CreateAsync(DatabaseBackupKind.Automatic, null, null, CancellationToken.None);
        await runner.DumpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await secondService.CreateAsync(DatabaseBackupKind.Manual, "Проверка блокировки", null, CancellationToken.None);
        runner.ReleaseDump.TrySetResult(true);
        var firstResult = await first;

        Assert.Equal("database_backup_in_progress", second.ErrorCode);
        Assert.True(firstResult.Succeeded);
    }

    [Fact]
    public async Task OpenDownload_ReturnsManagedStreamAndAuditsExport()
    {
        Directory.CreateDirectory(_directory);
        const string fileName = "garagebalance_manual_20260715_093000_000.pgdump";
        await File.WriteAllBytesAsync(Path.Combine(_directory, fileName), [1, 2, 3, 4]);
        var audit = new CaptureAuditWriter();
        var unitOfWork = new CaptureUnitOfWork();
        var actorUserId = Guid.NewGuid();
        var service = CreateService(new FakeCommandRunner(), audit, unitOfWork);

        var result = await service.OpenDownloadAsync(fileName, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        await using (result.Value.Content)
        {
            using var copy = new MemoryStream();
            await result.Value.Content.CopyToAsync(copy);
            Assert.Equal([1, 2, 3, 4], copy.ToArray());
        }
        Assert.Equal(fileName, result.Value.FileName);
        Assert.Equal(4, result.Value.SizeBytes);
        var auditRequest = Assert.Single(audit.Requests);
        Assert.Equal("database.backup_downloaded", auditRequest.Action);
        Assert.Equal(actorUserId, auditRequest.ActorUserId);
        Assert.Equal("export", auditRequest.ActionKind);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData("../customer-copy.pgdump", "database_backup_file_invalid")]
    [InlineData("customer-copy.pgdump", "database_backup_file_invalid")]
    [InlineData("garagebalance_manual_20260715_093000_000.pgdump", "database_backup_not_found")]
    public async Task ManagedFileOperations_RejectInvalidOrMissingFiles(string fileName, string expectedCode)
    {
        var service = CreateService(new FakeCommandRunner());

        var download = await service.OpenDownloadAsync(fileName, null, CancellationToken.None);
        var deletion = await service.DeleteAsync(fileName, "Копия больше не нужна", null, CancellationToken.None);

        Assert.Equal(expectedCode, download.ErrorCode);
        Assert.Equal(expectedCode, deletion.ErrorCode);
    }

    [Fact]
    public async Task DeleteBackup_RequiresReasonDeletesOnlyRequestedFileAndAuditsAction()
    {
        Directory.CreateDirectory(_directory);
        const string fileName = "garagebalance_automatic_20260715_093000_000.pgdump";
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        await File.WriteAllTextAsync(path + ".manifest.json", "{}");
        var audit = new CaptureAuditWriter();
        var unitOfWork = new CaptureUnitOfWork();
        var actorUserId = Guid.NewGuid();
        var service = CreateService(new FakeCommandRunner(), audit, unitOfWork);

        var missingReason = await service.DeleteAsync(fileName, " ", actorUserId, CancellationToken.None);
        var shortReason = await service.DeleteAsync(fileName, "ab", actorUserId, CancellationToken.None);
        var longReason = await service.DeleteAsync(fileName, new string('a', 501), actorUserId, CancellationToken.None);
        var deleted = await service.DeleteAsync(fileName, "Истек установленный срок хранения", actorUserId, CancellationToken.None);

        Assert.Equal("database_backup_delete_reason_required", missingReason.ErrorCode);
        Assert.Equal("database_backup_delete_reason_invalid", shortReason.ErrorCode);
        Assert.Equal("database_backup_delete_reason_invalid", longReason.ErrorCode);
        Assert.True(deleted.Succeeded);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".manifest.json"));
        var auditRequest = Assert.Single(audit.Requests);
        Assert.Equal("database.backup_deleted", auditRequest.Action);
        Assert.Equal(actorUserId, auditRequest.ActorUserId);
        Assert.Equal("delete", auditRequest.ActionKind);
        Assert.Equal("Истек установленный срок хранения", auditRequest.Reason);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task OpenDownload_HonorsAnAlreadyCancelledRequest()
    {
        var service = CreateService(new FakeCommandRunner());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.OpenDownloadAsync(
            "garagebalance_manual_20260715_093000_000.pgdump",
            null,
            cancellation.Token));
    }

    private PostgresDatabaseBackupService CreateService(
        FakeCommandRunner runner,
        CaptureAuditWriter? audit = null,
        CaptureUnitOfWork? unitOfWork = null,
        bool enabled = true,
        int retentionCount = 30,
        string? directory = null,
        IBackupToolLocator? toolLocator = null,
        IStorageCatalog? storageCatalog = null,
        StorageConfigurationResolver? storageConfigurationResolver = null,
        IStorageProviderRegistry? storageProviderRegistry = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=postgres;Port=5432;Database=garagebalance;Username=postgres;Password=secret-password"
        }).Build();
        var options = Options.Create(new DatabaseBackupOptions
        {
            Enabled = enabled,
            AutomaticEnabled = true,
            Directory = directory ?? _directory,
            IntervalHours = 24,
            RetentionCount = retentionCount,
            PgDumpPath = "pg_dump",
            PgRestorePath = "pg_restore"
        });
        return new PostgresDatabaseBackupService(
            configuration,
            options,
            runner,
            toolLocator ?? new FakeToolLocator(available: true),
            audit ?? new CaptureAuditWriter(),
            unitOfWork ?? new CaptureUnitOfWork(),
            new FixedTimeProvider(_now),
            NullLogger<PostgresDatabaseBackupService>.Instance,
            storageProviderRegistry: storageProviderRegistry,
            storageConfigurationResolver: storageConfigurationResolver,
            storageCatalog: storageCatalog);
    }

    private StorageConfigurationResolver CreateAsyncMirrorResolver(long maximumPendingBytes = 20L * 1024 * 1024 * 1024)
    {
        var storage = new StorageOptions
        {
            Mode = StorageMode.AsyncMirror,
            Destinations =
            [
                new StorageDestinationOptions
                {
                    Id = "local-hot",
                    Type = StorageProviderType.LocalFileSystem,
                    FailureDomain = "local-host",
                    RootPath = _directory,
                    Capabilities = ["Read", "Write", "Stat", "Delete"]
                },
                new StorageDestinationOptions
                {
                    Id = "offsite-a",
                    Type = StorageProviderType.S3Compatible,
                    FailureDomain = "host-a",
                    Endpoint = "https://s3.example.test",
                    Bucket = "backups",
                    Prefix = "garagebalance",
                    AllowedEndpointHosts = ["s3.example.test"],
                    Capabilities = ["Read", "Write", "Stat", "Delete", "ServerSideEncryption"]
                }
            ],
            Pools = [new StoragePoolOptions { Id = "database-backups", DestinationIds = ["local-hot", "offsite-a"] }],
            Policies =
            [
                new StoragePolicyOptions
                {
                    Id = "database-backups",
                    DataClass = StorageDataClass.DatabaseBackup,
                    PoolId = "database-backups",
                    RequiredIndependentCopies = 2,
                    DesiredCopies = 2,
                    MinimumOffsiteCopies = 1
                }
            ],
            Replication = new StorageReplicationOptions { MaximumPendingBytes = maximumPendingBytes }
        };
        return new StorageConfigurationResolver(
            Options.Create(storage),
            Options.Create(new DatabaseBackupOptions { Directory = _directory }));
    }

    private sealed class FakeToolLocator(bool available) : IBackupToolLocator
    {
        public string? Resolve(string configuredPath) => available ? configuredPath : null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeCommandRunner : IBackupCommandRunner
    {
        public List<BackupCommand> Commands { get; } = [];
        public bool FailDump { get; init; }
        public bool FailVerification { get; init; }
        public bool WriteEmptyDump { get; init; }
        public bool HoldDump { get; init; }
        public TaskCompletionSource<bool> DumpStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseDump { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command.FileName == "pg_dump")
            {
                DumpStarted.TrySetResult(true);
                if (HoldDump)
                {
                    await ReleaseDump.Task.WaitAsync(cancellationToken);
                }

                if (FailDump)
                {
                    return new BackupCommandResult(1, "secret diagnostic must not be returned");
                }

                var outputIndex = command.Arguments.ToList().IndexOf("--file");
                await File.WriteAllBytesAsync(
                    command.Arguments[outputIndex + 1],
                    WriteEmptyDump ? [] : [1, 2, 3, 4],
                    cancellationToken);
                return new BackupCommandResult(0, string.Empty);
            }

            return FailVerification
                ? new BackupCommandResult(1, "invalid dump")
                : new BackupCommandResult(0, string.Empty);
        }
    }

    private sealed class CaptureAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];
        public AuditEvent? Add(AuditEventWriteRequest request)
        {
            Requests.Add(request);
            return null;
        }
    }

    private sealed class CaptureUnitOfWork : IApplicationUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureStorageCatalog : IStorageCatalog
    {
        public List<RegisterCommittedStorageObjectRequest> Registrations { get; } = [];
        public List<StorageObject> Objects { get; } = [];

        public Task<StorageCatalogRegistration> RegisterCommittedObjectAsync(RegisterCommittedStorageObjectRequest request, CancellationToken cancellationToken)
        {
            Registrations.Add(request);
            var existing = Objects.SingleOrDefault(item => item.OperationId == request.OperationId);
            if (existing is not null)
            {
                return Task.FromResult(new StorageCatalogRegistration(existing, existing.Replicas.Single(), [], true));
            }
            var storageObject = new StorageObject
            {
                OperationId = request.OperationId,
                DataClass = request.DataClass,
                LogicalKey = request.LogicalKey,
                CommittedGeneration = request.Generation,
                SizeBytes = request.SizeBytes,
                Sha256 = request.Sha256,
                PolicyId = request.PolicyId,
                PolicyRevision = request.PolicyRevision,
                State = request.ReplicationTargets.Count > 0
                    ? StorageObjectState.ProtectionPending
                    : StorageObjectState.CreatedLocal
            };
            var replica = new StorageObjectReplica
            {
                DestinationId = request.LocalDestinationId,
                FailureDomain = request.LocalFailureDomain,
                Generation = request.Generation,
                State = StorageReplicaState.Available,
                SizeBytes = request.SizeBytes,
                Sha256 = request.Sha256,
                LastVerifiedAtUtc = request.CreatedAtUtc
            };
            storageObject.Replicas.Add(replica);
            Objects.Add(storageObject);
            return Task.FromResult(new StorageCatalogRegistration(storageObject, replica, [], false));
        }

        public Task<StorageObject?> FindObjectAsync(Guid objectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageObject?> FindByLogicalKeyAsync(string tenantId, StorageDataClass dataClass, string logicalKey, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.SingleOrDefault(item => item.DataClass == dataClass && string.Equals(item.LogicalKey, logicalKey, StringComparison.Ordinal)));
        public Task<StorageTransferJob?> ClaimNextJobAsync(string leaseOwner, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageTransferContext> GetLeasedJobContextAsync(Guid jobId, string leaseOwner, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReplicationUploadingAsync(Guid jobId, string leaseOwner, string nativeLocator, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteReplicationAsync(Guid jobId, string leaseOwner, string nativeLocator, string? providerVersionId, string? providerChecksum, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleReplicationRetryAsync(Guid jobId, string leaseOwner, DateTimeOffset dueAtUtc, StorageReplicaState replicaState, string category, string safeError, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BlockReplicationAsync(Guid jobId, string leaseOwner, string category, string safeError, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteJobAsync(Guid jobId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleJobRetryAsync(Guid jobId, string leaseOwner, DateTimeOffset dueAtUtc, string category, string safeError, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageManifestEntry>> ExportManifestAsync(StorageDataClass dataClass, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LocalOnlyProviderRegistry(IStorageProvider localProvider) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) =>
            string.Equals(destinationId, localProvider.DestinationId, StringComparison.Ordinal)
                ? localProvider
                : throw new InvalidOperationException("Remote provider is intentionally not needed by the backup creation path.");

        public IReadOnlyList<IStorageProvider> GetAll() => [localProvider];
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
