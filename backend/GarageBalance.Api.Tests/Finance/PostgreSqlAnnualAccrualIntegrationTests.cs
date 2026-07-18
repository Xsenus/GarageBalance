using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAnnualAccrualIntegrationTests
{
    [PostgreSqlFact]
    public async Task AnnualAccruals_PersistAccountingYearAndDatabaseRejectsInvalidYear()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PG-ANNUAL-YEAR", PeopleCount = 1, FloorCount = 1 };
        var membership = await context.IncomeTypes.SingleAsync(item => item.Code == "membership" && !item.IsArchived);
        var water = await context.IncomeTypes.SingleAsync(item => item.Code == "water" && !item.IsArchived);
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);

        var annual = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, membership.Id, new DateOnly(2027, 6, 1), 700m, "manual", "Годовой взнос"),
            null,
            CancellationToken.None);
        var monthly = await service.CreateAccrualAsync(
            new CreateAccrualRequest(garage.Id, water.Id, new DateOnly(2027, 7, 1), 100m, "manual", "Вода"),
            null,
            CancellationToken.None);

        Assert.True(annual.Succeeded, annual.ErrorMessage);
        Assert.Equal(2027, annual.Value!.AccountingYear);
        Assert.True(monthly.Succeeded, monthly.ErrorMessage);
        Assert.Null(monthly.Value!.AccountingYear);
        Assert.Equal(2027, await context.Accruals
            .Where(item => item.Id == annual.Value.Id)
            .Select(item => item.AccountingYear)
            .SingleAsync());

        context.Accruals.Add(new Accrual
        {
            GarageId = garage.Id,
            IncomeTypeId = membership.Id,
            AccountingMonth = new DateOnly(2028, 6, 1),
            AccountingYear = 1800,
            DueDate = new DateOnly(2028, 6, 30),
            OverdueFromDate = new DateOnly(2028, 7, 31),
            Amount = 700m,
            Source = "invalid-year-test"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
