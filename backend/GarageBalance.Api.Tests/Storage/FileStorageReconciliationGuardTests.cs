using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class FileStorageReconciliationGuardTests
{
    [Fact]
    public async Task ReadOnlyPeekDoesNotCreateOrRewriteMaintenanceArtifacts()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-reconciliation-peek-").FullName;
        try
        {
            var guard = Create(root);
            Assert.False((await guard.PeekStateAsync(CancellationToken.None)).Paused);
            Assert.Empty(Directory.GetFiles(root));
            await guard.PauseAsync(11, CancellationToken.None);
            var path = Path.Combine(root, ".storage-reconciliation.json");
            var before = await File.ReadAllBytesAsync(path);
            Assert.True((await guard.PeekStateAsync(CancellationToken.None)).Paused);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PauseSurvivesRestartAndOnlyExplicitReasonedResumeClearsIt()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-reconciliation-state-").FullName;
        try
        {
            var guard = Create(root);
            Assert.False((await guard.GetStateAsync(CancellationToken.None)).Paused);
            Assert.True((await guard.PauseAsync(11, CancellationToken.None)).Paused);
            var restarted = Create(root);
            var state = await restarted.GetStateAsync(CancellationToken.None);
            Assert.True(state.Paused);
            Assert.True(state.PersistenceAvailable);
            Assert.Equal(11, state.AnomalyCount);
            await Assert.ThrowsAsync<ArgumentException>(() => restarted.ResumeAsync(" ", CancellationToken.None));
            Assert.True((await Create(root).GetStateAsync(CancellationToken.None)).Paused);
            var resumed = await restarted.ResumeAsync("Storage checked by operator", CancellationToken.None);
            Assert.False(resumed.Paused);
            Assert.NotNull(resumed.ResumedAtUtc);
            Assert.False((await Create(root).GetStateAsync(CancellationToken.None)).Paused);
            var json = await File.ReadAllTextAsync(Path.Combine(root, ".storage-reconciliation.json"));
            Assert.Contains("Storage checked by operator", json, StringComparison.Ordinal);
            Assert.Contains("paused", json, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidOrLockedStateFailsClosedAndResumeDoesNotPretendToSucceed()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-reconciliation-invalid-").FullName;
        try
        {
            var statePath = Path.Combine(root, ".storage-reconciliation.json");
            await File.WriteAllTextAsync(statePath, "not-json");
            var guard = Create(root);
            var invalid = await guard.GetStateAsync(CancellationToken.None);
            Assert.True(invalid.Paused);
            Assert.False(invalid.PersistenceAvailable);
            Assert.False((await guard.ResumeAsync("Operator recovered invalid state", CancellationToken.None)).Paused);
            await using (var held = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var unavailable = await guard.GetStateAsync(CancellationToken.None);
                Assert.True(unavailable.Paused);
                Assert.False(unavailable.PersistenceAvailable);
                await Assert.ThrowsAsync<IOException>(() => guard.ResumeAsync("Operator checked storage", CancellationToken.None));
            }
            Assert.False((await guard.GetStateAsync(CancellationToken.None)).Paused);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationDoesNotClearPreviouslyPersistedPause()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-reconciliation-cancel-").FullName;
        try
        {
            var guard = Create(root);
            await guard.PauseAsync(11, CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.ResumeAsync("Operator approved resume", new CancellationToken(true)));
            Assert.True((await Create(root).GetStateAsync(CancellationToken.None)).Paused);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static FileStorageReconciliationGuard Create(string root) => new(
        new StorageConfigurationResolver(Options.Create(new StorageOptions()),
            Options.Create(new DatabaseBackupOptions { Directory = root })), TimeProvider.System);
}
