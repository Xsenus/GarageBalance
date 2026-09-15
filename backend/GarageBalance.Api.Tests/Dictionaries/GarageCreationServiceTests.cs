using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageCreationServiceTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 16);

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_CreatesGarageAccrualTargetedIncomeAllocationAndAuditAtomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incomeType = new IncomeType { Name = "Членский взнос", Code = "annual_membership_creation" };
        var tariff = new Tariff
        {
            Name = "Членский 2026",
            CalculationBase = TariffCalculationBases.People,
            Rate = 300m,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Годовой членский взнос",
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            PaymentDueMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = tariff,
            UnitName = "руб./чел."
        });
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);
        var actorUserId = Guid.NewGuid();

        var result = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(
                new UpsertGarageRequest("A-17", 3, 2, null, 0m, null, null, "Создан с оплатой"),
                [new PaidAnnualPaymentRequest(incomeType.Id, 600m)]),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var garage = Assert.Single(database.Context.Garages);
        var accrual = Assert.Single(database.Context.Accruals);
        var income = Assert.Single(database.Context.FinancialOperations);
        var allocation = Assert.Single(database.Context.AccrualPaymentAllocations);
        Assert.Equal(garage.Id, result.Value!.Id);
        Assert.Equal(300m, result.Value.Balance);
        Assert.Equal(900m, accrual.Amount);
        Assert.Equal(600m, income.Amount);
        Assert.Equal(accrual.Id, income.TargetAccrualId);
        Assert.Equal(accrual.Id, allocation.AccrualId);
        Assert.Equal(600m, allocation.Amount);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "dictionary.garage_created" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.annual_accrual_calculated_for_garage_card" && item.ActorUserId == actorUserId);
        Assert.Contains(database.Context.AuditEvents, item => item.Action == "finance.income_created" && item.ActorUserId == actorUserId);
    }

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_RejectsDuplicateOrExcessivePaymentBeforeCreatingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incomeType = new IncomeType { Name = "Охрана", Code = "annual_security_creation" };
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Годовая охрана",
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            PaymentDueMonth = 1,
            OverdueGraceDays = 30,
            IncomeType = incomeType,
            Tariff = new Tariff
            {
                Name = "Охрана 2026",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 500m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            },
            UnitName = "руб."
        });
        await database.Context.SaveChangesAsync();
        var service = CreateService(database.Context);
        var garage = new UpsertGarageRequest("B-5", 1, 1, null, 0m, null, null, null);

        var duplicate = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(garage,
                [new PaidAnnualPaymentRequest(incomeType.Id, 100m), new PaidAnnualPaymentRequest(incomeType.Id, 200m)]),
            null,
            CancellationToken.None);
        var excessive = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(garage, [new PaidAnnualPaymentRequest(incomeType.Id, 500.01m)]),
            null,
            CancellationToken.None);

        Assert.Equal("garage_annual_payment_duplicate", duplicate.ErrorCode);
        Assert.Equal("garage_annual_payment_amount_exceeds_cost", excessive.ErrorCode);
        Assert.Empty(database.Context.Garages);
        Assert.Empty(database.Context.Accruals);
        Assert.Empty(database.Context.FinancialOperations);
    }

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_RejectsMissingAndInvalidPaymentsBeforeCreatingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        var garage = new UpsertGarageRequest("C-9", 1, 1, null, 0m, null, null, null);

        var missing = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(garage, []),
            null,
            CancellationToken.None);
        var invalid = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(garage, [new PaidAnnualPaymentRequest(Guid.Empty, 0m)]),
            null,
            CancellationToken.None);

        Assert.Equal("garage_annual_payments_required", missing.ErrorCode);
        Assert.Equal("garage_annual_payments_invalid", invalid.ErrorCode);
        Assert.Empty(database.Context.Garages);
    }

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_RejectsUnavailablePaymentBeforeCreatingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);

        var result = await service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(
                new UpsertGarageRequest("D-4", 1, 1, null, 0m, null, null, null),
                [new PaidAnnualPaymentRequest(Guid.NewGuid(), 100m)]),
            null,
            CancellationToken.None);

        Assert.Equal("garage_annual_payment_not_available", result.ErrorCode);
        Assert.Empty(database.Context.Garages);
    }

    [Fact]
    public async Task CreateWithAnnualPaymentsAsync_PropagatesCancellationBeforeCreatingGarage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = CreateService(database.Context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateWithAnnualPaymentsAsync(
            new CreateGarageWithAnnualPaymentsRequest(
                new UpsertGarageRequest("E-2", 1, 1, null, 0m, null, null, null),
                [new PaidAnnualPaymentRequest(Guid.NewGuid(), 100m)]),
            null,
            cancellation.Token));

        Assert.Empty(database.Context.Garages);
    }

    private static GarageCreationService CreateService(GarageBalanceDbContext context)
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
        var businessDateProvider = TestBusinessDateProvider.From(timeProvider);
        return new GarageCreationService(
            DictionaryServiceTestFactory.Create(context, BusinessDate),
            FinanceServiceTestFactory.Create(context, timeProvider),
            new EfExpenseBatchTransactionRunner(context),
            businessDateProvider);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestDatabase(SqliteConnection connection, GarageBalanceDbContext context) : IAsyncDisposable
    {
        public GarageBalanceDbContext Context { get; } = context;

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
