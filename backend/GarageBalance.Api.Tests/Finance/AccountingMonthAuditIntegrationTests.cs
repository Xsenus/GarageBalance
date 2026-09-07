using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AccountingMonthAuditIntegrationTests
{
    [PostgreSqlFact]
    public async Task MonthRoundTripPreservesPaymentAndRecordsBothMonths()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "MONTH-47", CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var incomeType = new IncomeType { Code = "month_audit", Name = "Month audit" };
        context.Garages.Add(garage);
        context.IncomeTypes.Add(incomeType);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var september = new DateOnly(2026, 9, 1);
        var october = september.AddMonths(1);
        var request = new CreateIncomeOperationRequest(garage.Id, incomeType.Id, september.AddDays(5), september, 364.62m, "MONTH-47", "Полная оплата сен.26");
        var created = await service.CreateIncomeAsync(request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var id = created.Value!.Id;
        var auditService = new AuditService(new EfAuditEventRepository(context));
        foreach (var (oldMonth, newMonth) in new[] { (september, october), (october, september) })
        {
            var result = await service.UpdateIncomeAsync(id, request with { AccountingMonth = newMonth }, null, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
            var stored = await context.FinancialOperations.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(newMonth, stored.AccountingMonth);
            Assert.Equal(request.Amount, stored.Amount);
            Assert.Equal(request.OperationDate, stored.OperationDate);
            Assert.Equal(request.DocumentNumber, stored.DocumentNumber);
            Assert.Equal(request.Comment, stored.Comment);
            Assert.False(stored.IsCanceled);
            var audit = await context.AuditEvents.AsNoTracking().Where(item => item.Action == "finance.income_updated").OrderByDescending(item => item.CreatedAtUtc).FirstAsync();
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.Equal("Расчетный месяц", metadata.RootElement.GetProperty("fieldName").GetString());
            Assert.Equal(oldMonth.ToString("MM.yyyy"), metadata.RootElement.GetProperty("oldValue").GetString());
            Assert.Equal(newMonth.ToString("MM.yyyy"), metadata.RootElement.GetProperty("newValue").GetString());
            var detail = await auditService.GetEventAsync(audit.Id, CancellationToken.None);
            Assert.Equal(oldMonth.ToString("MM.yyyy"), detail!.OldValue);
            Assert.Equal(newMonth.ToString("MM.yyyy"), detail.NewValue);
            Assert.Equal(364.62m, (await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None)).IncomeTotal);
        }
        Assert.Equal(2, await context.AuditEvents.CountAsync(item => item.Action == "finance.income_updated"));
    }
}
