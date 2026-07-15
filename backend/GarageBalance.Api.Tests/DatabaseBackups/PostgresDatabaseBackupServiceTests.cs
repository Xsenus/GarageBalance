using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Backups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        Assert.True(File.Exists(Path.Combine(_directory, result.Value.FileName)));
        Assert.False(File.Exists(Path.Combine(_directory, result.Value.FileName + ".tmp")));
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
    public async Task GetStatus_UsesPersistentOperatingSystemDirectoryForAutoMode()
    {
        var service = CreateService(new FakeCommandRunner(), directory: "auto");

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(Path.IsPathFullyQualified(status.Directory));
        Assert.EndsWith(Path.Combine("GarageBalance", "backups"), status.Directory, StringComparison.OrdinalIgnoreCase);
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

    private PostgresDatabaseBackupService CreateService(
        FakeCommandRunner runner,
        CaptureAuditWriter? audit = null,
        CaptureUnitOfWork? unitOfWork = null,
        bool enabled = true,
        int retentionCount = 30,
        string? directory = null,
        IBackupToolLocator? toolLocator = null)
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
            NullLogger<PostgresDatabaseBackupService>.Instance);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
