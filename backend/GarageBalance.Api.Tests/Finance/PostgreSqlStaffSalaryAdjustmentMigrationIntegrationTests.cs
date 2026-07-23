using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlStaffSalaryAdjustmentMigrationIntegrationTests
{
    private const string PreviousMigration = "20260723123802_SeparateExpensePaymentType";

    [PostgreSqlFact]
    public async Task MigrationCreatesProtectedStaffSalaryAdjustmentTable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var tableBeforeMigration = await downgradeContext.Database
                .SqlQueryRaw<string?>(
                    """
                    SELECT to_regclass('public.staff_salary_adjustments')::text AS "Value"
                    """)
                .SingleAsync();
            Assert.Null(tableBeforeMigration);
            await downgradeContext.Database.MigrateAsync();
        }

        Guid staffMemberId;
        await using (var context = database.CreateContext())
        {
            var department = new StaffDepartment { Name = $"Миграция зарплаты {Guid.NewGuid():N}" };
            var staffMember = new StaffMember { FullName = $"Сотрудник {Guid.NewGuid():N}", Department = department, Rate = 100m };
            staffMemberId = staffMember.Id;
            context.AddRange(
                department,
                staffMember,
                new StaffSalaryAdjustment
                {
                    StaffMember = staffMember,
                    AccountingMonth = new DateOnly(2026, 7, 1),
                    AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                    Amount = 25m,
                    Reason = "Проверка миграции"
                });
            await context.SaveChangesAsync();
            Assert.Equal(1, await context.StaffSalaryAdjustments.CountAsync(adjustment => adjustment.StaffMemberId == staffMemberId));
        }

        await using (var invalidTypeContext = database.CreateContext())
        {
            invalidTypeContext.StaffSalaryAdjustments.Add(new StaffSalaryAdjustment
            {
                StaffMemberId = staffMemberId,
                AccountingMonth = new DateOnly(2026, 7, 1),
                AdjustmentType = "gift",
                Amount = 10m,
                Reason = "Недопустимый тип"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => invalidTypeContext.SaveChangesAsync());
        }

        await using (var invalidAmountContext = database.CreateContext())
        {
            invalidAmountContext.StaffSalaryAdjustments.Add(new StaffSalaryAdjustment
            {
                StaffMemberId = staffMemberId,
                AccountingMonth = new DateOnly(2026, 7, 1),
                AdjustmentType = StaffSalaryAdjustmentTypes.Penalty,
                Amount = 0m,
                Reason = "Недопустимая сумма"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => invalidAmountContext.SaveChangesAsync());
        }

        await using var deleteContext = database.CreateContext();
        var staffToDelete = await deleteContext.StaffMembers.SingleAsync(staffMember => staffMember.Id == staffMemberId);
        deleteContext.StaffMembers.Remove(staffToDelete);
        await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
    }
}
