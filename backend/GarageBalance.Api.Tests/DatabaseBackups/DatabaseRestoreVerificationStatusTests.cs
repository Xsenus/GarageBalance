using System.Text.Json;
using GarageBalance.Api.Infrastructure.Backups;
using Microsoft.Extensions.Configuration;

namespace GarageBalance.Api.Tests.Backups;

public sealed class DatabaseRestoreVerificationStatusTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gb-restore-status-{Guid.NewGuid():N}.json");
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingConfigurationAndFileDoNotClaimVerified()
    {
        Assert.Equal("not_configured", DatabaseRestoreVerificationStatus.Read(new ConfigurationBuilder().Build(), Now).State);
        Assert.Equal("not_run", Read().State);
    }

    [Theory]
    [InlineData(1, true, null, "verified")]
    [InlineData(169, true, null, "stale")]
    [InlineData(1, false, null, "failed")]
    [InlineData(0, false, "in_progress", "running")]
    [InlineData(1, false, "in_progress", "stale")]
    [InlineData(169, false, "in_progress", "stale")]
    public void ReportsVerificationFailureRunningAndAge(int ageHours, bool success, string? error, string state)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            startedAtUtc = Now.AddHours(-ageHours).AddMinutes(-2),
            completedAtUtc = Now.AddHours(-ageHours),
            succeeded = success,
            backupCreatedAtUtc = Now.AddHours(-ageHours - 1),
            rtoSeconds = 120,
            errorCode = error,
            internalSecret = "must-not-appear"
        }));
        var result = Read();
        Assert.Equal(state, result.State);
        Assert.DoesNotContain("must-not-appear", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(1800, 1860, "running")]
    [InlineData(1800, 1861, "stale")]
    [InlineData(300, 361, "stale")]
    public void RunningVerificationExpiresAfterItsDeadlineAndCleanupGrace(int deadline, int elapsed, string expected)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            startedAtUtc = Now.AddSeconds(-elapsed),
            errorCode = "in_progress"
        }));
        Assert.Equal(expected, Read(deadline).State);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"startedAtUtc\":\"2099-01-01T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":1,\"startedAtUtc\":\"2026-09-22T00:00:00Z\",\"succeeded\":true}")]
    public void RejectsMalformedIncompleteAndFutureReports(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Equal("invalid", Read().State);
    }

    [Fact]
    public void RejectsOversizedReport()
    {
        File.WriteAllText(_path, new string(' ', 65537));
        Assert.Equal("invalid", Read().State);
    }

    private GarageBalance.Api.Application.Backups.DatabaseRestoreVerificationDto Read(int deadline = 1800) => DatabaseRestoreVerificationStatus.Read(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Storage:Recovery:VerificationReportPath"] = _path, ["Storage:Recovery:MaximumDrillSeconds"] = deadline.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build(), Now);

    public void Dispose() => File.Delete(_path);
}
