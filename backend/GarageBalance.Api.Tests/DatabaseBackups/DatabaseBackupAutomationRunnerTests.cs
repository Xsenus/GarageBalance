using GarageBalance.Api.Application.Backups;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Backups;

public sealed class DatabaseBackupAutomationRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunIfDue_CreatesAutomaticBackupWhenNoRecentAutomaticCopyExists()
    {
        var service = new FakeBackupService(CreateStatus([
            new DatabaseBackupFileDto("garagebalance_manual.pgdump", 1, Now.AddMinutes(-5), "manual")
        ]));
        var runner = CreateRunner(service);

        var created = await runner.RunIfDueAsync(CancellationToken.None);

        Assert.True(created);
        Assert.Equal(DatabaseBackupKind.Automatic, service.ReceivedKind);
    }

    [Fact]
    public async Task RunIfDue_SkipsBackupUntilConfiguredIntervalHasElapsed()
    {
        var service = new FakeBackupService(CreateStatus([
            new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, Now.AddHours(-23), "automatic")
        ]));
        var runner = CreateRunner(service);

        var created = await runner.RunIfDueAsync(CancellationToken.None);

        Assert.False(created);
        Assert.Null(service.ReceivedKind);
    }

    [Fact]
    public async Task RunIfDue_ReportsFalseWhenBackupServiceRejectsAutomaticRun()
    {
        var service = new FakeBackupService(CreateStatus([]))
        {
            CreateResult = DatabaseBackupResult<DatabaseBackupFileDto>.Failure("database_backup_in_progress", "Busy")
        };
        var runner = CreateRunner(service);

        Assert.False(await runner.RunIfDueAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RunIfDue_DoesNotReadDatabaseWhenBackupOrAutomationIsDisabled(bool enabled, bool automaticEnabled)
    {
        var service = new FakeBackupService(CreateStatus([])) { ThrowOnStatusRead = true };
        var runner = new DatabaseBackupAutomationRunner(
            service,
            Options.Create(new DatabaseBackupOptions { Enabled = enabled, AutomaticEnabled = automaticEnabled, IntervalHours = 24 }),
            new FixedTimeProvider(Now),
            NullLogger<DatabaseBackupAutomationRunner>.Instance);

        Assert.False(await runner.RunIfDueAsync(CancellationToken.None));
        Assert.Equal(0, service.StatusReadCount);
    }

    private static DatabaseBackupAutomationRunner CreateRunner(FakeBackupService service)
    {
        return new DatabaseBackupAutomationRunner(
            service,
            Options.Create(new DatabaseBackupOptions { Enabled = true, AutomaticEnabled = true, IntervalHours = 24 }),
            new FixedTimeProvider(Now),
            NullLogger<DatabaseBackupAutomationRunner>.Instance);
    }

    private static DatabaseBackupStatusDto CreateStatus(IReadOnlyList<DatabaseBackupFileDto> backups) =>
        new(true, true, 24, 30, "/backups", false, backups.FirstOrDefault()?.CreatedAtUtc, null, backups);

    private sealed class FakeBackupService(DatabaseBackupStatusDto status) : IDatabaseBackupService
    {
        public DatabaseBackupKind? ReceivedKind { get; private set; }
        public DatabaseBackupResult<DatabaseBackupFileDto>? CreateResult { get; init; }
        public bool ThrowOnStatusRead { get; init; }
        public int StatusReadCount { get; private set; }

        public Task<DatabaseBackupStatusDto> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusReadCount++;
            return ThrowOnStatusRead
                ? throw new InvalidOperationException("Status must not be read while automation is disabled.")
                : Task.FromResult(status);
        }

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(DatabaseBackupKind kind, string? reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedKind = kind;
            return Task.FromResult(CreateResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(
                new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, Now, "automatic")));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
