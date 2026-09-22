using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageOptionsTests
{
    [Fact]
    public void LegacyConfiguration_UsesSingleLocalDestinationWithoutCloudCredentials()
    {
        var options = new StorageOptions();
        var validation = new StorageOptionsValidator().Validate(null, options);
        var resolver = new StorageConfigurationResolver(
            Options.Create(options),
            Options.Create(new DatabaseBackupOptions { Directory = "D:/garage-backups" }));

        var effective = resolver.Resolve();

        Assert.True(validation.Succeeded);
        Assert.Equal(StorageMode.Single, effective.Mode);
        var destination = Assert.Single(effective.Destinations);
        Assert.Equal("local-hot", destination.Id);
        Assert.Equal("D:/garage-backups", destination.RootPath);
        Assert.Equal(StorageProviderType.LocalFileSystem, destination.Type);
        Assert.Null(destination.Endpoint);
        Assert.Equal(1, Assert.Single(effective.Policies).RequiredIndependentCopies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsyncMirror_AcceptsOneOrTwoIndependentOffsiteDestinations(bool includeSecondOffsite)
    {
        var options = CreateValidAsyncMirror(includeSecondOffsite);

        var result = new StorageOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
    }

    [Fact]
    public void Validator_RejectsDuplicateUnknownDisabledAndNonIndependentDestinations()
    {
        var options = CreateValidAsyncMirror(includeSecondOffsite: false);
        options.Destinations.Add(CreateS3("offsite-a", "host-a", "https://s3-b.example.test"));
        options.Pools[0].DestinationIds.Add("missing-destination");
        options.Policies[0] = new StoragePolicyOptions
        {
            Id = "database-backups",
            DataClass = StorageDataClass.DatabaseBackup,
            PoolId = "database-backups",
            RequiredIndependentCopies = 3,
            DesiredCopies = 3,
            MinimumOffsiteCopies = 2
        };

        var result = new StorageOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("duplicated", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("unknown destination", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("copy count", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsUnsafeEndpointTenantAndUnsupportedCapability()
    {
        var options = CreateValidAsyncMirror(includeSecondOffsite: false);
        options.Destinations[1] = new StorageDestinationOptions
        {
            Id = "offsite-a",
            Type = StorageProviderType.S3Compatible,
            FailureDomain = "host-a",
            TenantId = "other-tenant",
            Endpoint = "http://169.254.169.254/latest/meta-data",
            Bucket = "private-backups",
            Prefix = "garagebalance",
            AllowedEndpointHosts = ["s3-a.example.test"],
            Capabilities = ["Read", "Write", "Stat", "MagicVendorCapability"]
        };

        var result = new StorageOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("another tenant", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("must use HTTPS", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("allowlisted", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("unsupported capability", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AllowsExplicitInsecureLoopbackOnlyForIsolatedTests()
    {
        var options = CreateValidAsyncMirror(includeSecondOffsite: false);
        options.Destinations[1] = new StorageDestinationOptions
        {
            Id = "offsite-a",
            Type = StorageProviderType.S3Compatible,
            FailureDomain = "host-a",
            Endpoint = "http://127.0.0.1:9000",
            Bucket = "private-backups",
            Prefix = "garagebalance",
            AllowInsecureLoopbackEndpoint = true,
            AllowedEndpointHosts = ["127.0.0.1"],
            Capabilities = ["Read", "Write", "Stat", "Delete"]
        };

        var result = new StorageOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
    }

    [Theory]
    [InlineData("../backup.pgdump")]
    [InlineData("backups//copy.pgdump")]
    [InlineData("/backups/copy.pgdump")]
    [InlineData("backups/./copy.pgdump")]
    [InlineData("backups/copy.pgdump/")]
    public void ObjectKeyNormalizer_RejectsTraversalAndAmbiguousPaths(string key)
    {
        Assert.Throws<ArgumentException>(() => StorageObjectKey.Normalize(key));
    }

    [Fact]
    public void ObjectKeyNormalizer_NormalizesDirectorySeparators()
    {
        Assert.Equal("database/2026/copy.pgdump", StorageObjectKey.Normalize("database\\2026\\copy.pgdump"));
    }

    [Fact]
    public void PublicStorageOptions_DoNotExposeCredentialValues()
    {
        var propertyNames = typeof(StorageDestinationOptions).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AccessKey", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    private static StorageOptions CreateValidAsyncMirror(bool includeSecondOffsite)
    {
        var destinations = new List<StorageDestinationOptions>
        {
            new()
            {
                Id = "local-hot",
                Type = StorageProviderType.LocalFileSystem,
                FailureDomain = "local-host",
                RootPath = "D:/garage-backups",
                Capabilities = ["Read", "Write", "Stat", "Delete"]
            },
            CreateS3("offsite-a", "host-a", "https://s3-a.example.test")
        };
        if (includeSecondOffsite)
        {
            destinations.Add(CreateS3("offsite-b", "host-b", "https://s3-b.example.test"));
        }

        return new StorageOptions
        {
            Mode = StorageMode.AsyncMirror,
            Destinations = destinations,
            Pools =
            [
                new StoragePoolOptions
                {
                    Id = "database-backups",
                    DestinationIds = destinations.Select(destination => destination.Id).ToList()
                }
            ],
            Policies =
            [
                new StoragePolicyOptions
                {
                    Id = "database-backups",
                    DataClass = StorageDataClass.DatabaseBackup,
                    PoolId = "database-backups",
                    RequiredIndependentCopies = 2,
                    MinimumOffsiteCopies = 1,
                    DesiredCopies = includeSecondOffsite ? 3 : 2
                }
            ]
        };
    }

    private static StorageDestinationOptions CreateS3(string id, string failureDomain, string endpoint) => new()
    {
        Id = id,
        Type = StorageProviderType.S3Compatible,
        FailureDomain = failureDomain,
        Endpoint = endpoint,
        Bucket = "private-backups",
        Prefix = "garagebalance",
        AllowedEndpointHosts = [new Uri(endpoint).Host],
        Capabilities = ["Read", "Write", "Stat", "Delete", "ServerSideEncryption"]
    };
}
