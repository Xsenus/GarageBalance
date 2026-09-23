using System.Text.Json;
using GarageBalance.Api.Application.Backups;

namespace GarageBalance.Api.Infrastructure.Backups;

public static class DatabaseRestoreVerificationStatus
{
    public static DatabaseRestoreVerificationDto Read(IConfiguration configuration, DateTimeOffset now)
    {
        var maximumAge = Math.Clamp(configuration.GetValue("Storage:Recovery:MaximumVerificationAgeHours", 168), 1, 8760);
        var path = configuration["Storage:Recovery:VerificationReportPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            return new("not_configured", null, null, null, maximumAge);
        }
        try
        {
            if (!File.Exists(path))
            {
                return new("not_run", null, null, null, maximumAge);
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is 0 or > 65536)
            {
                return new("invalid", null, null, null, maximumAge);
            }
            var report = JsonSerializer.Deserialize<VerificationReport>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (report?.SchemaVersion != 1 || report.StartedAtUtc > now.AddMinutes(5) || report.StartedAtUtc == default ||
                report.CompletedAtUtc > now.AddMinutes(5) || report.BackupCreatedAtUtc > now.AddMinutes(5) ||
                report.RtoSeconds is < 0 || (report.RtoSeconds.HasValue && !double.IsFinite(report.RtoSeconds.Value)))
            {
                return new("invalid", null, null, null, maximumAge);
            }
            if (report.ErrorCode == "in_progress")
            {
                // A crashed verifier must not look active for the entire weekly verification interval.
                var maximumDrillSeconds = Math.Clamp(configuration.GetValue("Storage:Recovery:MaximumDrillSeconds", 1800), 30, 7200);
                return new(now - report.StartedAtUtc > TimeSpan.FromSeconds(maximumDrillSeconds + 60) ? "stale" : "running", null, null, null, maximumAge);
            }
            if (report.CompletedAtUtc is null || report.CompletedAtUtc < report.StartedAtUtc ||
                (report.Succeeded && (report.BackupCreatedAtUtc is null || report.RtoSeconds is null)))
            {
                return new("invalid", null, null, null, maximumAge);
            }
            var state = !report.Succeeded ? "failed"
                : now - report.CompletedAtUtc > TimeSpan.FromHours(maximumAge) ? "stale" : "verified";
            return new(state, report.CompletedAtUtc, report.BackupCreatedAtUtc, report.RtoSeconds, maximumAge);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new("invalid", null, null, null, maximumAge);
        }
    }

    private sealed record VerificationReport(int SchemaVersion, DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc, bool Succeeded, DateTimeOffset? BackupCreatedAtUtc,
        double? RtoSeconds, string? ErrorCode);
}
