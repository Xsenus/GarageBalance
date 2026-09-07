using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlNegativeFundPaymentTests
{
    [PostgreSqlFact]
    public async Task NegativeFundConsent_DoesNotBypassBankOrCashAndPreservesReconciliation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var month = new DateOnly(2026, 9, 1);
        var fund = new Fund { Name = "Фонд подтверждения", NormalizedName = "ФОНД ПОДТВЕРЖДЕНИЯ", Balance = -10m };
        var type = new ExpenseType { Name = "Услуга подтверждения", Code = "consent_test" };
        var supplier = new Supplier { Name = "Поставщик подтверждения", Group = new SupplierGroup { Name = "Контроль согласия" }, SupplierService = new SupplierService { Name = "Услуга подтверждения" }, ExpenseType = type, ExpenseFund = fund };
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(supplier,
                new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Withdraw, Amount = 10m, BalanceBefore = 0m, BalanceAfter = -10m, Reason = "Синтетический начальный остаток", CreatedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) },
                new CashBankBalanceOperation { Account = CashBankAccounts.Bank, OperationKind = CashBankBalanceOperationKinds.OpeningBalance, Direction = CashBankBalanceDirections.Increase, OperationDate = month, Amount = 2m, Reason = "Синтетический остаток банка" });
            await seed.SaveChangesAsync();
        }
        var request = new CreateExpenseOperationRequest(supplier.Id, type.Id, month.AddDays(6), month, 1m, "CONSENT-PAYMENT", null,
            ExpensePaymentSource: ExpensePaymentSources.Bank, ExpenseFundId: fund.Id);
        async Task<FinanceResult<FinancialOperationDto>> PayAsync(CreateExpenseOperationRequest payment)
        {
            await using var context = database.CreateContext();
            return await FinanceServiceTestFactory.Create(context).CreateExpenseAsync(payment, null, CancellationToken.None);
        }
        Assert.Equal("fund_balance_insufficient", (await PayAsync(request)).ErrorCode);
        Assert.Equal("bank_amount_insufficient", (await PayAsync(request with { Amount = 3m, ConfirmNegativeFundBalance = true })).ErrorCode);
        Assert.Equal("cash_amount_insufficient", (await PayAsync(request with { ExpensePaymentSource = ExpensePaymentSources.Cash, ConfirmNegativeFundBalance = true })).ErrorCode);
        await using var verify = database.CreateContext();
        Assert.Equal(0, await verify.FinancialOperations.CountAsync());
        Assert.Equal(0, await verify.AuditEvents.CountAsync(item => item.Action == "finance.expense_created"));
        var fundService = new FundService(new EfFundRepository(verify), new AuditEventWriter(verify));
        var before = await fundService.GetReconciliationAsync(CancellationToken.None);
        Assert.True(before.IsReconciled);
        var paid = await PayAsync(request with { ConfirmNegativeFundBalance = true });
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        Assert.True(paid.Value!.NegativeFundBalanceConfirmed);
        var after = await fundService.GetReconciliationAsync(CancellationToken.None);
        Assert.True(after.IsReconciled);
        Assert.Equal(before.CashAndBankTotal - 1m, after.CashAndBankTotal);
        Assert.Equal(before.NamedFundTotal - 1m, after.NamedFundTotal);
        Assert.Equal(before.UnallocatedTotal, after.UnallocatedTotal);
        Assert.Equal(-11m, (await verify.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
        var withdrawal = Assert.Single(await verify.FundOperations.Where(item => item.SourceFinancialOperationId == paid.Value.Id).ToListAsync());
        Assert.Equal(1m, withdrawal.Amount);
        var audit = Assert.Single(await verify.AuditEvents.Where(item => item.Action == "finance.expense_created").ToListAsync());
        Assert.Contains("negativeFundBalanceConfirmed", audit.MetadataJson);
        Assert.Contains("true", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }
}
