using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Releases;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfApplicationUnitOfWork(GarageBalanceDbContext dbContext) : IApplicationUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChangeTracker.DetectChanges();
            var canceledDevices = dbContext.ChangeTracker.Entries<MeterDevice>()
                .Where(entry => entry.State == EntityState.Modified && entry.Entity.IsCanceled &&
                    !entry.OriginalValues.GetValue<bool>(nameof(MeterDevice.IsCanceled))).ToArray();
            if (canceledDevices.Length == 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            // A filtered unique index cannot order EF's two updates: vacate the active slot first,
            // then restore the preceding device and all readings/accruals/audit in the same transaction.
            var ownsTransaction = dbContext.Database.CurrentTransaction is null;
            var transaction = dbContext.Database.CurrentTransaction ?? await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var savepoint = $"meter_cancel_{Guid.NewGuid():N}";
            var savepointCreated = false;
            try
            {
                await transaction.CreateSavepointAsync(savepoint, cancellationToken);
                savepointCreated = true;
                foreach (var entry in canceledDevices)
                {
                    var device = entry.Entity;
                    var originalVersion = entry.OriginalValues.GetValue<Guid>(nameof(MeterDevice.Version));
                    var updated = await dbContext.MeterDevices.Where(item => item.Id == device.Id && item.Version == originalVersion && !item.IsCanceled)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsCanceled, true)
                            .SetProperty(item => item.CancellationReason, device.CancellationReason)
                            .SetProperty(item => item.CanceledAtUtc, device.CanceledAtUtc)
                            .SetProperty(item => item.Version, device.Version)
                            .SetProperty(item => item.UpdatedAtUtc, device.UpdatedAtUtc), cancellationToken);
                    if (updated != 1) throw new ApplicationConcurrencyException(new DbUpdateConcurrencyException("Meter device changed during cancellation."));
                    entry.OriginalValues.SetValues(entry.CurrentValues);
                    entry.State = EntityState.Unchanged;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
                savepointCreated = false;
                if (ownsTransaction) await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                if (savepointCreated) await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                if (ownsTransaction) await transaction.DisposeAsync();
            }
        }
        catch (DbUpdateConcurrencyException exception) when (
            exception.Entries.Any(entry => entry.Entity is MeterReading or MeterDevice or GarageMeterConnection or StaffSalaryAdjustment))
        {
            throw new ApplicationConcurrencyException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is MeterDevice or GarageMeterConnection || entry.Entity is MeterReading && entry.State == EntityState.Added))
        {
            throw new ApplicationPersistenceConflictException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is AppReleaseRecord or ExpensePaymentBatch or ExpensePaymentBatchOperation) &&
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApplicationPersistenceConflictException(exception);
        }
    }
}
