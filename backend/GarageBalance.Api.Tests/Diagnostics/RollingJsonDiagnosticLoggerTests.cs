using System.IO.Compression;
using System.Text.Json;
using GarageBalance.Api.Application.Diagnostics;
using GarageBalance.Api.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Diagnostics;

public sealed class RollingJsonDiagnosticLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"garagebalance-diagnostics-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = new(2026, 7, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Logger_RedactsSensitiveValuesRotatesRetentionAndBuildsBoundedPackage()
    {
        Directory.CreateDirectory(_directory);
        var expired = Path.Combine(_directory, "garagebalance-errors-20260601-001.jsonl");
        var foreign = Path.Combine(_directory, "customer-notes.txt");
        await File.WriteAllTextAsync(expired, "expired");
        await File.WriteAllTextAsync(foreign, "keep");
        File.SetLastWriteTimeUtc(expired, _now.AddDays(-40).UtcDateTime);
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("GarageBalance.Tests");

        logger.LogError(
            new InvalidOperationException("raw-exception-personal-data"),
            "Failure password=secret-value token:abc123 email owner@example.org phone +7 913 111-22-33");

        var status = provider.GetStatus();
        Assert.True(status.Enabled);
        Assert.Equal(1, status.FileCount);
        Assert.True(status.TotalSizeBytes > 0);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(foreign));
        var logFile = Assert.Single(Directory.GetFiles(_directory, "garagebalance-errors-*.jsonl"));
        await using var logStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var logReader = new StreamReader(logStream);
        var log = await logReader.ReadToEndAsync();
        Assert.Contains("[redacted]", log, StringComparison.Ordinal);
        Assert.Contains("[email]", log, StringComparison.Ordinal);
        Assert.Contains("[phone]", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.org", log, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-exception-personal-data", log, StringComparison.Ordinal);

        var package = await provider.CreatePackageAsync(CancellationToken.None);

        Assert.NotNull(package);
        Assert.EndsWith(".zip", package.FileName, StringComparison.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(package.Content), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("errors/garagebalance-errors-", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.Name == "customer-notes.txt");
    }

    [Fact]
    public async Task DisabledLogger_DoesNotCreateDirectoryOrPackage()
    {
        using var provider = CreateProvider(enabled: false);

        provider.CreateLogger("Tests").LogError("Must not be written");

        Assert.False(Directory.Exists(_directory));
        Assert.False(provider.GetStatus().Enabled);
        Assert.Null(await provider.CreatePackageAsync(CancellationToken.None));
    }

    [Fact]
    public void Sanitizer_TruncatesOversizedDiagnosticsAndKeepsSafeText()
    {
        var sanitized = DiagnosticLogSanitizer.Sanitize("safe " + new string('x', 25_000));

        Assert.StartsWith("safe ", sanitized, StringComparison.Ordinal);
        Assert.EndsWith("…[truncated]", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length < 21_000);
    }

    private RollingJsonDiagnosticLoggerProvider CreateProvider(bool enabled = true) => new(
        Options.Create(new DiagnosticLoggingOptions
        {
            Enabled = enabled,
            Directory = _directory,
            RetentionDays = 14,
            MaxFileSizeMb = 1,
            PackageDays = 7,
            PackageMaxSizeMb = 2
        }),
        new FixedTimeProvider(_now));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
