using System.Globalization;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Storage;

namespace GarageBalance.Api.Tests.Storage;

public sealed class ReadableBackupLayoutTests
{
    [Theory]
    [InlineData("2026-09-28T07:30:59.0000000+00:00", "09_2026/sgk_28092026_143059")]
    [InlineData("2026-12-31T18:00:00.0000000+00:00", "01_2027/sgk_01012027_010000")]
    public void NameUsesOriginalBusinessTimeAndImmutableIdentity(string original, string expected)
    {
        var operation = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var created = DateTimeOffset.Parse(original, CultureInfo.InvariantCulture);
        var key = ReadableBackupLayout.BuildKey("backups", "Asia/Novosibirsk", created, operation, 1);
        Assert.Equal($"backups/{expected}_{operation:N}_g1.pgdump", key);
        Assert.NotEqual(key, ReadableBackupLayout.BuildKey("backups", "Asia/Novosibirsk", created, Guid.NewGuid(), 1));
        Assert.NotEqual(key, ReadableBackupLayout.BuildKey("backups", "Asia/Novosibirsk", created, operation, 2));
    }

    [Theory]
    [InlineData("../backups", "UTC", 1)]
    [InlineData("backups", "UTC", 0)]
    public void InvalidLayoutIsRejected(string prefix, string zone, long generation) =>
        Assert.Throws<ArgumentException>(() => ReadableBackupLayout.BuildKey(prefix, zone, DateTimeOffset.UtcNow, Guid.NewGuid(), generation));

    [Fact]
    public void MissingIdentityIsRejected() => Assert.Throws<ArgumentException>(() =>
        ReadableBackupLayout.BuildKey("backups", "UTC", DateTimeOffset.UtcNow, Guid.Empty, 1));

    [Fact]
    public async Task S3AllowsLegacyAndNewNamespaceButNotNeighbours()
    {
        var destination = new EffectiveStorageDestination("remote-a", StorageProviderType.S3Compatible, StorageDestinationState.Enabled,
            "remote-host", "garagebalance", null, new Uri("https://s3.test"), "bucket", "legacy", "us-east-1", true,
            64 * 1024 * 1024, 16 * 1024 * 1024, "DefaultChain", StorageCapability.Read | StorageCapability.Stat,
            BackupPrefix: "backups");
        var client = new StubClient();
        using var provider = new S3CompatibleStorageProvider(destination, client, TimeProvider.System);
        await provider.StatAsync("legacy/object", CancellationToken.None);
        await provider.StatAsync("backups/09_2026/object", CancellationToken.None);
        Assert.Equal(2, client.Stats);
        foreach (var path in new[] { "backups-other/object", "legacy-other/object", "backups/../object" })
            await Assert.ThrowsAnyAsync<Exception>(() => provider.StatAsync(path, CancellationToken.None));
        var request = new StorageWriteRequest(Guid.NewGuid(), "copy.pgdump", 1, 4, new string('a', 64), new Dictionary<string, string>());
        Assert.StartsWith("legacy/", provider.GetWriteLocator(request));
        Assert.StartsWith("legacy/", provider.GetWriteLocator(request with { Metadata = new Dictionary<string, string> { ["garagebalance-data-class"] = "RecoverySecrets" } }));
        Assert.Throws<ArgumentException>(() => provider.GetWriteLocator(request with { Metadata = new Dictionary<string, string> { ["garagebalance-data-class"] = "DatabaseBackup" } }));
        Assert.StartsWith("backups/", provider.GetWriteLocator(request with
        {
            Metadata = new Dictionary<string, string>
            {
                ["garagebalance-data-class"] = "DatabaseBackup",
                [ReadableBackupLayout.CreatedAtMetadataKey] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            }
        }));
        Assert.Null(ReadableBackupLayout.TryBuildKey(destination with { BackupPrefix = null }, request));
    }

    private sealed class StubClient : IS3ObjectClient
    {
        public int Stats { get; private set; }
        public Task<S3StatResponse?> StatAsync(string bucket, string key, CancellationToken token) { Stats++; return Task.FromResult<S3StatResponse?>(null); }
        public Task<S3WriteResponse> PutAsync(string bucket, string key, Stream content, long size, string checksum, IReadOnlyDictionary<string, string> metadata, CancellationToken token) => throw new NotSupportedException();
        public Task<S3ReadResponse> GetAsync(string bucket, string key, CancellationToken token) => throw new NotSupportedException();
        public Task<string> BeginMultipartAsync(string bucket, string key, IReadOnlyDictionary<string, string> metadata, CancellationToken token) => throw new NotSupportedException();
        public Task<S3UploadedPart> UploadPartAsync(string bucket, string key, string uploadId, int partNumber, Stream content, long size, string checksum, bool isLastPart, CancellationToken token) => throw new NotSupportedException();
        public Task<S3WriteResponse> CompleteMultipartAsync(string bucket, string key, string uploadId, IReadOnlyList<S3UploadedPart> parts, CancellationToken token) => throw new NotSupportedException();
        public Task AbortMultipartAsync(string bucket, string key, string uploadId, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string bucket, string key, CancellationToken token) => throw new NotSupportedException();
        public Task<Uri> CreateDownloadLinkAsync(string bucket, string key, DateTimeOffset expires, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
