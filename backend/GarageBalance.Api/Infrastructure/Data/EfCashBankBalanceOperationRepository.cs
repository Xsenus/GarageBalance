using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfCashBankBalanceOperationRepository(GarageBalanceDbContext dbContext)
    : ICashBankBalanceOperationRepository
{
    public void Add(CashBankBalanceOperation operation) =>
        dbContext.CashBankBalanceOperations.Add(operation);

    public async Task<CashBankBalanceOperationTotals> GetTotalsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.CashBankBalanceOperations
            .AsNoTracking()
            .GroupBy(operation => operation.Account)
            .Select(group => new
            {
                Account = group.Key,
                OpeningBalance = group.Sum(operation =>
                    operation.OperationKind == CashBankBalanceOperationKinds.OpeningBalance
                        ? operation.Direction == CashBankBalanceDirections.Increase
                            ? operation.Amount
                            : -operation.Amount
                        : 0m),
                NetAdjustment = group.Sum(operation =>
                    operation.Direction == CashBankBalanceDirections.Increase
                        ? operation.Amount
                        : -operation.Amount)
            })
            .ToListAsync(cancellationToken);

        var cash = rows.SingleOrDefault(row => row.Account == CashBankAccounts.Cash);
        var bank = rows.SingleOrDefault(row => row.Account == CashBankAccounts.Bank);
        return new CashBankBalanceOperationTotals(
            cash?.OpeningBalance ?? 0m,
            bank?.OpeningBalance ?? 0m,
            cash?.NetAdjustment ?? 0m,
            bank?.NetAdjustment ?? 0m);
    }

    public async Task<IReadOnlyList<CashBankBalanceOperation>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.CashBankBalanceOperations
            .AsNoTracking()
            .OrderByDescending(operation => operation.OperationDate)
            .ThenByDescending(operation => operation.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}
