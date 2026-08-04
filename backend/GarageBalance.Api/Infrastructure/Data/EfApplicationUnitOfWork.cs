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
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (
            exception.Entries.Any(entry => entry.Entity is MeterReading))
        {
            throw new ApplicationConcurrencyException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is MeterReading && entry.State == EntityState.Added))
        {
            throw new ApplicationPersistenceConflictException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.Entries.Any(entry => entry.Entity is AppReleaseRecord) &&
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApplicationPersistenceConflictException(exception);
        }
    }
}
