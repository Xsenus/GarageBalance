using System.Security.Cryptography;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Storage;

namespace GarageBalance.Api.Tests.Storage;

public sealed class LocalFileStorageProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"garagebalance-local-provider-{Guid.NewGuid():N}");

    [Fact]
    public async Task ProviderContract_WritesStatsReadsAndDeletesExactVerifiedBytes()
    {
        var provider = new LocalFileStorageProvider("local-hot", root);
        byte[] bytes = [1, 2, 3, 4, 5];
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using var source = new MemoryStream(bytes);

        var written = await provider.WriteAsync(
            new StorageWriteRequest(Guid.NewGuid(), "database/2026/copy.pgdump", 1, bytes.Length, sha256, new Dictionary<string, string>()),
            source,
            CancellationToken.None);

        Assert.Equal("database/2026/copy.pgdump", written.NativeLocator);
        Assert.Equal(sha256, written.ProviderChecksum);
        var stat = await provider.StatAsync(written.NativeLocator, CancellationToken.None);
        Assert.NotNull(stat);
        Assert.Equal(bytes.Length, stat.SizeBytes);
        Assert.Equal(sha256, stat.ProviderChecksum);
        await using (var read = await provider.OpenReadAsync(written.NativeLocator, CancellationToken.None))
        {
            using var copied = new MemoryStream();
            await read.CopyToAsync(copied);
            Assert.Equal(bytes, copied.ToArray());
        }
        Assert.Null(await provider.GetDownloadLinkAsync(written.NativeLocator, TimeSpan.FromMinutes(1), CancellationToken.None));
        await provider.DeleteAsync(written.NativeLocator, CancellationToken.None);
        Assert.Null(await provider.StatAsync(written.NativeLocator, CancellationToken.None));
    }

    [Fact]
    public async Task Write_RejectsChecksumMismatchAndRemovesTemporaryObject()
    {
        var provider = new LocalFileStorageProvider("local-hot", root);
        await using var source = new MemoryStream([1, 2, 3, 4]);

        var error = await Assert.ThrowsAsync<StorageProviderException>(() => provider.WriteAsync(
            new StorageWriteRequest(Guid.NewGuid(), "copy.pgdump", 1, 4, new string('a', 64), new Dictionary<string, string>()),
            source,
            CancellationToken.None));

        Assert.Equal(StorageErrorCategory.ChecksumOrStale, error.Category);
        Assert.Empty(Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task CommitVerifiedFile_MovesExistingLocalDumpWithoutCreatingSecondFullCopy()
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "copy.pgdump.tmp");
        byte[] bytes = [9, 8, 7, 6];
        await File.WriteAllBytesAsync(sourcePath, bytes);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var provider = new LocalFileStorageProvider("local-hot", root);

        var result = await provider.CommitVerifiedFileAsync(
            new StorageWriteRequest(Guid.NewGuid(), "copy.pgdump", 1, bytes.Length, sha256, new Dictionary<string, string>()),
            sourcePath,
            CancellationToken.None);

        Assert.False(File.Exists(sourcePath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, result.NativeLocator)));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task Provider_RejectsTraversalAndHonorsCancellation()
    {
        var provider = new LocalFileStorageProvider("local-hot", root);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.StatAsync("../outside", CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.OpenReadAsync("copy.pgdump", cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
