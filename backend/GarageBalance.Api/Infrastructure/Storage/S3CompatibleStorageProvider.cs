using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using GarageBalance.Api.Application.Storage;

namespace GarageBalance.Api.Infrastructure.Storage;

public sealed record S3WriteResponse(string? VersionId, string? EntityTag, string? ChecksumSha256);
public sealed record S3UploadedPart(int PartNumber, string EntityTag, string? ChecksumSha256);
public sealed record S3ReadResponse(Stream Content, IReadOnlyDictionary<string, string> Metadata);
public sealed record S3StatResponse(
    long SizeBytes,
    string? VersionId,
    string? EntityTag,
    string? ChecksumSha256,
    IReadOnlyDictionary<string, string> Metadata);

public interface IS3ObjectClient : IDisposable
{
    Task<S3WriteResponse> PutAsync(
        string bucket,
        string key,
        Stream content,
        long contentLength,
        string checksumSha256Base64,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);
    Task<string> BeginMultipartAsync(
        string bucket,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);
    Task<S3UploadedPart> UploadPartAsync(
        string bucket,
        string key,
        string uploadId,
        int partNumber,
        Stream content,
        long contentLength,
        string checksumSha256Base64,
        bool isLastPart,
        CancellationToken cancellationToken);
    Task<S3WriteResponse> CompleteMultipartAsync(
        string bucket,
        string key,
        string uploadId,
        IReadOnlyList<S3UploadedPart> parts,
        CancellationToken cancellationToken);
    Task AbortMultipartAsync(string bucket, string key, string uploadId, CancellationToken cancellationToken);
    Task<S3ReadResponse> GetAsync(string bucket, string key, CancellationToken cancellationToken);
    Task<S3StatResponse?> StatAsync(string bucket, string key, CancellationToken cancellationToken);
    Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken);
    Task<Uri> CreateDownloadLinkAsync(string bucket, string key, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
}

public interface IS3ObjectClientFactory
{
    IS3ObjectClient Create(EffectiveStorageDestination destination);
}

public sealed class AwsS3ObjectClientFactory : IS3ObjectClientFactory
{
    public IS3ObjectClient Create(EffectiveStorageDestination destination)
    {
        if (destination.Endpoint is null)
        {
            throw new InvalidOperationException($"S3 destination '{destination.Id}' has no endpoint.");
        }
        var configuration = new AmazonS3Config
        {
            ServiceURL = destination.Endpoint.AbsoluteUri.TrimEnd('/'),
            UseHttp = destination.Endpoint.Scheme == Uri.UriSchemeHttp,
            ForcePathStyle = destination.ForcePathStyle,
            AuthenticationRegion = destination.SigningRegion,
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromMinutes(5)
        };
        return new AwsS3ObjectClient(new AmazonS3Client(configuration));
    }
}

