using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageWorksheetMeterScopeTests
{
    private sealed class WorksheetClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    }

    [Theory]
    [InlineData("water")]
    [InlineData("electricity")]
    [InlineData("service_11111111111111111111111111111111")]
    public async Task SqliteWorksheetIncludesRestrictedMeterOnlyForSelectedGarage(string meterKind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyScopeAsync(database.Context, meterKind);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlWorksheetIncludesRestrictedMeterOnlyForSelectedGarage()
    {
        foreach (var meterKind in new[] { "water", "electricity", "service_11111111111111111111111111111111" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var context = database.CreateContext();
            await VerifyScopeAsync(context, meterKind);
        }
    }

    private static async Task VerifyScopeAsync(GarageBalanceDbContext context, string meterKind)
    {
        var month = new DateOnly(2026, 10, 1);
        var included = new Garage { Number = "SCOPE-IN", RegisteredOn = month.AddYears(-1) };
        var excluded = new Garage { Number = "SCOPE-OUT", RegisteredOn = month.AddYears(-1) };
        var income = await context.IncomeTypes.SingleOrDefaultAsync(i => i.Code == meterKind && !i.IsArchived)
            ?? new IncomeType { Name = "Выборочный счётчик", Code = meterKind };
        foreach (var old in await context.ChargeServiceSettings.Where(s => s.IncomeTypeId == income.Id && !s.IsArchived).ToArrayAsync())
            old.IsArchived = true;
        var tariff = new Tariff
        {
            Name = "Выборочный счётчик",
            CalculationBase = meterKind == "water" ? TariffCalculationBases.MeterWater : TariffCalculationBases.MeterElectricity,
            Rate = 13.25m,
            EffectiveFrom = month
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Выборочный счётчик",
            IsRegular = true,
            IsMetered = true,
            MeterKind = meterKind,
            IncomeType = income,
            Tariff = tariff,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            AppliesToSelectedGarages = true,
            GarageIds = [included.Id]
        };
        context.AddRange(included, excluded, setting);
        await context.SaveChangesAsync();
        var query = new EfGarageIncomeWorksheetQuery(context);
        var inData = await query.GetAsync(included.Id, month, month, CancellationToken.None);
        var outData = await query.GetAsync(excluded.Id, month, month, CancellationToken.None);
        Assert.Contains(inData!.MeterIncomeTypes, row => row.IncomeTypeId == income.Id && row.MeterKind == meterKind);
        Assert.DoesNotContain(outData!.MeterIncomeTypes, row => row.IncomeTypeId == income.Id);

        setting.GarageIds = [];
        await context.SaveChangesAsync();
        var emptyScope = await query.GetAsync(included.Id, month, month, CancellationToken.None);
        Assert.DoesNotContain(emptyScope!.MeterIncomeTypes, row => row.IncomeTypeId == income.Id);
        setting.GarageIds = [included.Id];
        await context.SaveChangesAsync();

        // Historical amounts remain visible after a service's scope is narrowed.
        context.Accruals.Add(new Accrual
        {
            Garage = excluded,
            IncomeType = income,
            Tariff = tariff,
            AccountingMonth = month,
            Amount = 26.50m,
            Source = AccrualSources.Regular
        });
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, new WorksheetClock());
        var historical = await finance.GetGarageIncomeWorksheetAsync(excluded.Id, new(month, month), CancellationToken.None);
        Assert.True(historical.Succeeded, historical.ErrorMessage);
        var historicalRow = Assert.Single(historical.Value!.Rows, row => row.IncomeTypeId == income.Id);
        Assert.Equal(26.50m, historicalRow.AccrualAmount);
        Assert.Null(historicalRow.MeterKind);

        setting.AppliesToSelectedGarages = false;
        setting.GarageIds = [];
        await context.SaveChangesAsync();
        var unrestricted = await query.GetAsync(excluded.Id, month, month, CancellationToken.None);
        Assert.Contains(unrestricted!.MeterIncomeTypes, row => row.IncomeTypeId == income.Id);
    }
}
