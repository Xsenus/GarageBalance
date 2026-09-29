using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageTariffAssignmentTests
{
    [PostgreSqlFact]
    public async Task SharedVersionLookupIncludesArchivedServicesAndHistoricalReferencesButExcludesTheOwner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fixture = Fixture();
        fixture.ChargeServiceSetting.Tariff = fixture.Tariff;
        fixture.ChargeServiceSetting.TariffId = fixture.TariffId;
        context.Add(fixture);
        await context.SaveChangesAsync();
        var repository = new EfChargeServiceSettingRepository(context);
        Assert.False(await repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, fixture.TariffId, CancellationToken.None));
        var other = new ChargeServiceSetting { Name = "Архивная услуга общей версии", IsArchived = true, TariffId = fixture.TariffId };
        context.Add(other);
        await context.SaveChangesAsync();
        Assert.True(await repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, fixture.TariffId, CancellationToken.None));
        other.TariffId = null;
        context.Add(new ChargeServiceTariffVersion { ChargeServiceSettingId = other.Id, TariffId = fixture.TariffId, EffectiveFrom = new(2050, 9, 1), IsArchived = true });
        await context.SaveChangesAsync();
        Assert.True(await repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, fixture.TariffId, CancellationToken.None));
        Assert.False(await repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasOtherServiceTariffReferenceAsync(Guid.Empty, fixture.TariffId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, Guid.Empty, CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.HasOtherServiceTariffReferenceAsync(fixture.ChargeServiceSettingId, fixture.TariffId, canceled.Token));
    }

    [PostgreSqlFact]
    public async Task GeneralBasisCompatibilityUsesServerScopedInclusiveIntervalsAndIgnoresArchivedAssignments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var assignment = Fixture();
        assignment.EffectiveFrom = new(2050, 10, 1);
        assignment.EffectiveTo = new(2050, 10, 31);
        context.Add(assignment);
        await context.SaveChangesAsync();
        var repository = new EfChargeServiceSettingRepository(context);
        var id = assignment.ChargeServiceSettingId;
        var basis = assignment.Tariff.CalculationBase;
        Assert.False(await repository.HasIncompatibleIndividualTariffAsync(id, basis, assignment.EffectiveFrom, null, CancellationToken.None));
        var different = basis == TariffCalculationBases.People ? TariffCalculationBases.Fixed : TariffCalculationBases.People;
        Assert.True(await repository.HasIncompatibleIndividualTariffAsync(id, different, assignment.EffectiveTo.Value, null, CancellationToken.None));
        Assert.True(await repository.HasIncompatibleIndividualTariffAsync(id, different, new(2050, 9, 1), assignment.EffectiveFrom, CancellationToken.None));
        Assert.False(await repository.HasIncompatibleIndividualTariffAsync(id, different, new(2050, 11, 1), null, CancellationToken.None));
        Assert.False(await repository.HasIncompatibleIndividualTariffAsync(id, different, new(2050, 9, 1), new(2050, 9, 30), CancellationToken.None));
        Assert.False(await repository.HasIncompatibleIndividualTariffAsync(Guid.NewGuid(), different, assignment.EffectiveFrom, null, CancellationToken.None));
        assignment.IsArchived = true;
        await context.SaveChangesAsync();
        Assert.False(await repository.HasIncompatibleIndividualTariffAsync(id, different, assignment.EffectiveFrom, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasIncompatibleIndividualTariffAsync(Guid.Empty, basis, assignment.EffectiveFrom, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasIncompatibleIndividualTariffAsync(id, basis, assignment.EffectiveFrom, new(2050, 9, 1), CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.HasIncompatibleIndividualTariffAsync(id, basis, assignment.EffectiveFrom, null, canceled.Token));
    }

    [PostgreSqlFact]
    public async Task WriteRepositoryFiltersPagesAndScopesLookupsToTheRequestedService()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = Fixture();
        context.Add(first);
        await context.SaveChangesAsync();
        var archived = ReferenceCopy(first);
        archived.IsArchived = true;
        var second = ReferenceCopy(first);
        second.Garage = new Garage { Number = "100" };
        second.GarageId = second.Garage.Id;
        context.AddRange(archived, second);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new EfGarageTariffAssignmentRepository(context, new EfAccrualPaymentAllocationRepository(context));
        var page = await repository.GetPageAsync(first.ChargeServiceSettingId, null, false, 1, 1, CancellationToken.None);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(200m, page.Items[0].Tariff.Rate);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(2, (await repository.GetPageAsync(first.ChargeServiceSettingId, first.GarageId, true, 0, 10, CancellationToken.None)).TotalCount);
        Assert.Null(await repository.FindGarageIdAsync(Guid.NewGuid(), first.Id, CancellationToken.None));
        Assert.Null(await repository.FindForUpdateAsync(Guid.NewGuid(), first.Id, CancellationToken.None));
        Assert.Equal(first.GarageId, await repository.FindGarageIdAsync(first.ChargeServiceSettingId, first.Id, CancellationToken.None));
        Assert.Equal(first.Id, (await repository.FindForUpdateAsync(first.ChargeServiceSettingId, first.Id, CancellationToken.None))!.Id);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetPageAsync(first.ChargeServiceSettingId, null, false, -1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetPageAsync(first.ChargeServiceSettingId, null, false, 0, 501, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task WriteScopeRollsBackUnsavedCompletionAndCommitsExplicitCompletion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = Fixture();
        context.Add(first);
        await context.SaveChangesAsync();
        var repository = new EfGarageTariffAssignmentRepository(context, new EfAccrualPaymentAllocationRepository(context));
        await using (await repository.BeginWriteAsync([first.GarageId, first.GarageId], CancellationToken.None))
        {
            first.Comment = "Незавершённое изменение";
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
        var row = (await repository.FindForUpdateAsync(first.ChargeServiceSettingId, first.Id, CancellationToken.None))!;
        Assert.Equal("Основание тестового назначения", row.Comment);
        await using (var scope = await repository.BeginWriteAsync([first.GarageId], CancellationToken.None))
        {
            row.Comment = "Подтверждённое изменение";
            await context.SaveChangesAsync();
            await scope.CommitAsync(CancellationToken.None);
        }
        await using var fresh = database.CreateContext();
        Assert.Equal("Подтверждённое изменение", (await fresh.GarageTariffAssignments.SingleAsync(item => item.Id == first.Id)).Comment);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.BeginWriteAsync([first.GarageId], canceled.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.BeginWriteAsync([], CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task AssignmentWriteSerializesWithWorksheetAndReleasesLocksAfterRollback()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var garageId = Guid.NewGuid();
        var repository = new EfGarageTariffAssignmentRepository(first, new EfAccrualPaymentAllocationRepository(first));
        var worksheetRepository = new EfAccrualPaymentAllocationRepository(second);
        await using (await repository.BeginWriteAsync([garageId], CancellationToken.None))
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worksheetRepository.AcquireGarageIncomeWorksheetLockAsync(garageId, deadline.Token));
        }
        using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (await worksheetRepository.AcquireGarageIncomeWorksheetLockAsync(garageId, releaseDeadline.Token)) { }
        await using (var outer = await first.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginWriteAsync([garageId], CancellationToken.None));
            await outer.RollbackAsync();
        }
        await using (await repository.BeginWriteAsync([garageId], releaseDeadline.Token)) { }
    }

    [PostgreSqlFact]
    public async Task OverlapCheckUsesInclusiveDatesAndIgnoresArchivedAndUnrelatedAssignments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = Fixture();
        first.EffectiveTo = new(2050, 9, 15);
        context.Add(first);
        await context.SaveChangesAsync();
        var repository = new EfGarageTariffAssignmentRepository(context, new EfAccrualPaymentAllocationRepository(context));
        Assert.True(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [first.GarageId], new(2050, 9, 15), null, null, CancellationToken.None));
        Assert.False(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [first.GarageId], new(2050, 9, 16), null, null, CancellationToken.None));
        Assert.False(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [first.GarageId], first.EffectiveFrom, null, first.Id, CancellationToken.None));
        Assert.False(await repository.HasOverlapAsync(Guid.NewGuid(), [first.GarageId], first.EffectiveFrom, null, null, CancellationToken.None));
        Assert.False(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [Guid.NewGuid()], first.EffectiveFrom, null, null, CancellationToken.None));
        first.IsArchived = true;
        await context.SaveChangesAsync();
        Assert.False(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [first.GarageId], first.EffectiveFrom, null, null, CancellationToken.None));
        Assert.False(await repository.HasOverlapAsync(first.ChargeServiceSettingId, [], first.EffectiveFrom, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasOverlapAsync(first.ChargeServiceSettingId, [first.GarageId], first.EffectiveFrom, first.EffectiveFrom.AddDays(-1), null, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task PeriodQueryReadsMultipleMonthsAndServicesWithoutMixingOrIgnoringInvalidPeriods()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var original = Fixture();
        original.EffectiveTo = new(2050, 9, 15);
        context.Add(original);
        await context.SaveChangesAsync();
        var later = ReferenceCopy(original);
        later.EffectiveFrom = new(2050, 10, 1);
        var otherService = ReferenceCopy(original);
        otherService.ChargeServiceSetting = new ChargeServiceSetting { Name = "Параллельная услуга периода", IsRegular = true };
        otherService.ChargeServiceSettingId = otherService.ChargeServiceSetting.Id;
        var archived = ReferenceCopy(original);
        archived.IsArchived = true;
        var otherGarage = ReferenceCopy(original);
        otherGarage.Garage = new Garage { Number = "87-PERIOD" };
        otherGarage.GarageId = otherGarage.Garage.Id;
        context.AddRange(later, otherService, archived, otherGarage);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var query = new EfGarageTariffAssignmentQuery(context);
        var rows = await query.GetForGaragePeriodAsync(original.GarageId, new(2050, 9, 18), new(2050, 10, 4), CancellationToken.None);
        Assert.Equal(new[] { original.Id, later.Id, otherService.Id }.Order(), rows.Select(row => row.Id).Order());
        Assert.All(rows, row => Assert.Equal(200m, row.Tariff.Rate));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await query.GetForGaragePeriodAsync(Guid.NewGuid(), new(2050, 9, 1), new(2050, 10, 1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetForGaragePeriodAsync(original.GarageId, new(2050, 10, 1), new(2050, 9, 1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetForGaragePeriodAsync(original.GarageId, new(2000, 1, 1), new(2050, 1, 1), CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetForGaragePeriodAsync(original.GarageId, new(2050, 9, 1), new(2050, 10, 1), canceled.Token));
        var overlapping = ReferenceCopy(original);
        overlapping.EffectiveFrom = new(2050, 9, 2);
        context.Add(overlapping);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetForGaragePeriodAsync(original.GarageId, new(2050, 9, 1), new(2050, 10, 1), CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task BatchQueryKeepsIndependentGarageSchedulesAndFiltersBeforeMaterializing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var first = Fixture();
        context.Add(first);
        await context.SaveChangesAsync();
        var second = ReferenceCopy(first);
        second.Garage = new Garage { Number = "86" };
        second.GarageId = second.Garage.Id;
        var excluded = ReferenceCopy(first);
        excluded.Garage = new Garage { Number = "87" };
        excluded.GarageId = excluded.Garage.Id;
        var archived = ReferenceCopy(first);
        archived.IsArchived = true;
        context.AddRange(second, excluded, archived);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var query = new EfGarageTariffAssignmentQuery(context);
        var rows = await query.GetApplicableForGaragesAsync(
            [first.GarageId, second.GarageId, first.GarageId], first.ChargeServiceSettingId,
            first.EffectiveFrom, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), rows.Select(row => row.Id).Order());
        Assert.All(rows, row => Assert.Equal(200m, row.Tariff.Rate));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await query.GetApplicableForGaragesAsync([], first.ChargeServiceSettingId, first.EffectiveFrom, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => query.GetApplicableForGaragesAsync(
            Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(), first.ChargeServiceSettingId,
            first.EffectiveFrom, CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetApplicableForGaragesAsync(
            [], first.ChargeServiceSettingId, first.EffectiveFrom, canceled.Token));

        var overlap = ReferenceCopy(first);
        overlap.EffectiveFrom = first.EffectiveFrom.AddDays(1);
        context.Add(overlap);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetApplicableForGaragesAsync(
            [first.GarageId, second.GarageId], first.ChargeServiceSettingId, first.EffectiveFrom, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task ApplicableQueryFiltersGarageServiceDatesAndArchivedAssignmentsBeforeMaterializing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var original = Fixture();
        original.EffectiveFrom = new(2050, 8, 15);
        original.EffectiveTo = new(2050, 9, 15);
        context.Add(original);
        await context.SaveChangesAsync();
        var later = ReferenceCopy(original);
        later.EffectiveFrom = new(2050, 9, 16);
        var past = ReferenceCopy(original);
        past.EffectiveFrom = new(2050, 7, 1);
        past.EffectiveTo = new(2050, 8, 14);
        var future = ReferenceCopy(original);
        future.EffectiveFrom = new(2050, 10, 1);
        var archived = ReferenceCopy(original);
        archived.IsArchived = true;
        var otherGarage = ReferenceCopy(original);
        otherGarage.Garage = new Garage { Number = "86" };
        otherGarage.GarageId = otherGarage.Garage.Id;
        var otherService = ReferenceCopy(original);
        otherService.ChargeServiceSetting = new ChargeServiceSetting { Name = "Другая услуга проверки 2050", IsRegular = true };
        otherService.ChargeServiceSettingId = otherService.ChargeServiceSetting.Id;
        context.AddRange(later, past, future, archived, otherGarage, otherService);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var query = new EfGarageTariffAssignmentQuery(context);
        var rows = await query.GetApplicableAsync(original.GarageId, original.ChargeServiceSettingId, new(2050, 9, 18), CancellationToken.None);
        Assert.Equal([original.Id, later.Id], rows.Select(row => row.Id).ToArray());
        Assert.All(rows, row => Assert.Equal(200m, row.Tariff.Rate));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await query.GetApplicableAsync(Guid.NewGuid(), original.ChargeServiceSettingId, new(2050, 9, 1), CancellationToken.None));
        Assert.Empty(await query.GetApplicableAsync(original.GarageId, Guid.NewGuid(), new(2050, 9, 1), CancellationToken.None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetApplicableAsync(original.GarageId, original.ChargeServiceSettingId, new(2050, 9, 1), canceled.Token));
    }

    [PostgreSqlFact]
    public async Task ApplicableQueryRejectsOverlapsInsteadOfSilentlyApplyingAnArbitraryRate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var original = Fixture();
        context.Add(original);
        await context.SaveChangesAsync();
        var overlap = ReferenceCopy(original);
        overlap.EffectiveFrom = original.EffectiveFrom.AddDays(1);
        context.Add(overlap);
        await context.SaveChangesAsync();
        var query = new EfGarageTariffAssignmentQuery(context);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetApplicableAsync(original.GarageId, original.ChargeServiceSettingId, original.EffectiveFrom, CancellationToken.None));
        Assert.Contains("пересекаются", error.Message, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task ApplicableQueryDoesNotSilentlyTruncateCorruptOverlappingHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var original = Fixture();
        original.EffectiveFrom = new(2050, 1, 1);
        context.Add(original);
        await context.SaveChangesAsync();
        for (var index = 1; index <= 31; index++)
        {
            var overlap = ReferenceCopy(original);
            overlap.EffectiveFrom = original.EffectiveFrom.AddDays(index);
            context.Add(overlap);
        }
        await context.SaveChangesAsync();
        var query = new EfGarageTariffAssignmentQuery(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetApplicableAsync(original.GarageId, original.ChargeServiceSettingId, new(2050, 9, 1), CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task MigrationAppliesRollsBackAndReappliesWithoutChangingPreviousTables()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var garages = await context.Garages.CountAsync();
        await context.Database.MigrateAsync("20260922053527_AddStorageCatalog");
        Assert.Contains("20260928111147_AddGarageTariffAssignments", await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(garages, await context.Garages.CountAsync());
        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await context.GarageTariffAssignments.CountAsync());
        Assert.Equal(garages, await context.Garages.CountAsync());
    }

    [PostgreSqlFact]
    public async Task AssignmentRoundTripsReferencesOpenEndedDatesAndVersion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var assignment = Fixture();
        context.Add(assignment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.GarageTariffAssignments.Include(item => item.Garage)
            .Include(item => item.ChargeServiceSetting).Include(item => item.Tariff).SingleAsync();
        Assert.Equal("85", stored.Garage.Number);
        Assert.Equal("Индивидуальная ставка проверки 2050", stored.Tariff.Name);
        Assert.Equal(200m, stored.Tariff.Rate);
        Assert.Equal(new DateOnly(2050, 9, 1), stored.EffectiveFrom);
        Assert.Null(stored.EffectiveTo);
        Assert.NotEqual(Guid.Empty, stored.Version);
        Assert.Equal("Основание тестового назначения", stored.Comment);
        Assert.False(stored.IsArchived);
    }

    [PostgreSqlFact]
    public async Task ActiveDuplicateIsRejectedArchivedHistoryIsRetainedAndConcurrentUpdateFails()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var original = Fixture();
        first.Add(original);
        await first.SaveChangesAsync();
        var duplicate = ReferenceCopy(original);
        first.Add(duplicate);
        var duplicateError = await Assert.ThrowsAsync<DbUpdateException>(() => first.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateError.InnerException).SqlState);
        first.ChangeTracker.Clear();
        var current = await first.GarageTariffAssignments.SingleAsync();
        await using var second = database.CreateContext();
        var stale = await second.GarageTariffAssignments.SingleAsync();
        var oldVersion = current.Version;
        current.IsArchived = true;
        await first.SaveChangesAsync();
        Assert.NotEqual(oldVersion, current.Version);
        stale.Comment = "Конкурирующее изменение";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        first.Add(ReferenceCopy(original));
        await first.SaveChangesAsync();
        Assert.Equal(2, await first.GarageTariffAssignments.CountAsync());
        Assert.Equal(1, await first.GarageTariffAssignments.CountAsync(item => !item.IsArchived));
    }

    [PostgreSqlFact]
    public async Task InvalidPeriodMissingGarageAndPhysicalTariffDeletionAreRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var original = Fixture();
        context.Add(original);
        await context.SaveChangesAsync();
        var invalid = ReferenceCopy(original);
        invalid.EffectiveFrom = new(2050, 10, 1);
        invalid.EffectiveTo = new(2050, 9, 30);
        context.Add(invalid);
        var periodError = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(periodError.InnerException).SqlState);
        context.ChangeTracker.Clear();
        var missingGarage = ReferenceCopy(original);
        missingGarage.GarageId = Guid.NewGuid();
        context.Add(missingGarage);
        var referenceError = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(referenceError.InnerException).SqlState);
        context.ChangeTracker.Clear();
        var tariff = await context.Tariffs.SingleAsync(item => item.Id == original.TariffId);
        context.Remove(tariff);
        var deleteError = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(deleteError.InnerException).SqlState);
    }

    private static GarageTariffAssignment Fixture()
    {
        var garage = new Garage { Number = "85" };
        var tariff = new Tariff { Name = "Индивидуальная ставка проверки 2050", CalculationBase = TariffCalculationBases.Fixed, Rate = 200m, EffectiveFrom = new(2050, 9, 1) };
        var service = new ChargeServiceSetting { Name = "Услуга проверки назначения 2050", IsRegular = true };
        return new GarageTariffAssignment
        {
            Garage = garage,
            GarageId = garage.Id,
            Tariff = tariff,
            TariffId = tariff.Id,
            ChargeServiceSetting = service,
            ChargeServiceSettingId = service.Id,
            EffectiveFrom = new(2050, 9, 1),
            Comment = "Основание тестового назначения",
        };
    }

    private static GarageTariffAssignment ReferenceCopy(GarageTariffAssignment original) => new()
    {
        GarageId = original.GarageId,
        ChargeServiceSettingId = original.ChargeServiceSettingId,
        TariffId = original.TariffId,
        EffectiveFrom = original.EffectiveFrom,
    };
}
