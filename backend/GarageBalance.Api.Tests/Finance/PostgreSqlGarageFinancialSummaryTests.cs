using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageFinancialSummaryTests
{
    [PostgreSqlFact]
    public async Task Balance_IncludesFutureAccrualsExcludesCanceledRowsAndPreservesCredit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "SUMMARY-LOCAL", StartingBalance = 100m };
        var type = new IncomeType { Name = "Контроль баланса", Code = "summary_test" };
        var month = new DateOnly(2099, 9, 1);
        var payment = new FinancialOperation { Garage = garage, IncomeType = type, OperationKind = FinancialOperationKinds.Income, OperationDate = month, AccountingMonth = month, Amount = 50m };
        context.AddRange(payment,
            new Accrual { Garage = garage, IncomeType = type, AccountingMonth = month, DueDate = month.AddDays(29), OverdueFromDate = month.AddMonths(1), Amount = 200m, Source = AccrualSources.Manual },
            new Accrual { Garage = garage, IncomeType = type, AccountingMonth = month.AddMonths(1), Amount = 900m, Source = AccrualSources.Manual, IsCanceled = true },
            new FinancialOperation { Garage = garage, IncomeType = type, OperationKind = FinancialOperationKinds.Income, OperationDate = month, AccountingMonth = month, Amount = 999m, IsCanceled = true });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var result = await service.GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(250m, result.Value!.Balance);
        Assert.Equal(50m, result.Value.Total);
        payment.Amount = 400m;
        await context.SaveChangesAsync();
        var credit = await service.GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None);
        Assert.Equal(-100m, credit.Value!.Balance);
        Assert.Equal(0m, credit.Value.Total);
        payment.IsCanceled = true;
        await context.SaveChangesAsync();
        Assert.Equal(300m, (await service.GetGarageOverdueDebtAsync(garage.Id, CancellationToken.None)).Value!.Balance);
    }
}
