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
    public async Task RunIfDue_SkipsBackupAlreadyCreatedOnTheSameLocalCalendarDay()
    {
        var service = new FakeBackupService(CreateStatus([
            new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, Now.AddHours(-1), "automatic")
        ]));
        var runner = CreateRunner(service);

        var created = await runner.RunIfDueAsync(CancellationToken.None);

        Assert.False(created);
        Assert.Null(service.ReceivedKind);
    }

    [Fact]
    public async Task RunIfDue_DoesNotMissNextWindowWhenPreviousBackupWasLate()
    {
        var currentWindow = new DateTimeOffset(2026, 7, 16, 4, 3, 0, TimeSpan.Zero);
        var previousWindow = new DateTimeOffset(2026, 7, 15, 4, 52, 0, TimeSpan.Zero);
        var service = new FakeBackupService(CreateStatus([
            new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, previousWindow, "automatic")
        ]));
        var runner = new DatabaseBackupAutomationRunner(
            service,
            Options.Create(new DatabaseBackupOptions
            {
                Enabled = true,
                AutomaticEnabled = true,
                IntervalHours = 24,
                AutomaticWindowStartHour = 2,
                AutomaticWindowEndHour = 5,
                AutomaticWindowTimeZoneId = "UTC"
            }),
            new FixedTimeProvider(currentWindow),
            NullLogger<DatabaseBackupAutomationRunner>.Instance);

        Assert.True(await runner.RunIfDueAsync(CancellationToken.None));
        Assert.Equal(DatabaseBackupKind.Automatic, service.ReceivedKind);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task RunIfDue_ConvertsLongIntervalsToCalendarWindowDays(int elapsedDays, bool expectedCreated)
    {
        var now = new DateTimeOffset(2026, 7, 16, 3, 0, 0, TimeSpan.Zero);
        var service = new FakeBackupService(CreateStatus([
            new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, now.AddDays(-elapsedDays), "automatic")
        ]));
        var runner = new DatabaseBackupAutomationRunner(
            service,
            Options.Create(new DatabaseBackupOptions
            {
                Enabled = true,
                AutomaticEnabled = true,
                IntervalHours = 48,
                AutomaticWindowStartHour = 2,
                AutomaticWindowEndHour = 5,
                AutomaticWindowTimeZoneId = "UTC"
            }),
            new FixedTimeProvider(now),
            NullLogger<DatabaseBackupAutomationRunner>.Instance);

        Assert.Equal(expectedCreated, await runner.RunIfDueAsync(CancellationToken.None));
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

    [Fact]
    public async Task RunIfDue_DoesNotCompeteWithWorkingRequestsOutsideAutomaticWindow()
    {
        var service = new FakeBackupService(CreateStatus([])) { ThrowOnStatusRead = true };
        var runner = new DatabaseBackupAutomationRunner(
            service,
            Options.Create(new DatabaseBackupOptions
            {
                Enabled = true,
                AutomaticEnabled = true,
                IntervalHours = 24,
                AutomaticWindowStartHour = 2,
                AutomaticWindowEndHour = 5,
                AutomaticWindowTimeZoneId = "UTC"
            }),
            new FixedTimeProvider(Now),
            NullLogger<DatabaseBackupAutomationRunner>.Instance);

        Assert.False(await runner.RunIfDueAsync(CancellationToken.None));
        Assert.Equal(0, service.StatusReadCount);
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
            Options.Create(new DatabaseBackupOptions
            {
                Enabled = true,
                AutomaticEnabled = true,
                IntervalHours = 24,
                AutomaticWindowStartHour = 0,
                AutomaticWindowEndHour = 24,
                AutomaticWindowTimeZoneId = "UTC"
            }),
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

        public Task<DateTimeOffset?> GetLastSuccessfulAutomaticBackupAtUtcAsync(CancellationToken cancellationToken)
        {
            StatusReadCount++;
            return ThrowOnStatusRead
                ? throw new InvalidOperationException("Status must not be read while automation is disabled.")
                : Task.FromResult(status.Backups
                    .Where(backup => backup.Kind == "automatic")
                    .MaxBy(backup => backup.CreatedAtUtc)
                    ?.CreatedAtUtc);
        }

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> CreateAsync(DatabaseBackupKind kind, string? reason, Guid? actorUserId, CancellationToken cancellationToken)
        {
            ReceivedKind = kind;
            return Task.FromResult(CreateResult ?? DatabaseBackupResult<DatabaseBackupFileDto>.Success(
                new DatabaseBackupFileDto("garagebalance_automatic.pgdump", 1, Now, "automatic")));
        }

        public Task<DatabaseBackupResult<DatabaseBackupDownloadDto>> OpenDownloadAsync(string fileName, Guid? actorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(DatabaseBackupResult<DatabaseBackupDownloadDto>.Failure("not_supported", "Not supported."));

        public Task<DatabaseBackupResult<DatabaseBackupFileDto>> DeleteAsync(string fileName, string? reason, Guid? actorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(DatabaseBackupResult<DatabaseBackupFileDto>.Failure("not_supported", "Not supported."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
