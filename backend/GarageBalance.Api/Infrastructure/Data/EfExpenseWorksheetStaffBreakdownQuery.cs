using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

internal static class EfExpenseWorksheetStaffBreakdownQuery
{
    public static async Task<ExpenseWorksheetStaffBreakdownData> GetAsync(
        GarageBalanceDbContext dbContext,
        DateOnly businessDate,
        string businessTimeZoneId,
        Guid staffMemberId,
        Guid? expenseTypeId,
        DateOnly monthFrom,
        DateOnly monthTo,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var staffMember = await dbContext.StaffMembers
            .AsNoTracking()
            .Where(member => member.Id == staffMemberId)
            .Select(member => new
            {
                member.Rate,
                member.IsArchived,
                member.CreatedAtUtc,
                member.UpdatedAtUtc,
                SalaryAccrualDay = dbContext.ApplicationSettings
                    .Where(setting => setting.Key == ApplicationSettingsService.SalaryAccrualDayKey)
                    .Select(setting => setting.IntegerValue)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (staffMember is null)
        {
            return new ExpenseWorksheetStaffBreakdownData([], 0, 0m, 0m, 0m, 0m);
        }

        var salaryMonthTo = monthTo;
        var currentBusinessMonth = new DateOnly(businessDate.Year, businessDate.Month, 1);
        var salaryAccrualDay = staffMember.SalaryAccrualDay ?? ApplicationSettingsService.DefaultSalaryAccrualDay;
        if (monthTo >= currentBusinessMonth && (monthFrom > currentBusinessMonth || businessDate.Day < salaryAccrualDay))
        {
            salaryMonthTo = currentBusinessMonth.AddMonths(-1);
        }
        else if (monthTo > currentBusinessMonth)
        {
            salaryMonthTo = currentBusinessMonth;
        }

        var businessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(businessTimeZoneId);
        var staffCreatedMonth = MonthPeriod.Normalize(DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(staffMember.CreatedAtUtc, businessTimeZone).DateTime));
        var salaryMonthFrom = staffCreatedMonth > monthFrom ? staffCreatedMonth : monthFrom;
        var ratePeriods = await dbContext.StaffSalaryRatePeriods.AsNoTracking()
            .Where(period => period.StaffMemberId == staffMemberId && period.EffectiveFrom <= salaryMonthTo)
            .OrderBy(period => period.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var employmentPeriods = await dbContext.StaffEmploymentPeriods.AsNoTracking()
            .Where(period => period.StaffMemberId == staffMemberId && period.EffectiveFrom < salaryMonthTo.AddMonths(1))
            .OrderBy(period => period.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var staffUpdatedMonth = MonthPeriod.Normalize(DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(staffMember.UpdatedAtUtc, businessTimeZone).DateTime));
        var salaryEntries = new List<ExpenseWorksheetSupplierBreakdownEntryData>();
        for (var month = salaryMonthFrom; month <= salaryMonthTo; month = month.AddMonths(1))
        {
            if (!StaffSalaryTimeline.IsEmployed(
                    month,
                    staffCreatedMonth,
                    staffMember.IsArchived,
                    staffUpdatedMonth,
                    employmentPeriods))
            {
                continue;
            }

            salaryEntries.Add(new ExpenseWorksheetSupplierBreakdownEntryData(
                CreateSalaryEntryId(staffMemberId, month),
                "salary",
                month,
                null,
                StaffSalaryTimeline.GetRate(month, staffMember.Rate, ratePeriods),
                null,
                null,
                "salary",
                staffMember.CreatedAtUtc));
        }

        var adjustments = dbContext.StaffSalaryAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                adjustment.StaffMemberId == staffMemberId &&
                adjustment.AccountingMonth >= monthFrom &&
                adjustment.AccountingMonth <= monthTo)
            .Select(adjustment => new
            {
                adjustment.Id,
                EntryKind = adjustment.AdjustmentType,
                adjustment.AccountingMonth,
                OperationDate = (DateOnly?)null,
                adjustment.Amount,
                adjustment.DocumentNumber,
                Comment = (string?)adjustment.Reason,
                Source = (string?)null,
                adjustment.CreatedAtUtc,
                adjustment.IsCanceled,
                Version = (Guid?)adjustment.Version,
                adjustment.CancellationReason
            });
        var payments = dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation =>
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.StaffMemberId == staffMemberId &&
                (!expenseTypeId.HasValue || operation.ExpenseTypeId == expenseTypeId) &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .Select(operation => new
            {
                operation.Id,
                EntryKind = "payment",
                operation.AccountingMonth,
                OperationDate = (DateOnly?)operation.OperationDate,
                operation.Amount,
                operation.DocumentNumber,
                operation.Comment,
                Source = operation.ExpensePaymentSource,
                operation.CreatedAtUtc,
                operation.IsCanceled,
                Version = (Guid?)null,
                CancellationReason = (string?)null
            });
        var persistedEntries = adjustments.Concat(payments);
        var summaries = await persistedEntries
            .Where(item => !item.IsCanceled)
            .GroupBy(item => item.EntryKind)
            .Select(group => new
            {
                EntryKind = group.Key,
                Count = group.Count(),
                Amount = group.Sum(item => item.Amount)
            })
            .ToListAsync(cancellationToken);
        var persistedCount = await persistedEntries.CountAsync(cancellationToken);

        var persistedTake = offset > int.MaxValue - limit ? int.MaxValue : offset + limit;
        var rawItems = await persistedEntries
            .OrderByDescending(item => item.AccountingMonth)
            .ThenByDescending(item => item.OperationDate.HasValue)
            .ThenByDescending(item => item.OperationDate)
            .ThenBy(item => item.Id)
            .Take(persistedTake)
            .ToListAsync(cancellationToken);
        var persistedItems = rawItems.Select(item => new ExpenseWorksheetSupplierBreakdownEntryData(
            item.Id,
            item.EntryKind,
            item.AccountingMonth,
            item.OperationDate,
            item.Amount,
            item.DocumentNumber,
            item.Comment,
            item.Source,
            item.CreatedAtUtc,
            item.IsCanceled,
            item.Version,
            item.CancellationReason));
        var items = salaryEntries
            .Concat(persistedItems)
            .OrderByDescending(item => item.AccountingMonth)
            .ThenByDescending(item => item.OperationDate.HasValue)
            .ThenByDescending(item => item.OperationDate)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .ToList();
        var bonusSummary = summaries.FirstOrDefault(item => item.EntryKind == StaffSalaryAdjustmentTypes.Bonus);
        var penaltySummary = summaries.FirstOrDefault(item => item.EntryKind == StaffSalaryAdjustmentTypes.Penalty);
        var paymentSummary = summaries.FirstOrDefault(item => item.EntryKind == "payment");

        return new ExpenseWorksheetStaffBreakdownData(
            items,
            salaryEntries.Count + persistedCount,
            MoneyMath.RoundMoney(salaryEntries.Sum(entry => entry.Amount)),
            bonusSummary?.Amount ?? 0m,
            penaltySummary?.Amount ?? 0m,
            paymentSummary?.Amount ?? 0m);
    }

    private static Guid CreateSalaryEntryId(Guid staffMemberId, DateOnly accountingMonth)
    {
        var bytes = staffMemberId.ToByteArray();
        var monthKey = (accountingMonth.Year * 100) + accountingMonth.Month;
        BitConverter.TryWriteBytes(bytes.AsSpan(12, sizeof(int)), monthKey);
        return new Guid(bytes);
    }
}
