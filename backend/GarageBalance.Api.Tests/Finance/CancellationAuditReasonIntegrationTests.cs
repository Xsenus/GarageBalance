using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class CancellationAuditReasonIntegrationTests
{
    [PostgreSqlFact]
    public async Task PostgreSqlCancellationsStoreOneUserReasonAndExportIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "REASON-43", InitialWaterMeterValue = 0 };
        var income = new IncomeType { Code = "reason_income", Name = "Reason income" };
        var expense = new ExpenseType { Code = "reason_expense", Name = "Reason expense" };
        var supplier = new Supplier { Name = "Reason supplier", Group = new SupplierGroup { Name = "Reason group" } };
        var staff = new StaffMember { FullName = "Reason employee", Department = new StaffDepartment { Name = "Reason department" } };
        var month = new DateOnly(2026, 6, 1);
        var operations = new[]
        {
            new FinancialOperation { OperationKind = FinancialOperationKinds.Income, Garage = garage, IncomeType = income },
            new FinancialOperation { OperationKind = FinancialOperationKinds.Expense, Supplier = supplier, ExpenseType = expense, ExpensePaymentSource = "bank" },
            new FinancialOperation { OperationKind = FinancialOperationKinds.Expense, StaffMember = staff, ExpenseType = expense, ExpensePaymentSource = "bank" }
        };
        foreach (var operation in operations)
        {
            operation.Comment = new string('к', 1000);
            operation.Amount = 1m;
            operation.AccountingMonth = month;
            operation.OperationDate = month.AddDays(19);
            context.FinancialOperations.Add(operation);
        }
        var accrual = new Accrual { Garage = garage, IncomeType = income, Amount = 1, AccountingMonth = month, DueDate = month.AddDays(25), Source = AccrualSources.Manual };
        var supplierAccrual = new SupplierAccrual { Supplier = supplier, ExpenseType = expense, Amount = 1, AccountingMonth = month, Source = AccrualSources.Manual };
        var reading = new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = month, ReadingDate = month.AddDays(19), CurrentValue = 0, PreviousValue = 0, Consumption = 0 };
        supplierAccrual.Comment = new string('к', 1000);
        reading.Comment = new string('к', 1000);
        context.Accruals.Add(accrual);
        context.SupplierAccruals.Add(supplierAccrual);
        context.MeterReadings.Add(reading);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        const string prefix = "Возврат за сен.26. Сумма 100.60.\n";
        var reason = prefix + new string('я', 500 - prefix.Length);
        var request = new CancelFinanceEntryRequest(reason);
        foreach (var operation in operations)
        {
            var result = await service.CancelOperationAsync(operation.Id, request, null, CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        Assert.True((await service.CancelAccrualAsync(accrual.Id, request, null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CancelSupplierAccrualAsync(supplierAccrual.Id, request, null, CancellationToken.None)).Succeeded);
        Assert.True((await service.CancelMeterReadingAsync(reading.Id, request, null, CancellationToken.None)).Succeeded);
        var expectedComment = $"{new string('к', 1000)}{Environment.NewLine}Отменено: {reason}";
        Assert.All(await context.FinancialOperations.AsNoTracking().ToListAsync(), item => Assert.Equal(expectedComment, item.Comment));
        Assert.Equal(expectedComment, (await context.SupplierAccruals.AsNoTracking().SingleAsync()).Comment);
        Assert.Equal(expectedComment, (await context.MeterReadings.AsNoTracking().SingleAsync()).Comment);
        var events = await context.AuditEvents.AsNoTracking().Where(item => item.Action == "finance.operation_canceled" || item.Action == "finance.accrual_canceled" || item.Action == "finance.supplier_accrual_canceled" || item.Action == "finance.meter_reading_canceled").ToListAsync();
        Assert.Equal(6, events.Count);
        foreach (var audit in events)
        {
            using var metadata = JsonDocument.Parse(audit.MetadataJson!);
            Assert.Equal(reason, metadata.RootElement.GetProperty("reason").GetString());
            Assert.Contains(reason, audit.Summary);
            Assert.Equal(1, audit.Summary.Split("Причина:", StringSplitOptions.None).Length - 1);
        }
        Assert.All(await context.FinancialOperations.AsNoTracking().ToListAsync(), item => Assert.True(item.IsCanceled));
        Assert.Equal(0m, (await service.GetSummaryAsync(new FinancialOperationListRequest(null, null, null, null), CancellationToken.None)).IncomeTotal);
        var auditService = new AuditService(new EfAuditEventRepository(context));
        var filter = new AuditEventListRequest(null, null, "finance.operation_canceled", null);
        Assert.All(await auditService.GetEventsAsync(filter, CancellationToken.None), item => Assert.Equal(reason, item.Reason));
        Assert.Contains(reason, Encoding.UTF8.GetString((await auditService.ExportEventsCsvAsync(filter, CancellationToken.None)).Content));
        using var stream = new MemoryStream((await auditService.ExportEventsXlsxAsync(filter, CancellationToken.None)).Content);
        using var archive = new ZipArchive(stream);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        Assert.Contains("Возврат за сен.26. Сумма 100.60.", await reader.ReadToEndAsync());
    }
}
