using System.Reflection;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageTariffAssignmentServiceTests
{
    private static readonly DateOnly Month = new(2050, 9, 1);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2050, 9, 30, 12, 0, 0, TimeSpan.Zero);
    }

    [PostgreSqlFact]
    public async Task CreateUpdateAndArchiveRecalculateUnpaidAmountsRetainPaidSnapshotsAndAuditEachGarage()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(fixture.Service.Id, Request(fixture) with { GarageIds = [fixture.First.Id, fixture.Second.Id] }, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(2, created.Value!.Count);
        var first = created.Value.Single(row => row.GarageId == fixture.First.Id);
        var second = created.Value.Single(row => row.GarageId == fixture.Second.Id);
        Assert.Equal(first.TariffId, second.TariffId);
        Assert.All(await context.Accruals.Where(row => row.IncomeTypeId == fixture.Income.Id).ToListAsync(), row => Assert.Equal(200m, row.Amount));
        var paidAccrual = await context.Accruals.SingleAsync(row => row.GarageId == fixture.Second.Id && row.IncomeTypeId == fixture.Income.Id);
        var paidSnapshot = paidAccrual.CalculationDetailsJson;
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = Month,
            AccountingMonth = Month,
            Amount = 200,
            GarageId = fixture.Second.Id,
            IncomeTypeId = fixture.Income.Id
        };
        context.AddRange(payment, new AccrualPaymentAllocation { FinancialOperation = payment, Accrual = paidAccrual, Amount = 200, IsActive = true });
        await context.SaveChangesAsync();
        var updated = await service.UpdateAsync(fixture.Service.Id, first.Id,
            new(Month, null, 300, null, " Изменённый комментарий ", first.Version, fixture.Service.Version, "Уточнение ставки"), null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        Assert.NotEqual(first.Version, updated.Value!.Version);
        Assert.NotEqual(first.TariffId, updated.Value.TariffId);
        Assert.Equal("Изменённый комментарий", updated.Value.Comment);
        Assert.Equal(200m, (await context.Tariffs.SingleAsync(row => row.Id == first.TariffId)).Rate);
        Assert.Equal(300m, (await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id)).Amount);
        var paidUpdate = await service.UpdateAsync(fixture.Service.Id, second.Id,
            new(Month, null, 500, null, null, second.Version, fixture.Service.Version, "Новая ставка"), null, CancellationToken.None);
        Assert.True(paidUpdate.Succeeded, paidUpdate.ErrorMessage);
        Assert.Equal(200m, paidAccrual.Amount);
        Assert.Equal(paidSnapshot, paidAccrual.CalculationDetailsJson);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => service.UpdateAsync(fixture.Service.Id, first.Id,
            new(Month, null, 700, null, null, first.Version, fixture.Service.Version, "Устаревшая карточка"), null, CancellationToken.None));
        var archived = await service.ArchiveAsync(fixture.Service.Id, first.Id,
            new(updated.Value.Version, "Возврат к общему тарифу"), null, CancellationToken.None);
        Assert.True(archived.Succeeded, archived.ErrorMessage);
        Assert.True(archived.Value!.IsArchived);
        Assert.Equal(100m, (await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id)).Amount);
        Assert.Equal(200m, paidAccrual.Amount);
        Assert.Equal(5, await context.AuditEvents.CountAsync(row => row.Action.StartsWith("garage_tariff.")));
        Assert.All(await context.AuditEvents.Where(row => row.Action.StartsWith("garage_tariff.")).ToListAsync(), row => Assert.NotNull(row.RelatedGarageId));
        var page = await service.GetPageAsync(fixture.Service.Id, null, false, 0, 10, CancellationToken.None);
        Assert.Equal(second.Id, Assert.Single(page.Value!.Items).Id);
        var history = await service.GetPageAsync(fixture.Service.Id, fixture.First.Id, true, 0, 10, CancellationToken.None);
        Assert.True(Assert.Single(history.Value!.Items).IsArchived);
        var catalog = new EfTariffRepository(context);
        Assert.DoesNotContain(await catalog.GetListAsync(null, true, 500, CancellationToken.None), row => row.IsIndividual);
        Assert.Null(await catalog.FindActiveAsync(first.TariffId, CancellationToken.None));
        Assert.DoesNotContain((await catalog.GetPageAsync(null, true, 0, 500, CancellationToken.None)).Items, row => row.Id == first.TariffId);
    }

    [PostgreSqlFact]
    public async Task UpdatingDatesRejectsOverlapAndRestoresGeneralRateInTheOldPeriod()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var service = CreateService(context);
        var initial = Request(fixture) with { EffectiveTo = Month.AddDays(14) };
        var created = await service.CreateAsync(fixture.Service.Id, initial, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var first = Assert.Single(created.Value!);
        var later = await service.CreateAsync(fixture.Service.Id,
            initial with { EffectiveFrom = Month.AddDays(15), EffectiveTo = Month.AddDays(29), Rate = 300 }, null, CancellationToken.None);
        Assert.True(later.Succeeded, later.ErrorMessage);
        Assert.Equal(250m, (await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id)).Amount);
        var overlapping = await service.UpdateAsync(fixture.Service.Id, first.Id,
            new(Month, Month.AddDays(15), 500, null, null, first.Version, fixture.Service.Version, "Пересекающийся период"), null, CancellationToken.None);
        Assert.Equal("garage_tariff_period_overlap", overlapping.ErrorCode);
        var moved = await service.UpdateAsync(fixture.Service.Id, first.Id,
            new(Month.AddMonths(1), Month.AddMonths(1).AddDays(14), 200, null, null, first.Version, fixture.Service.Version, "Перенос на будущий месяц"), null, CancellationToken.None);
        Assert.True(moved.Succeeded, moved.ErrorMessage);
        Assert.Equal(200m, (await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id)).Amount);
        Assert.False(await context.Accruals.AnyAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id && row.AccountingMonth == Month.AddMonths(1)));
        var second = Assert.Single(later.Value!);
        var archive = await service.ArchiveAsync(fixture.Service.Id, second.Id, new(second.Version, "Отмена второго периода"), null, CancellationToken.None);
        Assert.True(archive.Succeeded, archive.ErrorMessage);
        Assert.Equal(100m, (await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id)).Amount);
    }

    [PostgreSqlFact]
    public async Task RecalculationFailureRollsBackAssignmentsClonedRatesAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var failing = DispatchProxy.Create<IFinanceService, FailingFinanceProxy>();
        var result = await CreateService(context, failing).CreateAsync(fixture.Service.Id, Request(fixture), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("simulated_recalculation_failure", result.ErrorCode);
        await using var fresh = database.CreateContext();
        Assert.Empty(await fresh.GarageTariffAssignments.ToListAsync());
        Assert.False(await fresh.Tariffs.AnyAsync(row => row.IsIndividual));
        Assert.False(await fresh.AuditEvents.AnyAsync(row => row.Action.StartsWith("garage_tariff.")));
        context.ChangeTracker.Clear();
        var created = await CreateService(context).CreateAsync(fixture.Service.Id, Request(fixture), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var dto = Assert.Single(created.Value!);
        var update = await CreateService(context, failing).UpdateAsync(fixture.Service.Id, dto.Id,
            new(Month, null, 300, null, null, dto.Version, fixture.Service.Version, "Проверка отката изменения"), null, CancellationToken.None);
        Assert.False(update.Succeeded);
        context.ChangeTracker.Clear();
        var archive = await CreateService(context, failing).ArchiveAsync(fixture.Service.Id, dto.Id,
            new(dto.Version, "Проверка отката отмены"), null, CancellationToken.None);
        Assert.False(archive.Succeeded);
        await using var checkedContext = database.CreateContext();
        var retained = await checkedContext.GarageTariffAssignments.Include(row => row.Tariff).SingleAsync();
        Assert.False(retained.IsArchived);
        Assert.Equal(dto.Version, retained.Version);
        Assert.Equal(200m, retained.Tariff.Rate);
        Assert.Equal(1, await checkedContext.Tariffs.CountAsync(row => row.IsIndividual));
        Assert.Equal(1, await checkedContext.AuditEvents.CountAsync(row => row.Action.StartsWith("garage_tariff.")));
    }

    [PostgreSqlFact]
    public async Task FutureTieredAssignmentsValidateEveryBoundaryAndDoNotRecalculateBeforeTheyApply()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        fixture.Service.IsMetered = true;
        fixture.Service.Tariff!.CalculationBase = TariffCalculationBases.MeterElectricity;
        fixture.Service.UnitName = "кВт·ч";
        fixture.Service.MeterKind = MeterKinds.ForService(fixture.Service.Id);
        await context.SaveChangesAsync();
        var service = CreateService(context, DispatchProxy.Create<IFinanceService, FailingFinanceProxy>());
        var upperId = Guid.NewGuid();
        var finalId = Guid.NewGuid();
        var request = Request(fixture) with
        {
            EffectiveFrom = Month.AddMonths(1),
            Tiers = [new(upperId, " Первые 100 ", 100.0004m, 1.23456m), new(finalId, "Остальные", null, 2)]
        };
        IReadOnlyList<UpsertElectricityTariffTierRequest>[] invalid = [
            [new(null, "Одна", null, 1)],
            [null!, new(null, "Последняя", null, 2)],
            [new(null, "Первая", null, 1), new(null, "Последняя", null, 2)],
            [new(null, "Первая", 100, 1), new(null, "Последняя", 200, 2)],
            [new(null, "Первая", 0.0001m, 1), new(null, "Последняя", null, 2)],
            [new(null, "Первая", 100, 0), new(null, "Последняя", null, 2)],
            [new(null, " ", 100, 1), new(null, "Последняя", null, 2)],
            [new(Guid.Empty, "Первая", 100, 1), new(null, "Последняя", null, 2)],
            [new(upperId, "Первая", 100, 1), new(upperId, "Последняя", null, 2)],
            [new(null, "Первая", 100, 1), new(null, "Вторая", 99, 2), new(null, "Последняя", null, 3)],
            Enumerable.Range(0, 21).Select(index => new UpsertElectricityTariffTierRequest(null, "Ступень", index == 20 ? null : index + 1, 1)).ToArray()
        ];
        foreach (var tiers in invalid)
            Assert.False((await service.CreateAsync(fixture.Service.Id, request with { Tiers = tiers }, null, CancellationToken.None)).Succeeded);
        var created = await service.CreateAsync(fixture.Service.Id, request, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var dto = Assert.Single(created.Value!);
        Assert.Equal("Первые 100", dto.Tiers[0].Name);
        Assert.Equal(100m, dto.Tiers[0].UpperBound);
        Assert.Equal(1.2346m, dto.Tiers[0].Rate);
        Assert.Equal(upperId, dto.Tiers[0].Id);
        Assert.Equal(2, dto.Tiers.Count);
        Assert.False(await context.Accruals.AnyAsync(row => row.IncomeTypeId == fixture.Income.Id));
        var updated = await service.UpdateAsync(fixture.Service.Id, dto.Id,
            new(request.EffectiveFrom, null, 3, request.Tiers, null, dto.Version, fixture.Service.Version, "Уточнение будущей ставки"), null, CancellationToken.None);
        Assert.True(updated.Succeeded, updated.ErrorMessage);
        var archived = await service.ArchiveAsync(fixture.Service.Id, dto.Id,
            new(updated.Value!.Version, "Будущий тариф не нужен"), null, CancellationToken.None);
        Assert.True(archived.Succeeded, archived.ErrorMessage);
        Assert.Null(await new EfTariffRepository(context).FindArchivedAsync(dto.TariffId, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task InvalidRequestsOverlapsAndMissingEntitiesDoNotCreateRevisions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var service = CreateService(context);
        var valid = Request(fixture);
        CreateGarageTariffAssignmentsRequest[] invalid = [
            valid with { GarageIds = [] }, valid with { GarageIds = [Guid.Empty] },
            valid with { GarageIds = [fixture.First.Id, fixture.First.Id] },
            valid with { GarageIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() },
            valid with { GarageIds = [Guid.NewGuid()] }, valid with { Rate = 0 }, valid with { Rate = 1000000000m },
            valid with { EffectiveFrom = new(1999, 1, 1) }, valid with { EffectiveTo = Month.AddDays(-1) },
            valid with { EffectiveTo = new(9999, 1, 1) }, valid with { Reason = " " },
            valid with { Reason = new string('x', 1001) }, valid with { Comment = new string('x', 2001) },
            valid with { Tiers = [new(null, "Ступень", 10, 1), new(null, "Последняя", null, 2)] }
        ];
        foreach (var request in invalid)
        {
            var result = await service.CreateAsync(fixture.Service.Id, request, null, CancellationToken.None);
            Assert.False(result.Succeeded);
        }
        Assert.False((await service.CreateAsync(Guid.NewGuid(), valid, null, CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => service.CreateAsync(fixture.Service.Id, valid with { ServiceVersion = Guid.NewGuid() }, null, CancellationToken.None));
        Assert.False(await context.Tariffs.AnyAsync(row => row.IsIndividual));
        var first = await service.CreateAsync(fixture.Service.Id, valid, null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.False((await service.CreateAsync(fixture.Service.Id, valid, null, CancellationToken.None)).Succeeded);
        Assert.False((await service.UpdateAsync(Guid.NewGuid(), first.Value![0].Id,
            new(Month, null, 200, null, null, first.Value[0].Version, fixture.Service.Version, "Проверка"), null, CancellationToken.None)).Succeeded);
        Assert.False((await service.ArchiveAsync(fixture.Service.Id, first.Value[0].Id, new(first.Value[0].Version, " "), null, CancellationToken.None)).Succeeded);
        Assert.Equal(1, await context.GarageTariffAssignments.CountAsync());
        Assert.Equal(1, await context.Tariffs.CountAsync(row => row.IsIndividual));
        Assert.False((await service.GetPageAsync(fixture.Service.Id, Guid.Empty, false, 0, 10, CancellationToken.None)).Succeeded);
        Assert.False((await service.GetPageAsync(Guid.NewGuid(), null, false, 0, 10, CancellationToken.None)).Succeeded);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(fixture.Service.Id, valid, null, canceled.Token));
    }

    [PostgreSqlFact]
    public async Task FutureAssignmentsRejectIncompatibleScheduledUnitsBeforeAnyWritesAndAllowExactBoundaries()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var from = Month.AddMonths(1);
        var changedFrom = Month.AddMonths(2);
        var future = new Tariff { Name = "Будущий тариф на человека", CalculationBase = TariffCalculationBases.People, Rate = 75, EffectiveFrom = changedFrom };
        context.AddRange(new ChargeServiceTariffVersion { ChargeServiceSettingId = fixture.Service.Id, Tariff = fixture.Service.Tariff!, TariffId = fixture.Service.TariffId!.Value, EffectiveFrom = Month, EffectiveTo = changedFrom.AddDays(-1) },
            new ChargeServiceTariffVersion { ChargeServiceSettingId = fixture.Service.Id, Tariff = future, TariffId = future.Id, EffectiveFrom = changedFrom });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var invalid = Request(fixture) with { EffectiveFrom = from };
        var baselineAudits = await context.AuditEvents.CountAsync();
        var rejected = await service.CreateAsync(fixture.Service.Id, invalid, null, CancellationToken.None);
        Assert.Equal("garage_tariff_calculation_base_conflict", rejected.ErrorCode);
        Assert.False(await context.GarageTariffAssignments.AnyAsync());
        Assert.False(await context.Tariffs.AnyAsync(item => item.IsIndividual));
        Assert.Equal(baselineAudits, await context.AuditEvents.CountAsync());
        var accepted = await service.CreateAsync(fixture.Service.Id, invalid with { EffectiveTo = changedFrom.AddDays(-1) }, null, CancellationToken.None);
        Assert.True(accepted.Succeeded, accepted.ErrorMessage);
        var saved = Assert.Single(accepted.Value!);
        var audits = await context.AuditEvents.CountAsync();
        var revisions = await context.Tariffs.CountAsync(item => item.IsIndividual);
        var update = new UpdateGarageTariffAssignmentRequest(from, changedFrom, 250, null, null, saved.Version, fixture.Service.Version, "Продлить период");
        var refusedUpdate = await service.UpdateAsync(fixture.Service.Id, saved.Id, update, null, CancellationToken.None);
        Assert.Equal("garage_tariff_calculation_base_conflict", refusedUpdate.ErrorCode);
        var unchanged = await context.GarageTariffAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(saved.Version, unchanged.Version);
        Assert.Equal(changedFrom.AddDays(-1), unchanged.EffectiveTo);
        Assert.Equal(saved.TariffId, unchanged.TariffId);
        Assert.Equal(audits, await context.AuditEvents.CountAsync());
        Assert.Equal(revisions, await context.Tariffs.CountAsync(item => item.IsIndividual));
        future.IsArchived = true;
        await context.SaveChangesAsync();
        var extended = await service.UpdateAsync(fixture.Service.Id, saved.Id, update, null, CancellationToken.None);
        Assert.True(extended.Succeeded, extended.ErrorMessage);
        Assert.False(await context.Accruals.AnyAsync(item => item.GarageId == fixture.First.Id || item.GarageId == fixture.Second.Id));
    }

    [PostgreSqlFact]
    public async Task ChangingGeneralModeRejectsOverlappingIndividualUnitsUntilTheAssignmentIsArchived()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var fund = new Fund { Name = "Фонд проверки режимов", AllowOperations = true };
        context.Add(fund);
        fixture.Income.DestinationFundId = fund.Id;
        await context.SaveChangesAsync();
        var from = Month.AddMonths(1);
        var assignments = CreateService(context);
        var assigned = await assignments.CreateAsync(fixture.Service.Id,
            Request(fixture) with { EffectiveFrom = from, EffectiveTo = from.AddMonths(1).AddDays(-1) }, null, CancellationToken.None);
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);
        var individual = Assert.Single(assigned.Value!);
        var dictionaries = DictionaryServiceTestFactory.Create(context, Month.AddDays(29));
        UpdateChargeServiceWithTariffRequest RequestMode() => new(
            new(fixture.Service.Name, true, 1, 1, 20, null, 30, false, false, "руб.", fixture.Income.Id, fixture.Service.TariffId, fixture.Service.Version),
            75, "regular", from, ChangeReason: "Новый способ расчёта", CalculationBase: TariffCalculationBases.People,
            TariffVersion: fixture.Service.Tariff!.Version);
        var beforeTariffs = await context.Tariffs.CountAsync();
        var beforeAudits = await context.AuditEvents.CountAsync();
        var beforeVersion = fixture.Service.Version;
        var conflict = await dictionaries.UpdateChargeServiceWithTariffAsync(fixture.Service.Id, RequestMode(), null, CancellationToken.None);
        Assert.Equal("charge_service_individual_tariff_base_conflict", conflict.ErrorCode);
        Assert.Equal(beforeTariffs, await context.Tariffs.CountAsync());
        Assert.Equal(beforeAudits, await context.AuditEvents.CountAsync());
        Assert.Equal(beforeVersion, (await context.ChargeServiceSettings.AsNoTracking().SingleAsync(item => item.Id == fixture.Service.Id)).Version);
        var archive = await assignments.ArchiveAsync(fixture.Service.Id, individual.Id, new(individual.Version, "Переход на другую базу"), null, CancellationToken.None);
        Assert.True(archive.Succeeded, archive.ErrorMessage);
        var changed = await dictionaries.UpdateChargeServiceWithTariffAsync(fixture.Service.Id, RequestMode(), null, CancellationToken.None);
        Assert.True(changed.Succeeded, changed.ErrorMessage);
        Assert.Equal(TariffCalculationBases.People, changed.Value!.Tariff.CalculationBase);
        Assert.True((await context.GarageTariffAssignments.AsNoTracking().SingleAsync()).IsArchived);
    }

    [PostgreSqlFact]
    public async Task AssignmentWritesWaitForTheSameAllocationLockAsGeneralModeChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var setup = database.CreateContext();
        var fixture = await Setup(setup);
        await using var blocker = database.CreateContext();
        var held = await new EfFundRepository(blocker).AcquireAllocationLockAsync(CancellationToken.None);
        await using var writer = database.CreateContext();
        var saving = CreateService(writer).CreateAsync(fixture.Service.Id,
            Request(fixture) with { EffectiveFrom = Month.AddMonths(1) }, null, CancellationToken.None);
        var waited = false;
        try
        {
            await using var observer = new NpgsqlConnection(database.ConnectionString);
            await observer.OpenAsync();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var command = observer.CreateCommand();
                command.CommandText = "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database()) AND NOT granted";
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
                {
                    waited = true;
                    break;
                }
                await Task.Delay(25);
            }
        }
        finally
        {
            await held.DisposeAsync();
        }
        var result = await saving;
        Assert.True(waited, "Назначение должно ждать общего lock перед чтением услуги и проверкой совместимости.");
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Single(result.Value!);
        await using var verification = database.CreateContext();
        Assert.Equal(1, await verification.GarageTariffAssignments.CountAsync(item => item.ChargeServiceSettingId == fixture.Service.Id));
    }

    [PostgreSqlFact]
    public async Task DirectCatalogCannotChangeTheBasisOfAServiceTariffWithoutHistoryButCanCorrectItsRate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var assigned = await CreateService(context).CreateAsync(fixture.Service.Id,
            Request(fixture) with { EffectiveFrom = Month.AddMonths(1) }, null, CancellationToken.None);
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);
        var general = fixture.Service.Tariff!;
        Assert.False(await context.ChargeServiceTariffVersions.AnyAsync(item => item.TariffId == general.Id));
        var dictionaries = DictionaryServiceTestFactory.Create(context, Month.AddDays(29));
        var beforeAudits = await context.AuditEvents.CountAsync();
        var rejected = await dictionaries.UpdateTariffAsync(general.Id,
            new(general.Name, TariffCalculationBases.People, 75, Month, "Смена базы", Version: general.Version), null, CancellationToken.None);
        Assert.Equal("tariff_history_version_required", rejected.ErrorCode);
        Assert.Equal(TariffCalculationBases.Fixed, general.CalculationBase);
        Assert.Equal(beforeAudits, await context.AuditEvents.CountAsync());
        var corrected = await dictionaries.UpdateTariffAsync(general.Id,
            new(general.Name, TariffCalculationBases.Fixed, 125, Month, "Правка ставки", Version: general.Version), null, CancellationToken.None);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        Assert.Equal(125m, corrected.Value!.Rate);
        Assert.Equal(200m, (await context.Tariffs.SingleAsync(item => item.IsIndividual)).Rate);
    }

    [PostgreSqlFact]
    public async Task AConflictInTheLastSchedulePeriodDoesNotMutateAnEarlierTrackedTariff()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var from = Month.AddMonths(1);
        var changesOn = Month.AddMonths(2);
        var general = fixture.Service.Tariff!;
        var future = new Tariff { Name = "Сетка на человека", CalculationBase = TariffCalculationBases.People, Rate = 75, EffectiveFrom = changesOn };
        context.AddRange(new ChargeServiceTariffVersion { ChargeServiceSettingId = fixture.Service.Id, Tariff = general, TariffId = general.Id, EffectiveFrom = Month, EffectiveTo = changesOn.AddDays(-1) },
            new ChargeServiceTariffVersion { ChargeServiceSettingId = fixture.Service.Id, Tariff = future, TariffId = future.Id, EffectiveFrom = changesOn });
        await context.SaveChangesAsync();
        var assigned = await CreateService(context).CreateAsync(fixture.Service.Id,
            Request(fixture) with { EffectiveFrom = from, EffectiveTo = changesOn.AddDays(-1) }, null, CancellationToken.None);
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);
        var beforeAudits = await context.AuditEvents.CountAsync();
        var beforeTariffs = await context.Tariffs.CountAsync();
        var request = new UpsertChargeServiceTariffScheduleRequest([
            new(general.Id, Month, from.AddDays(-1), 125, general.Version),
            new(future.Id, from, null, 75, future.Version)
        ], true, "Сдвиг сетки", fixture.Service.Version);
        var rejected = await DictionaryServiceTestFactory.Create(context, Month.AddDays(29))
            .UpdateChargeServiceTariffScheduleAsync(fixture.Service.Id, request, null, CancellationToken.None);
        Assert.Equal("charge_service_individual_tariff_base_conflict", rejected.ErrorCode);
        Assert.Equal(100m, general.Rate);
        Assert.Equal(100m, (await context.Tariffs.AsNoTracking().SingleAsync(item => item.Id == general.Id)).Rate);
        Assert.Equal(beforeAudits, await context.AuditEvents.CountAsync());
        Assert.Equal(beforeTariffs, await context.Tariffs.CountAsync());
        Assert.Equal(changesOn, (await context.ChargeServiceTariffVersions.AsNoTracking().SingleAsync(item => item.TariffId == future.Id)).EffectiveFrom);
    }

    [PostgreSqlFact]
    public Task ChangingModeClonesASharedGeneralTariffWithoutChangingAnotherServicesIndividualBasis() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.People, 75);

    [PostgreSqlFact]
    public Task ChangingRateClonesASharedGeneralTariffWithoutChangingAnotherServicesIndividualBasis() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.Fixed, 125);

    [PostgreSqlFact]
    public Task ReplacingScheduleClonesASharedGeneralTariffWithoutChangingAnotherServicesIndividualBasis() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.Fixed, 125, replaceSchedule: true);

    [PostgreSqlFact]
    public Task InlineRateCorrectionClonesASharedGeneralTariffWithoutChangingAnotherService() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.Fixed, 125, inlineCorrection: true);

    [PostgreSqlFact]
    public Task InlineRateCorrectionOfSharedTariffWithoutHistoryCreatesExactlyOnePeriod() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.Fixed, 125, inlineCorrection: true, withoutHistory: true);

    [PostgreSqlFact]
    public Task ModeChangeOfSharedTariffWithoutHistoryCreatesExactlyOnePeriod() =>
        AssertSharedTariffTermsAreIsolated(TariffCalculationBases.People, 75, withoutHistory: true);

    private static async Task AssertSharedTariffTermsAreIsolated(string calculationBase, int rate, bool replaceSchedule = false, bool inlineCorrection = false, bool withoutHistory = false)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var fund = new Fund { Name = "Фонд общих версий", AllowOperations = true };
        context.Add(fund);
        fixture.Income.DestinationFundId = fund.Id;
        var general = fixture.Service.Tariff!;
        var other = new ChargeServiceSetting { Name = "Другая услуга общего тарифа", IsRegular = true, PeriodicityMonths = 1, AccrualStartMonth = 1, IncomeTypeId = fixture.Income.Id, Tariff = general, TariffId = general.Id, UnitName = "руб." };
        context.Add(other);
        if (!withoutHistory)
        {
            context.AddRange(
                new ChargeServiceTariffVersion { ChargeServiceSettingId = fixture.Service.Id, Tariff = general, TariffId = general.Id, EffectiveFrom = Month },
                new ChargeServiceTariffVersion { ChargeServiceSetting = other, ChargeServiceSettingId = other.Id, Tariff = general, TariffId = general.Id, EffectiveFrom = Month });
        }
        await context.SaveChangesAsync();
        var assigned = await CreateService(context).CreateAsync(other.Id,
            Request(fixture) with { ServiceVersion = other.Version, EffectiveFrom = Month.AddMonths(1) }, null, CancellationToken.None);
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);
        var beforeVersion = general.Version;
        var dictionaries = DictionaryServiceTestFactory.Create(context, Month.AddDays(29));
        TariffDto changedTariff;
        if (replaceSchedule)
        {
            var changed = await dictionaries.UpdateChargeServiceTariffScheduleAsync(fixture.Service.Id,
                new([new(general.Id, Month, null, rate, general.Version)], true, "Правка общей сетки", fixture.Service.Version),
                null, CancellationToken.None);
            Assert.True(changed.Succeeded, changed.ErrorMessage);
            changedTariff = changed.Value!.Tariff;
        }
        else
        {
            var changed = await dictionaries.UpdateChargeServiceWithTariffAsync(fixture.Service.Id,
                new(new(fixture.Service.Name, true, 1, 1, 20, null, 30, false, false, "руб.", fixture.Income.Id, general.Id, fixture.Service.Version),
                    rate, inlineCorrection ? null : "regular", inlineCorrection ? null : Month,
                    ChangeReason: inlineCorrection ? null : "Изменение условий", CalculationBase: calculationBase, TariffVersion: general.Version),
                null, CancellationToken.None);
            Assert.True(changed.Succeeded, changed.ErrorMessage);
            changedTariff = changed.Value!.Tariff;
        }
        Assert.NotEqual(general.Id, changedTariff.Id);
        Assert.Equal(calculationBase, changedTariff.CalculationBase);
        Assert.Equal(rate, changedTariff.Rate);
        Assert.Equal(1, await context.ChargeServiceTariffVersions.CountAsync(item => item.ChargeServiceSettingId == fixture.Service.Id));
        var original = await context.Tariffs.AsNoTracking().SingleAsync(item => item.Id == general.Id);
        Assert.Equal(TariffCalculationBases.Fixed, original.CalculationBase);
        Assert.Equal(100m, original.Rate);
        Assert.Equal(beforeVersion, original.Version);
        Assert.Equal(general.Id, (await context.ChargeServiceSettings.AsNoTracking().SingleAsync(item => item.Id == other.Id)).TariffId);
        Assert.Equal(200m, (await context.Tariffs.AsNoTracking().SingleAsync(item => item.IsIndividual)).Rate);
    }

    [PostgreSqlFact]
    public async Task RebindingOrDisablingAServiceRejectsIncompatibleAssignmentsBeforeAnyWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var fund = new Fund { Name = "Фонд проверки привязки", AllowOperations = true };
        var incompatible = new Tariff { Name = "По людям", CalculationBase = TariffCalculationBases.People, Rate = 75, EffectiveFrom = Month };
        var compatible = new Tariff { Name = "Фиксированный альтернативный", CalculationBase = TariffCalculationBases.Fixed, Rate = 125, EffectiveFrom = Month };
        context.AddRange(fund, incompatible, compatible);
        fixture.Income.DestinationFundId = fund.Id;
        await context.SaveChangesAsync();
        var assigned = await CreateService(context).CreateAsync(fixture.Service.Id,
            Request(fixture) with { EffectiveFrom = Month.AddMonths(1) }, null, CancellationToken.None);
        Assert.True(assigned.Succeeded, assigned.ErrorMessage);
        var original = fixture.Service.TariffId;
        var request = new UpsertChargeServiceSettingRequest(fixture.Service.Name, true, 1, 1, 20,
            null, 30, false, false, "руб.", fixture.Income.Id, incompatible.Id, fixture.Service.Version);
        var dictionaries = DictionaryServiceTestFactory.Create(context, Month.AddDays(29));
        var beforeAudits = await context.AuditEvents.CountAsync();
        var rebound = await dictionaries.UpdateChargeServiceSettingAsync(fixture.Service.Id, request, null, CancellationToken.None);
        Assert.Equal("charge_service_individual_tariff_base_conflict", rebound.ErrorCode);
        var disabled = await dictionaries.UpdateChargeServiceSettingAsync(fixture.Service.Id,
            request with { IsRegular = false, TariffId = null, IncomeTypeId = null, PaymentDueDay = null }, null, CancellationToken.None);
        Assert.Equal("charge_service_individual_tariff_base_conflict", disabled.ErrorCode);
        Assert.Equal(original, fixture.Service.TariffId);
        Assert.True(fixture.Service.IsRegular);
        Assert.Equal(beforeAudits, await context.AuditEvents.CountAsync());
        Assert.False(await context.ChargeServiceTariffVersions.AnyAsync(item => item.ChargeServiceSettingId == fixture.Service.Id));
        var accepted = await dictionaries.UpdateChargeServiceSettingAsync(fixture.Service.Id,
            request with { TariffId = compatible.Id }, null, CancellationToken.None);
        Assert.True(accepted.Succeeded, accepted.ErrorMessage);
        Assert.Equal(compatible.Id, accepted.Value!.TariffId);
        Assert.Equal(200m, (await context.Tariffs.AsNoTracking().SingleAsync(item => item.IsIndividual)).Rate);
    }

    [PostgreSqlFact]
    public async Task ArchivedGarageAssignmentCanBeRetiredWithoutRecalculationButCannotBeEdited()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = await Setup(context);
        var created = await CreateService(context).CreateAsync(fixture.Service.Id, Request(fixture), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var assignment = Assert.Single(created.Value!);
        var accrual = await context.Accruals.SingleAsync(row => row.GarageId == fixture.First.Id && row.IncomeTypeId == fixture.Income.Id);
        var snapshot = accrual.CalculationDetailsJson;
        var storedSnapshot = await context.Accruals.AsNoTracking().Where(row => row.Id == accrual.Id)
            .Select(row => row.CalculationDetailsJson).SingleAsync();
        fixture.First.IsArchived = true;
        await context.SaveChangesAsync();
        var beforeAudit = await context.AuditEvents.CountAsync();
        var beforeTariffs = await context.Tariffs.CountAsync();
        var service = CreateService(context, DispatchProxy.Create<IFinanceService, FailingFinanceProxy>());
        // Future dates previously bypassed recalculation and allowed edits to archived garages.
        var changed = await service.UpdateAsync(fixture.Service.Id, assignment.Id,
            new(Month.AddMonths(1), null, 300, null, null, assignment.Version, fixture.Service.Version, "Попытка изменения архивного гаража"), null, CancellationToken.None);
        Assert.False(changed.Succeeded);
        Assert.Equal("garage_not_found", changed.ErrorCode);
        Assert.Equal(beforeTariffs, await context.Tariffs.CountAsync());
        Assert.Equal(beforeAudit, await context.AuditEvents.CountAsync());
        var archived = await service.ArchiveAsync(fixture.Service.Id, assignment.Id,
            new(assignment.Version, "Отмена назначения архивного гаража"), null, CancellationToken.None);
        Assert.True(archived.Succeeded, archived.ErrorMessage);
        Assert.True(archived.Value!.IsArchived);
        Assert.Equal(200m, accrual.Amount);
        Assert.Equal(snapshot, accrual.CalculationDetailsJson);
        Assert.False(accrual.IsCanceled);
        Assert.Equal(beforeAudit + 1, await context.AuditEvents.CountAsync());
        context.ChangeTracker.Clear();
        Assert.True((await context.GarageTariffAssignments.SingleAsync(row => row.Id == assignment.Id)).IsArchived);
        var storedAccrual = await context.Accruals.SingleAsync(row => row.Id == accrual.Id);
        Assert.Equal(200m, storedAccrual.Amount);
        Assert.Equal(storedSnapshot, storedAccrual.CalculationDetailsJson);
    }

    public class FailingFinanceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IFinanceService.CalculateGarageIncomeWorksheetAsync)
                ? Task.FromResult(FinanceResult<GarageIncomeWorksheetDto>.Failure("simulated_recalculation_failure", "Ошибка проверки перерасчёта"))
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private static GarageTariffAssignmentService CreateService(GarageBalanceDbContext context, IFinanceService? finance = null) => new(
        new EfGarageTariffAssignmentRepository(context, new EfAccrualPaymentAllocationRepository(context)),
        new EfFundRepository(context),
        new EfGarageRepository(context, new TestBusinessDateProvider(new(2050, 9, 30))),
        new EfChargeServiceSettingRepository(context), new EfTariffRepository(context), new EfApplicationUnitOfWork(context),
        new AuditEventWriter(context), finance ?? FinanceServiceTestFactory.Create(context, new FixedClock()),
        new TestBusinessDateProvider(new(2050, 9, 30)), new FixedClock());

    private sealed record Fixture(Garage First, Garage Second, IncomeType Income, ChargeServiceSetting Service);
    private static async Task<Fixture> Setup(GarageBalanceDbContext context)
    {
        var first = new Garage { Number = "85-ASSIGN-WRITE", RegisteredOn = Month };
        var second = new Garage { Number = "86-ASSIGN-WRITE", RegisteredOn = Month };
        var income = new IncomeType { Name = "Услуга проверки назначения", Code = "assignment_write_test" };
        var tariff = new Tariff { Name = "Общий тариф проверки записи", Rate = 100, CalculationBase = TariffCalculationBases.Fixed, EffectiveFrom = Month };
        var setting = new ChargeServiceSetting
        {
            Name = income.Name,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = income,
            IncomeTypeId = income.Id,
            Tariff = tariff,
            TariffId = tariff.Id,
            UnitName = "руб."
        };
        context.AddRange(first, second, setting);
        await context.SaveChangesAsync();
        return new(first, second, income, setting);
    }

    private static CreateGarageTariffAssignmentsRequest Request(Fixture fixture) => new([fixture.First.Id], Month, null, 200, null, null, fixture.Service.Version, "Индивидуальная ставка");
}
