using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class EfFinancialJournalQueryTests
{
    [Fact]
    public async Task GetPageAsync_CombinesAllFinancialEntityGroupsAndMarksProtectedRows()
    {
        await using var database = await JournalDatabase.CreateAsync();
        var seeded = await database.SeedAsync();
        var query = new EfFinancialJournalQuery(database.Context);

        var result = await query.GetPageAsync(
            new FinancialJournalRequest(null, null, null, null, null, null, 0, 100),
            CancellationToken.None);

        Assert.Equal(7, result.TotalCount);
        Assert.Equal(7, result.Items.Select(item => item.EntityType).Distinct().Count());
        Assert.Equal(seeded.BalanceOperationId, result.Items[0].Id);
        var regularAccrual = Assert.Single(result.Items, item => item.EntityType == "accrual");
        Assert.False(regularAccrual.CanEdit);
        Assert.True(regularAccrual.CanCancel);
        Assert.Contains("предпросмотр", regularAccrual.CorrectionHint, StringComparison.OrdinalIgnoreCase);
        var fundMovement = Assert.Single(result.Items, item => item.EntityType == "fund_operation");
        Assert.False(fundMovement.CanEdit);
        Assert.Contains("исходное", fundMovement.CorrectionHint, StringComparison.OrdinalIgnoreCase);
        var startingCorrection = Assert.Single(result.Items, item => item.EntityType == "cash_bank_balance_operation");
        Assert.NotNull(startingCorrection.ProtectionReason);
        Assert.False(startingCorrection.CanCancel);
    }

    [Fact]
    public async Task GetPageAsync_AppliesPeriodTypeCounterpartyStatusDocumentAndPaginationFilters()
    {
        await using var database = await JournalDatabase.CreateAsync();
        await database.SeedAsync();
        var query = new EfFinancialJournalQuery(database.Context);

        var financial = await query.GetPageAsync(
            new FinancialJournalRequest(
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30),
                "financial_operation",
                "103",
                "active",
                "7",
                0,
                10),
            CancellationToken.None);
        var item = Assert.Single(financial.Items);
        Assert.Equal("financial_operation", item.EntityType);
        Assert.Equal("ПКО-7", item.DocumentNumber);

        var canceled = await query.GetPageAsync(
            new FinancialJournalRequest(null, null, null, null, "canceled", null, 0, 10),
            CancellationToken.None);
        Assert.Empty(canceled.Items);

        var paged = await query.GetPageAsync(
            new FinancialJournalRequest(null, null, null, null, null, null, 1, 2),
            CancellationToken.None);
        Assert.Equal(7, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(1, paged.Offset);
        Assert.Equal(2, paged.Limit);
    }

    private sealed class JournalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private JournalDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<JournalDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new GarageBalanceDbContext(
                new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new JournalDatabase(connection, context);
        }

        public async Task<SeededJournal> SeedAsync()
        {
            var owner = new Owner { LastName = "Иванов", FirstName = "Иван" };
            var garage = new Garage { Number = "103", Owner = owner, PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = "Членский взнос", Code = "membership" };
            var expenseType = new ExpenseType { Name = "Ремонт", Code = "repair" };
            var group = new SupplierGroup { Name = "Подрядчики" };
            var supplier = new Supplier { Name = "Мастер", Group = group };
            var department = new StaffDepartment { Name = "Правление" };
            var staff = new StaffMember { FullName = "Сотрудник Петров", Department = department, Rate = 1000m };
            var fund = new Fund { Name = "Ремонтный фонд", NormalizedName = "РЕМОНТНЫЙ ФОНД" };
            var operation = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 9, 15),
                AccountingMonth = new DateOnly(2026, 9, 1),
                Amount = 500m,
                Garage = garage,
                IncomeType = incomeType,
                DocumentNumber = "ПКО-7",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)
            };
            var accrual = new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 9, 1),
                DueDate = new DateOnly(2026, 9, 10),
                OverdueFromDate = new DateOnly(2026, 9, 11),
                Amount = 700m,
                Source = AccrualSources.Regular,
                CreatedAtUtc = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)
            };
            var supplierAccrual = new SupplierAccrual
            {
                Supplier = supplier,
                ExpenseType = expenseType,
                AccountingMonth = new DateOnly(2026, 9, 1),
                Amount = 800m,
                Source = AccrualSources.Manual,
                DocumentNumber = "СЧ-1",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero)
            };
            var adjustment = new StaffSalaryAdjustment
            {
                StaffMember = staff,
                AccountingMonth = new DateOnly(2026, 9, 1),
                AdjustmentType = StaffSalaryAdjustmentTypes.Bonus,
                Amount = 100m,
                Reason = "Премия",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero)
            };
            var fundOperation = new FundOperation
            {
                Fund = fund,
                SourceFinancialOperation = operation,
                OperationKind = FundOperationKinds.Deposit,
                Amount = 500m,
                BalanceBefore = 0m,
                BalanceAfter = 500m,
                Reason = "Автоматическое распределение",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 15, 8, 1, 0, TimeSpan.Zero)
            };
            var transfer = new CashBankTransfer
            {
                TransferDate = new DateOnly(2026, 9, 20),
                Amount = 300m,
                CreatedAtUtc = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero)
            };
            var balanceOperation = new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Bank,
                OperationKind = CashBankBalanceOperationKinds.Adjustment,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 9, 30),
                Amount = 50m,
                Reason = "Сверка банка",
                CreatedAtUtc = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero)
            };
            Context.AddRange(
                owner,
                garage,
                incomeType,
                expenseType,
                group,
                supplier,
                department,
                staff,
                fund,
                operation,
                accrual,
                supplierAccrual,
                adjustment,
                fundOperation,
                transfer,
                balanceOperation);
            await Context.SaveChangesAsync();
            return new SeededJournal(balanceOperation.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record SeededJournal(Guid BalanceOperationId);
}
