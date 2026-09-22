using System.Text;
using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Import;

public sealed class ImportDryRunQueueTests
{
    [Fact]
    public void DefaultOptions_UseSharedFiftyMegabyteFileLimit()
    {
        var options = new ImportDryRunQueueOptions();

        Assert.Equal(50, options.MaximumFileSizeMegabytes);
        Assert.Equal(50L * 1024L * 1024L, ImportFileLimits.MaximumFileSizeBytes);
        Assert.Equal(512, options.MaximumWorkDirectorySizeMegabytes);
    }

    [Fact]
    public void OptionsValidation_RejectsADeploymentLimitThatWouldDriftFromClientContract()
    {
        var options = new ImportDryRunQueueOptions { MaximumFileSizeMegabytes = 49 };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(options.MaximumFileSizeMegabytes)));
    }

    [Fact]
    public async Task Dispatcher_RejectsUploadWhenManagedWorkDirectoryHasReachedItsBound()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagebalance-import-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = Options.Create(new ImportDryRunQueueOptions
            {
                WorkDirectory = root,
                MaximumFileSizeMegabytes = 50,
                MaximumWorkDirectorySizeMegabytes = 50
            });
            var occupiedPath = ImportDryRunWorkFiles.GetPath(options.Value, Guid.NewGuid());
            await using (var occupied = File.Create(occupiedPath))
            {
                occupied.SetLength(50L * 1024L * 1024L);
            }
            var dispatcher = new ImportDryRunDispatcher(
                null!,
                new ImportDryRunQueue(options),
                options);
            await using var upload = CreateAccessLikeStream("new import");

            var result = await dispatcher.QueueAsync(
                "new.accdb",
                upload,
                upload.Length,
                null,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal("import_staging_capacity_exceeded", result.ErrorCode);
            Assert.Single(Directory.EnumerateFiles(root, "*.pending"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Dispatcher_ReturnsQueuedRunBeforeWorkerProcessesAndWorkerCleansStagedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagebalance-import-queue-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "queue.db");
        Directory.CreateDirectory(root);
        ServiceProvider? provider = null;
        try
        {
            var options = Options.Create(new ImportDryRunQueueOptions
            {
                Capacity = 2,
                WorkDirectory = root,
                MaximumFileSizeMegabytes = 1
            });
            provider = BuildProvider(databasePath, options);
            await EnsureDatabaseAsync(provider);
            using var scope = provider.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IImportDryRunDispatcher>();
            await using var content = CreateAccessLikeStream("garage owner");

            var queued = await dispatcher.QueueAsync(
                "archive.accdb",
                content,
                content.Length,
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.True(queued.Succeeded);
            Assert.Equal("queued", queued.Value!.Status);
            var queue = provider.GetRequiredService<IImportDryRunQueue>();
            var job = await queue.DequeueAsync(CancellationToken.None);
            var stagedPath = ImportDryRunWorkFiles.GetPath(options.Value, job.RunId);
            Assert.True(File.Exists(stagedPath));

            var worker = new ImportDryRunWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                queue,
                options,
                NullLogger<ImportDryRunWorker>.Instance);
            await worker.ProcessAsync(job, CancellationToken.None);

            using var verificationScope = provider.CreateScope();
            var context = verificationScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
            var stored = await context.AccessImportRuns.AsNoTracking().SingleAsync(run => run.Id == job.RunId);
            Assert.Equal("completed", stored.Status);
            Assert.False(File.Exists(stagedPath));
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Worker_RecoversPersistedQueuedRunAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagebalance-import-recovery-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "queue.db");
        Directory.CreateDirectory(root);
        ServiceProvider? provider = null;
        try
        {
            var options = Options.Create(new ImportDryRunQueueOptions
            {
                Capacity = 2,
                WorkDirectory = root,
                MaximumFileSizeMegabytes = 1
            });
            provider = BuildProvider(databasePath, options);
            await EnsureDatabaseAsync(provider);
            var runId = Guid.NewGuid();
            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IImportService>();
                await service.CreateQueuedDryRunAsync(
                    new QueuedAccessImportDryRunRequest(runId, "restart.mdb", 12, null),
                    CancellationToken.None);
            }
            await File.WriteAllBytesAsync(
                ImportDryRunWorkFiles.GetPath(options.Value, runId),
                CreateAccessLikeStream("payment").ToArray());

            var queue = provider.GetRequiredService<IImportDryRunQueue>();
            var worker = new ImportDryRunWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                queue,
                options,
                NullLogger<ImportDryRunWorker>.Instance);
            await worker.RecoverQueuedJobsAsync(CancellationToken.None);

            Assert.Equal(runId, (await queue.DequeueAsync(CancellationToken.None)).RunId);
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Worker_ReportsTransientQueueRecoveryFailureWithoutTerminating()
    {
        var options = Options.Create(new ImportDryRunQueueOptions());
        var worker = new ImportDryRunWorker(
            new ThrowingScopeFactory(new InvalidOperationException("Database is temporarily unavailable.")),
            new ImportDryRunQueue(options),
            options,
            NullLogger<ImportDryRunWorker>.Instance);

        var recovered = await worker.TryRecoverQueuedJobsAsync(CancellationToken.None);

        Assert.False(recovered);
    }

    [Fact]
    public async Task Worker_PropagatesQueueRecoveryCancellationDuringShutdown()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = Options.Create(new ImportDryRunQueueOptions());
        var worker = new ImportDryRunWorker(
            new ThrowingScopeFactory(new OperationCanceledException(cancellation.Token)),
            new ImportDryRunQueue(options),
            options,
            NullLogger<ImportDryRunWorker>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.TryRecoverQueuedJobsAsync(cancellation.Token));
    }

    [Fact]
    public async Task Worker_ReportsUnexpectedQueuedJobFailureWithoutTerminating()
    {
        var options = Options.Create(new ImportDryRunQueueOptions());
        var worker = new ImportDryRunWorker(
            new ThrowingScopeFactory(new InvalidOperationException("Service scope is temporarily unavailable.")),
            new ImportDryRunQueue(options),
            options,
            NullLogger<ImportDryRunWorker>.Instance);

        var processed = await worker.TryProcessAsync(new ImportDryRunJob(Guid.NewGuid()), CancellationToken.None);

        Assert.False(processed);
    }

    [Fact]
    public async Task Worker_PropagatesQueuedJobCancellationDuringShutdown()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = Options.Create(new ImportDryRunQueueOptions());
        var worker = new ImportDryRunWorker(
            new ThrowingScopeFactory(new OperationCanceledException(cancellation.Token)),
            new ImportDryRunQueue(options),
            options,
            NullLogger<ImportDryRunWorker>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.TryProcessAsync(new ImportDryRunJob(Guid.NewGuid()), cancellation.Token));
    }

    [Fact]
    public async Task OrphanSweeper_QuarantinesUnknownPendingFile_DeletesExpiredQuarantine_AndPreservesQueuedRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagebalance-import-orphans-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "queue.db");
        Directory.CreateDirectory(root);
        ServiceProvider? provider = null;
        try
        {
            var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
            var options = Options.Create(new ImportDryRunQueueOptions
            {
                WorkDirectory = root,
                MaximumFileSizeMegabytes = 50,
                OrphanRetentionHours = 24
            });
            provider = BuildProvider(databasePath, options);
            await EnsureDatabaseAsync(provider);

            var unknownRunId = Guid.NewGuid();
            var quarantinedRunId = Guid.NewGuid();
            var queuedRunId = Guid.NewGuid();
            var unknownPath = ImportDryRunWorkFiles.GetPath(options.Value, unknownRunId);
            var quarantinePath = Path.Combine(root, $"{quarantinedRunId:N}.orphan");
            var queuedPath = ImportDryRunWorkFiles.GetPath(options.Value, queuedRunId);
            await File.WriteAllTextAsync(unknownPath, "unknown");
            await File.WriteAllTextAsync(quarantinePath, "expired");
            await File.WriteAllTextAsync(queuedPath, "queued");
            var oldTimestamp = now.AddHours(-25).UtcDateTime;
            File.SetLastWriteTimeUtc(unknownPath, oldTimestamp);
            File.SetLastWriteTimeUtc(quarantinePath, oldTimestamp);
            File.SetLastWriteTimeUtc(queuedPath, oldTimestamp);

            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IImportService>();
                var result = await service.CreateQueuedDryRunAsync(
                    new QueuedAccessImportDryRunRequest(queuedRunId, "queued.accdb", 6, null),
                    CancellationToken.None);
                Assert.True(result.Succeeded);
            }

            var sweeper = new ImportDryRunOrphanSweeper(
                provider.GetRequiredService<IServiceScopeFactory>(),
                options,
                new FixedTimeProvider(now),
                NullLogger<ImportDryRunOrphanSweeper>.Instance);

            var changed = await sweeper.SweepOnceAsync(CancellationToken.None);

            Assert.Equal(2, changed);
            Assert.False(File.Exists(unknownPath));
            var newlyQuarantinedPath = Path.Combine(root, $"{unknownRunId:N}.orphan");
            Assert.True(File.Exists(newlyQuarantinedPath));
            Assert.Equal(now.UtcDateTime, File.GetLastWriteTimeUtc(newlyQuarantinedPath), TimeSpan.FromSeconds(2));
            Assert.False(File.Exists(quarantinePath));
            Assert.True(File.Exists(queuedPath));

            var immediateSecondSweep = await sweeper.SweepOnceAsync(CancellationToken.None);
            Assert.Equal(0, immediateSecondSweep);
            Assert.True(File.Exists(newlyQuarantinedPath));
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ServiceProvider BuildProvider(string databasePath, IOptions<ImportDryRunQueueOptions> options)
    {
        var services = new ServiceCollection();
        services.AddDbContext<GarageBalanceDbContext>(builder => builder.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IImportRepository, EfImportRepository>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IAccessImportReader, FakeAccessImportReader>();
        services.AddScoped<IAuditEventStore>(provider => provider.GetRequiredService<GarageBalanceDbContext>());
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddSingleton(options);
        services.AddSingleton<IImportDryRunQueue, ImportDryRunQueue>();
        services.AddScoped<IImportDryRunDispatcher, ImportDryRunDispatcher>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>().Database.EnsureCreatedAsync();
    }

    private static MemoryStream CreateAccessLikeStream(string text)
    {
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        return new MemoryStream([.. signature, .. Encoding.UTF8.GetBytes(text)]);
    }

    private sealed class FakeAccessImportReader : IAccessImportReader
    {
        public Task<AccessImportReaderStatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AccessImportReaderStatusDto(
                "test",
                "Test",
                true,
                "ready",
                "Ready",
                [],
                DateTimeOffset.UtcNow));
    }

    private sealed class ThrowingScopeFactory(Exception exception) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw exception;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
