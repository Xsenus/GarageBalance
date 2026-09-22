using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Import;

public static class ImportFileLimits
{
    public const int MaximumFileSizeMegabytes = 50;
    public const long MaximumFileSizeBytes = MaximumFileSizeMegabytes * 1024L * 1024L;
    public const long MultipartRequestSizeBytes = (MaximumFileSizeMegabytes + 1L) * 1024L * 1024L;
}

public sealed class ImportDryRunQueueOptions
{
    public const string SectionName = "ImportProcessing";

    [Range(1, 32)]
    public int Capacity { get; init; } = 4;

    [Required]
    public string WorkDirectory { get; init; } = "auto";

    [Range(ImportFileLimits.MaximumFileSizeMegabytes, ImportFileLimits.MaximumFileSizeMegabytes)]
    public int MaximumFileSizeMegabytes { get; init; } = ImportFileLimits.MaximumFileSizeMegabytes;

    [Range(ImportFileLimits.MaximumFileSizeMegabytes, 102400)]
    public int MaximumWorkDirectorySizeMegabytes { get; init; } = 512;

    [Range(1, 720)]
    public int OrphanRetentionHours { get; init; } = 24;

    [Range(1, 1440)]
    public int OrphanSweepIntervalMinutes { get; init; } = 60;
}

public sealed record ImportDryRunJob(Guid RunId);

public interface IImportDryRunQueue
{
    bool TryQueue(ImportDryRunJob job);
    ValueTask<ImportDryRunJob> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class ImportDryRunQueue : IImportDryRunQueue
{
    private readonly Channel<ImportDryRunJob> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _queuedRunIds = new();

    public ImportDryRunQueue(IOptions<ImportDryRunQueueOptions> options)
    {
        _channel = Channel.CreateBounded<ImportDryRunJob>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryQueue(ImportDryRunJob job)
    {
        if (!_queuedRunIds.TryAdd(job.RunId, 0))
        {
            return true;
        }

        if (_channel.Writer.TryWrite(job))
        {
            return true;
        }

        _queuedRunIds.TryRemove(job.RunId, out _);
        return false;
    }

    public async ValueTask<ImportDryRunJob> DequeueAsync(CancellationToken cancellationToken)
    {
        var job = await _channel.Reader.ReadAsync(cancellationToken);
        _queuedRunIds.TryRemove(job.RunId, out _);
        return job;
    }
}

public interface IImportDryRunDispatcher
{
    Task<ImportResult<AccessImportRunDto>> QueueAsync(
        string fileName,
        Stream content,
        long declaredLength,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}

public sealed class ImportDryRunDispatcher(
    IImportService importService,
    IImportDryRunQueue queue,
    IOptions<ImportDryRunQueueOptions> options) : IImportDryRunDispatcher
{
    private static readonly SemaphoreSlim WorkDirectoryLock = new(1, 1);
    private readonly ImportDryRunQueueOptions _options = options.Value;

    public async Task<ImportResult<AccessImportRunDto>> QueueAsync(
        string fileName,
        Stream content,
        long declaredLength,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(fileName.Trim());
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (extension is not ".accdb" and not ".mdb")
        {
            return ImportResult<AccessImportRunDto>.Failure(
                "access_extension_required",
                "Для dry-run импорта нужен файл .accdb или .mdb.");
        }

        var maximumBytes = _options.MaximumFileSizeMegabytes * 1024L * 1024L;
        if (declaredLength <= 0)
        {
            return ImportResult<AccessImportRunDto>.Failure("file_empty", "Файл Access пустой.");
        }

        if (declaredLength > maximumBytes)
        {
            return ImportResult<AccessImportRunDto>.Failure(
                "file_too_large",
                $"Файл Access превышает допустимый размер {_options.MaximumFileSizeMegabytes} МБ.");
        }

        var runId = Guid.NewGuid();
        var path = ImportDryRunWorkFiles.GetPath(_options, runId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WorkDirectoryLock.WaitAsync(cancellationToken);
        try
        {
            var maximumWorkDirectoryBytes = _options.MaximumWorkDirectorySizeMegabytes * 1024L * 1024L;
            var occupiedBytes = ImportDryRunWorkFiles.GetManagedFiles(_options).Sum(file => file.Length);
            if (occupiedBytes > maximumWorkDirectoryBytes - declaredLength)
            {
                return ImportResult<AccessImportRunDto>.Failure(
                    "import_staging_capacity_exceeded",
                    "Временное хранилище импорта заполнено. Дождитесь завершения текущих импортов или обратитесь к администратору.");
            }

            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyBoundedAsync(content, destination, maximumBytes, cancellationToken);
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            var actualLength = new FileInfo(path).Length;
            var queued = await importService.CreateQueuedDryRunAsync(
                new QueuedAccessImportDryRunRequest(runId, safeName, actualLength, actorUserId),
                cancellationToken);
            if (!queued.Succeeded)
            {
                File.Delete(path);
                return queued;
            }

            _ = queue.TryQueue(new ImportDryRunJob(runId));
            return queued;
        }
        catch (InvalidDataException)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return ImportResult<AccessImportRunDto>.Failure(
                "file_too_large",
                $"Файл Access превышает допустимый размер {_options.MaximumFileSizeMegabytes} МБ.");
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
        finally
        {
            WorkDirectoryLock.Release();
        }
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Import file exceeded the configured size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

internal static class ImportDryRunWorkFiles
{
    internal static string GetDirectory(ImportDryRunQueueOptions options) =>
        string.Equals(options.WorkDirectory, "auto", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetTempPath(), "GarageBalance", "import-queue")
            : Path.GetFullPath(options.WorkDirectory);

    internal static string GetPath(ImportDryRunQueueOptions options, Guid runId)
    {
        return Path.Combine(GetDirectory(options), $"{runId:N}.pending");
    }

    internal static IEnumerable<FileInfo> GetManagedFiles(ImportDryRunQueueOptions options)
    {
        var directory = GetDirectory(options);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file =>
                (file.Extension is ".pending" or ".orphan") &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _))
            .ToArray();
    }
}

public sealed class ImportDryRunOrphanSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<ImportDryRunQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<ImportDryRunOrphanSweeper> logger) : BackgroundService
{
    private readonly ImportDryRunQueueOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Access import orphan sweep failed and will be retried. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.OrphanSweepIntervalMinutes), stoppingToken);
        }
    }

    internal async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        var directory = ImportDryRunWorkFiles.GetDirectory(_options);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().AddHours(-_options.OrphanRetentionHours).UtcDateTime;
        var changed = 0;
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IImportRepository>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (file.LastWriteTimeUtc >= cutoff || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var extension = file.Extension;
            if (extension is not ".pending" and not ".orphan")
            {
                continue;
            }

            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out var runId))
            {
                continue;
            }

            var status = await repository.FindRunStatusAsync(runId, cancellationToken);
            if (status?.Status is "queued" or "processing")
            {
                continue;
            }

            if (extension == ".pending")
            {
                var quarantinePath = Path.Combine(directory, $"{runId:N}.orphan");
                File.Move(file.FullName, quarantinePath, overwrite: false);
                File.SetLastWriteTimeUtc(quarantinePath, timeProvider.GetUtcNow().UtcDateTime);
                logger.LogWarning("Quarantined orphaned Access import staging file. RunId={RunId}", runId);
            }
            else
            {
                file.Delete();
                logger.LogWarning("Deleted expired quarantined Access import staging file. RunId={RunId}", runId);
            }

            changed++;
        }

        return changed;
    }
}