public sealed class AwsS3ObjectClient(IAmazonS3 client) : IS3ObjectClient
{
    public async Task<S3WriteResponse> PutAsync(
        string bucket,
        string key,
        Stream content,
        long contentLength,
        string checksumSha256Base64,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = "application/octet-stream",
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumSHA256 = checksumSha256Base64,
            IfNoneMatch = "*",
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };
        request.Headers.ContentLength = contentLength;
        foreach (var item in metadata)
        {
            request.Metadata.Add(item.Key, item.Value);
        }
        var response = await client.PutObjectAsync(request, cancellationToken);
        return new S3WriteResponse(response.VersionId, response.ETag, response.ChecksumSHA256);
    }

    public async Task<S3ReadResponse> GetAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
        return new S3ReadResponse(
            new OwnedResponseStream(response.ResponseStream, response),
            ReadMetadata(response.Metadata));
    }

    public async Task<string> BeginMultipartAsync(
        string bucket,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = bucket,
            Key = key,
            ContentType = "application/octet-stream",
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };
        foreach (var item in metadata)
        {
            request.Metadata.Add(item.Key, item.Value);
        }
        var response = await client.InitiateMultipartUploadAsync(request, cancellationToken);
        return response.UploadId;
    }

    public async Task<S3UploadedPart> UploadPartAsync(
        string bucket,
        string key,
        string uploadId,
        int partNumber,
        Stream content,
        long contentLength,
        string checksumSha256Base64,
        bool isLastPart,
        CancellationToken cancellationToken)
    {
        var response = await client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = bucket,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
            PartSize = contentLength,
            IsLastPart = isLastPart,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumSHA256 = checksumSha256Base64
        }, cancellationToken);
        return new S3UploadedPart(partNumber, response.ETag, response.ChecksumSHA256);
    }

    public async Task<S3WriteResponse> CompleteMultipartAsync(
        string bucket,
        string key,
        string uploadId,
        IReadOnlyList<S3UploadedPart> parts,
        CancellationToken cancellationToken)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = bucket,
            Key = key,
            UploadId = uploadId,
            IfNoneMatch = "*"
        };
        request.AddPartETags(parts.Select(part => new PartETag(part.PartNumber, part.EntityTag)));
        var response = await client.CompleteMultipartUploadAsync(request, cancellationToken);
        return new S3WriteResponse(response.VersionId, response.ETag, response.ChecksumSHA256);
    }

    public Task AbortMultipartAsync(
        string bucket,
        string key,
        string uploadId,
        CancellationToken cancellationToken) =>
        client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = bucket,
            Key = key,
            UploadId = uploadId
        }, cancellationToken);

    public async Task<S3StatResponse?> StatAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = bucket, Key = key },
                cancellationToken);
            return new S3StatResponse(
                response.ContentLength,
                response.VersionId,
                response.ETag,
                response.ChecksumSHA256,
                ReadMetadata(response.Metadata));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, cancellationToken);

    public async Task<Uri> CreateDownloadLinkAsync(
        string bucket,
        string key,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = await client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = client.Config.UseHttp ? Protocol.HTTP : Protocol.HTTPS,
            Expires = expiresAtUtc.UtcDateTime
        });
        return new Uri(url, UriKind.Absolute);
    }

    public void Dispose() => client.Dispose();

    private static IReadOnlyDictionary<string, string> ReadMetadata(MetadataCollection metadata)
    {
        const string headerPrefix = "x-amz-meta-";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in metadata.Keys)
        {
            var normalizedKey = key.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase)
                ? key[headerPrefix.Length..]
                : key;
            result[normalizedKey] = metadata[key];
        }
        return result;
    }

    private sealed class OwnedResponseStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            owner.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

