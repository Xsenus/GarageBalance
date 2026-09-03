using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfStaffSalaryAdjustmentRepository(GarageBalanceDbContext dbContext)
    : IStaffSalaryAdjustmentRepository
{
    public async Task<IAsyncDisposable> AcquireMonthlyLockAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        var normalizedMonth = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"staff-salary:{staffMemberId:N}:{normalizedMonth:yyyyMM}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(hash);
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_lock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new AdvisoryLockLease(connection, key, closeConnection);
        }
        catch
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }

            throw;
        }
    }

    public async Task<StaffSalaryAdjustmentTotals> GetTotalsAsync(
        Guid staffMemberId,
        DateOnly accountingMonth,
        Guid? excludedAdjustmentId,
        CancellationToken cancellationToken)
    {
        var totals = await dbContext.StaffSalaryAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                adjustment.StaffMemberId == staffMemberId &&
                adjustment.AccountingMonth == accountingMonth &&
                !adjustment.IsCanceled &&
                (!excludedAdjustmentId.HasValue || adjustment.Id != excludedAdjustmentId.Value))
            .GroupBy(_ => 1)
            .Select(group => new StaffSalaryAdjustmentTotals(
                group.Sum(adjustment =>
                    adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus
                        ? adjustment.Amount
                        : 0m),
                group.Sum(adjustment =>
                    adjustment.AdjustmentType == StaffSalaryAdjustmentTypes.Penalty
                        ? adjustment.Amount
                        : 0m)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? new StaffSalaryAdjustmentTotals(0m, 0m);
    }

    public Task<StaffSalaryAdjustment?> FindForUpdateAsync(Guid adjustmentId, CancellationToken cancellationToken) =>
        dbContext.StaffSalaryAdjustments
            .Include(adjustment => adjustment.StaffMember)
            .ThenInclude(staffMember => staffMember.Department)
            .SingleOrDefaultAsync(adjustment => adjustment.Id == adjustmentId, cancellationToken);

    public async Task ReloadAsync(StaffSalaryAdjustment adjustment, CancellationToken cancellationToken)
    {
        await dbContext.Entry(adjustment).ReloadAsync(cancellationToken);
        await dbContext.Entry(adjustment).Reference(item => item.StaffMember).LoadAsync(cancellationToken);
        await dbContext.Entry(adjustment.StaffMember).Reference(item => item.Department).LoadAsync(cancellationToken);
    }

    public void Add(StaffSalaryAdjustment adjustment)
    {
        dbContext.StaffSalaryAdjustments.Add(adjustment);
    }

    private sealed class AdvisoryLockLease(System.Data.Common.DbConnection connection, long key, bool closeConnection)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
