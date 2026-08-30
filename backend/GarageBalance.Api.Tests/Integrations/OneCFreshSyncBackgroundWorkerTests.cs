using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Integrations;

public sealed class OneCFreshSyncBackgroundWorkerTests
{
    [Fact]
    public async Task Worker_ExecutesQueuedAdapterInItsOwnScope()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagebalance-one-c-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        ServiceProvider? provider = null;
        try
        {
            var databasePath = Path.Combine(root, "worker.db");
            var options = Options.Create(new OneCFreshSyncBackgroundOptions
            {
                Capacity = 2,
                AdapterTimeoutSeconds = 30
            });
            var adapter = new SignalingAdapter();
            var services = new ServiceCollection();
            services.AddDbContext<GarageBalanceDbContext>(builder => builder.UseSqlite($"Data Source={databasePath}"));
            services.AddScoped<IApplicationUnitOfWork, EfApplicationUnitOfWork>();
            services.AddScoped<IAuditEventStore>(sp => sp.GetRequiredService<GarageBalanceDbContext>());
            services.AddScoped<IAuditEventWriter, AuditEventWriter>();
            services.AddSingleton<IIntegrationSecretSettingsService>(new SecretSettingsService());
            services.AddSingleton<IOneCFreshSyncAdapter>(adapter);
            services.AddSingleton(options);
            services.AddSingleton<IOneCFreshSyncBackgroundQueue, OneCFreshSyncBackgroundQueue>();
            services.AddScoped<OneCFreshSyncService>();
            provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>().Database.EnsureCreatedAsync();
            }

            var queue = provider.GetRequiredService<IOneCFreshSyncBackgroundQueue>();
            Assert.True(queue.TryQueue(new OneCFreshSyncBackgroundJob(
                new OneCFreshSyncRequest("background"),
                Guid.NewGuid(),
                IsRetry: false)));
            var worker = new OneCFreshSyncBackgroundWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                queue,
                NullLogger<OneCFreshSyncBackgroundWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);
            await adapter.Called.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var completedAuditWritten = false;
            for (var attempt = 0; attempt < 50 && !completedAuditWritten; attempt++)
            {
                using var verificationScope = provider.CreateScope();
                var context = verificationScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
                completedAuditWritten = await context.AuditEvents
                    .AsNoTracking()
                    .AnyAsync(audit => audit.Action == "one_c_fresh.sync_completed");
                if (!completedAuditWritten)
                {
                    await Task.Delay(20);
                }
            }
            await worker.StopAsync(CancellationToken.None);

            Assert.True(completedAuditWritten);
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

    private sealed class SignalingAdapter : IOneCFreshSyncAdapter
    {
        public TaskCompletionSource Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OneCFreshSyncAdapterResult> StartAsync(
            OneCFreshSyncAdapterRequest request,
            CancellationToken cancellationToken)
        {
            Called.TrySetResult();
            return Task.FromResult(OneCFreshSyncAdapterResult.Started("Started", "external-1"));
        }
    }

    private sealed class SecretSettingsService : IIntegrationSecretSettingsService
    {
        public Task<IntegrationSecretSettingResult<string>> GetSecretAsync(
            string provider,
            string settingKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(IntegrationSecretSettingResult<string>.Success("secret"));

        public Task<IntegrationSecretSettingResult<IReadOnlyDictionary<string, string>>> GetSecretsAsync(
            string provider,
            IReadOnlyCollection<string> settingKeys,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(
            string? provider,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationSecretSettingDto>>([]);

        public Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
            UpsertIntegrationSecretRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