public sealed class ImportDryRunWorker(
    IServiceScopeFactory scopeFactory,
    IImportDryRunQueue queue,
    IOptions<ImportDryRunQueueOptions> options,
    ILogger<ImportDryRunWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RecoveryRetryDelay = TimeSpan.FromSeconds(5);
    private readonly ImportDryRunQueueOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverQueuedJobsUntilSuccessfulAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            ImportDryRunJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!await TryProcessAsync(job, stoppingToken))
            {
                await Task.Delay(RecoveryRetryDelay, stoppingToken);
            }
            await RecoverQueuedJobsUntilSuccessfulAsync(stoppingToken);
        }
    }

    private async Task RecoverQueuedJobsUntilSuccessfulAsync(CancellationToken cancellationToken)
    {
        while (!await TryRecoverQueuedJobsAsync(cancellationToken))
        {
            await Task.Delay(RecoveryRetryDelay, cancellationToken);
        }
    }

    internal async Task<bool> TryRecoverQueuedJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecoverQueuedJobsAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Access import queue recovery failed and will be retried. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return false;
        }
    }

    internal async Task RecoverQueuedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IImportRepository>();
        foreach (var runId in await repository.GetQueuedRunIdsAsync(cancellationToken))
        {
            _ = queue.TryQueue(new ImportDryRunJob(runId));
        }
    }

    internal async Task<bool> TryProcessAsync(ImportDryRunJob job, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessAsync(job, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Access import queued job failed unexpectedly and will be recovered. RunId={RunId} ExceptionType={ExceptionType}",
                job.RunId,
                exception.GetType().Name);
            return false;
        }
    }

    internal async Task ProcessAsync(ImportDryRunJob job, CancellationToken cancellationToken)
    {
        var path = ImportDryRunWorkFiles.GetPath(_options, job.RunId);
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IImportService>();
        var removeStagedFile = false;
        try
        {
            if (!File.Exists(path))
            {
                await service.FailQueuedDryRunAsync(job.RunId, "staged_file_missing", cancellationToken);
                return;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await service.ProcessQueuedDryRunAsync(job.RunId, stream, cancellationToken);
            removeStagedFile = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Access import dry-run failed. RunId={RunId} ExceptionType={ExceptionType}",
                job.RunId,
                exception.GetType().Name);
            await service.FailQueuedDryRunAsync(job.RunId, "processing_failed", CancellationToken.None);
            removeStagedFile = true;
        }
        finally
        {
            if (removeStagedFile && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
