using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlExpensePaymentTypeMigrationIntegrationTests
{
    private const string PreviousMigration = "20260723114059_LinkChargeServicesToExpenseTypes";

    [PostgreSqlFact]
    public async Task MigrationSeparatesPaymentTypeAndLinksHistoricalAtomicAccrual()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var atomicOperationId = Guid.NewGuid();
        var atomicAccrualId = Guid.NewGuid();
        var ordinaryOperationId = Guid.NewGuid();
        await using (var legacyContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = $"Миграция типов выплат {Guid.NewGuid():N}" };
            var cashArticle = new ExpenseType { Name = "Выплата без чека", Code = "no_receipt" };
            var ordinaryArticle = new ExpenseType
            {
                Name = $"Водоснабжение миграция {Guid.NewGuid():N}",
                Code = $"water_{Guid.NewGuid():N}"
            };
            var cashService = new ChargeServiceSetting { Name = "Кассовая статья", ExpenseType = cashArticle };
            var ordinaryService = new ChargeServiceSetting { Name = "Водоснабжение", ExpenseType = ordinaryArticle };
            var cashSupplier = new Supplier { Name = $"Кассовый поставщик {Guid.NewGuid():N}", Group = group, ChargeServiceSetting = cashService };
            var ordinarySupplier = new Supplier { Name = $"Обычный поставщик {Guid.NewGuid():N}", Group = group, ChargeServiceSetting = ordinaryService };
            legacyContext.AddRange(group, cashArticle, ordinaryArticle, cashService, ordinaryService, cashSupplier, ordinarySupplier);
            await legacyContext.SaveChangesAsync();

            var createdAt = DateTimeOffset.UtcNow;
            await InsertLegacyExpenseAsync(
                legacyContext,
                atomicOperationId,
                cashSupplier.Id,
                cashArticle.Id,
                125m,
                "LEGACY-CASH",
                createdAt);
            await legacyContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO supplier_accruals
                    ("Id", "SupplierId", "ExpenseTypeId", "AccountingMonth", "Amount", "Source",
                     "DocumentNumber", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({atomicAccrualId}, {cashSupplier.Id}, {cashArticle.Id}, {new DateOnly(2026, 7, 1)}, {125m},
                     {AccrualSources.Manual}, {"LEGACY-CASH"}, {false}, {createdAt}, {createdAt});
                """);
            await InsertLegacyExpenseAsync(
                legacyContext,
                ordinaryOperationId,
                ordinarySupplier.Id,
                ordinaryArticle.Id,
                80m,
                "LEGACY-RECEIPT",
                createdAt.AddMinutes(2));
        }

        await using (var migrateContext = database.CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var atomicOperation = await verificationContext.FinancialOperations.SingleAsync(operation => operation.Id == atomicOperationId);
        var ordinaryOperation = await verificationContext.FinancialOperations.SingleAsync(operation => operation.Id == ordinaryOperationId);
        var linkedAccrual = await verificationContext.SupplierAccruals.SingleAsync(accrual => accrual.Id == atomicAccrualId);
        Assert.Equal(ExpensePaymentTypes.WithoutReceipt, atomicOperation.ExpensePaymentType);
        Assert.Equal(ExpensePaymentTypes.WithReceipt, ordinaryOperation.ExpensePaymentType);
        Assert.Equal(atomicOperationId, linkedAccrual.SourceFinancialOperationId);

        verificationContext.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 7, 22),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 1m,
            ExpensePaymentType = "cash"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => verificationContext.SaveChangesAsync());
    }

    private static Task InsertLegacyExpenseAsync(
        GarageBalanceDbContext context,
        Guid operationId,
        Guid supplierId,
        Guid expenseTypeId,
        decimal amount,
        string documentNumber,
        DateTimeOffset createdAt) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO financial_operations
                ("Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount",
                 "DocumentNumber", "SupplierId", "ExpenseTypeId", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ({operationId}, {FinancialOperationKinds.Expense}, {new DateOnly(2026, 7, 20)},
                 {new DateOnly(2026, 7, 1)}, {amount}, {documentNumber}, {supplierId}, {expenseTypeId},
                 {false}, {createdAt}, {createdAt});
            """);
}
