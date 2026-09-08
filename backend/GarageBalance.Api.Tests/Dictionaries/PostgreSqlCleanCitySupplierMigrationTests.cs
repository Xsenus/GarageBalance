using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlCleanCitySupplierMigrationTests
{
    private const string PreviousMigration = "20260908053253_ReconcileGelendzhikTariffCatalog";

    [PostgreSqlFact]
    public async Task Migration_ReconcilesCleanCityServiceAndExpenseTypeWithoutDeletingHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid trashExpenseTypeId;
        Guid trashFundId;
        Guid waterFundId;
        Guid historicalPaymentId;
        Guid historicalAccrualId;
        Guid historicalAuditId;
        Guid historicalPaymentVersion;
        Guid waterExpenseTypeId;
        var month = new DateOnly(2026, 9, 1);
        var historyTimestamp = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            var trashService = await context.SupplierServices.FirstOrDefaultAsync(item =>
                !item.IsArchived && (EF.Functions.ILike(item.Name, "%мусор%") || EF.Functions.ILike(item.Name, "%отход%")))
                ?? new SupplierService { Name = "Вывоз мусора — тест миграции" };
            var waterService = await context.SupplierServices.FirstOrDefaultAsync(item =>
                !item.IsArchived && EF.Functions.ILike(item.Name, "%вод%"))
                ?? new SupplierService { Name = "Водоснабжение — тест миграции" };
            var trashExpenseType = await context.ExpenseTypes.SingleAsync(item => item.Code == "trash_removal");
            var waterExpenseType = await context.ExpenseTypes.SingleAsync(item => item.Code == "water_supply");
            var trashFund = new Fund
            {
                Name = "Вывоз мусора — тест миграции",
                NormalizedName = "ВЫВОЗ МУСОРА — ТЕСТ МИГРАЦИИ",
                Balance = 500m
            };
            var waterFund = new Fund
            {
                Name = "Водоснабжение — тест миграции",
                NormalizedName = "ВОДОСНАБЖЕНИЕ — ТЕСТ МИГРАЦИИ",
                Balance = 900m
            };
            var group = new SupplierGroup { Name = "Тест миграции Чистого города" };
            var supplier = new Supplier
            {
                Name = "ООО «Чистый город»",
                Group = group,
                SupplierService = waterService,
                ExpenseType = waterExpenseType,
                ExpenseFund = trashFund
            };
            if (context.Entry(trashService).State == EntityState.Detached)
            {
                context.Add(trashService);
            }
            context.Add(supplier);
            var historicalPayment = new FinancialOperation
            {
                Supplier = supplier,
                ExpenseType = waterExpenseType,
                ExpenseFund = waterFund,
                OperationKind = FinancialOperationKinds.Expense,
                ExpensePaymentSource = ExpensePaymentSources.Bank,
                ExpensePaymentType = ExpensePaymentTypes.WithReceipt,
                OperationDate = month,
                AccountingMonth = month,
                Amount = 100m,
                DocumentNumber = "CLEAN-CITY-BEFORE",
                Comment = "Историческая выплата до исправления связи",
                CreatedAtUtc = historyTimestamp,
                UpdatedAtUtc = historyTimestamp
            };
            var historicalAccrual = new SupplierAccrual
            {
                Supplier = supplier,
                ExpenseType = waterExpenseType,
                ExpenseFund = waterFund,
                AccountingMonth = month,
                Amount = 200m,
                Source = AccrualSources.Manual,
                DocumentNumber = "CLEAN-CITY-ACCRUAL-BEFORE",
                Comment = "Историческое начисление до исправления связи",
                CreatedAtUtc = historyTimestamp,
                UpdatedAtUtc = historyTimestamp
            };
            var historicalAudit = new AuditEvent
            {
                Action = "finance.expense_created",
                EntityType = "financial_operation",
                EntityId = historicalPayment.Id.ToString(),
                Summary = "Синтетическая запись истории до исправления связи",
                MetadataJson = "{\"amount\":100}",
                CreatedAtUtc = historyTimestamp
            };
            context.AddRange(historicalPayment, historicalAccrual, historicalAudit,
                new FundOperation
                {
                    Fund = waterFund,
                    SourceFinancialOperation = historicalPayment,
                    OperationKind = FundOperationKinds.Withdraw,
                    Amount = 100m,
                    BalanceBefore = 1000m,
                    BalanceAfter = 900m,
                    Reason = "Историческое списание с водного фонда",
                    CreatedAtUtc = historyTimestamp,
                    UpdatedAtUtc = historyTimestamp
                },
                new CashBankBalanceOperation
                {
                    Account = CashBankAccounts.Bank,
                    OperationKind = CashBankBalanceOperationKinds.OpeningBalance,
                    Direction = CashBankBalanceDirections.Increase,
                    OperationDate = month,
                    Amount = 2000m,
                    Reason = "Синтетический остаток для проверки выплаты после миграции"
                });
            await context.SaveChangesAsync();
            supplierId = supplier.Id;
            trashExpenseTypeId = trashExpenseType.Id;
            trashFundId = trashFund.Id;
            waterFundId = waterFund.Id;
            waterExpenseTypeId = waterExpenseType.Id;
            historicalPaymentId = historicalPayment.Id;
            historicalPaymentVersion = historicalPayment.Version;
            historicalAccrualId = historicalAccrual.Id;
            historicalAuditId = historicalAudit.Id;

            Assert.Equal(1, await context.Suppliers.CountAsync(item =>
                item.Name.Contains("Чистый город") &&
                item.ExpenseFundId != null &&
                context.Funds.Any(fund => fund.Id == item.ExpenseFundId && fund.Name.Contains("мусор"))));
            Assert.True(await context.SupplierServices.AnyAsync(item =>
                !item.IsArchived && item.Id == trashService.Id));

            await context.Database.MigrateAsync();
            Assert.Contains("20260908094046_ReconcileCleanCitySupplierService", await context.Database.GetAppliedMigrationsAsync());
        }

        await using var verification = database.CreateContext();
        var migrated = await verification.Suppliers.AsNoTracking().SingleAsync(item => item.Id == supplierId);
        var migratedServiceName = await verification.SupplierServices
            .Where(item => item.Id == migrated.SupplierServiceId)
            .Select(item => item.Name)
            .SingleAsync();
        Assert.True(
            migratedServiceName.Contains("мусор", StringComparison.OrdinalIgnoreCase) ||
            migratedServiceName.Contains("отход", StringComparison.OrdinalIgnoreCase),
            $"Unexpected migrated service: {migratedServiceName}; supplier fund: {await verification.Funds.Where(item => item.Id == migrated.ExpenseFundId).Select(item => item.Name).SingleAsync()}");
        Assert.Equal(trashExpenseTypeId, migrated.ExpenseTypeId);
        Assert.Equal(trashFundId, migrated.ExpenseFundId);
        var preservedPayment = Assert.Single(await verification.FinancialOperations.AsNoTracking()
            .Where(item => item.SupplierId == supplierId).ToListAsync());
        Assert.Equal(historicalPaymentId, preservedPayment.Id);
        Assert.Equal(historicalPaymentVersion, preservedPayment.Version);
        Assert.Equal(waterExpenseTypeId, preservedPayment.ExpenseTypeId);
        Assert.Equal(waterFundId, preservedPayment.ExpenseFundId);
        Assert.Equal(100m, preservedPayment.Amount);
        Assert.Equal(month, preservedPayment.OperationDate);
        Assert.Equal(month, preservedPayment.AccountingMonth);
        Assert.Equal("CLEAN-CITY-BEFORE", preservedPayment.DocumentNumber);
        Assert.Equal("Историческая выплата до исправления связи", preservedPayment.Comment);
        Assert.Equal(historyTimestamp, preservedPayment.UpdatedAtUtc);
        Assert.False(preservedPayment.IsCanceled);
        var preservedAccrual = Assert.Single(await verification.SupplierAccruals.AsNoTracking()
            .Where(item => item.SupplierId == supplierId).ToListAsync());
        Assert.Equal(historicalAccrualId, preservedAccrual.Id);
        Assert.Equal(waterExpenseTypeId, preservedAccrual.ExpenseTypeId);
        Assert.Equal(waterFundId, preservedAccrual.ExpenseFundId);
        Assert.Equal(200m, preservedAccrual.Amount);
        Assert.Equal(month, preservedAccrual.AccountingMonth);
        Assert.Equal(AccrualSources.Manual, preservedAccrual.Source);
        Assert.Equal("CLEAN-CITY-ACCRUAL-BEFORE", preservedAccrual.DocumentNumber);
        Assert.Equal("Историческое начисление до исправления связи", preservedAccrual.Comment);
        Assert.Equal(historyTimestamp, preservedAccrual.UpdatedAtUtc);
        Assert.False(preservedAccrual.IsCanceled);
        var preservedDisbursement = await verification.FundOperations.AsNoTracking()
            .SingleAsync(item => item.SourceFinancialOperationId == historicalPaymentId);
        Assert.Equal(waterFundId, preservedDisbursement.FundId);
        Assert.Equal(100m, preservedDisbursement.Amount);
        Assert.Equal(1000m, preservedDisbursement.BalanceBefore);
        Assert.Equal(900m, preservedDisbursement.BalanceAfter);
        Assert.False(preservedDisbursement.IsCanceled);
        var preservedAudit = await verification.AuditEvents.AsNoTracking().SingleAsync(item => item.Id == historicalAuditId);
        Assert.Equal("finance.expense_created", preservedAudit.Action);
        Assert.Equal(historicalPaymentId.ToString(), preservedAudit.EntityId);
        Assert.Equal("{\"amount\":100}", preservedAudit.MetadataJson);
        Assert.Equal(historyTimestamp, preservedAudit.CreatedAtUtc);
        Assert.Single(await verification.AuditEvents.Where(item =>
            item.EntityId == supplierId.ToString() && item.Action == "dictionary.supplier_service_link_reconciled").ToListAsync());
        Assert.Equal(500m, (await verification.Funds.AsNoTracking().SingleAsync(item => item.Id == trashFundId)).Balance);
        Assert.Equal(900m, (await verification.Funds.AsNoTracking().SingleAsync(item => item.Id == waterFundId)).Balance);

        var service = FinanceServiceTestFactory.Create(verification);
        var request = new CreateExpenseOperationRequest(
            supplierId, trashExpenseTypeId, month.AddDays(1), month, 150m,
            "CLEAN-CITY-AFTER", "Выплата после исправления услуги",
            ExpensePaymentSource: ExpensePaymentSources.Bank);
        var mismatch = await service.CreateExpenseAsync(request with { ExpenseFundId = waterFundId }, null, CancellationToken.None);
        Assert.Equal("supplier_expense_fund_mismatch", mismatch.ErrorCode);
        Assert.Single(await verification.FinancialOperations.Where(item => item.SupplierId == supplierId).ToListAsync());

        var paid = await service.CreateExpenseAsync(request, null, CancellationToken.None);
        Assert.True(paid.Succeeded, paid.ErrorMessage);
        Assert.Equal(trashFundId, paid.Value!.ExpenseFundId);
        var newDisbursement = await verification.FundOperations.AsNoTracking()
            .SingleAsync(item => item.SourceFinancialOperationId == paid.Value.Id);
        Assert.Equal(trashFundId, newDisbursement.FundId);
        Assert.Equal(150m, newDisbursement.Amount);
        Assert.Equal(500m, newDisbursement.BalanceBefore);
        Assert.Equal(350m, newDisbursement.BalanceAfter);
        Assert.Equal(350m, (await verification.Funds.AsNoTracking().SingleAsync(item => item.Id == trashFundId)).Balance);
        Assert.Equal(900m, (await verification.Funds.AsNoTracking().SingleAsync(item => item.Id == waterFundId)).Balance);
        Assert.Equal(2, await verification.FinancialOperations.CountAsync(item => item.SupplierId == supplierId));
        Assert.Single(await verification.AuditEvents.Where(item =>
            item.EntityId == paid.Value.Id.ToString() && item.Action == "finance.expense_created").ToListAsync());
    }
}
