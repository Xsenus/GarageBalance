using System.Security.Cryptography;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;

namespace GarageBalance.Api.Infrastructure.Storage;

public interface ILocalFileStorageProvider : IStorageProvider
{
    Task<StorageWriteResult> CommitVerifiedFileAsync(
        StorageWriteRequest request,
        string sourcePath,
        CancellationToken cancellationToken);
}

public sealed class LocalFileStorageProvider : ILocalFileStorageProvider
{
    private readonly string rootPath;

    public LocalFileStorageProvider(string destinationId, string configuredRootPath)
    {
        if (!StorageObjectKey.IsValidId(destinationId))
        {
            throw new ArgumentException("Local storage destination id is invalid.", nameof(destinationId));
        }
        DestinationId = destinationId;
        rootPath = DatabaseBackupPathResolver.Resolve(configuredRootPath);
    }

    public string DestinationId { get; }
    public StorageCapability Capabilities =>
        StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat | StorageCapability.Delete;

    public async Task<StorageWriteResult> CommitVerifiedFileAsync(
        StorageWriteRequest request,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ValidateWriteRequest(request);
        var normalizedSource = Path.GetFullPath(sourcePath);
        EnsureInsideRoot(normalizedSource);
        var source = new FileInfo(normalizedSource);
        if (!source.Exists || source.Length != request.SizeBytes)
        {
            throw new StorageProviderException(StorageErrorCategory.ChecksumOrStale, "Local storage source size does not match its committed metadata.");
        }
        await using (var content = new FileStream(
            normalizedSource,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var actualSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));
            if (!string.Equals(actualSha256, request.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageProviderException(StorageErrorCategory.ChecksumOrStale, "Local storage source checksum does not match its committed metadata.");
            }
        }

        var key = StorageObjectKey.Normalize(request.ObjectKey);
        var finalPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        try
        {
            File.Move(normalizedSource, finalPath, overwrite: false);
            return new StorageWriteResult(key, null, request.Sha256.ToLowerInvariant());
        }
        catch (IOException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.Conflict, "Local storage could not commit the object.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.ProviderForbidden, "Local storage access was denied.", exception);
        }
    }

    public async Task<StorageWriteResult> WriteAsync(
        StorageWriteRequest request,
        Stream content,
        CancellationToken cancellationToken)
    {
        ValidateWriteRequest(request);
        var key = StorageObjectKey.Normalize(request.ObjectKey);

        var finalPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var temporaryPath = finalPath + $".{request.OperationId:N}.tmp";
        try
        {
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long written = 0;
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                written += read;
                if (written > request.SizeBytes)
                {
                    throw new StorageProviderException(StorageErrorCategory.ChecksumOrStale, "Storage source is larger than its committed size.");
                }
                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await destination.FlushAsync(cancellationToken);
            var actualSha256 = Convert.ToHexStringLower(hasher.GetHashAndReset());
            if (written != request.SizeBytes || !string.Equals(actualSha256, request.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageProviderException(StorageErrorCategory.ChecksumOrStale, "Storage source size or checksum does not match its committed metadata.");
            }
            destination.Close();
            File.Move(temporaryPath, finalPath, overwrite: false);
            return new StorageWriteResult(key, null, actualSha256);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (StorageProviderException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.Conflict, "Local storage could not commit the object.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.ProviderForbidden, "Local storage access was denied.", exception);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(StorageObjectKey.Normalize(nativeLocator));
        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (FileNotFoundException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.ObjectMissing, "Local storage object was not found.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.ProviderForbidden, "Local storage access was denied.", exception);
        }
    }

    public async Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(StorageObjectKey.Normalize(nativeLocator));
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }
        await using var content = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        return new StorageObjectStat(
            file.Length,
            Convert.ToHexStringLower(hash),
            null,
            new Dictionary<string, string>());
    }

    public Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(StorageObjectKey.Normalize(nativeLocator));
        try
        {
            File.Delete(path);
            return Task.CompletedTask;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StorageProviderException(StorageErrorCategory.ProviderForbidden, "Local storage access was denied.", exception);
        }
    }

    public Task<StorageDownloadLink?> GetDownloadLinkAsync(
        string nativeLocator,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<StorageDownloadLink?>(null);
    }

    private string ResolvePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(rootPath, key.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideRoot(path);
        return path;
    }

    private void EnsureInsideRoot(string path)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported, "Storage object key escapes the configured root.");
        }
    }

    private static void ValidateWriteRequest(StorageWriteRequest request)
    {
        if (request.Generation < 1 || request.SizeBytes < 1 || request.Sha256.Length != 64 || !request.Sha256.All(Uri.IsHexDigit))
        {
            throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported, "Storage write metadata is invalid.");
        }
    }
}

public interface IStorageProviderRegistry
{
    IStorageProvider GetRequired(string destinationId);
    IReadOnlyList<IStorageProvider> GetAll();
}

public sealed class StorageProviderRegistry : IStorageProviderRegistry, IDisposable
{
    private readonly IReadOnlyDictionary<string, IStorageProvider> providers;

    public StorageProviderRegistry(
        StorageConfigurationResolver resolver,
        IS3ObjectClientFactory s3ClientFactory,
        TimeProvider timeProvider)
    {
        providers = resolver.Resolve().Destinations
            .Select(destination => destination.Type switch
            {
                StorageProviderType.LocalFileSystem => (IStorageProvider)new LocalFileStorageProvider(
                    destination.Id,
                    destination.RootPath ?? throw new InvalidOperationException($"Local destination '{destination.Id}' has no root path.")),
                StorageProviderType.S3Compatible => new S3CompatibleStorageProvider(
                    destination,
                    s3ClientFactory.Create(destination),
                    timeProvider),
                _ => throw new InvalidOperationException($"Storage provider type '{destination.Type}' is not supported.")
            })
            .ToDictionary(provider => provider.DestinationId, StringComparer.Ordinal);
    }

    public IStorageProvider GetRequired(string destinationId) =>
        providers.TryGetValue(destinationId, out var provider)
            ? provider
            : throw new InvalidOperationException($"Storage provider '{destinationId}' is not registered.");

    public IReadOnlyList<IStorageProvider> GetAll() => providers.Values.ToArray();

    public void Dispose()
    {
        foreach (var provider in providers.Values.OfType<IDisposable>())
        {
            provider.Dispose();
        }
    }
}
