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
        var credentials = new BasicAWSCredentials(
            Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.AccessKeyVariable)!,
            Environment.GetEnvironmentVariable(S3CompatibleFactAttribute.SecretKeyVariable)!);
        var configuration = new AmazonS3Config
        {
            ServiceURL = endpoint,
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var amazonClient = new AmazonS3Client(credentials, configuration);
        var bucket = $"garagebalance-test-{Guid.NewGuid():N}";
        await amazonClient.PutBucketAsync(new PutBucketRequest { BucketName = bucket });

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
            "us-east-1",
            true,
            64L * 1024 * 1024,
            16 * 1024 * 1024,
            "EnvironmentOrWorkloadIdentity",
            StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat |
                StorageCapability.Delete | StorageCapability.DownloadLink | StorageCapability.ServerSideEncryption);
        using var provider = new S3CompatibleStorageProvider(
            destination,
            new AwsS3ObjectClient(amazonClient),
            TimeProvider.System);
        byte[] bytes = "garagebalance isolated s3 integration"u8.ToArray();
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var operationId = Guid.NewGuid();
        string? locator = null;

        try
        {
            await using var input = new MemoryStream(bytes, writable: false);
            var written = await provider.WriteAsync(
                new StorageWriteRequest(
                    operationId,
                    "database/integration.pgdump",
                    1,
                    bytes.Length,
                    sha256,
                    new Dictionary<string, string> { ["backup-kind"] = "integration" }),
                input,
                CancellationToken.None);
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
            await amazonClient.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket });
        }
    }
}
