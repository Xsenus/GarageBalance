using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class StagingDatabaseResetServiceTests
{
    [Fact]
    public async Task Reset_RefusesDisabledConfigurationBeforeBackup()
    {
        var backup = new FakeBackupService();
        await using var context = CreateContext();
        var service = CreateService(context, backup, enabled: false);

        var result = await service.ResetAsync(ValidRequest(), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("database_reset_disabled", result.ErrorCode);
        Assert.Equal(0, backup.CreateCallCount);
    }

    [Theory]
    [InlineData("garagebalance", "reset-password", "ОЧИСТИТЬ БАЗУ", "Причина", "database_reset_wrong_target")]
    [InlineData("garagebalance_staging", "wrong", "ОЧИСТИТЬ БАЗУ", "Причина", "database_reset_password_invalid")]
    [InlineData("garagebalance_staging", "reset-password", "очистить базу", "Причина", "database_reset_confirmation_invalid")]
    [InlineData("garagebalance_staging", "reset-password", "ОЧИСТИТЬ БАЗУ", "", "database_reset_reason_invalid")]
    public async Task Reset_RejectsUnsafeRequestBeforeBackup(
        string database,
        string password,
        string confirmation,
        string reason,
        string expectedError)
    {
        var backup = new FakeBackupService();
        await using var context = CreateContext();
        var service = CreateService(context, backup, database: database);

        var result = await service.ResetAsync(
            new StagingDatabaseResetRequest(password, confirmation, reason),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Equal(0, backup.CreateCallCount);
    }

    [Fact]
    public async Task Reset_StopsWhenVerifiedBackupCannotBeCreated()
    {
        var backup = new FakeBackupService
        {
            CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure(
                "database_backup_dump_failed",
                "Копия не создана.")
        };
        await using var context = CreateContext();
        var service = CreateService(context, backup);

        var result = await service.ResetAsync(ValidRequest(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("database_reset_backup_failed", result.ErrorCode);
        Assert.Equal("Копия не создана.", result.ErrorMessage);
        Assert.Equal(1, backup.CreateCallCount);
        Assert.Equal(DatabaseBackupKind.PreUpdate, backup.ReceivedKind);
        Assert.Equal("Подготовка чистой базы", backup.ReceivedReason);
    }

    private static StagingDatabaseResetRequest ValidRequest() =>
        new("reset-password", "ОЧИСТИТЬ БАЗУ", "Подготовка чистой базы");

    private static GarageBalanceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new GarageBalanceDbContext(options);
    }

    private static StagingDatabaseResetService CreateService(
        GarageBalanceDbContext context,
        FakeBackupService backup,
        bool enabled = true,
        string database = "garagebalance_staging")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Host=localhost;Database={database};Username=test"
            })
            .Build();
        return new StagingDatabaseResetService(
            context,
            backup,
            configuration,
            Options.Create(new StagingDatabaseResetOptions { Enabled = enabled, Password = "reset-password" }),
            NullLogger<StagingDatabaseResetService>.Instance);
    }

    private sealed class FakeBackupService : IDatabaseBackupService
    {
        public DatabaseBackupResult<DatabaseBackupFileDto>? CreateResult { get; set; }
        public int CreateCallCount { get; private set; }
        public DatabaseBackupKind? ReceivedKind { get; private set; }
        public string? ReceivedReason { get; private set; }

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(
            DatabaseBackupKind kind,
            string? reason,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            ReceivedKind = kind;
            ReceivedReason = reason;
            return Task.FromResult(CreateResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(
                new DatabaseBackupFileDto("garagebalance_pre_update_20260904_120000_000.pgdump", 1024, DateTimeOffset.UtcNow, "pre_update")));
        }

        public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(string fileName, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(string fileName, string? reason, Guid? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
