using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseBatchStaffDebtQuery(
    GarageBalanceDbContext dbContext,
    IBusinessDateProvider businessDateProvider) : IExpenseBatchStaffDebtQuery
{
    public async Task<IReadOnlyList<ExpenseBatchDebt>> GetAsync(
        IReadOnlyList<Guid> staffMemberIds,
        DateOnly monthTo,
        DateOnly? salaryAccrualMonthTo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staffMemberIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (monthTo.Day != 1 || salaryAccrualMonthTo is { Day: not 1 }
            || salaryAccrualMonthTo > monthTo || staffMemberIds.Contains(Guid.Empty))
        {
            throw new ArgumentException("Некорректные параметры расчёта зарплатных долгов.");
        }
        var ids = staffMemberIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var salaryId = await dbContext.ExpenseTypes.AsNoTracking()
            .Where(item => item.Code == "salary" && !item.IsArchived)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ExpenseBatchPreparationException("salary_expense_type_not_found", "Действующая статья «Зарплата» не найдена.");
        var staff = await dbContext.StaffMembers.AsNoTracking()
            .Where(item => ids.Contains(item.Id)).OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        if (staff.Count != ids.Length)
        {
            throw new ExpenseBatchPreparationException("expense_batch_data_changed", "Состав сотрудников изменился. Обновите предварительный расчёт.");
        }
        // These are the selected employees' configuration histories, not payment rows.
        var rates = await dbContext.StaffSalaryRatePeriods.AsNoTracking()
            .Where(item => ids.Contains(item.StaffMemberId) && item.EffectiveFrom <= monthTo)
            .OrderBy(item => item.EffectiveFrom).ToListAsync(cancellationToken);
        var employment = await dbContext.StaffEmploymentPeriods.AsNoTracking()
            .Where(item => ids.Contains(item.StaffMemberId))
            .ToListAsync(cancellationToken);
        var payments = dbContext.FinancialOperations.AsNoTracking()
            .Where(item => item.StaffMemberId.HasValue && ids.Contains(item.StaffMemberId.Value)
                && item.ExpenseTypeId == salaryId && item.OperationKind == FinancialOperationKinds.Expense
                && !item.IsCanceled && item.AccountingMonth <= monthTo);
        var adjustments = dbContext.StaffSalaryAdjustments.AsNoTracking()
            .Where(item => ids.Contains(item.StaffMemberId) && !item.IsCanceled && item.AccountingMonth <= monthTo);
        var firstPayment = await payments.MinAsync(item => (DateOnly?)item.AccountingMonth, cancellationToken);
        var firstAdjustment = await adjustments.MinAsync(item => (DateOnly?)item.AccountingMonth, cancellationToken);
        var firstMonth = staff.Select(item => MonthPeriod.Normalize(businessDateProvider.ToBusinessDate(item.CreatedAtUtc)))
            .Append(firstPayment ?? monthTo).Append(firstAdjustment ?? monthTo).Append(monthTo).Min();
        var monthCount = (monthTo.Year - firstMonth.Year) * 12 + monthTo.Month - firstMonth.Month + 1;
        if (monthCount > 600)
        {
            throw new ExpenseBatchPreparationException("expense_batch_history_too_long", "История зарплат для массовой выплаты превышает 600 месяцев.");
        }

        // Group and sum historical operations in PostgreSQL; the result is bounded
        // by selected employees and the validated month range.
        var paid = await payments.Where(item => item.AccountingMonth >= firstMonth)
            .GroupBy(item => new { StaffMemberId = item.StaffMemberId!.Value, item.AccountingMonth })
            .Select(group => new { group.Key.StaffMemberId, group.Key.AccountingMonth, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => (item.StaffMemberId, item.AccountingMonth), item => item.Amount, cancellationToken);
        var adjusted = await adjustments.Where(item => item.AccountingMonth >= firstMonth)
            .GroupBy(item => new { item.StaffMemberId, item.AccountingMonth })
            .Select(group => new
            {
                group.Key.StaffMemberId,
                group.Key.AccountingMonth,
                Amount = group.Sum(item => item.AdjustmentType == StaffSalaryAdjustmentTypes.Bonus ? item.Amount : -item.Amount)
            })
            .ToDictionaryAsync(item => (item.StaffMemberId, item.AccountingMonth), item => item.Amount, cancellationToken);
        var ratesByStaff = rates.ToLookup(item => item.StaffMemberId);
        var employmentByStaff = employment.ToLookup(item => item.StaffMemberId);
        var result = new List<ExpenseBatchDebt>();
        foreach (var member in staff)
        {
            var memberRates = ratesByStaff[member.Id].ToArray();
            var memberEmployment = employmentByStaff[member.Id].ToArray();
            var createdMonth = MonthPeriod.Normalize(businessDateProvider.ToBusinessDate(member.CreatedAtUtc));
            var updatedMonth = MonthPeriod.Normalize(businessDateProvider.ToBusinessDate(member.UpdatedAtUtc));
            for (var index = 0; index < monthCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var month = firstMonth.AddMonths(index);
                // Employment history can predate registration in this accounting system.
                // The worksheet starts base salary at the member's creation month.
                var baseAmount = month >= createdMonth && salaryAccrualMonthTo.HasValue && month <= salaryAccrualMonthTo.Value
                    ? StaffSalaryTimeline.CalculateBaseAccrual(month, month, member.Rate, createdMonth,
                        member.IsArchived, updatedMonth, memberRates, memberEmployment)
                    : 0m;
                var key = (member.Id, month);
                var remaining = MoneyMath.RoundMoney(baseAmount + adjusted.GetValueOrDefault(key) - paid.GetValueOrDefault(key));
                if (remaining != 0)
                {
                    result.Add(new ExpenseBatchDebt("staff", member.Id, salaryId, null, month, remaining));
                }
            }
        }
        return result;
    }
}
