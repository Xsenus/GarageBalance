using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

internal static class EfExpenseWorksheetStaffBreakdownQuery
{
    public static async Task<ExpenseWorksheetStaffBreakdownData> GetAsync(
        GarageBalanceDbContext dbContext,
        DateOnly businessDate,
        Guid staffMemberId,
        Guid expenseTypeId,
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
                member.CreatedAtUtc,
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

        var staffCreatedMonth = new DateOnly(
            staffMember.CreatedAtUtc.UtcDateTime.Year,
            staffMember.CreatedAtUtc.UtcDateTime.Month,
            1);
        var salaryMonthFrom = staffCreatedMonth > monthFrom ? staffCreatedMonth : monthFrom;
        var salaryEntries = new List<ExpenseWorksheetSupplierBreakdownEntryData>();
        for (var month = salaryMonthFrom; month <= salaryMonthTo; month = month.AddMonths(1))
        {
            salaryEntries.Add(new ExpenseWorksheetSupplierBreakdownEntryData(
                CreateSalaryEntryId(staffMemberId, month),
                "salary",
                month,
                null,
                staffMember.Rate,
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
                adjustment.CreatedAtUtc
            });
        var payments = dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.StaffMemberId == staffMemberId &&
                operation.ExpenseTypeId == expenseTypeId &&
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
                operation.CreatedAtUtc
            });
        var persistedEntries = adjustments.Concat(payments);
        var summaries = await persistedEntries
            .GroupBy(item => item.EntryKind)
            .Select(group => new
            {
                EntryKind = group.Key,
                Count = group.Count(),
                Amount = group.Sum(item => item.Amount)
            })
            .ToListAsync(cancellationToken);

        var persistedSkip = Math.Max(0, offset - salaryEntries.Count);
        var rawItems = await persistedEntries
            .OrderByDescending(item => item.AccountingMonth)
            .ThenByDescending(item => item.OperationDate.HasValue)
            .ThenByDescending(item => item.OperationDate)
            .ThenBy(item => item.Id)
            .Skip(persistedSkip)
            .Take(limit + salaryEntries.Count)
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
            item.CreatedAtUtc));
        var items = salaryEntries
            .Concat(persistedItems)
            .OrderByDescending(item => item.AccountingMonth)
            .ThenByDescending(item => item.OperationDate.HasValue)
            .ThenByDescending(item => item.OperationDate)
            .ThenBy(item => item.Id)
            .Skip(offset - persistedSkip)
            .Take(limit)
            .ToList();
        var bonusSummary = summaries.FirstOrDefault(item => item.EntryKind == StaffSalaryAdjustmentTypes.Bonus);
        var penaltySummary = summaries.FirstOrDefault(item => item.EntryKind == StaffSalaryAdjustmentTypes.Penalty);
        var paymentSummary = summaries.FirstOrDefault(item => item.EntryKind == "payment");

        return new ExpenseWorksheetStaffBreakdownData(
            items,
            salaryEntries.Count + summaries.Sum(item => item.Count),
            staffMember.Rate * salaryEntries.Count,
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
