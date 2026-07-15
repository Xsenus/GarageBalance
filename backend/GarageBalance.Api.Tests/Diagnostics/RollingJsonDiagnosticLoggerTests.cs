using System.IO.Compression;
using System.Text;
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
            "Ошибка оплаты password=secret-value token:abc123 email owner@example.org phone +7 913 111-22-33");

        var status = provider.GetStatus();
        Assert.True(status.Enabled);
        Assert.Equal(1, status.FileCount);
        Assert.True(status.TotalSizeBytes > 0);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(foreign));
        var logFile = Assert.Single(Directory.GetFiles(_directory, "garagebalance-errors-*.jsonl"));
        await using var activeLogStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var activeLogBuffer = new MemoryStream();
        await activeLogStream.CopyToAsync(activeLogBuffer);
        var logBytes = activeLogBuffer.ToArray();
        Assert.False(logBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var log = new UTF8Encoding(false, true).GetString(logBytes);
        Assert.Contains("Ошибка оплаты", log, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u041e", log, StringComparison.OrdinalIgnoreCase);
        using var parsedLog = JsonDocument.Parse(log.Trim());
        Assert.Equal("Ошибка оплаты password=[redacted] token=[redacted] email [email] phone [phone]", parsedLog.RootElement.GetProperty("message").GetString());
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
        var manifestEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("manifest.json"));
        var manifestText = await ReadStrictUtf8Async(manifestEntry);
        Assert.Contains("Пакет содержит только обезличенные журналы ошибок", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u041f", manifestText, StringComparison.OrdinalIgnoreCase);
        using var manifestJson = JsonDocument.Parse(manifestText);
        Assert.Equal("utf-8", manifestJson.RootElement.GetProperty("encoding").GetString());
        var archivedLogEntry = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("errors/garagebalance-errors-", StringComparison.Ordinal));
        var archivedLog = await ReadStrictUtf8Async(archivedLogEntry);
        Assert.Contains("Ошибка оплаты", archivedLog, StringComparison.Ordinal);
        Assert.DoesNotContain(archive.Entries, entry => entry.Name == "customer-notes.txt");
    }

    [Fact]
    public async Task Logger_ReportsStorageFailureWithoutThrowingOrBreakingStatusEndpoint()
    {
        Directory.CreateDirectory(_directory);
        var blockedPath = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(blockedPath, "blocked", Encoding.UTF8);
        using var provider = CreateProvider(directory: blockedPath);
        var logger = provider.CreateLogger("GarageBalance.Tests");

        var exception = Record.Exception(() => logger.LogError("Ошибка записи журнала"));
        var status = provider.GetStatus();

        Assert.Null(exception);
        Assert.True(status.Enabled);
        Assert.Equal(0, status.FileCount);
        Assert.Contains("Не удалось записать диагностический журнал", status.LastWriteError, StringComparison.Ordinal);
        var package = await provider.CreatePackageAsync(CancellationToken.None);
        Assert.NotNull(package);
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

    private RollingJsonDiagnosticLoggerProvider CreateProvider(bool enabled = true, string? directory = null) => new(
        Options.Create(new DiagnosticLoggingOptions
        {
            Enabled = enabled,
            Directory = directory ?? _directory,
            RetentionDays = 14,
            MaxFileSizeMb = 1,
            PackageDays = 7,
            PackageMaxSizeMb = 2
        }),
        new FixedTimeProvider(_now));

    private static async Task<string> ReadStrictUtf8Async(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        return new UTF8Encoding(false, true).GetString(bytes);
    }

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
