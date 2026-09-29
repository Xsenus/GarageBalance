using System.Data;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfDailyServicePaymentReportQuery(GarageBalanceDbContext dbContext) : IDailyServicePaymentReportQuery
{
    public async Task<DailyServicePaymentReportData> GetRowsAsync(DateOnly throughDate, Guid? garageId, int offset, int limit, CancellationToken cancellationToken, bool forExport = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        offset = Math.Max(0, offset);
        limit = QueryLimits.NormalizePageSize(limit, maximumSize: forExport ? QueryLimits.MaximumReportExportRows : QueryLimits.MaximumPageSize);
        // Page, day totals and month totals must describe the same committed payments.
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var from = new DateOnly(throughDate.Year, throughDate.Month, 1);
        var payments = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income && !operation.IsCanceled
                && operation.GarageId != null && operation.OperationDate >= from && operation.OperationDate <= throughDate);
        if (garageId.HasValue)
        {
            payments = payments.Where(operation => operation.GarageId == garageId.Value);
        }
        var genericPayments = payments.Where(operation => operation.IncomeTypeId == null
            && operation.FeeCampaignId == null && operation.IrregularPaymentId == null);
        var typedPayments = payments.Where(operation => operation.IncomeTypeId != null
            || operation.FeeCampaignId != null || operation.IrregularPaymentId != null).Select(operation => new
            {
                Date = operation.OperationDate,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                operation.Amount,
                Code = operation.FeeCampaignId != null || operation.IrregularPaymentId != null ? "other" : operation.IncomeType!.Code,
            });
        var allocatedPayments = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation => allocation.IsActive && !allocation.Accrual.IsCanceled)
            .Join(genericPayments, allocation => allocation.FinancialOperationId, operation => operation.Id,
                (allocation, operation) => new
                {
                    Date = operation.OperationDate,
                    GarageId = operation.GarageId!.Value,
                    GarageNumber = operation.Garage!.Number,
                    allocation.Amount,
                    Code = allocation.Accrual.FeeCampaignId != null || allocation.Accrual.IrregularPaymentId != null
                        ? "other" : allocation.Accrual.IncomeType.Code,
                });
        var unallocatedPayments = genericPayments.Select(operation => new
        {
            Date = operation.OperationDate,
            GarageId = operation.GarageId!.Value,
            GarageNumber = operation.Garage!.Number,
            Amount = operation.Amount - (dbContext.AccrualPaymentAllocations
                .Where(allocation => allocation.FinancialOperationId == operation.Id && allocation.IsActive && !allocation.Accrual.IsCanceled)
                .Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
            Code = (string?)"other",
        });
        if (await unallocatedPayments.AnyAsync(payment => payment.Amount < 0m, cancellationToken))
        {
            throw new InvalidOperationException("Распределённая сумма общей оплаты превышает платёж. Проверьте распределение перед формированием отчёта.");
        }
        // Typed receipts already identify the service. Only legacy/general receipts
        // are split by active allocations; their unallocated remainder is counted once.
        var classified = typedPayments.Concat(allocatedPayments).Concat(unallocatedPayments.Where(payment => payment.Amount != 0m));
        var grouped = classified.GroupBy(payment => new { payment.Date, payment.GarageId, payment.GarageNumber })
            .Select(group => new
            {
                group.Key.Date,
                group.Key.GarageId,
                group.Key.GarageNumber,
                Electricity = group.Sum(payment => payment.Code == "electricity" ? payment.Amount : 0m),
                Water = group.Sum(payment => payment.Code == "water" ? payment.Amount : 0m),
                Trash = group.Sum(payment => payment.Code == "trash" ? payment.Amount : 0m),
                OutdoorLighting = group.Sum(payment => payment.Code == "outdoor_lighting" ? payment.Amount : 0m),
                Membership = group.Sum(payment => payment.Code == "membership" ? payment.Amount : 0m),
                Target = group.Sum(payment => payment.Code == "target" ? payment.Amount : 0m),
                Other = group.Sum(payment => payment.Code == null || (payment.Code != "electricity" && payment.Code != "water" && payment.Code != "trash"
                    && payment.Code != "outdoor_lighting" && payment.Code != "membership" && payment.Code != "target") ? payment.Amount : 0m),
            });
        var count = await grouped.CountAsync(cancellationToken);
        var hasOther = await classified.AnyAsync(payment => payment.Code == null || (payment.Code != "electricity"
            && payment.Code != "water" && payment.Code != "trash" && payment.Code != "outdoor_lighting"
            && payment.Code != "membership" && payment.Code != "target"), cancellationToken);
        var page = await grouped.OrderBy(row => row.Date).ThenBy(row => row.GarageNumber.Length)
            .ThenBy(row => row.GarageNumber).ThenBy(row => row.GarageId).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        // Only daily aggregates (at most 31 rows) are materialized outside the bounded page.
        var days = await grouped.GroupBy(row => row.Date).OrderBy(group => group.Key)
            .Select(group => new
            {
                Date = group.Key,
                Electricity = group.Sum(row => row.Electricity),
                Water = group.Sum(row => row.Water),
                Trash = group.Sum(row => row.Trash),
                OutdoorLighting = group.Sum(row => row.OutdoorLighting),
                Membership = group.Sum(row => row.Membership),
                Target = group.Sum(row => row.Target),
                Other = group.Sum(row => row.Other),
            }).ToListAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return new DailyServicePaymentReportData(
            page.Select(row => new DailyServicePaymentRow(row.Date, row.GarageId, row.GarageNumber,
                new(row.Electricity, row.Water, row.Trash, row.OutdoorLighting, row.Membership, row.Target, row.Other))).ToArray(),
            days.Select(day => new DailyServicePaymentDayTotal(day.Date,
                new(day.Electricity, day.Water, day.Trash, day.OutdoorLighting, day.Membership, day.Target, day.Other))).ToArray(),
            new(days.Sum(day => day.Electricity), days.Sum(day => day.Water), days.Sum(day => day.Trash),
                days.Sum(day => day.OutdoorLighting), days.Sum(day => day.Membership), days.Sum(day => day.Target), days.Sum(day => day.Other)),
            count, hasOther, offset, limit);
    }
}
