using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Backups;

public sealed class DatabaseStartupHostedServiceTests
{
    [Fact]
    public async Task StartAndStop_DoNotCreateDatabaseScopeWhenStartupMigrationsAreDisabled()
    {
        var scopeFactory = new ThrowingScopeFactory();
        var service = new DatabaseStartupHostedService(
            scopeFactory,
            Options.Create(new DatabaseStartupOptions { ApplyMigrationsOnStartup = false }),
            NullLogger<DatabaseStartupHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopeFactory.CreateScopeCount);
    }

    [PostgreSqlFact]
    public async Task Start_MigratesEmptyDatabaseWithoutPreMigrationBackup()
    {
        await using var database = await PostgreSqlTestDatabase.CreateEmptyAsync();
        var backup = new RecordingBackupService
        {
            CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure("must_not_run", "Empty database")
        };
        await using var provider = CreateProvider(database, backup);
        var service = CreateService(provider);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, backup.CreateCount);
        await using var context = database.CreateContext();
        Assert.Equal(
            context.Database.GetMigrations().Count(),
            (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [PostgreSqlFact]
    public async Task Start_RequiresSuccessfulBackupBeforeMigratingExistingDatabase()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260921033539_AddOwnerAdditionalPhones");
        var backup = new RecordingBackupService
        {
            CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure("backup_failed", "Failure")
        };
        await using var provider = CreateProvider(database, backup);
        var service = CreateService(provider);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));
        Assert.Contains("backup_failed", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, backup.CreateCount);
        Assert.Equal(DatabaseBackupKind.PreUpdate, backup.LastKind);
        await using (var before = database.CreateContext())
        {
            Assert.Single(await before.Database.GetPendingMigrationsAsync());
        }

        backup.CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Success(
            new DatabaseBackupFileDto("pre-migration.pgdump", 1, DateTimeOffset.UtcNow, "pre_update"));
        await service.StartAsync(CancellationToken.None);

        Assert.Equal(2, backup.CreateCount);
        await using var after = database.CreateContext();
        Assert.Empty(await after.Database.GetPendingMigrationsAsync());
    }

    private static ServiceProvider CreateProvider(
        PostgreSqlTestDatabase database,
        IDatabaseBackupService backup)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext());
        services.AddSingleton(backup);
        return services.BuildServiceProvider();
    }

    private static DatabaseStartupHostedService CreateService(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DatabaseStartupOptions
            {
                ApplyMigrationsOnStartup = true,
                RequirePreMigrationBackup = true
            }),
            NullLogger<DatabaseStartupHostedService>.Instance);

    private sealed class RecordingBackupService : IDatabaseBackupService
    {
        public DatabaseBackupResult<DatabaseBackupFileDto> CreateResult { get; set; } =
            DatabaseBackupResult<DatabaseBackupFileDto>.Failure("not_configured", "Not configured");
        public int CreateCount { get; private set; }
        public DatabaseBackupKind? LastKind { get; private set; }

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(
            DatabaseBackupKind kind,
            string? reason,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            LastKind = kind;
            return Task.FromResult(CreateResult);
        }

        public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(
            string fileName,
            Guid? actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(
            string fileName,
            string? reason,
            Guid? actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCount++;
            throw new InvalidOperationException("A database scope must not be created when startup migrations are disabled.");
        }
    }
}
