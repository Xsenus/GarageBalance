using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeteredAccrualCommentMigrationTests
{
    [PostgreSqlFact]
    public async Task MigrationPreservesNotesAndRejectsLossyRollback()
    {
        const string previousMigration = "20260907064334_AddGarageQuickListConcurrencyVersion";
        await using var database = await PostgreSqlTestDatabase.CreateAsync(previousMigration);
        await using var context = database.CreateContext();
        var original = new string('я', 1000);
        var accrual = new Accrual
        {
            Garage = new Garage { Number = "COMMENT-MIGRATION" },
            IncomeType = new IncomeType { Name = "Comment migration", Code = "comment_migration" },
            AccountingMonth = new DateOnly(2026, 6, 1),
            DueDate = new DateOnly(2026, 6, 26),
            Amount = 1m,
            Source = AccrualSources.Manual,
            Comment = original
        };
        context.Accruals.Add(accrual);
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync();
        Assert.Equal(original, await context.Accruals.AsNoTracking().Where(item => item.Id == accrual.Id).Select(item => item.Comment).SingleAsync());
        accrual.Comment = original + "; показание отменено";
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync(previousMigration));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, error.SqlState);
        Assert.Equal(accrual.Comment, await context.Accruals.AsNoTracking().Where(item => item.Id == accrual.Id).Select(item => item.Comment).SingleAsync());
        accrual.Comment = original;
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync(previousMigration);
        await context.Database.MigrateAsync();
        Assert.Equal(original, await context.Accruals.AsNoTracking().Where(item => item.Id == accrual.Id).Select(item => item.Comment).SingleAsync());
    }
}
