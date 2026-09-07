using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGroupSalaryConcurrencyTests
{
    [PostgreSqlFact]
    public Task ConcurrentSalaryWithoutDocument_CreatesOneBatchAndOneAudit() => VerifyConcurrentSalaryAsync(null);

    [PostgreSqlFact]
    public Task ConcurrentSalaryWithDocument_CreatesOneBatchAndOneAudit() => VerifyConcurrentSalaryAsync("SAL-CONCURRENT");

    [PostgreSqlFact]
    public async Task SalaryLock_CanceledWaitReleasesConnectionAndCanRetry()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        foreach (var initiallyOpen in new[] { false, true })
        {
            await using var owner = database.CreateContext();
            await using var waiter = database.CreateContext();
            if (initiallyOpen) await owner.Database.OpenConnectionAsync();
            var groupId = Guid.NewGuid();
            await using (var lease = await new EfSupplierAccrualRepository(owner).AcquireGroupSalaryLockAsync(groupId, new DateOnly(2026, 9, 1), CancellationToken.None))
            {
                using var cancellation = new CancellationTokenSource();
                var waiting = new EfSupplierAccrualRepository(waiter).AcquireGroupSalaryLockAsync(groupId, new DateOnly(2026, 9, 27), cancellation.Token);
                await WaitForLockOrCompletionAsync(database.ConnectionString, waiting);
                Assert.False(waiting.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
                Assert.Equal(ConnectionState.Closed, waiter.Database.GetDbConnection().State);
                await using var otherMonth = await new EfSupplierAccrualRepository(waiter).AcquireGroupSalaryLockAsync(groupId, new DateOnly(2026, 10, 1), CancellationToken.None);
            }
            Assert.Equal(initiallyOpen ? ConnectionState.Open : ConnectionState.Closed, owner.Database.GetDbConnection().State);
            await using var retry = await new EfSupplierAccrualRepository(waiter).AcquireGroupSalaryLockAsync(groupId, new DateOnly(2026, 9, 1), CancellationToken.None);
        }
    }

    [PostgreSqlFact]
    public async Task CanceledSalarySave_LeavesNoRowsOrAuditAndAllowsRetry()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = await SeedGroupAsync(database);
        var gate = new SaveGate();
        await using var canceled = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options);
        var request = new GenerateSupplierGroupSalaryAccrualsRequest(group.Id, new DateOnly(2026, 9, 1), 100m, null, null);
        using var cancellation = new CancellationTokenSource();
        var pending = FinanceServiceTestFactory.Create(canceled).GenerateSupplierGroupSalaryAccrualsAsync(request, null, cancellation.Token);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await using var retry = database.CreateContext();
        Assert.Equal(0, await retry.SupplierAccruals.CountAsync());
        Assert.Equal(0, await retry.AuditEvents.CountAsync(item => item.Action == "finance.supplier_group_salary_accruals_generated"));
        var result = await FinanceServiceTestFactory.Create(retry).GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, result.Value!.CreatedCount);
    }

    private static async Task VerifyConcurrentSalaryAsync(string? documentNumber)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var group = await SeedGroupAsync(database);

        var gate = new SaveGate();
        await using var first = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options);
        await using var second = database.CreateContext();
        var request = new GenerateSupplierGroupSalaryAccrualsRequest(group.Id, new DateOnly(2026, 9, 1), 100m, documentNumber, "Контроль");
        var firstTask = FinanceServiceTestFactory.Create(first).GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var secondTask = FinanceServiceTestFactory.Create(second).GenerateSupplierGroupSalaryAccrualsAsync(request, null, CancellationToken.None);
        try
        {
            // Release the first save only once the second request either waits on the
            // database lock or completes, independently of which request the scheduler favors.
            await WaitForLockOrCompletionAsync(database.ConnectionString, secondTask);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        var results = await Task.WhenAll(firstTask, secondTask);
        Assert.Single(results, result => result.Succeeded);
        Assert.Equal("salary_accruals_empty", Assert.Single(results, result => !result.Succeeded).ErrorCode);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.SupplierAccruals.CountAsync(item => !item.IsCanceled));
        Assert.Equal(200m, await verify.SupplierAccruals.SumAsync(item => item.Amount));
        var audit = Assert.Single(await verify.AuditEvents.Where(item => item.Action == "finance.supplier_group_salary_accruals_generated").ToListAsync());
        Assert.Equal(documentNumber, audit.RelatedDocumentNumber);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("2", metadata.RootElement.GetProperty("createdCount").GetString());
    }

    private static async Task<SupplierGroup> SeedGroupAsync(PostgreSqlTestDatabase database)
    {
        var group = new SupplierGroup { Name = "Контроль зарплаты" };
        await using var seed = database.CreateContext();
        if (!await seed.ExpenseTypes.AnyAsync(item => item.Code == "salary"))
            seed.ExpenseTypes.Add(new ExpenseType { Name = "Зарплата", Code = "salary", IsSystem = true });
        seed.Suppliers.AddRange(
            new Supplier { Name = "Первый участник", Group = group },
            new Supplier { Name = "Второй участник", Group = group });
        await seed.SaveChangesAsync();
        return group;
    }

    private static async Task WaitForLockOrCompletionAsync(string connectionString, Task pending)
    {
        await using var inspection = new NpgsqlConnection(connectionString);
        await inspection.OpenAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!pending.IsCompleted)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory')", inspection);
            if ((bool)(await command.ExecuteScalarAsync(deadline.Token))!) return;
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
