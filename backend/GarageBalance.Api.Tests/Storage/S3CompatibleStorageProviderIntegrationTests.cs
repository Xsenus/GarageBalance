using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Storage;

public sealed class S3CompatibleStorageProviderIntegrationTests
{
    [S3CompatibleFact]
    public async Task RealEndpoint_WriteStatReadLinkAndDelete_PreserveImmutableObject()
    {
        var endpoint = Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.EndpointVariable)!;
        var existingBucket = Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.ExistingBucketVariable);
        var region = Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.RegionVariable) ?? "us-east-1";
        var kmsKeyId = Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.KmsKeyIdVariable);
        var encryptionMode = string.IsNullOrWhiteSpace(kmsKeyId) ? S3EncryptionMode.SseS3 : S3EncryptionMode.SseKms;
        var credentials = new BasicAWSCredentials(
            Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.AccessKeyVariable)!,
            Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.SecretKeyVariable)!);
        var configuration = new AmazonS3Config
        {
            ServiceURL = endpoint,
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            ForcePathStyle = true,
            AuthenticationRegion = region,
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var amazonClient = new AmazonS3Client(credentials, configuration);
        var createsBucket = string.IsNullOrWhiteSpace(existingBucket);
        var bucket = createsBucket ? $"garagebalance-test-{Guid.NewGuid():N}" : existingBucket!;
        if (createsBucket)
        {
            await amazonClient.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        }

        var destination = new EffectiveStorageDestination(
            "integration-s3",
            StorageProviderType.S3Compatible,
            StorageDestinationState.Enabled,
            "isolated-test",
            "garagebalance",
            null,
            new Uri(endpoint),
            bucket,
            "private",
            region,
            true,
            5L * 1024 * 1024,
            5 * 1024 * 1024,
            "EnvironmentOrWorkloadIdentity",
            StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat |
                StorageCapability.Delete | StorageCapability.DownloadLink | StorageCapability.ServerSideEncryption |
                StorageCapability.MultipartUpload,
            encryptionMode,
            kmsKeyId);
        using var provider = new S3CompatibleStorageProvider(
            destination,
            new AwsS3ObjectClient(amazonClient, encryptionMode, kmsKeyId),
            TimeProvider.System);
        try
        {
            await VerifyRoundTripAsync("garagebalance isolated s3 integration"u8.ToArray());
            var multipartBytes = new byte[5 * 1024 * 1024 + 1];
            RandomNumberGenerator.Fill(multipartBytes);
            await VerifyRoundTripAsync(multipartBytes);
        }
        finally
        {
            if (createsBucket)
            {
                await amazonClient.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket });
            }
        }

        async Task VerifyRoundTripAsync(byte[] bytes)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var operationId = Guid.NewGuid();
            var writeRequest = new StorageWriteRequest(
                operationId,
                $"database/integration-{operationId:N}.pgdump",
                1,
                bytes.Length,
                sha256,
                new Dictionary<string, string> { ["backup-kind"] = "integration" });
            string? locator = provider.GetWriteLocator(writeRequest);

            try
            {
                await using var input = new MemoryStream(bytes, writable: false);
                var written = await provider.WriteAsync(writeRequest, input, CancellationToken.None);
                locator = written.NativeLocator;

                var stat = await provider.StatAsync(locator, CancellationToken.None);
                Assert.NotNull(stat);
                Assert.Equal(bytes.Length, stat.SizeBytes);
                Assert.Equal(sha256, stat.ProviderChecksum, ignoreCase: true);

                await using var stored = await provider.OpenReadAsync(locator, CancellationToken.None);
                using var output = new MemoryStream();
                await stored.CopyToAsync(output);
                Assert.Equal(bytes, output.ToArray());

                var link = await provider.GetDownloadLinkAsync(locator, TimeSpan.FromMinutes(2), CancellationToken.None);
                Assert.NotNull(link);
                Assert.Equal(endpoint.TrimEnd('/'), link.Url.GetLeftPart(UriPartial.Authority).TrimEnd('/'));

                await provider.DeleteAsync(locator, CancellationToken.None);
                locator = null;
                Assert.Null(await provider.StatAsync(written.NativeLocator, CancellationToken.None));
            }
            finally
            {
                if (locator is not null)
                {
                    await provider.DeleteAsync(locator, CancellationToken.None);
                }
            }
        }
    }
}
