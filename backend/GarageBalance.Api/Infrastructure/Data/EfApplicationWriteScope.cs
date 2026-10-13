using GarageBalance.Api.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageBalance.Api.Infrastructure.Data;

internal sealed class EfApplicationWriteScope(
    GarageBalanceDbContext context,
    IDbContextTransaction transaction,
    string? savepoint) : IApplicationWriteScope
{
    private bool completed;
    private bool disposed;
    private readonly Dictionary<object, TrackingSnapshot> trackedBefore = context.ChangeTracker.Entries()
        .ToDictionary(entry => entry.Entity,
            entry => new TrackingSnapshot(entry.State, entry.CurrentValues.Clone(), entry.OriginalValues.Clone()),
            ReferenceEqualityComparer.Instance);

    public static async Task<IApplicationWriteScope> BeginAsync(GarageBalanceDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } outer)
        {
            var name = $"gb_write_{Guid.NewGuid():N}";
            await outer.CreateSavepointAsync(name, cancellationToken);
            return new EfApplicationWriteScope(context, outer, name);
        }

        // Advisory leases can outlive the transaction. Keep their physical session
        // pinned until this scope and the enclosing leases have both been disposed.
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return new EfApplicationWriteScope(context, await context.Database.BeginTransactionAsync(cancellationToken), null);
        }
        catch
        {
            await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (completed) return;
        if (savepoint is null) await transaction.CommitAsync(cancellationToken);
        else await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
        completed = true;
    }

    public async Task RollbackAsync()
    {
        if (completed) return;
        try
        {
            if (savepoint is null) await transaction.RollbackAsync(CancellationToken.None);
            else
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
        }
        finally
        {
            completed = true;
            foreach (var entry in context.ChangeTracker.Entries().ToArray())
                if (!trackedBefore.ContainsKey(entry.Entity)) entry.State = EntityState.Detached;
            foreach (var (entity, snapshot) in trackedBefore)
            {
                var entry = context.Entry(entity);
                entry.CurrentValues.SetValues(snapshot.Current);
                entry.OriginalValues.SetValues(snapshot.Original);
                entry.State = snapshot.State;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            await RollbackAsync();
        }
        finally
        {
            if (savepoint is null)
            {
                try { await transaction.DisposeAsync(); }
                finally { await context.Database.CloseConnectionAsync(); }
            }
        }
    }

    private sealed record TrackingSnapshot(EntityState State, PropertyValues Current, PropertyValues Original);
}
