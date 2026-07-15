using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GarageBalance.Api.Application.Diagnostics;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Diagnostics;

public sealed class RollingJsonDiagnosticLoggerProvider : ILoggerProvider, IDiagnosticLogStore
{
    private const string FilePrefix = "garagebalance-errors-";
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    private readonly object _gate = new();
    private readonly DiagnosticLoggingOptions _options;
    private readonly TimeProvider _timeProvider;
    private StreamWriter? _writer;
    private string? _currentPath;
    private DateOnly _currentDate;
    private int _currentSequence;
    private string? _lastWriteError;

    public RollingJsonDiagnosticLoggerProvider(IOptions<DiagnosticLoggingOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticFileLogger(this, categoryName);

    public DiagnosticLogStatusDto GetStatus()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Flush();
                var files = EnumerateManagedFiles().ToArray();
                return CreateStatus(
                    files.Length,
                    files.Sum(file => file.Length),
                    files.FirstOrDefault()?.LastWriteTimeUtc is { } lastWrite
                        ? new DateTimeOffset(lastWrite, TimeSpan.Zero)
                        : null);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                SetStorageError("прочитать состояние", exception);
                return CreateStatus(0, 0, null);
            }
        }
    }

    public Task<DiagnosticPackage?> CreatePackageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            return Task.FromResult<DiagnosticPackage?>(null);
        }

        lock (_gate)
        {
            try
            {
                _writer?.Flush();
                var now = _timeProvider.GetUtcNow();
                var cutoff = now.AddDays(-_options.PackageDays);
                var maximumBytes = _options.PackageMaxSizeMb * 1024L * 1024L;
                var selected = new List<FileInfo>();
                long selectedBytes = 0;
                foreach (var file in EnumerateManagedFiles().Where(file => file.LastWriteTimeUtc >= cutoff.UtcDateTime))
                {
                    if (file.Length > maximumBytes || selectedBytes + file.Length > maximumBytes)
                    {
                        continue;
                    }

                    selected.Add(file);
                    selectedBytes += file.Length;
                }

                using var output = new MemoryStream();
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
                {
                    var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var manifestStream = manifest.Open())
                    {
                        JsonSerializer.Serialize(manifestStream, new
                        {
                            createdAtUtc = now,
                            application = "GarageBalance",
                            formatVersion = 1,
                            encoding = "utf-8",
                            includedDays = _options.PackageDays,
                            includedFiles = selected.Select(file => new { file.Name, file.Length }).ToArray(),
                            note = "Пакет содержит только обезличенные журналы ошибок. База данных, .env, backup и пользовательские документы не включены."
                        }, ManifestJsonOptions);
                    }

                    foreach (var file in selected)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry($"errors/{file.Name}", CompressionLevel.Optimal);
                        using var source = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var target = entry.Open();
                        source.CopyTo(target);
                    }
                }

                var name = $"garagebalance-diagnostics-{now:yyyyMMdd-HHmmss}.zip";
                return Task.FromResult<DiagnosticPackage?>(new DiagnosticPackage(name, output.ToArray()));
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                SetStorageError("сформировать диагностический пакет", exception);
                return Task.FromResult<DiagnosticPackage?>(null);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel level) => _options.Enabled && level >= LogLevel.Error;

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                var now = _timeProvider.GetUtcNow();
                EnsureWriter(now);
                var entry = JsonSerializer.Serialize(new
                {
                    timestampUtc = now,
                    level = level.ToString(),
                    category = DiagnosticLogSanitizer.Sanitize(category),
                    eventId = eventId.Id,
                    message = DiagnosticLogSanitizer.Sanitize(message),
                    exception = exception is null ? null : DiagnosticLogSanitizer.SanitizeException(exception)
                }, LogJsonOptions);
                _writer!.WriteLine(entry);
                _writer.Flush();
                _lastWriteError = null;
            }
            catch (Exception writeException) when (IsStorageException(writeException))
            {
                SetStorageError("записать диагностический журнал", writeException);
            }
        }
    }

    private DiagnosticLogStatusDto CreateStatus(int fileCount, long totalSizeBytes, DateTimeOffset? lastEntryAtUtc) => new(
        _options.Enabled,
        _options.RetentionDays,
        _options.PackageDays,
        _options.PackageMaxSizeMb,
        fileCount,
        totalSizeBytes,
        lastEntryAtUtc,
        _lastWriteError);

    private void SetStorageError(string operation, Exception exception)
    {
        _lastWriteError = $"Не удалось {operation}: {exception.GetType().Name}.";
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    private void EnsureWriter(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var maxBytes = _options.MaxFileSizeMb * 1024L * 1024L;
        if (_writer is not null && _currentDate == date && _currentPath is not null && new FileInfo(_currentPath).Length < maxBytes)
        {
            return;
        }

        _writer?.Dispose();
        Directory.CreateDirectory(_options.Directory);
        _currentDate = date;
        _currentSequence = FindNextSequence(date, maxBytes);
        _currentPath = Path.Combine(_options.Directory, $"{FilePrefix}{date:yyyyMMdd}-{_currentSequence:000}.jsonl");
        _writer = new StreamWriter(new FileStream(_currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false));
        DeleteExpiredFiles(now);
    }

    private int FindNextSequence(DateOnly date, long maxBytes)
    {
        var files = EnumerateManagedFiles()
            .Where(file => file.Name.StartsWith($"{FilePrefix}{date:yyyyMMdd}-", StringComparison.Ordinal))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var last = files.LastOrDefault();
        if (last is null)
        {
            return 1;
        }

        var sequenceText = Path.GetFileNameWithoutExtension(last.Name).Split('-').Last();
        var sequence = int.TryParse(sequenceText, out var parsed) ? parsed : 1;
        return last.Length < maxBytes ? sequence : sequence + 1;
    }

    private void DeleteExpiredFiles(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-_options.RetentionDays).UtcDateTime;
        foreach (var file in EnumerateManagedFiles().Where(file => file.LastWriteTimeUtc < cutoff))
        {
            file.Delete();
        }
    }

    private IEnumerable<FileInfo> EnumerateManagedFiles()
    {
        if (!Directory.Exists(_options.Directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_options.Directory, $"{FilePrefix}????????-???.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal);
    }

    private sealed class DiagnosticFileLogger(RollingJsonDiagnosticLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
            }
        }
    }
}
