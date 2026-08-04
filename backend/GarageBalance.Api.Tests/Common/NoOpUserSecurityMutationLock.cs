using GarageBalance.Api.Application.Auth;

namespace GarageBalance.Api.Tests.Common;

internal sealed class NoOpUserSecurityMutationLock : IUserSecurityMutationLock
{
    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable>(NoOpLease.Instance);

    private sealed class NoOpLease : IAsyncDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
