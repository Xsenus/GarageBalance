using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFreeformAccrualWorksheetTests
{
    [PostgreSqlFact]
    public async Task FreeformAccruals_ShareOnePayableRowAndPreserveReasonsAndFifoAllocations()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "FREEFORM-WORKSHEET-1" };
        var destination = await context.IncomeTypes.SingleAsync(item => item.Code == "other_payments");
        var template = new IrregularPayment { Name = destination.Name, Amount = 400m };
        var month = new DateOnly(2026, 9, 1);
        context.AddRange(garage, template);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);

        var first = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(garage.Id, null, "Первая работа", 100m, month, null),
            null, CancellationToken.None);
        var second = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(garage.Id, null, "Вторая работа", 200m, month, null),
            null, CancellationToken.None);
        var catalog = await service.CreateIrregularAccrualAsync(
            new CreateIrregularAccrualRequest(garage.Id, template.Id, template.Name, template.Amount, month, null),
            null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.True(catalog.Succeeded, catalog.ErrorMessage);

        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.IncomeTypeId == destination.Id && item.IrregularPaymentId is null && item.FeeCampaignId is null);
        Assert.Equal(first.Value!.IncomeTypeId, row.IncomeTypeId);
        Assert.Equal(first.Value.IncomeTypeName, row.IncomeTypeName);
        Assert.Equal(300m, row.AccrualAmount);
        Assert.Equal(300m, row.Debt);
        Assert.NotNull(row.Reason);
        Assert.Contains("Первая работа", row.Reason, StringComparison.Ordinal);
        Assert.Contains("Вторая работа", row.Reason, StringComparison.Ordinal);
        Assert.Equal(400m, Assert.Single(worksheet.Value.Rows, item => item.IrregularPaymentId == template.Id).Debt);

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garage.Id, first.Value.IncomeTypeId, month.AddDays(20), month,
                150m, null, "Частичная оплата общих разовых начислений"),
            null, CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        var allocations = await context.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.FinancialOperationId == payment.Value!.Id && item.IsActive)
            .ToListAsync();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(100m, Assert.Single(allocations, item => item.AccrualId == first.Value.Id).Amount);
        Assert.Equal(50m, Assert.Single(allocations, item => item.AccrualId == second.Value!.Id).Amount);
        Assert.DoesNotContain(allocations, item => item.AccrualId == catalog.Value!.Id);

        context.ChangeTracker.Clear();
        var reloaded = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(reloaded.Succeeded, reloaded.ErrorMessage);
        var paidRow = Assert.Single(reloaded.Value!.Rows, item => item.IncomeTypeId == destination.Id && item.IrregularPaymentId is null && item.FeeCampaignId is null);
        Assert.Equal(300m, paidRow.AccrualAmount);
        Assert.Equal(150m, paidRow.IncomeAmount);
        Assert.Equal(150m, paidRow.Debt);
        Assert.Equal(400m, Assert.Single(reloaded.Value.Rows, item => item.IrregularPaymentId == template.Id).Debt);

        var incomeType = await context.IncomeTypes.SingleAsync(item => item.Id == first.Value.IncomeTypeId);
        incomeType.Name = "Переименованные прочие оплаты";
        context.Accruals.Add(new Accrual
        {
            GarageId = garage.Id,
            IncomeTypeId = incomeType.Id,
            AccountingMonth = month,
            DueDate = month.AddMonths(1).AddDays(-1),
            OverdueFromDate = month.AddMonths(2),
            Amount = 25m,
            Source = AccrualSources.Manual,
            Comment = "Историческое начисление без отдельного основания"
        });
        await context.SaveChangesAsync();
        var renamed = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(renamed.Succeeded, renamed.ErrorMessage);
        var renamedRow = Assert.Single(renamed.Value!.Rows, item => item.IncomeTypeId == destination.Id && item.IrregularPaymentId is null && item.FeeCampaignId is null);
        Assert.Equal(incomeType.Name, renamedRow.IncomeTypeName);
        Assert.Equal(325m, renamedRow.AccrualAmount);
        Assert.Equal(150m, renamedRow.IncomeAmount);
        Assert.Equal(175m, renamedRow.Debt);
        Assert.NotNull(renamedRow.Reason);
        Assert.Contains("Первая работа", renamedRow.Reason, StringComparison.Ordinal);
        Assert.Contains("Вторая работа", renamedRow.Reason, StringComparison.Ordinal);
        Assert.Contains("Историческое начисление без отдельного основания", renamedRow.Reason, StringComparison.Ordinal);
        Assert.Equal("Первая работа", (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == first.Value.Id)).Basis);
        Assert.Equal("Вторая работа", (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == second.Value!.Id)).Basis);
        Assert.Equal(template.Name, Assert.Single(renamed.Value.Rows, item => item.IrregularPaymentId == template.Id).IncomeTypeName);
    }

    [PostgreSqlFact]
    public async Task FreeformAccruals_KeepOverpaymentInTheSameMonthlyRowAfterReload()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var garage = new Garage { Number = "FREEFORM-WORKSHEET-ADVANCE" };
        var month = new DateOnly(2026, 9, 1);
        Guid incomeTypeId;
        await using (var context = database.CreateContext())
        {
            context.Add(garage);
            await context.SaveChangesAsync();
            var service = FinanceServiceTestFactory.Create(context);
            var first = await service.CreateIrregularAccrualAsync(
                new CreateIrregularAccrualRequest(garage.Id, null, "Ремонт первой детали", 100m, month, null),
                null, CancellationToken.None);
            var second = await service.CreateIrregularAccrualAsync(
                new CreateIrregularAccrualRequest(garage.Id, null, "Ремонт второй детали", 200m, month, null),
                null, CancellationToken.None);
            Assert.True(first.Succeeded, first.ErrorMessage);
            Assert.True(second.Succeeded, second.ErrorMessage);
            incomeTypeId = first.Value!.IncomeTypeId;
            var paid = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(garage.Id, first.Value!.IncomeTypeId, month.AddDays(20), month, 400m, null, null),
                null, CancellationToken.None);
            Assert.True(paid.Succeeded, paid.ErrorMessage);
        }

        await using var verification = database.CreateContext();
        var worksheet = await FinanceServiceTestFactory.Create(verification)
            .GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var row = Assert.Single(worksheet.Value!.Rows, item => item.IncomeTypeId == incomeTypeId && item.IrregularPaymentId is null && item.FeeCampaignId is null);
        Assert.Equal(300m, row.AccrualAmount);
        Assert.Equal(300m, row.IncomeAmount);
        Assert.Equal(100m, row.AdvanceAmount);
        Assert.Equal(0m, row.Debt);
        Assert.Equal(100m, worksheet.Value.AdvanceTotal);
        Assert.Equal(-100m, worksheet.Value.ClosingBalance);
        Assert.NotNull(row.Reason);
        Assert.Contains("Ремонт первой детали", row.Reason, StringComparison.Ordinal);
        Assert.Contains("Ремонт второй детали", row.Reason, StringComparison.Ordinal);
    }
}
