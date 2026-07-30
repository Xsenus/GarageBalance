using System.Text;
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
}
