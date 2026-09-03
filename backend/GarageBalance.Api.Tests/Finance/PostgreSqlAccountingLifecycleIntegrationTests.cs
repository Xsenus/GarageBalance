using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlAccountingLifecycleIntegrationTests
{
    [PostgreSqlFact]
    public async Task CompleteAccountingLifecycle_RoutesFundsAndKeepsAllReportsAndExportsConsistent()
    {
        var month = new DateOnly(2035, 7, 1);
        var dateTo = month.AddMonths(1).AddDays(-1);
        var marker = $"E2E-{Guid.NewGuid():N}";

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        await context.Garages.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsArchived, true));

        var electricityFund = Fund($"{marker}-Электроэнергия", 100);
        var membershipFund = Fund($"{marker}-Членские взносы", 110);
        var electricityIncome = await context.IncomeTypes.SingleAsync(item => item.Code == "electricity");
        electricityIncome.DestinationFund = electricityFund;
        var membershipIncome = new IncomeType
        {
            Name = $"{marker}-Членский взнос",
            Code = $"e2e_membership_{Guid.NewGuid():N}",
            DestinationFund = membershipFund
        };
        var owner = new Owner { LastName = marker, FirstName = "Владелец" };
        var garage = new Garage
        {
            Number = marker,
            PeopleCount = 1,
            FloorCount = 1,
            Owner = owner,
            InitialElectricityMeterValue = 100m
        };
        var tariff = new Tariff
        {
            Name = $"{marker}-Пороговый тариф",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 2m,
            ElectricityFirstThreshold = 50m,
            ElectricitySecondThreshold = 100m,
            ElectricityFirstRate = 2m,
            ElectricitySecondRate = 3m,
            ElectricityThirdRate = 5m,
            EffectiveFrom = month
        };
        var expenseType = await context.ExpenseTypes.SingleAsync(item => item.Code == "electricity");
        var serviceSetting = await context.ChargeServiceSettings.SingleAsync(item => item.IncomeTypeId == electricityIncome.Id);
        await context.ChargeServiceSettings
            .Where(item => item.Id != serviceSetting.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsArchived, true));
        serviceSetting.Tariff = tariff;
        serviceSetting.HasTieredTariff = true;
        serviceSetting.UnitName = "кВт·ч";
        var tariffVersion = new ChargeServiceTariffVersion
        {
            ChargeServiceSetting = serviceSetting,
            EffectiveFrom = month,
            Tariff = tariff
        };
        var membershipTariff = new Tariff
        {
            Name = $"{marker}-Членский тариф",
            CalculationBase = TariffCalculationBases.Fixed,
            Rate = 500m,
            EffectiveFrom = month
        };
        var membershipService = new ChargeServiceSetting
        {
            Name = $"{marker}-Членский взнос",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            IncomeType = membershipIncome,
            Tariff = membershipTariff,
            UnitName = "руб."
        };
        var membershipTariffVersion = new ChargeServiceTariffVersion
        {
            ChargeServiceSetting = membershipService,
            EffectiveFrom = month,
            Tariff = membershipTariff
        };
        var supplierGroup = new SupplierGroup { Name = $"{marker}-Поставщики" };
        var supplier = new Supplier
        {
            Name = $"{marker}-Энергосбыт",
            Group = supplierGroup,
            ChargeServiceSetting = serviceSetting,
            ExpenseType = expenseType,
            ExpenseFund = electricityFund
        };
        var department = new StaffDepartment { Name = $"{marker}-Бухгалтерия" };
        var staffMember = new StaffMember { FullName = $"{marker}-Бухгалтер", Department = department, Rate = 100m };
        context.AddRange(
            electricityFund, membershipFund, membershipIncome, owner, garage,
            tariff, tariffVersion, membershipTariff, membershipService, membershipTariffVersion,
            supplierGroup, supplier, department, staffMember);
        await context.SaveChangesAsync();

        var finance = FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2035, 8, 9, 12, 0, 0, TimeSpan.Zero)));

        var reading = await finance.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(garage.Id, MeterKinds.Electricity, month, month.AddDays(9), 230m, "Сквозная проверка"),
            null,
            CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(130m, reading.Value!.Consumption);

        var calculatedWorksheet = await finance.CalculateGarageIncomeWorksheetAsync(
            garage.Id,
            new GarageIncomeWorksheetRequest(month, month),
            null,
            CancellationToken.None);
        Assert.True(calculatedWorksheet.Succeeded, calculatedWorksheet.ErrorMessage);
        Assert.Contains(calculatedWorksheet.Value!.Rows, item =>
            item.IncomeTypeId == electricityIncome.Id && item.AccrualAmount == 400m);
        Assert.Contains(calculatedWorksheet.Value.Rows, item =>
            item.IncomeTypeId == membershipIncome.Id && item.AccrualAmount == 500m);

        var electricityAccrual = await context.Accruals
            .SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == electricityIncome.Id);
        Assert.Equal(400m, electricityAccrual.Amount);
        Assert.Equal(AccrualSources.Regular, electricityAccrual.Source);

        var receiptBatchId = Guid.NewGuid();
        var payment = await finance.CreateFullGaragePaymentAsync(
            new CreateFullGaragePaymentRequest(
                garage.Id,
                month.AddDays(19),
                [
                    new CreateFullGaragePaymentLineRequest(electricityIncome.Id, month, 400m, marker),
                    new CreateFullGaragePaymentLineRequest(membershipIncome.Id, month, 500m, marker)
                ],
                receiptBatchId),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        Assert.Equal(900m, payment.Value!.TotalAmount);

        var bankDeposit = await finance.CreateCashBankTransferAsync(
            new CreateCashBankTransferRequest(month.AddDays(20), 400m, marker),
            null,
            CancellationToken.None);
        Assert.True(bankDeposit.Succeeded, bankDeposit.ErrorMessage);

        var supplierAccrual = await finance.CreateSupplierAccrualAsync(
            new CreateSupplierAccrualRequest(supplier.Id, expenseType.Id, month, 300m, AccrualSources.Manual, $"{marker}-СЧЕТ", marker),
            null,
            CancellationToken.None);
        Assert.True(supplierAccrual.Succeeded, supplierAccrual.ErrorMessage);
        Assert.Equal(electricityFund.Id, supplierAccrual.Value!.ExpenseFundId);

        var supplierPayment = await finance.CreateExpenseAsync(
            new CreateExpenseOperationRequest(
                supplier.Id, expenseType.Id, month.AddDays(21), month, 250m, $"{marker}-РКО", marker,
                ExpensePaymentTypes.WithReceipt, ExpensePaymentSources.Bank),
            null,
            CancellationToken.None);
        Assert.True(supplierPayment.Succeeded, supplierPayment.ErrorMessage);
        Assert.Equal(electricityFund.Id, supplierPayment.Value!.ExpenseFundId);

        var staffPayment = await finance.CreateStaffPaymentAsync(
            new CreateStaffPaymentRequest(staffMember.Id, month.AddDays(22), month, 100m, $"{marker}-ЗП", marker),
            null,
            CancellationToken.None);
        Assert.True(staffPayment.Succeeded, staffPayment.ErrorMessage);
        Assert.Equal(ExpensePaymentSources.Cash, staffPayment.Value!.ExpensePaymentSource);
        Assert.Null(staffPayment.Value.ExpenseFundId);

        context.ChangeTracker.Clear();
        Assert.Equal(150m, await context.Funds.Where(item => item.Id == electricityFund.Id).Select(item => item.Balance).SingleAsync());
        Assert.Equal(500m, await context.Funds.Where(item => item.Id == membershipFund.Id).Select(item => item.Balance).SingleAsync());
        Assert.Equal(2, await context.FundOperations.CountAsync(item => item.OperationKind == FundOperationKinds.Deposit));
        Assert.Equal(1, await context.FundOperations.CountAsync(item => item.OperationKind == FundOperationKinds.Withdraw));
        var lifecycleAccruals = await context.Accruals
            .Where(item => !item.IsCanceled && item.AccountingMonth == month && item.GarageId == garage.Id)
            .OrderBy(item => item.Amount)
            .Select(item => new { item.Amount, item.Source, item.IncomeTypeId })
            .ToListAsync();
        Assert.Collection(
            lifecycleAccruals,
            item => Assert.Equal((500m, AccrualSources.Regular, membershipIncome.Id), (item.Amount, item.Source, item.IncomeTypeId)),
            item => Assert.Equal((400m, AccrualSources.Regular, electricityIncome.Id), (item.Amount, item.Source, item.IncomeTypeId)));

        var reports = CreateReportService(context);
        var consolidatedRequest = new ConsolidatedReportRequest(month, month, marker);
        var garageRequest = new GarageReportRequest(month, month, marker, false);
        var incomeRequest = new IncomeReportRequest(month, dateTo, marker, [], [], [], "all");
        var expenseRequest = new ExpenseReportRequest(month, dateTo, marker, [], [], "all");
        var fundRequest = new FundChangeReportRequest(new DateOnly(2026, 8, 1), dateTo, marker);
        var cashRequest = new CashPaymentReportRequest(month, dateTo, marker);
        var bankRequest = new BankDepositReportRequest(month, dateTo, marker);
        var feeRequest = new FeeReportRequest(membershipIncome.Name);

        var consolidated = await reports.GetConsolidatedReportAsync(consolidatedRequest, CancellationToken.None);
        var garages = await reports.GetGarageReportAsync(garageRequest, CancellationToken.None);
        var income = await reports.GetIncomeReportAsync(incomeRequest, CancellationToken.None);
        var expenses = await reports.GetExpenseReportAsync(expenseRequest, CancellationToken.None);
        var fundChanges = await reports.GetFundChangeReportAsync(fundRequest, CancellationToken.None);
        var cash = await reports.GetCashPaymentReportAsync(cashRequest, CancellationToken.None);
        var bank = await reports.GetBankDepositReportAsync(bankRequest, CancellationToken.None);
        var fees = await reports.GetFeeReportAsync(feeRequest, CancellationToken.None);

        Assert.True(consolidated.Succeeded, consolidated.ErrorMessage);
        Assert.Equal((900m, 350m), (consolidated.Value!.IncomeTotal, consolidated.Value.ExpenseTotal));
        Assert.True(consolidated.Value.AccrualTotal >= 900m);
        Assert.True(garages.Succeeded, garages.ErrorMessage);
        Assert.Equal((900m, 900m, 0m), (garages.Value!.AccrualTotal, garages.Value.IncomeTotal, garages.Value.Difference));
        Assert.True(income.Succeeded, income.ErrorMessage);
        Assert.Equal((900m, 900m, 0m), (income.Value!.AccrualTotal, income.Value.IncomeTotal, income.Value.Debt));
        Assert.True(expenses.Succeeded, expenses.ErrorMessage);
        Assert.Equal((400m, 350m, 50m), (expenses.Value!.AccrualTotal, expenses.Value.ExpenseTotal, expenses.Value.Difference));
        Assert.True(fundChanges.Succeeded, fundChanges.ErrorMessage);
        Assert.Equal((900m, 250m), (fundChanges.Value!.DepositTotal, fundChanges.Value.WithdrawalTotal));
        Assert.True(cash.Succeeded, cash.ErrorMessage);
        Assert.Equal(100m, cash.Value!.Total);
        Assert.Single(cash.Value.Rows);
        Assert.Equal($"{marker}-ЗП", cash.Value.Rows[0].DocumentNumber);
        Assert.True(bank.Succeeded, bank.ErrorMessage);
        Assert.Equal(400m, bank.Value!.Total);
        Assert.True(fees.Succeeded, fees.ErrorMessage);
        Assert.Equal((500m, 500m, 0m), (fees.Value!.AccruedTotal, fees.Value.CollectedTotal, fees.Value.DebtTotal));

        await AssertExportsAsync(
            () => reports.ExportConsolidatedReportXlsxAsync(consolidatedRequest, CancellationToken.None),
            () => reports.ExportConsolidatedReportPdfAsync(consolidatedRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportGarageReportXlsxAsync(garageRequest, CancellationToken.None),
            () => reports.ExportGarageReportPdfAsync(garageRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportIncomeReportXlsxAsync(incomeRequest, CancellationToken.None),
            () => reports.ExportIncomeReportPdfAsync(incomeRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportExpenseReportXlsxAsync(expenseRequest, CancellationToken.None),
            () => reports.ExportExpenseReportPdfAsync(expenseRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportFundChangeReportXlsxAsync(fundRequest, CancellationToken.None),
            () => reports.ExportFundChangeReportPdfAsync(fundRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportCashPaymentReportXlsxAsync(cashRequest, CancellationToken.None),
            () => reports.ExportCashPaymentReportPdfAsync(cashRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportBankDepositReportXlsxAsync(bankRequest, CancellationToken.None),
            () => reports.ExportBankDepositReportPdfAsync(bankRequest, CancellationToken.None));
        await AssertExportsAsync(
            () => reports.ExportFeeReportXlsxAsync(feeRequest, CancellationToken.None),
            () => reports.ExportFeeReportPdfAsync(feeRequest, CancellationToken.None));

        var replacement = await finance.ReplaceMeterDeviceAsync(
            new ReplaceMeterDeviceRequest(
                garage.Id, MeterKinds.Electricity, month.AddMonths(1), month.AddMonths(1).AddDays(4),
                $"{marker}-NEW", 0m, 5m, 230m, "Плановая замена", null, null),
            null,
            CancellationToken.None);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        Assert.Equal($"{marker}-NEW", replacement.Value!.Device.SerialNumber);
        Assert.True(replacement.Value.Reading.IsMeterReplacement);
        Assert.Equal(2, await context.MeterDevices.CountAsync(item => item.GarageId == garage.Id));
    }

    private static Fund Fund(string name, int sortOrder) => new()
    {
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        SortOrder = sortOrder,
        IsSystem = false
    };

    private static ReportService CreateReportService(GarageBalanceDbContext context) => new(
        new EfCashMovementReportQuery(context),
        new EfFundChangeReportQuery(context),
        new EfConsolidatedMonthlyReportQuery(context),
        new EfConsolidatedGarageReportQuery(context),
        new EfGarageReportQuery(context),
        new EfFeeReportQuery(context),
        new EfExpenseReportQuery(context),
        new EfIncomeReportQuery(context),
        new EfApplicationUnitOfWork(context),
        new AuditEventWriter(context),
        TestBusinessDateProvider.From(new FixedTimeProvider(new DateTimeOffset(2035, 8, 9, 12, 0, 0, TimeSpan.Zero))));

    private static async Task AssertExportsAsync(
        Func<Task<ReportResult<ReportExportFileDto>>> xlsxFactory,
        Func<Task<ReportResult<ReportExportFileDto>>> pdfFactory)
    {
        var xlsx = await xlsxFactory();
        var pdf = await pdfFactory();
        Assert.True(xlsx.Succeeded, xlsx.ErrorMessage);
        Assert.True(pdf.Succeeded, pdf.ErrorMessage);
        Assert.True(xlsx.Value!.Content.Length > 100);
        Assert.Equal((byte)'P', xlsx.Value.Content[0]);
        Assert.Equal((byte)'K', xlsx.Value.Content[1]);
        Assert.True(pdf.Value!.Content.Length > 100);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Value.Content, 0, 4));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
