using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class ChargeServiceTariffTransactionTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    [Theory]
    [InlineData("inline", false)]
    [InlineData("dated", false)]
    [InlineData("schedule", false)]
    [InlineData("mode", false)]
    [InlineData("schedule", true)]
    public async Task FailedRecalculation_RollsBackTariffAccrualAndAudit(string path, bool canceled)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRollbackAsync(database.Context, path, canceled);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_FailedRecalculationRollsBackAllMonthsAndTariff()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRollbackAsync(context, "dated", false);
    }

    private static async Task AssertRollbackAsync(GarageBalanceDbContext context, string path, bool canceled)
    {
        var (setting, accrual) = await SeedAsync(context);
        var tariffId = setting.TariffId;
        var tariffCount = await context.Tariffs.CountAsync();
        var auditCount = await context.AuditEvents.CountAsync();
        var recalculation = new FailingRecalculation(context, accrual.Id, canceled);
        var service = DictionaryServiceTestFactory.Create(context, October, recalculation);
        var request = new UpsertChargeServiceSettingRequest(setting.Name, true, 1, 1, 30, null, 30,
            path == "mode", false, "гараж", setting.IncomeTypeId, setting.TariffId, Version: setting.Version);
        async Task InvokeAsync()
        {
            if (path == "schedule")
            {
                var result = await service.UpdateChargeServiceTariffScheduleAsync(setting.Id,
                    new([new(null, October, null, 200m)], true, "Synthetic tariff rollback", setting.Version), null, CancellationToken.None);
                Assert.True(result.Succeeded, result.ErrorMessage);
            }
            else
            {
                var result = await service.UpdateChargeServiceWithTariffAsync(setting.Id,
                    new(request, 200m, path == "mode" ? "metered" : null, path == "inline" ? null : October,
                        ChangeReason: "Synthetic tariff rollback", CalculationBase: path == "mode" ? TariffCalculationBases.MeterElectricity : null), null, CancellationToken.None);
                Assert.True(result.Succeeded, result.ErrorMessage);
            }
        }
        if (canceled) await Assert.ThrowsAsync<OperationCanceledException>(InvokeAsync);
        else await Assert.ThrowsAsync<InvalidOperationException>(InvokeAsync);
        Assert.Equal(1, recalculation.Calls);
        Assert.Null(context.Database.CurrentTransaction);
        context.ChangeTracker.Clear();
        var restored = await context.ChargeServiceSettings.Include(row => row.Tariff).SingleAsync(row => row.Id == setting.Id);
        Assert.Equal(tariffId, restored.TariffId);
        Assert.Equal(100m, restored.Tariff!.Rate);
        Assert.False(restored.IsMetered);
        Assert.Equal(tariffCount, await context.Tariffs.CountAsync());
        Assert.Equal(100m, (await context.Accruals.SingleAsync(row => row.Id == accrual.Id)).Amount);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        Assert.Empty(await context.ChargeServiceTariffVersions.Where(row => row.ChargeServiceSettingId == setting.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingTransaction_RetainsEarlierWritesAndNeverCommitsOuter(bool commitInner)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertSavepointAsync(database.Context, commitInner);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_SavepointDoesNotCommitOuterTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertSavepointAsync(context, true);
    }

    private static async Task AssertSavepointAsync(GarageBalanceDbContext context, bool commitInner)
    {
        await using var outer = await context.Database.BeginTransactionAsync();
        var (setting, _) = await SeedAsync(context);
        await using (var inner = await new EfChargeServiceSettingRepository(context).BeginTariffWriteAsync(CancellationToken.None))
        {
            setting.Tariff!.Rate = 250;
            await context.SaveChangesAsync();
            if (commitInner)
            {
                await inner.CommitAsync(CancellationToken.None);
                await inner.CommitAsync(CancellationToken.None);
            }
            else
            {
                await inner.RollbackAsync();
                await inner.RollbackAsync();
            }
        }
        Assert.Same(outer, context.Database.CurrentTransaction);
        context.ChangeTracker.Clear();
        Assert.Equal(commitInner ? 250m : 100m, (await context.Tariffs.SingleAsync(row => row.Id == setting.TariffId)).Rate);
        await outer.RollbackAsync();
        context.ChangeTracker.Clear();
        Assert.False(await context.ChargeServiceSettings.AnyAsync(row => row.Id == setting.Id));
    }

    [Fact]
    public async Task InvalidChange_RollsBackPendingDictionarySideEffects()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (setting, _) = await SeedAsync(context);
        var units = await context.MeasurementUnits.CountAsync();
        var result = await DictionaryServiceTestFactory.Create(context, October).UpdateChargeServiceWithTariffAsync(setting.Id,
            new(new(setting.Name, true, 1, 1, 30, null, 30, false, false, "Synthetic new unit", setting.IncomeTypeId, setting.TariffId),
                200, "invalid", October), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("charge_service_tariff_mode_invalid", result.ErrorCode);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(units, await context.MeasurementUnits.CountAsync());
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Equal(100m, setting.Tariff!.Rate);
    }

    private static async Task<(ChargeServiceSetting Setting, Accrual Accrual)> SeedAsync(GarageBalanceDbContext context)
    {
        var setting = new ChargeServiceSetting
        {
            Name = $"Synthetic tariff transaction {Guid.NewGuid():N}",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            UnitName = "гараж",
            Tariff = new Tariff { Name = "Synthetic tariff transaction", CalculationBase = TariffCalculationBases.Fixed, Rate = 100, EffectiveFrom = new(2026, 1, 1) },
            IncomeType = new IncomeType
            {
                Name = "Synthetic tariff transaction",
                Code = $"synthetic_tariff_{Guid.NewGuid():N}",
                DestinationFund = new Fund { Name = $"Synthetic tariff transaction {Guid.NewGuid():N}", NormalizedName = $"SYNTHETIC TARIFF {Guid.NewGuid():N}", IsSystem = false }
            }
        };
        var accrual = new Accrual
        {
            Garage = new Garage { Number = $"synthetic-{Guid.NewGuid():N}", RegisteredOn = October, PeopleCount = 1, FloorCount = 1 },
            IncomeType = setting.IncomeType,
            Tariff = setting.Tariff,
            AccountingMonth = October,
            Source = AccrualSources.Regular,
            Amount = 100
        };
        context.AddRange(setting, accrual);
        await context.SaveChangesAsync();
        return (setting, accrual);
    }

    private sealed class FailingRecalculation(GarageBalanceDbContext context, Guid accrualId, bool canceled) : ITariffAccrualRecalculationService
    {
        public int Calls { get; private set; }
        public async Task RecalculateExistingUnpaidAsync(Guid chargeServiceId, Guid incomeTypeId, DateOnly affectedFrom,
            Guid? actorUserId, string reason, CancellationToken cancellationToken)
        {
            Calls++;
            var accrual = await context.Accruals.SingleAsync(row => row.Id == accrualId, cancellationToken);
            accrual.Amount = 999;
            await context.SaveChangesAsync(cancellationToken);
            if (canceled) throw new OperationCanceledException("Synthetic canceled recalculation");
            throw new InvalidOperationException("Synthetic failure after partial recalculation");
        }
    }
}
