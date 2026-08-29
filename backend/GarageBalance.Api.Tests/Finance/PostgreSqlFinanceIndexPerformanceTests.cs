using System.Data.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinanceIndexPerformanceTests
{
    [PostgreSqlFact]
    public async Task FinanceWorkingListSearchPlansUseTrigramIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedSearchVolumeAsync(database);
        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        await new EfAccrualRepository(context).GetPageAsync(null, null, "основание для поиска", 0, 25, CancellationToken.None);
        await AssertCapturedPlanUsesAsync(database.ConnectionString, capture.TakeSingle(), "IX_accruals_Basis_trgm");

        await new EfMeterReadingRepository(context).GetPageAsync(null, null, null, "показание для поиска", 0, 25, CancellationToken.None);
        await AssertCapturedPlanUsesAsync(database.ConnectionString, capture.TakeSingle(), "IX_meter_readings_Comment_trgm");

        await new EfSupplierAccrualRepository(context).GetPageAsync(null, null, "счет-2026", null, 0, 25, CancellationToken.None);
        await AssertCapturedPlanUsesAsync(database.ConnectionString, capture.TakeSingle(), "IX_supplier_accruals_DocumentNumber_trgm");

        await new EfIrregularPaymentRepository(context).GetListAsync("разовый платёж для поиска", false, 25, CancellationToken.None);
        AssertCapturedSearchUsesIlike(capture.TakeSingle());

        await new EfChargeServiceSettingRepository(context).GetListAsync(
            "регулярная услуга для поиска",
            false,
            null,
            null,
            25,
            new DateOnly(2026, 8, 29),
            CancellationToken.None);
        AssertCapturedSearchUsesIlike(capture.TakeSingleContaining("FROM charge_service_settings"));

        await new EfFeeCampaignRepository(context).GetListAsync("цель объявленного сбора", false, 25, CancellationToken.None);
        AssertCapturedSearchUsesIlike(capture.TakeSingle(), expectedPredicateCount: 2);

        await new EfTariffRepository(context).GetListAsync("база тарифа для поиска", false, 25, CancellationToken.None);
        AssertCapturedSearchUsesIlike(capture.TakeSingle(), expectedPredicateCount: 2);

        await new EfExpenseTypeRepository(context).GetListAsync("expense_code_needle", false, 25, CancellationToken.None);
        AssertCapturedSearchUsesIlike(capture.TakeSingle(), expectedPredicateCount: 2);
    }

    [PostgreSqlFact]
    public async Task FinanceSummarySearchTreatsWildcardsLiterallyOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 8, 29),
                AccountingMonth = new DateOnly(2026, 8, 1),
                Amount = 100m,
                Comment = "Оплата 100%_готово"
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 8, 29),
                AccountingMonth = new DateOnly(2026, 8, 1),
                Amount = 200m,
                Comment = "Оплата 100 процентов готово"
            });
        await context.SaveChangesAsync();

        var result = await new EfFinanceTotalsQuery(context).GetAsync(
            null,
            null,
            null,
            "%_",
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(1, result.OperationCount);
        Assert.Equal(1, result.ExpenseCount);
        Assert.Equal(100m, result.ExpenseTotal);
    }

    [PostgreSqlFact]
    public async Task DictionarySearchTreatsWildcardsLiterallyOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.IrregularPayments.AddRange(
            new IrregularPayment { Name = "Разовый 100%_готово", Amount = 100m },
            new IrregularPayment { Name = "Разовый 100 процентов готово", Amount = 200m });
        context.ChargeServiceSettings.AddRange(
            new ChargeServiceSetting { Name = "Услуга 100%_готово" },
            new ChargeServiceSetting { Name = "Услуга 100 процентов готово" });
        var incomeType = new IncomeType { Name = "Поступления для поиска сборов" };
        context.FeeCampaigns.AddRange(
            new FeeCampaign
            {
                Name = "Сбор 100%_готово",
                Goal = "Цель без шаблона",
                IncomeType = incomeType,
                ContributionAmount = 100m,
                TargetAmount = 1000m,
                StartsOn = new DateOnly(2026, 8, 1)
            },
            new FeeCampaign
            {
                Name = "Сбор без шаблона",
                Goal = "Цель 100%_готово",
                IncomeType = incomeType,
                ContributionAmount = 200m,
                TargetAmount = 2000m,
                StartsOn = new DateOnly(2026, 8, 2)
            },
            new FeeCampaign
            {
                Name = "Сбор 100 процентов готово",
                Goal = "Цель 100 процентов готово",
                IncomeType = incomeType,
                ContributionAmount = 300m,
                TargetAmount = 3000m,
                StartsOn = new DateOnly(2026, 8, 3)
            });
        context.Tariffs.AddRange(
            new Tariff
            {
                Name = "Тариф 100%_готово",
                CalculationBase = "Фиксированная",
                Rate = 100m,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            },
            new Tariff
            {
                Name = "Тариф без шаблона",
                CalculationBase = "База 100%_готово",
                Rate = 200m,
                EffectiveFrom = new DateOnly(2026, 8, 2)
            },
            new Tariff
            {
                Name = "Тариф 100 процентов готово",
                CalculationBase = "База 100 процентов готово",
                Rate = 300m,
                EffectiveFrom = new DateOnly(2026, 8, 3)
            });
        context.ExpenseTypes.AddRange(
            new ExpenseType { Name = "Статья 100%_готово", Code = "literal_wildcard_name" },
            new ExpenseType { Name = "Статья без шаблона", Code = "expense_code_needle" },
            new ExpenseType { Name = "Статья 100 процентов готово", Code = "plain_expense_code" });
        await context.SaveChangesAsync();

        var irregularPayments = await new EfIrregularPaymentRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var chargeServices = await new EfChargeServiceSettingRepository(context)
            .GetListAsync("%_", false, null, null, 25, new DateOnly(2026, 8, 29), CancellationToken.None);
        var feeCampaigns = await new EfFeeCampaignRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var tariffs = await new EfTariffRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var expenseTypesByLiteralWildcard = await new EfExpenseTypeRepository(context)
            .GetListAsync("%_", false, 25, CancellationToken.None);
        var expenseTypesByCode = await new EfExpenseTypeRepository(context)
            .GetListAsync("code_needle", false, 25, CancellationToken.None);

        Assert.Collection(irregularPayments, item => Assert.Equal("Разовый 100%_готово", item.Name));
        Assert.Collection(chargeServices, item => Assert.Equal("Услуга 100%_готово", item.Name));
        Assert.Equal(["Сбор без шаблона", "Сбор 100%_готово"], feeCampaigns.Select(item => item.Name).ToArray());
        Assert.Equal(["Тариф без шаблона", "Тариф 100%_готово"], tariffs.Select(item => item.Name).ToArray());
        Assert.Collection(expenseTypesByLiteralWildcard, item => Assert.Equal("Статья 100%_готово", item.Name));
        Assert.Collection(expenseTypesByCode, item => Assert.Equal("expense_code_needle", item.Code));
    }

    [PostgreSqlFact]
    public async Task FinanceSummaryRelatedSearchUsesRawIlikeForIndexedNames()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage { Number = "ГАРАЖ-%_-СВОДКА", PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = "Взнос сводки" };
            setupContext.Accruals.Add(new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 8, 1),
                DueDate = new DateOnly(2026, 9, 1),
                OverdueFromDate = new DateOnly(2026, 10, 1),
                Amount = 250m,
                Source = AccrualSources.Manual
            });
            await setupContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfFinanceTotalsQuery(context).GetAsync(
            null,
            null,
            null,
            "%_",
            null,
            null,
            null,
            CancellationToken.None);
        var command = capture.TakeSingle();

        Assert.Equal(1, result.AccrualCount);
        Assert.Equal(250m, result.AccrualTotal);
        Assert.DoesNotContain("lower(", command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ГАРАЖ-%_-СВОДКА", command.CommandText, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task FinanceSummaryRelatedSearchKeepsEverySectionExact()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            var garage = new Garage { Number = "гараж-маркер", PeopleCount = 1, FloorCount = 1 };
            var incomeType = new IncomeType { Name = "доход-маркер" };
            var expenseType = new ExpenseType { Name = "расход-маркер" };
            var supplier = new Supplier
            {
                Name = "поставщик-маркер",
                Group = new SupplierGroup { Name = "Группа сводки" }
            };
            var staffMember = new StaffMember
            {
                FullName = "сотрудник-маркер",
                Department = new StaffDepartment { Name = "Отдел сводки" }
            };
            setupContext.FinancialOperations.AddRange(
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = new DateOnly(2026, 8, 20),
                    AccountingMonth = new DateOnly(2026, 8, 1),
                    Amount = 10m,
                    Garage = garage
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Expense,
                    OperationDate = new DateOnly(2026, 8, 21),
                    AccountingMonth = new DateOnly(2026, 8, 1),
                    Amount = 20m,
                    Supplier = supplier
                },
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Expense,
                    OperationDate = new DateOnly(2026, 8, 22),
                    AccountingMonth = new DateOnly(2026, 8, 1),
                    Amount = 30m,
                    StaffMember = staffMember
                });
            setupContext.Accruals.Add(new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 8, 1),
                DueDate = new DateOnly(2026, 9, 1),
                OverdueFromDate = new DateOnly(2026, 10, 1),
                Amount = 40m,
                Source = AccrualSources.Manual
            });
            setupContext.MeterReadings.Add(new MeterReading
            {
                Garage = garage,
                MeterKind = MeterKinds.Water,
                AccountingMonth = new DateOnly(2026, 8, 1),
                ReadingDate = new DateOnly(2026, 8, 20),
                PreviousValue = 1m,
                CurrentValue = 2m,
                Consumption = 1m
            });
            setupContext.SupplierAccruals.Add(new SupplierAccrual
            {
                Supplier = supplier,
                ExpenseType = expenseType,
                AccountingMonth = new DateOnly(2026, 8, 1),
                Amount = 50m,
                Source = AccrualSources.Manual
            });
            await setupContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var query = new EfFinanceTotalsQuery(context);
        var garageResult = await query.GetAsync(null, null, null, "гараж-маркер", null, null, null, CancellationToken.None);
        var supplierResult = await query.GetAsync(null, null, null, "поставщик-маркер", null, null, null, CancellationToken.None);
        var staffResult = await query.GetAsync(null, null, null, "сотрудник-маркер", null, null, null, CancellationToken.None);
        var incomeTypeResult = await query.GetAsync(null, null, null, "доход-маркер", null, null, null, CancellationToken.None);
        var expenseTypeResult = await query.GetAsync(null, null, null, "расход-маркер", null, null, null, CancellationToken.None);

        Assert.Equal((1, 1, 1), (garageResult.OperationCount, garageResult.AccrualCount, garageResult.MeterReadingCount));
        Assert.Equal((1, 1), (supplierResult.OperationCount, supplierResult.SupplierAccrualCount));
        Assert.Equal(1, staffResult.OperationCount);
        Assert.Equal(1, incomeTypeResult.AccrualCount);
        Assert.Equal(1, expenseTypeResult.SupplierAccrualCount);
    }

    [PostgreSqlFact]
    public async Task FinanceAndFundPredicatesUsePurposeBuiltIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var indexes = await ReadIndexesAsync(connection);
        AssertIndex(indexes, "IX_financial_operations_OperationKind_OperationDate_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~", "income");
        AssertIndex(indexes, "IX_financial_operations_SupplierId", "\"SupplierId\"");
        AssertIndex(indexes, "IX_financial_operations_StaffMemberId", "\"StaffMemberId\"");
        AssertIndex(indexes, "IX_accruals_AccountingMonth_GarageId_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc", "\"DueDateNeedsReview\" = false");
        AssertIndex(indexes, "IX_accrual_payment_allocations_AccrualId_FinancialOperationId", "\"IsActive\" = true");
        AssertIndex(indexes, "IX_supplier_accruals_AccountingMonth_SupplierId_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_fund_operations_FundId_CreatedAtUtc_Id", "\"FundId\"");
        AssertIndex(indexes, "IX_fund_operations_CreatedAtUtc", "\"CreatedAtUtc\"");
        AssertIndex(indexes, "IX_cash_bank_transfers_TransferDate", "\"TransferDate\"");
        AssertIndex(indexes, "IX_funds_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_fund_operations_OperationKind_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_fund_operations_Reason_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_expense_types_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_expense_types_Code_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_income_types_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_supplier_accruals_DocumentNumber_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_financial_operations_DocumentNumber_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_financial_operations_Comment_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_financial_operations_CounterpartyName_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_accruals_Comment_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_accruals_Basis_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_irregular_payments_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_charge_service_settings_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_fee_campaigns_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_fee_campaigns_Goal_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_tariffs_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_tariffs_CalculationBase_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_meter_readings_Comment_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_supplier_accruals_Comment_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_cash_bank_transfers_Comment_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_garages_Number_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_suppliers_Name_trgm", "gin_trgm_ops");
        AssertIndex(indexes, "IX_staff_members_FullName_trgm", "gin_trgm_ops");

        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_OperationKind_OperationDate_Id",
            """
            SELECT "Id" FROM financial_operations
            WHERE "IsCanceled" = false
              AND "OperationKind" = 'expense'
              AND "OperationDate" BETWEEN DATE '2026-01-01' AND DATE '2026-12-31'
            ORDER BY "OperationDate" DESC, "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~",
            """
            SELECT "Id" FROM financial_operations
            WHERE "IsCanceled" = false
              AND "OperationKind" = 'income'
              AND "GarageId" = '00000000-0000-0000-0000-000000000001'
              AND "IncomeTypeId" = '00000000-0000-0000-0000-000000000002'
            ORDER BY "OperationDate", "CreatedAtUtc";
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_AccountingMonth_GarageId_Id",
            """
            SELECT "Id" FROM accruals
            WHERE "IsCanceled" = false
              AND "AccountingMonth" BETWEEN DATE '2026-01-01' AND DATE '2026-12-01'
            ORDER BY "AccountingMonth" DESC, "GarageId", "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc",
            """
            SELECT "Id" FROM accruals
            WHERE "IsCanceled" = false
              AND "DueDateNeedsReview" = false
              AND "GarageId" = '00000000-0000-0000-0000-000000000001'
              AND "IncomeTypeId" = '00000000-0000-0000-0000-000000000002'
            ORDER BY "DueDate", "CreatedAtUtc";
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accrual_payment_allocations_AccrualId_FinancialOperationId",
            """
            SELECT "FinancialOperationId" FROM accrual_payment_allocations
            WHERE "IsActive" = true
              AND "AccrualId" = '00000000-0000-0000-0000-000000000003';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_supplier_accruals_AccountingMonth_SupplierId_Id",
            """
            SELECT "Id" FROM supplier_accruals
            WHERE "IsCanceled" = false
              AND "AccountingMonth" BETWEEN DATE '2026-01-01' AND DATE '2026-12-01'
            ORDER BY "AccountingMonth" DESC, "SupplierId", "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_fund_operations_FundId_CreatedAtUtc_Id",
            """
            SELECT "Id" FROM fund_operations
            WHERE "FundId" = '00000000-0000-0000-0000-000000000004'
              AND "CreatedAtUtc" >= TIMESTAMPTZ '2026-01-01T00:00:00Z'
            ORDER BY "CreatedAtUtc", "Id";
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_SupplierId",
            """
            SELECT "Id" FROM financial_operations
            WHERE "SupplierId" = '00000000-0000-0000-0000-000000000005'
              AND "OperationKind" = 'expense'
              AND "OperationDate" BETWEEN DATE '2026-01-01' AND DATE '2026-12-31';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_StaffMemberId",
            """
            SELECT "Id" FROM financial_operations
            WHERE "StaffMemberId" = '00000000-0000-0000-0000-000000000006'
              AND "OperationKind" = 'expense'
              AND "OperationDate" BETWEEN DATE '2026-01-01' AND DATE '2026-12-31';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_fund_operations_CreatedAtUtc",
            """
            SELECT "Id" FROM fund_operations
            WHERE "IsCanceled" = false
              AND "CreatedAtUtc" >= TIMESTAMPTZ '2026-01-01T00:00:00Z'
              AND "CreatedAtUtc" < TIMESTAMPTZ '2027-01-01T00:00:00Z';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_cash_bank_transfers_TransferDate",
            """
            SELECT "Id" FROM cash_bank_transfers
            WHERE "IsCanceled" = false
              AND "TransferDate" BETWEEN DATE '2026-01-01' AND DATE '2026-12-31';
            """);
        await SeedSearchVolumeAsync(database);
        await connection.CloseAsync();
        await connection.OpenAsync();
        await AssertPlanUsesAsync(
            connection,
            "IX_funds_Name_trgm",
            """SELECT "Id" FROM funds WHERE "Name" ILIKE '%резерв%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_fund_operations_Reason_trgm",
            """
            SELECT "Id" FROM fund_operations
            WHERE "Reason" ILIKE '%корректировка%' ESCAPE '\';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_fund_operations_OperationKind_trgm",
            """
            SELECT "Id" FROM fund_operations
            WHERE "OperationKind" ILIKE '%withdraw%' ESCAPE '\';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_expense_types_Name_trgm",
            """SELECT "Id" FROM expense_types WHERE "Name" ILIKE '%электроэнергия%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_expense_types_Code_trgm",
            """SELECT "Id" FROM expense_types WHERE "Code" ILIKE '%expense\_code\_needle%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_income_types_Name_trgm",
            """SELECT "Id" FROM income_types WHERE "Name" ILIKE '%членский взнос%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_supplier_accruals_DocumentNumber_trgm",
            """SELECT "Id" FROM supplier_accruals WHERE "DocumentNumber" ILIKE '%счет-2026%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_DocumentNumber_trgm",
            """
            SELECT "Id" FROM financial_operations
            WHERE "DocumentNumber" ILIKE '%акт-2026%' ESCAPE '\';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_Comment_trgm",
            """
            SELECT "Id" FROM financial_operations
            WHERE "Comment" ILIKE '%ремонт ворот%' ESCAPE '\';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_CounterpartyName_trgm",
            """SELECT "Id" FROM financial_operations WHERE "CounterpartyName" ILIKE '%подрядчик%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_Comment_trgm",
            """SELECT "Id" FROM accruals WHERE "Comment" ILIKE '%начисление%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_Basis_trgm",
            """SELECT "Id" FROM accruals WHERE "Basis" ILIKE '%основание%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_irregular_payments_Name_trgm",
            """SELECT "Id" FROM irregular_payments WHERE "Name" ILIKE '%разовый платёж%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_charge_service_settings_Name_trgm",
            """SELECT "Id" FROM charge_service_settings WHERE "Name" ILIKE '%регулярная услуга%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_fee_campaigns_Name_trgm",
            """SELECT "Id" FROM fee_campaigns WHERE "Name" ILIKE '%объявленный сбор%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_fee_campaigns_Goal_trgm",
            """SELECT "Id" FROM fee_campaigns WHERE "Goal" ILIKE '%цель объявленного сбора%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_tariffs_Name_trgm",
            """SELECT "Id" FROM tariffs WHERE "Name" ILIKE '%тариф для поиска%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_tariffs_CalculationBase_trgm",
            """SELECT "Id" FROM tariffs WHERE "CalculationBase" ILIKE '%база тарифа%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_meter_readings_Comment_trgm",
            """SELECT "Id" FROM meter_readings WHERE "Comment" ILIKE '%показание%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_supplier_accruals_Comment_trgm",
            """SELECT "Id" FROM supplier_accruals WHERE "Comment" ILIKE '%поставщик%' ESCAPE '\';""");
        await AssertPlanUsesAsync(
            connection,
            "IX_cash_bank_transfers_Comment_trgm",
            """
            SELECT "Id" FROM cash_bank_transfers
            WHERE "Comment" ILIKE '%сдача кассы%' ESCAPE '\';
            """);
    }

    [PostgreSqlFact]
    public async Task AllocationAndFundTailQueriesHonorCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var allocationRepository = new EfAccrualPaymentAllocationRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            allocationRepository.RebuildAsync(
                [new AccrualPaymentAllocationKey(Guid.NewGuid(), Guid.NewGuid())],
                cancellation.Token));

        var fundRepository = new EfFundRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fundRepository.GetOperationsFromAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fundRepository.GetOperationsSinceAsync(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                cancellation.Token));
    }

    private static async Task SeedSearchVolumeAsync(PostgreSqlTestDatabase database)
    {
        await using var context = database.CreateContext();
        var funds = Enumerable.Range(0, 300)
            .Select(index => new Fund
            {
                Name = index == 173 ? "Резерв для поиска" : $"Фонд производительности {index:D3}",
                NormalizedName = $"PERFORMANCE FUND {index:D3}",
                SortOrder = 1000 + index
            })
            .ToArray();
        var operationFund = funds[0];
        var garage = new Garage { Number = "PERF-SEARCH", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = "Начисления производительности" };
        var expenseType = new ExpenseType { Name = "Расходы производительности" };
        var supplierGroup = new SupplierGroup { Name = "Поставщики производительности" };
        var supplier = new Supplier { Name = "Поставщик производительности", Group = supplierGroup };
        context.Funds.AddRange(funds);
        context.AddRange(garage, incomeType, expenseType, supplierGroup, supplier);
        context.IrregularPayments.AddRange(Enumerable.Range(0, 300).Select(index => new IrregularPayment
        {
            Name = index == 173 ? "Разовый платёж для поиска" : $"Разовый платёж {index:D3}",
            Amount = 1m
        }));
        context.ChargeServiceSettings.AddRange(Enumerable.Range(0, 300).Select(index => new ChargeServiceSetting
        {
            Name = index == 193 ? "Регулярная услуга для поиска" : $"Услуга производительности {index:D3}"
        }));
        context.FeeCampaigns.AddRange(Enumerable.Range(0, 300).Select(index => new FeeCampaign
        {
            Name = index == 211 ? "Объявленный сбор для поиска" : $"Объявленный сбор {index:D3}",
            Goal = index == 212 ? "Цель объявленного сбора для поиска" : $"Цель сбора {index:D3}",
            IncomeType = incomeType,
            ContributionAmount = 1m,
            TargetAmount = 300m,
            StartsOn = new DateOnly(2026, 1, 1).AddDays(index)
        }));
        context.Tariffs.AddRange(Enumerable.Range(0, 300).Select(index => new Tariff
        {
            Name = index == 213 ? "Тариф для поиска" : $"Тариф производительности {index:D3}",
            CalculationBase = index == 214 ? "База тарифа для поиска" : $"База тарифа {index:D3}",
            Rate = 1m,
            EffectiveFrom = new DateOnly(2026, 1, 1).AddDays(index)
        }));
        context.FundOperations.AddRange(Enumerable.Range(0, 500).Select(index => new FundOperation
        {
            Fund = operationFund,
            OperationKind = index == 271 ? "withdraw-needle" : "deposit",
            Amount = 1m,
            BalanceBefore = index,
            BalanceAfter = index + 1,
            Reason = index == 349 ? "Корректировка для поиска" : $"Операция {index:D3}"
        }));
        context.ExpenseTypes.AddRange(Enumerable.Range(0, 300).Select(index => new ExpenseType
        {
            Name = index == 147 ? "Электроэнергия для поиска" : $"Статья производительности {index:D3}",
            Code = index == 173 ? "expense_code_needle" : $"performance_expense_{index:D3}"
        }));
        context.FinancialOperations.AddRange(Enumerable.Range(0, 500).Select(index => new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 1, 1).AddDays(index % 300),
            AccountingMonth = new DateOnly(2026, 1, 1),
            Amount = 1m,
            DocumentNumber = index == 201 ? "АКТ-2026-NEEDLE" : $"DOC-{index:D3}",
            Comment = index == 389 ? "Ремонт ворот для поиска" : $"Расход {index:D3}",
            CounterpartyName = index == 433 ? "Подрядчик для поиска" : $"Контрагент {index:D3}"
        }));
        context.Accruals.AddRange(Enumerable.Range(0, 5000).Select(index => new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 1, 1).AddMonths(index),
            DueDate = new DateOnly(2026, 2, 1).AddMonths(index),
            OverdueFromDate = new DateOnly(2026, 3, 1).AddMonths(index),
            Amount = 1m,
            Source = AccrualSources.Manual,
            Basis = index == 227 ? "Основание для поиска" : $"Основание {index:D3}",
            Comment = index == 317 ? "Начисление для поиска" : $"Начислено {index:D3}"
        }));
        context.MeterReadings.AddRange(Enumerable.Range(0, 5000).Select(index => new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new DateOnly(2026, 1, 1).AddMonths(index),
            ReadingDate = new DateOnly(2026, 1, 15).AddMonths(index),
            CurrentValue = index + 1,
            PreviousValue = index,
            Consumption = 1m,
            Comment = index == 283 ? "Показание для поиска" : $"Счётчик {index:D3}"
        }));
        context.SupplierAccruals.AddRange(Enumerable.Range(0, 5000).Select(index => new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = new DateOnly(2026, 1, 1).AddMonths(index),
            Amount = 1m,
            Source = AccrualSources.Manual,
            DocumentNumber = index == 251 ? "СЧЕТ-2026-NEEDLE" : $"SUP-{index:D3}",
            Comment = index == 367 ? "Поставщик для поиска" : $"Документ поставщика {index:D3}"
        }));
        context.CashBankTransfers.AddRange(Enumerable.Range(0, 500).Select(index => new CashBankTransfer
        {
            TransferDate = new DateOnly(2026, 1, 1).AddDays(index % 300),
            Amount = 1m,
            Comment = index == 411 ? "Сдача кассы для поиска" : $"Перевод {index:D3}"
        }));
        await context.SaveChangesAsync();
        // Flush GIN pending lists as well as refreshing statistics. During the full parallel
        // PostgreSQL suite an unflushed pending list can make the planner reject an otherwise
        // valid trigram index even with sequential scans disabled.
        await context.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE);");
    }

    private static async Task<Dictionary<string, string>> ReadIndexesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN (
                'financial_operations',
                'accruals',
                'meter_readings',
                'irregular_payments',
                'charge_service_settings',
                'fee_campaigns',
                'tariffs',
                'accrual_payment_allocations',
                'supplier_accruals',
                'fund_operations',
                'cash_bank_transfers',
                'funds',
                'expense_types',
                'income_types',
                'garages',
                'suppliers',
                'staff_members');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(0)] = reader.GetString(1);
        }

        return indexes;
    }

    private static void AssertIndex(
        IReadOnlyDictionary<string, string> indexes,
        string name,
        string expectedDefinition)
    {
        Assert.True(indexes.TryGetValue(name, out var definition), $"Index {name} was not created.");
        Assert.Contains(expectedDefinition, definition, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPlanUsesAsync(
        NpgsqlConnection connection,
        string indexName,
        string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SET enable_seqscan = off; SET enable_indexscan = on; SET enable_bitmapscan = on; SET jit = off; EXPLAIN (FORMAT TEXT) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        var plan = string.Join(Environment.NewLine, lines);
        Assert.True(
            plan.Contains(indexName, StringComparison.Ordinal),
            $"Expected PostgreSQL plan to use {indexName}.{Environment.NewLine}{plan}");
    }

    private static async Task AssertCapturedPlanUsesAsync(
        string connectionString,
        CapturedCommand captured,
        string indexName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SET enable_seqscan = off; SET enable_indexscan = on; SET enable_bitmapscan = on; SET jit = off; EXPLAIN (FORMAT TEXT) {captured.CommandText}";
        foreach (var parameter in captured.Parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        var plan = string.Join(Environment.NewLine, lines);
        Assert.True(
            plan.Contains(indexName, StringComparison.Ordinal),
            $"Expected PostgreSQL working-list plan to use {indexName}.{Environment.NewLine}{plan}");
    }

    private static void AssertCapturedSearchUsesIlike(CapturedCommand captured, int expectedPredicateCount = 1)
    {
        Assert.Equal(expectedPredicateCount, captured.CommandText.Split("ILIKE", StringSplitOptions.None).Length - 1);
        Assert.Contains("ESCAPE '\\'", captured.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(", captured.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CapturedCommand(
        string CommandText,
        IReadOnlyList<CapturedParameter> Parameters);

    private sealed record CapturedParameter(string Name, object? Value);

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        private readonly List<CapturedCommand> commands = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(new CapturedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new CapturedParameter(parameter.ParameterName, parameter.Value))
                    .ToList()));
            return ValueTask.FromResult(result);
        }

        public CapturedCommand TakeSingle()
        {
            var command = Assert.Single(commands);
            commands.Clear();
            return command;
        }

        public CapturedCommand TakeSingleContaining(string fragment)
        {
            var command = Assert.Single(commands, candidate => candidate.CommandText.Contains(fragment, StringComparison.Ordinal));
            commands.Clear();
            return command;
        }
    }
}
