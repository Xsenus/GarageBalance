using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialCommentStorageTests
{
    [PostgreSqlFact]
    public async Task OpeningDebtPayments_PreserveMaximumUserCommentAndFullPaymentRetry()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "LONG-COMMENT", StartingBalance = 1000m };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var month = new DateOnly(2026, 9, 1);
        var comment = new string('я', 1000);
        var expectedComment = $"Оплата входящего долга периода: {comment}";

        var single = await service.CreateGarageDebtPaymentAsync(
            new(garage.Id, month.AddDays(7), month, 100m, comment, Guid.NewGuid()), null, CancellationToken.None);
        Assert.True(single.Succeeded, single.ErrorMessage);
        Assert.Equal(expectedComment, single.Value!.Comment);

        var request = new CreateFullGaragePaymentRequest(garage.Id, month.AddDays(7),
            [new(null, month, 200m, comment, IsOpeningDebt: true)], Guid.NewGuid());
        var batch = await service.CreateFullGaragePaymentAsync(request, null, CancellationToken.None);
        Assert.True(batch.Succeeded, batch.ErrorMessage);
        Assert.Equal(expectedComment, Assert.Single(batch.Value!.Operations).Comment);

        var retry = await service.CreateFullGaragePaymentAsync(request, null, CancellationToken.None);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal(batch.Value.ReceiptBatchId, retry.Value!.ReceiptBatchId);
        Assert.Equal(Assert.Single(batch.Value.Operations).Id, Assert.Single(retry.Value.Operations).Id);
        var saved = await context.FinancialOperations.AsNoTracking().Where(item => item.GarageId == garage.Id).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Equal(300m, saved.Sum(item => item.Amount));
        Assert.All(saved, item => Assert.Equal(expectedComment, item.Comment));
    }

    [PostgreSqlFact]
    public async Task FundCancellation_PreservesOriginalCommentAndMaximumReason()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var originalComment = new string('к', 1000);
        var reason = new string('п', 1000);
        var fund = new Fund { Name = "Длинный комментарий", NormalizedName = "ДЛИННЫЙ КОММЕНТАРИЙ", Balance = 100m, IsSystem = false };
        var operation = new FundOperation
        {
            Fund = fund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = 100m,
            BalanceBefore = 0m,
            BalanceAfter = 100m,
            Reason = originalComment
        };
        context.FundOperations.Add(operation);
        await context.SaveChangesAsync();
        var service = new FundService(new EfFundRepository(context), new AuditEventWriter(context));

        var result = await service.CancelOperationAsync(operation.Id, new(reason), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var saved = await context.FundOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.True(saved.IsCanceled);
        Assert.Equal($"{originalComment}{Environment.NewLine}Отменено: {reason}", saved.Reason);
        Assert.Equal(0m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
    }

    [PostgreSqlFact]
    public async Task CommentStorageMigration_PreservesExistingTextAndExpandsOnlyGeneratedTextColumns()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20260908101341_AllowMultipleManualAccruals");
        await using var context = database.CreateContext();
        var originalComment = new string('к', 1000);
        var fund = new Fund { Name = "Миграция комментария", NormalizedName = "МИГРАЦИЯ КОММЕНТАРИЯ", IsSystem = false };
        var operation = new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Deposit, Amount = 1m, Reason = originalComment };
        context.FundOperations.Add(operation);
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();

        Assert.Equal(originalComment, (await context.FundOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id)).Reason);
        var expanded = await context.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type = 'text' AND (
                (table_name IN ('financial_operations', 'supplier_accruals', 'meter_readings') AND column_name = 'Comment')
                OR (table_name = 'fund_operations' AND column_name = 'Reason'))
            """).ToListAsync();
        Assert.Equal(4, expanded.Count);
        operation.Reason = $"{originalComment}\nОтменено: {originalComment}";
        await context.SaveChangesAsync();
        Assert.Equal(operation.Reason, (await context.FundOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id)).Reason);
        var downgradeError = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.MigrateAsync("20260908101341_AllowMultipleManualAccruals"));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, downgradeError.SqlState);
        Assert.Equal(operation.Reason, (await context.FundOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id)).Reason);
    }
}
