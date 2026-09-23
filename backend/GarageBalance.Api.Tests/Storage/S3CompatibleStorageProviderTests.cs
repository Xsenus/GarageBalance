using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Storage;

namespace GarageBalance.Api.Tests.Storage;

public sealed class S3CompatibleStorageProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(S3EncryptionMode.SseS3, null)]
    [InlineData(S3EncryptionMode.SseKms, "kms-key-123")]
    public async Task AwsClient_UsesConfiguredEncryptionForSingleAndMultipartWrites(
        S3EncryptionMode encryptionMode,
        string? kmsKeyId)
    {
        var sdk = DispatchProxy.Create<IAmazonS3, S3RequestSpy>();
        var spy = (S3RequestSpy)sdk;
        using var client = new AwsS3ObjectClient(sdk, encryptionMode, kmsKeyId);
        await using var content = new MemoryStream([1, 2, 3]);

        await client.PutAsync("bucket", "test-object", content, 3, "AQID", new Dictionary<string, string>(), CancellationToken.None);
        await client.BeginMultipartAsync("bucket", "test-multipart", new Dictionary<string, string>(), CancellationToken.None);
        await using var partContent = new MemoryStream([1, 2, 3]);
        var uploadedPart = await client.UploadPartAsync(
            "bucket", "test-multipart", "upload-1", 1, partContent, 3, "AQID", true, CancellationToken.None);
        await client.CompleteMultipartAsync(
            "bucket", "test-multipart", "upload-1", [uploadedPart], CancellationToken.None);

        var expectedMethod = encryptionMode == S3EncryptionMode.SseKms
            ? ServerSideEncryptionMethod.AWSKMS
            : ServerSideEncryptionMethod.AES256;
        Assert.Equal(expectedMethod, spy.PutRequest?.ServerSideEncryptionMethod);
        Assert.Equal(kmsKeyId, spy.PutRequest?.ServerSideEncryptionKeyManagementServiceKeyId);
        Assert.Equal(expectedMethod, spy.MultipartRequest?.ServerSideEncryptionMethod);
        Assert.Equal(kmsKeyId, spy.MultipartRequest?.ServerSideEncryptionKeyManagementServiceKeyId);
        Assert.Equal("AQID", uploadedPart.ChecksumSha256);
        Assert.Equal("AQID", spy.UploadPartRequest?.ChecksumSHA256);
        Assert.Equal("AQID", Assert.Single(spy.CompleteRequest?.PartETags ?? []).ChecksumSHA256);
    }

    [Fact]
    public async Task Write_UsesImmutableGenerationKeyChecksumAndSanitizedMetadata()
    {
        var client = new FakeS3Client();
        var provider = CreateProvider(client);
        byte[] bytes = [1, 2, 3, 4];
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var operationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await using var content = new MemoryStream(bytes);

        var result = await provider.WriteAsync(
            new StorageWriteRequest(
                operationId,
                "database/2026/copy.pgdump",
                3,
                bytes.Length,
                sha256,
                new Dictionary<string, string>
                {
                    ["backup-kind"] = "automatic",
                    ["secret-token"] = "must-not-leave-process"
                }),
            content,
            CancellationToken.None);

        Assert.Equal("private/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/g00000000000000000003/database/2026/copy.pgdump", result.NativeLocator);
        Assert.Equal("bucket", client.LastBucket);
        Assert.Equal(result.NativeLocator, client.LastKey);
        Assert.Equal(Convert.ToBase64String(Convert.FromHexString(sha256)), client.LastChecksumSha256Base64);
        Assert.Equal(sha256, client.LastMetadata["garagebalance-sha256"]);
        Assert.Equal("3", client.LastMetadata["garagebalance-generation"]);
        Assert.Equal(operationId.ToString("N"), client.LastMetadata["garagebalance-operation-id"]);
        Assert.DoesNotContain("secret-token", client.LastMetadata.Keys);
    }

    [Fact]
    public async Task Write_TreatsConditionalConflictAsIdempotentOnlyWhenMetadataMatches()
    {
        byte[] bytes = [1, 2, 3, 4];
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var operationId = Guid.NewGuid();
        var request = new StorageWriteRequest(operationId, "copy.pgdump", 1, 4, sha256, new Dictionary<string, string>());
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["garagebalance-sha256"] = sha256,
            ["garagebalance-generation"] = "1",
            ["garagebalance-operation-id"] = operationId.ToString("N")
        };
        var client = new FakeS3Client
        {
            PutException = new AmazonS3Exception("exists") { StatusCode = HttpStatusCode.PreconditionFailed },
            StatResult = new S3StatResponse(4, "v1", "etag", null, metadata)
        };
        var provider = CreateProvider(client);
        await using var firstContent = new MemoryStream(bytes);

        var result = await provider.WriteAsync(request, firstContent, CancellationToken.None);

        Assert.Equal("v1", result.ProviderVersionId);
        client.StatResult = client.StatResult with { SizeBytes = 5 };
        await using var secondContent = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<StorageProviderException>(
            () => provider.WriteAsync(request, secondContent, CancellationToken.None));
        Assert.Equal(StorageErrorCategory.Conflict, error.Category);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, StorageErrorCategory.ObjectMissing)]
    [InlineData(HttpStatusCode.Forbidden, null, StorageErrorCategory.ProviderForbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, null, StorageErrorCategory.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, StorageErrorCategory.TransientNetwork)]
    [InlineData(HttpStatusCode.BadRequest, "BadDigest", StorageErrorCategory.ChecksumOrStale)]
    [InlineData(HttpStatusCode.BadRequest, "ExpiredToken", StorageErrorCategory.CredentialsExpired)]
    public async Task Read_MapsProviderErrorsToStableCategories(
        HttpStatusCode statusCode,
        string? errorCode,
        StorageErrorCategory expected)
    {
        var client = new FakeS3Client
        {
            GetException = new AmazonS3Exception("native details") { StatusCode = statusCode, ErrorCode = errorCode }
        };
        var provider = CreateProvider(client);

        var error = await Assert.ThrowsAsync<StorageProviderException>(
            () => provider.OpenReadAsync("private/id/g00000000000000000001/copy.pgdump", CancellationToken.None));

        Assert.Equal(expected, error.Category);
        Assert.DoesNotContain("native details", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatReadDeleteAndShortDownloadLinkUseOnlyConfiguredBucketAndPrefix()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["garagebalance-sha256"] = new string('a', 64)
        };
        var client = new FakeS3Client
        {
            StatResult = new S3StatResponse(42, "v1", "etag", "provider-sha", metadata),
            ReadBytes = [7, 8, 9]
        };
        var provider = CreateProvider(client);
        const string locator = "private/id/g00000000000000000001/copy.pgdump";

        var stat = await provider.StatAsync(locator, CancellationToken.None);
        Assert.NotNull(stat);
        Assert.Equal(42, stat.SizeBytes);
        Assert.Equal(new string('a', 64), stat.ProviderChecksum);
        await using (var stream = await provider.OpenReadAsync(locator, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal([7, 8, 9], copy.ToArray());
        }
        var link = await provider.GetDownloadLinkAsync(locator, TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.NotNull(link);
        Assert.Equal(Now.AddMinutes(2), link.ExpiresAtUtc);
        Assert.Equal("https://download.example.test/object", link.Url.AbsoluteUri);
        await provider.DeleteAsync(locator, CancellationToken.None);
        Assert.Equal("bucket", client.LastBucket);
        Assert.Equal(locator, client.LastKey);
        await Assert.ThrowsAsync<StorageProviderException>(
            () => provider.StatAsync("another-prefix/copy.pgdump", CancellationToken.None));
    }

    [Fact]
    public async Task DownloadLink_IsUnavailableWithoutCapabilityAndLifetimeIsBounded()
    {
        var destination = CreateDestination() with
        {
            Capabilities = StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat
        };
        var provider = new S3CompatibleStorageProvider(destination, new FakeS3Client(), new FixedTimeProvider(Now));
        const string locator = "private/id/g00000000000000000001/copy.pgdump";

        Assert.Null(await provider.GetDownloadLinkAsync(locator, TimeSpan.FromMinutes(2), CancellationToken.None));
        var capable = CreateProvider(new FakeS3Client());
        await Assert.ThrowsAsync<StorageProviderException>(
            () => capable.GetDownloadLinkAsync(locator, TimeSpan.FromHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_IsPropagatedWithoutProviderWrapping()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new FakeS3Client
        {
            GetException = new OperationCanceledException(cancellation.Token)
        };
        var provider = CreateProvider(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.OpenReadAsync(
                "private/id/g00000000000000000001/copy.pgdump",
            cancellation.Token));
    }

    [Fact]
    public async Task MultipartWrite_UsesBoundedPartsAndAbortsWhenDeclaredChecksumDoesNotMatch()
    {
        var client = new FakeS3Client();
        var destination = CreateDestination() with
        {
            Capabilities = CreateDestination().Capabilities | StorageCapability.MultipartUpload
        };
        var provider = new S3CompatibleStorageProvider(destination, client, new FixedTimeProvider(Now));
        var bytes = new byte[5 * 1024 * 1024 + 17];
        RandomNumberGenerator.Fill(bytes);
        var request = new StorageWriteRequest(
            Guid.NewGuid(),
            "large.pgdump",
            1,
            bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            new Dictionary<string, string>());
        await using var content = new MemoryStream(bytes, writable: false);

        var result = await provider.WriteAsync(request, content, CancellationToken.None);

        Assert.Equal("multipart-etag", result.ProviderChecksum);
        Assert.Equal([1, 2], client.UploadedPartNumbers);
        Assert.Equal(0, client.PutCalls);
        Assert.False(client.AbortCalled);

        await using var corruptContent = new MemoryStream(bytes, writable: false);
        var corruptRequest = request with { OperationId = Guid.NewGuid(), Sha256 = new string('0', 64) };
        var error = await Assert.ThrowsAsync<StorageProviderException>(
            () => provider.WriteAsync(corruptRequest, corruptContent, CancellationToken.None));
        Assert.Equal(StorageErrorCategory.ChecksumOrStale, error.Category);
        Assert.True(client.AbortCalled);
    }

    private static S3CompatibleStorageProvider CreateProvider(FakeS3Client client) =>
        new(CreateDestination(), client, new FixedTimeProvider(Now));

    public class S3RequestSpy : DispatchProxy
    {
        public PutObjectRequest? PutRequest { get; private set; }
        public InitiateMultipartUploadRequest? MultipartRequest { get; private set; }
        public UploadPartRequest? UploadPartRequest { get; private set; }
        public CompleteMultipartUploadRequest? CompleteRequest { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAmazonS3.PutObjectAsync) => CapturePut(args),
            nameof(IAmazonS3.InitiateMultipartUploadAsync) => CaptureMultipart(args),
            nameof(IAmazonS3.UploadPartAsync) => CaptureUploadPart(args),
            nameof(IAmazonS3.CompleteMultipartUploadAsync) => CaptureComplete(args),
            nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException(targetMethod?.Name)
        };

        private Task<PutObjectResponse> CapturePut(object?[]? args)
        {
            PutRequest = Assert.IsType<PutObjectRequest>(args?[0]);
            return Task.FromResult(new PutObjectResponse());
        }

        private Task<InitiateMultipartUploadResponse> CaptureMultipart(object?[]? args)
        {
            MultipartRequest = Assert.IsType<InitiateMultipartUploadRequest>(args?[0]);
            return Task.FromResult(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        }

        private Task<UploadPartResponse> CaptureUploadPart(object?[]? args)
        {
            UploadPartRequest = Assert.IsType<UploadPartRequest>(args?[0]);
            return Task.FromResult(new UploadPartResponse { ETag = "etag-1" });
        }

        private Task<CompleteMultipartUploadResponse> CaptureComplete(object?[]? args)
        {
            CompleteRequest = Assert.IsType<CompleteMultipartUploadRequest>(args?[0]);
            return Task.FromResult(new CompleteMultipartUploadResponse { ETag = "complete-etag" });
        }
    }

    private static EffectiveStorageDestination CreateDestination() => new(
        "offsite-a",
        StorageProviderType.S3Compatible,
        StorageDestinationState.Enabled,
        "host-a",
        "garagebalance",
        null,
        new Uri("https://s3.example.test"),
        "bucket",
        "private",
        "us-east-1",
        true,
        5L * 1024 * 1024,
        5 * 1024 * 1024,
        "DefaultChain",
        StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat |
            StorageCapability.Delete | StorageCapability.DownloadLink | StorageCapability.ServerSideEncryption);

    private sealed class FakeS3Client : IS3ObjectClient
    {
        public Exception? PutException { get; init; }
        public Exception? GetException { get; init; }
        public S3StatResponse? StatResult { get; set; }
        public byte[] ReadBytes { get; init; } = [];
        public int PutCalls { get; private set; }
        public List<int> UploadedPartNumbers { get; } = [];
        public bool AbortCalled { get; private set; }
        public string? LastBucket { get; private set; }
        public string? LastKey { get; private set; }
        public string? LastChecksumSha256Base64 { get; private set; }
        public IReadOnlyDictionary<string, string> LastMetadata { get; private set; } = new Dictionary<string, string>();

        public Task<S3WriteResponse> PutAsync(string bucket, string key, Stream content, long contentLength, string checksumSha256Base64, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
        {
            PutCalls++;
            LastBucket = bucket;
            LastKey = key;
            LastChecksumSha256Base64 = checksumSha256Base64;
            LastMetadata = new Dictionary<string, string>(metadata);
            return PutException is null
                ? Task.FromResult(new S3WriteResponse("v1", "etag", checksumSha256Base64))
                : Task.FromException<S3WriteResponse>(PutException);
        }

        public Task<string> BeginMultipartAsync(string bucket, string key, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken) =>
            Task.FromResult("upload-1");

        public async Task<S3UploadedPart> UploadPartAsync(string bucket, string key, string uploadId, int partNumber, Stream content, long contentLength, string checksumSha256Base64, bool isLastPart, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Assert.Equal(contentLength, buffer.Length);
            Assert.Equal(checksumSha256Base64, Convert.ToBase64String(SHA256.HashData(buffer.ToArray())));
            UploadedPartNumbers.Add(partNumber);
            return new S3UploadedPart(partNumber, $"etag-{partNumber}", checksumSha256Base64);
        }

        public Task<S3WriteResponse> CompleteMultipartAsync(string bucket, string key, string uploadId, IReadOnlyList<S3UploadedPart> parts, CancellationToken cancellationToken) =>
            Task.FromResult(new S3WriteResponse("v1", "multipart-etag", null));

        public Task AbortMultipartAsync(string bucket, string key, string uploadId, CancellationToken cancellationToken)
        {
            AbortCalled = true;
            return Task.CompletedTask;
        }

        public Task<S3ReadResponse> GetAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            LastBucket = bucket;
            LastKey = key;
            return GetException is null
                ? Task.FromResult(new S3ReadResponse(new MemoryStream(ReadBytes, writable: false), new Dictionary<string, string>()))
                : Task.FromException<S3ReadResponse>(GetException);
        }

        public Task<S3StatResponse?> StatAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            LastBucket = bucket;
            LastKey = key;
            return Task.FromResult(StatResult);
        }

        public Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken)
        {
            LastBucket = bucket;
            LastKey = key;
            return Task.CompletedTask;
        }

        public Task<Uri> CreateDownloadLinkAsync(string bucket, string key, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
        {
            LastBucket = bucket;
            LastKey = key;
            return Task.FromResult(new Uri("https://download.example.test/object"));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