public sealed class S3CompatibleStorageProvider(
    EffectiveStorageDestination destination,
    IS3ObjectClient client,
    TimeProvider timeProvider) : IStorageProvider, IDisposable
{
    private const string Sha256MetadataKey = "garagebalance-sha256";
    private const string GenerationMetadataKey = "garagebalance-generation";
    private const string OperationMetadataKey = "garagebalance-operation-id";
    private readonly string bucket = destination.Bucket
        ?? throw new InvalidOperationException($"S3 destination '{destination.Id}' has no bucket.");

    public string DestinationId => destination.Id;
    public StorageCapability Capabilities => destination.Capabilities;

    public string GetWriteLocator(StorageWriteRequest request)
    {
        ValidateWriteRequest(request);
        return BuildKey(request);
    }

    public async Task<StorageWriteResult> WriteAsync(
        StorageWriteRequest request,
        Stream content,
        CancellationToken cancellationToken)
    {
        ValidateWriteRequest(request);
        var key = BuildKey(request);
        var metadata = request.Metadata
            .Where(item => IsSafeMetadata(item.Key, item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        metadata[Sha256MetadataKey] = request.Sha256.ToLowerInvariant();
        metadata[GenerationMetadataKey] = request.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata[OperationMetadataKey] = request.OperationId.ToString("N");
        var checksumBase64 = Convert.ToBase64String(Convert.FromHexString(request.Sha256));
        try
        {
            var response = Capabilities.HasFlag(StorageCapability.MultipartUpload) &&
                           request.SizeBytes >= destination.MultipartThresholdBytes
                ? await WriteMultipartAsync(key, request, content, metadata, cancellationToken)
                : await client.PutAsync(
                    bucket,
                    key,
                    content,
                    request.SizeBytes,
                    checksumBase64,
                    metadata,
                    cancellationToken);
            return new StorageWriteResult(key, response.VersionId, response.ChecksumSha256 ?? response.EntityTag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            S3StatResponse? existing;
            try
            {
                existing = await client.StatAsync(bucket, key, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception statException)
            {
                throw MapException(statException, cancellationToken, writeMayHaveUnknownOutcome: true);
            }
            if (existing is not null && IsExpected(existing, request))
            {
                return new StorageWriteResult(key, existing.VersionId, existing.ChecksumSha256 ?? existing.EntityTag);
            }
            throw new StorageProviderException(StorageErrorCategory.Conflict, "S3 object key already contains different immutable data.", exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception, cancellationToken, writeMayHaveUnknownOutcome: true);
        }
    }

    public async Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        var key = NormalizeNativeLocator(nativeLocator);
        try
        {
            return (await client.GetAsync(bucket, key, cancellationToken)).Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception, cancellationToken, writeMayHaveUnknownOutcome: false);
        }
    }

    public async Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        var key = NormalizeNativeLocator(nativeLocator);
        try
        {
            var stat = await client.StatAsync(bucket, key, cancellationToken);
            if (stat is null)
            {
                return null;
            }
            stat.Metadata.TryGetValue(Sha256MetadataKey, out var sha256);
            return new StorageObjectStat(
                stat.SizeBytes,
                sha256 ?? stat.ChecksumSha256 ?? stat.EntityTag,
                stat.VersionId,
                stat.Metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception, cancellationToken, writeMayHaveUnknownOutcome: false);
        }
    }

    public async Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken)
    {
        var key = NormalizeNativeLocator(nativeLocator);
        try
        {
            await client.DeleteAsync(bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception, cancellationToken, writeMayHaveUnknownOutcome: true);
        }
    }

    public async Task<StorageDownloadLink?> GetDownloadLinkAsync(
        string nativeLocator,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        if (!Capabilities.HasFlag(StorageCapability.DownloadLink))
        {
            return null;
        }
        if (lifetime < TimeSpan.FromSeconds(30) || lifetime > TimeSpan.FromMinutes(15))
        {
            throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported, "Download link lifetime must be between 30 seconds and 15 minutes.");
        }
        var expiresAtUtc = timeProvider.GetUtcNow().Add(lifetime);
        try
        {
            var url = await client.CreateDownloadLinkAsync(bucket, NormalizeNativeLocator(nativeLocator), expiresAtUtc, cancellationToken);
            return new StorageDownloadLink(url, expiresAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapException(exception, cancellationToken, writeMayHaveUnknownOutcome: false);
        }
    }

    public void Dispose() => client.Dispose();

    private async Task<S3WriteResponse> WriteMultipartAsync(
        string key,
        StorageWriteRequest request,
        Stream content,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var uploadId = await client.BeginMultipartAsync(bucket, key, metadata, cancellationToken);
        try
        {
            var parts = new List<S3UploadedPart>();
            using var fullHash = IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var remaining = request.SizeBytes;
            var partNumber = 1;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var partLength = (int)Math.Min(remaining, destination.MultipartPartSizeBytes);
                var buffer = GC.AllocateUninitializedArray<byte>(partLength);
                await content.ReadExactlyAsync(buffer, cancellationToken);
                fullHash.AppendData(buffer);
                var partChecksum = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(buffer));
                await using var partStream = new MemoryStream(buffer, writable: false);
                parts.Add(await client.UploadPartAsync(
                    bucket,
                    key,
                    uploadId,
                    partNumber,
                    partStream,
                    partLength,
                    partChecksum,
                    remaining == partLength,
                    cancellationToken));
                remaining -= partLength;
                partNumber++;
            }
            if (content.ReadByte() != -1 ||
                !string.Equals(Convert.ToHexStringLower(fullHash.GetHashAndReset()), request.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageProviderException(
                    StorageErrorCategory.ChecksumOrStale,
                    "Multipart content does not match the declared size or SHA-256 checksum.");
            }
            return await client.CompleteMultipartAsync(bucket, key, uploadId, parts, cancellationToken);
        }
        catch
        {
            try
            {
                await client.AbortMultipartAsync(bucket, key, uploadId, CancellationToken.None);
            }
            catch
            {
                // The durable replication job will reconcile a potentially unknown remote upload.
            }
            throw;
        }
    }

    private string BuildKey(StorageWriteRequest request)
    {
        var logicalKey = StorageObjectKey.Normalize(request.ObjectKey);
        return StorageObjectKey.Normalize(
            $"{destination.Prefix}/{request.OperationId:N}/g{request.Generation:D20}/{logicalKey}");
    }

    private string NormalizeNativeLocator(string nativeLocator)
    {
        var key = StorageObjectKey.Normalize(nativeLocator);
        var prefix = destination.Prefix.TrimEnd('/') + "/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported, "S3 locator is outside the configured prefix.");
        }
        return key;
    }

    private static bool IsExpected(S3StatResponse stat, StorageWriteRequest request) =>
        stat.SizeBytes == request.SizeBytes &&
        stat.Metadata.TryGetValue(Sha256MetadataKey, out var sha256) &&
        string.Equals(sha256, request.Sha256, StringComparison.OrdinalIgnoreCase) &&
        stat.Metadata.TryGetValue(GenerationMetadataKey, out var generation) &&
        string.Equals(generation, request.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
        stat.Metadata.TryGetValue(OperationMetadataKey, out var operationId) &&
        string.Equals(operationId, request.OperationId.ToString("N"), StringComparison.Ordinal);

    private static bool IsSafeMetadata(string key, string value) =>
        key.Length is > 0 and <= 80 && value.Length <= 500 &&
        !key.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
        !key.Contains("token", StringComparison.OrdinalIgnoreCase) &&
        !key.Contains("password", StringComparison.OrdinalIgnoreCase) &&
        key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') &&
        value.All(character => !char.IsControl(character));

    private static void ValidateWriteRequest(StorageWriteRequest request)
    {
        if (request.OperationId == Guid.Empty || request.Generation < 1 || request.SizeBytes < 1 ||
            request.Sha256.Length != 64 || !request.Sha256.All(Uri.IsHexDigit))
        {
            throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported, "S3 write metadata is invalid.");
        }
    }

    private static StorageProviderException MapException(
        Exception exception,
        CancellationToken cancellationToken,
        bool writeMayHaveUnknownOutcome)
    {
        if (exception is StorageProviderException providerException)
        {
            return providerException;
        }
        if (exception is AuthenticationException || exception.InnerException is AuthenticationException)
        {
            return new StorageProviderException(StorageErrorCategory.TlsSecurity, "TLS validation failed for the storage endpoint.", exception);
        }
        if (exception is AmazonS3Exception s3)
        {
            var category = s3.StatusCode switch
            {
                HttpStatusCode.NotFound => StorageErrorCategory.ObjectMissing,
                HttpStatusCode.Unauthorized => StorageErrorCategory.CredentialsExpired,
                HttpStatusCode.Forbidden => StorageErrorCategory.ProviderForbidden,
                HttpStatusCode.TooManyRequests => StorageErrorCategory.RateLimited,
                HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => StorageErrorCategory.Conflict,
                HttpStatusCode.InsufficientStorage => StorageErrorCategory.QuotaOrReadOnly,
                >= HttpStatusCode.InternalServerError => StorageErrorCategory.TransientNetwork,
                HttpStatusCode.BadRequest when string.Equals(s3.ErrorCode, "BadDigest", StringComparison.OrdinalIgnoreCase) => StorageErrorCategory.ChecksumOrStale,
                _ => writeMayHaveUnknownOutcome ? StorageErrorCategory.UnknownOutcome : StorageErrorCategory.ValidationOrUnsupported
            };
            if (string.Equals(s3.ErrorCode, "SlowDown", StringComparison.OrdinalIgnoreCase))
            {
                category = StorageErrorCategory.RateLimited;
            }
            else if (s3.ErrorCode is "ExpiredToken" or "InvalidAccessKeyId" or "SignatureDoesNotMatch")
            {
                category = StorageErrorCategory.CredentialsExpired;
            }
            else if (s3.ErrorCode is "QuotaExceeded" or "StorageFull")
            {
                category = StorageErrorCategory.QuotaOrReadOnly;
            }
            return new StorageProviderException(category, "S3-compatible storage operation failed.", exception);
        }
        if (exception is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return new StorageProviderException(
                writeMayHaveUnknownOutcome ? StorageErrorCategory.UnknownOutcome : StorageErrorCategory.TransientNetwork,
                "S3-compatible storage endpoint did not return a conclusive response.",
                exception);
        }
        return new StorageProviderException(
            writeMayHaveUnknownOutcome ? StorageErrorCategory.UnknownOutcome : StorageErrorCategory.TransientNetwork,
            "S3-compatible storage operation failed.",
            exception);
    }
}
