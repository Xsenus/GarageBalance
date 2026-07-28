using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinanceAvailableBalanceQuery(GarageBalanceDbContext dbContext) : IFinanceAvailableBalanceQuery
{
    private const int FinancialOperationCategory = 1;
    private const int BankDepositCategory = 2;
    private const int BalanceAdjustmentCategory = 3;
    private const long CashBalanceLockKey = 0x474243415348;
    private const long BankBalanceLockKey = 0x474242414E4B;

    public async Task<IAsyncDisposable> AcquireUpdateLockAsync(
        bool cashExpense,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = cashExpense ? CashBalanceLockKey : BankBalanceLockKey;
        try
        {
            await ExecuteAdvisoryLockCommandAsync(connection, "SELECT pg_advisory_lock(@lock_key)", lockKey, cancellationToken);
            return new PostgreSqlAdvisoryLockLease(connection, lockKey, closeConnection);
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

    public async Task<FinanceAvailableBalanceData> GetAsync(
        string[] cashExpenseTypeCodes,
        string[] cashExpenseTypeNames,
        CancellationToken cancellationToken)
    {
        var financialOperationQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = FinancialOperationCategory,
                IncomeTotal = group.Sum(operation => operation.OperationKind == FinancialOperationKinds.Income ? operation.Amount : 0m),
                BankDepositTotal = 0m,
                CashExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    (operation.ExpensePaymentSource == ExpensePaymentSources.Cash ||
                        (operation.ExpensePaymentSource == null &&
                            (operation.ExpensePaymentType == ExpensePaymentTypes.WithoutReceipt ||
                                (operation.ExpensePaymentType == null &&
                            operation.ExpenseType != null &&
                            ((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                                    cashExpenseTypeNames.Contains(operation.ExpenseType.Name))))))
                        ? operation.Amount
                        : 0m),
                BankExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    (operation.ExpensePaymentSource == ExpensePaymentSources.Bank ||
                        (operation.ExpensePaymentSource == null &&
                            operation.ExpensePaymentType != ExpensePaymentTypes.WithoutReceipt &&
                            (operation.ExpensePaymentType != null ||
                                operation.ExpenseType == null ||
                                !((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                                    cashExpenseTypeNames.Contains(operation.ExpenseType.Name)))))
                        ? operation.Amount
                        : 0m),
                CashAdjustmentTotal = 0m,
                BankAdjustmentTotal = 0m
            });
        var bankDepositQuery = dbContext.CashBankTransfers.AsNoTracking()
            .Where(transfer => !transfer.IsCanceled)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = BankDepositCategory,
                IncomeTotal = 0m,
                BankDepositTotal = group.Sum(transfer => transfer.Amount),
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                CashAdjustmentTotal = 0m,
                BankAdjustmentTotal = 0m
            });
        var balanceAdjustmentQuery = dbContext.CashBankBalanceOperations.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = BalanceAdjustmentCategory,
                IncomeTotal = 0m,
                BankDepositTotal = 0m,
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m,
                CashAdjustmentTotal = group.Sum(operation =>
                    operation.Account == CashBankAccounts.Cash
                        ? operation.Direction == CashBankBalanceDirections.Increase
                            ? operation.Amount
                            : -operation.Amount
                        : 0m),
                BankAdjustmentTotal = group.Sum(operation =>
                    operation.Account == CashBankAccounts.Bank
                        ? operation.Direction == CashBankBalanceDirections.Increase
                            ? operation.Amount
                            : -operation.Amount
                        : 0m)
            });

        var rows = await financialOperationQuery
            .Concat(bankDepositQuery)
            .Concat(balanceAdjustmentQuery)
            .ToListAsync(cancellationToken);

        return new FinanceAvailableBalanceData(
            rows.Sum(row => row.IncomeTotal),
            rows.Sum(row => row.BankDepositTotal),
            rows.Sum(row => row.CashExpenseTotal),
            rows.Sum(row => row.BankExpenseTotal))
        {
            CashAdjustmentTotal = rows.Sum(row => row.CashAdjustmentTotal),
            BankAdjustmentTotal = rows.Sum(row => row.BankAdjustmentTotal)
        };
    }

    private static async Task ExecuteAdvisoryLockCommandAsync(
        DbConnection connection,
        string commandText,
        long lockKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class PostgreSqlAdvisoryLockLease(
        DbConnection connection,
        long lockKey,
        bool closeConnection) : IAsyncDisposable
    {
        private bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                await ExecuteAdvisoryLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_unlock(@lock_key)",
                    lockKey,
                    CancellationToken.None);
            }
            finally
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
