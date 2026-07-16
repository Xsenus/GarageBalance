using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFinanceAvailableBalanceQuery(GarageBalanceDbContext dbContext) : IFinanceAvailableBalanceQuery
{
    private const int FinancialOperationCategory = 1;
    private const int BankDepositCategory = 2;

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
                    operation.ExpenseType != null &&
                    ((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                        cashExpenseTypeNames.Contains(operation.ExpenseType.Name))
                        ? operation.Amount
                        : 0m),
                BankExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    (operation.ExpenseType == null ||
                        !((operation.ExpenseType.Code != null && cashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                            cashExpenseTypeNames.Contains(operation.ExpenseType.Name)))
                        ? operation.Amount
                        : 0m)
            });
        var bankDepositQuery = dbContext.FundOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FundOperationKinds.Deposit)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = BankDepositCategory,
                IncomeTotal = 0m,
                BankDepositTotal = group.Sum(operation => operation.Amount),
                CashExpenseTotal = 0m,
                BankExpenseTotal = 0m
            });

        var rows = await financialOperationQuery
            .Concat(bankDepositQuery)
            .ToListAsync(cancellationToken);

        return new FinanceAvailableBalanceData(
            rows.Sum(row => row.IncomeTotal),
            rows.Sum(row => row.BankDepositTotal),
            rows.Sum(row => row.CashExpenseTotal),
            rows.Sum(row => row.BankExpenseTotal));
    }
}
