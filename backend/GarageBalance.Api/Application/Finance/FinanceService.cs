using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceService(
    IStaffMemberRepository staffMemberRepository,
    IGarageRepository garageRepository,
    IMissingMeterReadingQuery missingMeterReadingQuery,
    IGarageIncomeWorksheetQuery garageIncomeWorksheetQuery,
    IGarageBalanceHistoryQuery garageBalanceHistoryQuery,
    IFinanceAvailableBalanceQuery financeAvailableBalanceQuery,
    IExpenseWorksheetQuery expenseWorksheetQuery,
    IFinancialOperationDisplayQuery financialOperationDisplayQuery,
    IFinanceTotalsQuery financeTotalsQuery,
    IFinancialReportPeriodQuery financialReportPeriodQuery,
    IMeterReadingRepository meterReadingRepository,
    IFinancialOperationRepository financialOperationRepository,
    IAccrualRepository accrualRepository,
    IAccrualPaymentAllocationRepository accrualPaymentAllocationRepository,
    ISupplierAccrualRepository supplierAccrualRepository,
    IStaffSalaryAdjustmentRepository staffSalaryAdjustmentRepository,
    ICashBankTransferRepository cashBankTransferRepository,
    ISupplierGroupRepository supplierGroupRepository,
    ISupplierRepository supplierRepository,
    IExpenseTypeRepository expenseTypeRepository,
    IIncomeTypeRepository incomeTypeRepository,
    IIrregularPaymentRepository irregularPaymentRepository,
    ITariffRepository tariffRepository,
    IFeeCampaignRepository feeCampaignRepository,
    IChargeServiceSettingRepository chargeServiceSettingRepository,
    IIncomeFundAssignmentService incomeFundAssignmentService,
    IExpenseFundDisbursementService expenseFundDisbursementService,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter,
    TimeProvider timeProvider,
    IBusinessDateProvider businessDateProvider) : IFinanceService
{
    private static readonly JsonSerializerOptions PersistedJsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxAutomaticFeeCampaigns = 500;
    private const int MaxAutomaticMeteredServices = 500;
    private const int MaxBalanceHistoryMonths = 600;
    private const int EarlyElectricityPaymentWarningDays = 30;
    private const string DebtTransferIncomeTypeCode = "debt_transfer";
    private const string OtherPaymentsIncomeTypeCode = "other_payments";
    private const string OtherIncomeIncomeTypeCode = "other_income";
    private const string DebtTransferIncomeTypeName = "Перенос задолженности";
    private const string AdvancePaymentExpenseTypeName = CashExpenseClassification.AdvancePaymentExpenseTypeName;
    private const string NoReceiptPaymentExpenseTypeName = CashExpenseClassification.NoReceiptPaymentExpenseTypeName;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] CashExpenseTypeCodes = CashExpenseClassification.TypeCodes;
    private static readonly string[] CashExpenseTypeNames = CashExpenseClassification.TypeNames;
    private static readonly Guid WaterMeterChainLockId = new("c51ef8f1-f56d-4f41-950c-613c43a03ea1");
    private static readonly Guid ElectricityMeterChainLockId = new("a4427fef-41cb-4e85-bf59-cbd6d9337cf0");

    private static readonly HashSet<string> CashExpenseTypeKeys = CashExpenseTypeCodes
        .Select(NormalizeFinanceLookupKey)
        .Concat(CashExpenseTypeNames.Select(NormalizeFinanceLookupKey))
        .ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> FinanceFieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["operationDate"] = "Дата операции",
        ["accountingMonth"] = "Расчетный месяц",
        ["amount"] = "Сумма",
        ["documentNumber"] = "Документ",
        ["comment"] = "Комментарий",
        ["garage"] = "Гараж",
        ["supplier"] = "Поставщик",
        ["staffMember"] = "Сотрудник",
        ["incomeType"] = "Вид поступления",
        ["expenseType"] = "Услуга",
        ["expensePaymentType"] = "Тип выплаты",
        ["expensePaymentSource"] = "Источник выплаты",
        ["source"] = "Источник",
        ["meterKind"] = "Тип счетчика",
        ["readingDate"] = "Дата показания",
        ["currentValue"] = "Текущее показание",
        ["previousValue"] = "Предыдущее показание",
        ["consumption"] = "Расход",
        ["hasGapWarning"] = "Разрыв истории",
        ["dueDateNeedsReview"] = "Срок требует сверки"
    };

    public async Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var operations = await financialOperationRepository.GetListAsync(
            request.DateFrom,
            request.DateTo,
            NormalizeOptional(request.OperationKind),
            NormalizeSearch(request.Search),
            request.GarageId,
            request.SupplierId,
            request.StaffMemberId,
            NormalizeListLimit(request.Limit),
            cancellationToken);
        return await ToOperationDtosAsync(operations, cancellationToken);
    }

    public async Task<FinancePagedResult<FinancialOperationDto>> GetOperationsPageAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await financialOperationRepository.GetPageAsync(
            request.DateFrom,
            request.DateTo,
            NormalizeOptional(request.OperationKind),
            NormalizeSearch(request.Search),
            request.GarageId,
            request.SupplierId,
            request.StaffMemberId,
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        return new FinancePagedResult<FinancialOperationDto>(await ToOperationDtosAsync(page.Items, cancellationToken), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken)
    {
        var accruals = await accrualRepository.GetListAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            NormalizeSearch(request.Search),
            NormalizeListLimit(request.Limit),
            cancellationToken);
        return accruals.Select(ToDto).ToList();
    }

    public async Task<FinancePagedResult<AccrualDto>> GetAccrualsPageAsync(AccrualListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await accrualRepository.GetPageAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            NormalizeSearch(request.Search),
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        return new FinancePagedResult<AccrualDto>(page.Items.Select(ToDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<FinancePagedResult<AccrualDueDateReviewDto>> GetAccrualDueDateReviewPageAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(offset);
        var normalizedLimit = NormalizeListLimit(limit);
        var page = await accrualRepository.GetDueDateReviewPageAsync(normalizedOffset, normalizedLimit, cancellationToken);
        var items = page.Items.Select(accrual => new AccrualDueDateReviewDto(
            accrual.Id,
            accrual.Garage.Number,
            accrual.IncomeType.Name,
            accrual.AccountingMonth,
            accrual.Amount,
            accrual.Source,
            accrual.DueDate,
            accrual.OverdueFromDate,
            accrual.DueDateReviewReason ?? "historical_due_date_ambiguous")).ToList();
        return new FinancePagedResult<AccrualDueDateReviewDto>(items, page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
    {
        var accruals = await supplierAccrualRepository.GetListAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            NormalizeSearch(request.Search),
            request.SupplierId,
            NormalizeListLimit(request.Limit),
            cancellationToken);
        return accruals.Select(ToDto).ToList();
    }

    public async Task<FinancePagedResult<SupplierAccrualDto>> GetSupplierAccrualsPageAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await supplierAccrualRepository.GetPageAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            NormalizeSearch(request.Search),
            request.SupplierId,
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        return new FinancePagedResult<SupplierAccrualDto>(page.Items.Select(ToDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var readings = await meterReadingRepository.GetListAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            string.IsNullOrWhiteSpace(request.MeterKind) ? null : request.MeterKind.Trim(),
            NormalizeSearch(request.Search),
            NormalizeListLimit(request.Limit),
            cancellationToken);
        return readings.Select(ToDto).ToList();
    }

    public async Task<FinancePagedResult<MeterReadingDto>> GetMeterReadingsPageAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await meterReadingRepository.GetPageAsync(
            request.MonthFrom.HasValue ? MonthPeriod.Normalize(request.MonthFrom.Value) : null,
            request.MonthTo.HasValue ? MonthPeriod.Normalize(request.MonthTo.Value) : null,
            string.IsNullOrWhiteSpace(request.MeterKind) ? null : request.MeterKind.Trim(),
            NormalizeSearch(request.Search),
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        return new FinancePagedResult<MeterReadingDto>(page.Items.Select(ToDto).ToList(), page.TotalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<FinanceResult<MeterReadingYearPageDto>> GetMeterReadingYearPageAsync(
        MeterReadingYearRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Year is < 1900 or > 9999)
        {
            return FinanceResult<MeterReadingYearPageDto>.Failure("meter_reading_year_invalid", "Год показаний должен быть от 1900 до 9999.");
        }

        var meterKind = request.MeterKind?.Trim().ToLowerInvariant();
        if (!MeterKinds.IsValid(meterKind))
        {
            return FinanceResult<MeterReadingYearPageDto>.Failure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
        }

        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await meterReadingRepository.GetYearPageAsync(
            request.Year,
            meterKind!,
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        var result = new MeterReadingYearPageDto(
            page.Garages.Select(garage => new MeterReadingYearGarageDto(garage.Id, garage.Number)).ToList(),
            page.Readings.Select(reading => new MeterReadingYearValueDto(
                reading.Id,
                reading.GarageId,
                reading.AccountingMonth,
                reading.CurrentValue,
                reading.Version,
                reading.MeterDeviceId,
                reading.MeterDeviceSerialNumber,
                reading.IsMeterReplacement)).ToList(),
            page.TotalCount,
            normalizedOffset,
            normalizedLimit,
            GetCurrentAccountingMonth());
        return FinanceResult<MeterReadingYearPageDto>.Success(result);
    }

    public async Task<IReadOnlyList<MissingMeterReadingDto>> GetMissingMeterReadingsAsync(MissingMeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth ?? businessDateProvider.Today);
        var meterKinds = NormalizeMeterKindFilter(request.MeterKind);
        var search = NormalizeSearch(request.Search);
        var limit = NormalizeListLimit(request.Limit);

        var rows = await missingMeterReadingQuery.GetMissingAsync(month, meterKinds, search, limit, cancellationToken);
        return rows
            .Select(row => new MissingMeterReadingDto(row.GarageId, row.GarageNumber, row.OwnerName, row.MeterKind, month))
            .ToList();
    }

    public async Task<FinanceResult<GarageBalanceHistoryDto>> GetGarageBalanceHistoryAsync(Guid garageId, GarageBalanceHistoryRequest request, CancellationToken cancellationToken)
    {
        var defaultMonthTo = MonthPeriod.Normalize(businessDateProvider.Today);
        var monthTo = MonthPeriod.Normalize(request.MonthTo ?? defaultMonthTo);
        var monthFrom = MonthPeriod.Normalize(request.MonthFrom ?? monthTo.AddMonths(-5));
        if (monthFrom > monthTo)
        {
            return FinanceResult<GarageBalanceHistoryDto>.Failure("balance_history_period_invalid", "Дата начала истории баланса не может быть позже даты окончания.");
        }

        var monthCount = ((monthTo.Year - monthFrom.Year) * 12) + monthTo.Month - monthFrom.Month + 1;
        if (monthCount > MaxBalanceHistoryMonths)
        {
            return FinanceResult<GarageBalanceHistoryDto>.Failure("balance_history_period_too_large", $"Историю баланса можно построить максимум за {MaxBalanceHistoryMonths} месяцев.");
        }

        var historyData = await garageBalanceHistoryQuery.GetAsync(garageId, monthFrom, monthTo, cancellationToken);
        if (historyData is null)
        {
            return FinanceResult<GarageBalanceHistoryDto>.Failure("garage_not_found", "Гараж для истории баланса не найден.");
        }

        var accrualBuckets = historyData.AccrualBuckets
            .ToDictionary(item => item.AccountingMonth, item => item.Amount);
        var incomeBuckets = historyData.IncomeBuckets
            .ToDictionary(item => item.AccountingMonth, item => item.Amount);

        var rows = new List<GarageBalanceHistoryRowDto>(monthCount);
        var openingDebt = MoneyMath.RoundMoney(historyData.StartingBalance + historyData.PreviousAccrualTotal - historyData.PreviousIncomeTotal);
        var accrualTotal = 0m;
        var incomeTotal = 0m;
        for (var month = monthFrom; month <= monthTo; month = month.AddMonths(1))
        {
            var accrualAmount = MoneyMath.RoundMoney(accrualBuckets.GetValueOrDefault(month));
            var incomeAmount = MoneyMath.RoundMoney(incomeBuckets.GetValueOrDefault(month));
            var closingDebt = MoneyMath.RoundMoney(openingDebt + accrualAmount - incomeAmount);
            rows.Add(new GarageBalanceHistoryRowDto(month, openingDebt, accrualAmount, incomeAmount, closingDebt));
            accrualTotal = MoneyMath.RoundMoney(accrualTotal + accrualAmount);
            incomeTotal = MoneyMath.RoundMoney(incomeTotal + incomeAmount);
            openingDebt = closingDebt;
        }

        var dto = new GarageBalanceHistoryDto(
            historyData.GarageId,
            historyData.GarageNumber,
            historyData.OwnerName,
            monthFrom,
            monthTo,
            historyData.StartingBalance,
            accrualTotal,
            incomeTotal,
            rows.Count == 0 ? openingDebt : rows[^1].ClosingDebt,
            rows);
        return FinanceResult<GarageBalanceHistoryDto>.Success(dto);
    }

    public async Task<FinanceResult<GarageOverdueDebtDto>> GetGarageOverdueDebtAsync(Guid garageId, CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindActiveWithOwnerAsync(garageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<GarageOverdueDebtDto>.Failure("garage_not_found", "Гараж для расшифровки просрочки не найден.");
        }

        var asOfDate = businessDateProvider.Today;
        var accruals = await accrualRepository.GetOverdueDebtDetailsAsync(garageId, asOfDate, cancellationToken);
        var totals = await garageRepository.GetBalanceTotalsAsync([garageId], cancellationToken);
        var unallocatedIncome = Math.Max(
            totals.IncomeTotals.GetValueOrDefault(garageId) - totals.AllocatedIncomeTotals.GetValueOrDefault(garageId),
            0m);
        var openingOriginal = garage.StartingOverdueDebt ?? Math.Max(garage.StartingBalance, 0m);
        var openingOutstanding = Math.Max(openingOriginal - unallocatedIncome, 0m);
        var remainingCredit = Math.Max(unallocatedIncome - openingOriginal, 0m) + Math.Max(-garage.StartingBalance, 0m);
        var rows = new List<GarageOverdueDebtRowDto>(accruals.Count + 1);

        if (openingOutstanding > 0m)
        {
            rows.Add(new GarageOverdueDebtRowDto(
                "opening_balance",
                null,
                "Входящий долг",
                null,
                null,
                null,
                MoneyMath.RoundMoney(openingOriginal),
                MoneyMath.RoundMoney(openingOriginal - openingOutstanding),
                MoneyMath.RoundMoney(openingOutstanding)));
        }

        foreach (var accrual in accruals)
        {
            var creditApplied = Math.Min(remainingCredit, accrual.OutstandingAmount);
            remainingCredit = MoneyMath.RoundMoney(remainingCredit - creditApplied);
            var outstanding = MoneyMath.RoundMoney(accrual.OutstandingAmount - creditApplied);
            if (outstanding <= 0m)
            {
                continue;
            }

            rows.Add(new GarageOverdueDebtRowDto(
                "accrual",
                accrual.IncomeTypeId,
                accrual.IncomeTypeName,
                accrual.AccountingMonth,
                accrual.DueDate,
                accrual.OverdueFromDate,
                MoneyMath.RoundMoney(accrual.Amount),
                MoneyMath.RoundMoney(accrual.PaidAmount + creditApplied),
                outstanding));
        }

        var total = MoneyMath.RoundMoney(rows.Sum(row => row.OutstandingAmount));
        return FinanceResult<GarageOverdueDebtDto>.Success(new GarageOverdueDebtDto(
            garage.Id,
            garage.Number,
            garage.Owner?.FullName,
            asOfDate,
            total,
            rows));
    }

    public async Task<FinanceResult<GarageIncomeWorksheetDto>> GetGarageIncomeWorksheetAsync(Guid garageId, GarageIncomeWorksheetRequest request, CancellationToken cancellationToken)
    {
        var defaultMonthTo = GetCurrentAccountingMonth();
        var monthTo = MonthPeriod.Normalize(request.MonthTo ?? defaultMonthTo);
        var monthFrom = MonthPeriod.Normalize(request.MonthFrom ?? monthTo.AddMonths(-5));
        if (monthFrom > monthTo)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure("income_worksheet_period_invalid", "Дата начала формы поступлений не может быть позже даты окончания.");
        }

        var monthCount = ((monthTo.Year - monthFrom.Year) * 12) + monthTo.Month - monthFrom.Month + 1;
        if (monthCount > MaxBalanceHistoryMonths)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure("income_worksheet_period_too_large", $"Форму поступлений можно построить максимум за {MaxBalanceHistoryMonths} месяцев.");
        }

        var feeCampaignOptions = await EnsureFeeCampaignAccrualsForWorksheetAsync(garageId, monthFrom, monthTo, cancellationToken);
        var worksheetData = await garageIncomeWorksheetQuery.GetAsync(garageId, monthFrom, monthTo, cancellationToken);
        if (worksheetData is null)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure("garage_not_found", "Гараж для формы поступлений не найден.");
        }

        var openingBalance = MoneyMath.RoundMoney(
            worksheetData.StartingBalance + worksheetData.PreviousAccrualTotal - worksheetData.PreviousIncomeTotal);
        var openingDebt = MoneyMath.RoundMoney(Math.Max(openingBalance, 0m));
        var annualAccrualIds = worksheetData.AnnualAccruals
            .Select(accrual => accrual.AccrualId)
            .ToHashSet();
        var annualAllocations = worksheetData.Allocations
            .Where(allocation => annualAccrualIds.Contains(allocation.AccrualId))
            .GroupBy(allocation => allocation.AccrualId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var representedOpeningDebt = MoneyMath.RoundMoney(worksheetData.AnnualAccruals
            .Where(accrual => accrual.AccountingMonth < monthFrom)
            .Sum(accrual =>
            {
                var allocatedBeforePeriod = annualAllocations
                    .GetValueOrDefault(accrual.AccrualId)?
                    .Where(allocation => allocation.PaymentAccountingMonth < monthFrom)
                    .Sum(allocation => allocation.Amount) ?? 0m;
                return Math.Max(accrual.Amount - allocatedBeforePeriod, 0m);
            }));
        var unrepresentedOpeningDebt = MoneyMath.RoundMoney(Math.Max(openingDebt - representedOpeningDebt, 0m));
        var meterReadingByMonthKind = worksheetData.MeterReadings
            .GroupBy(reading => (reading.AccountingMonth, reading.MeterKind))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.UpdatedAtUtc)
                    .First());
        var meterKindByIncomeTypeId = worksheetData.MeterIncomeTypes
            .GroupBy(item => item.IncomeTypeId)
            .ToDictionary(group => group.Key, group => group.First().MeterKind);

        var accrualLookup = worksheetData.AccrualBuckets
            .GroupBy(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId, bucket.IncomeTypeName))
            .ToDictionary(
                group => group.Key,
                group => group.First() with { Amount = MoneyMath.RoundMoney(group.Sum(bucket => bucket.Amount)) });
        var calculationLookup = (worksheetData.Calculations ?? [])
            .GroupBy(item => (item.AccountingMonth, item.IncomeTypeId, item.IncomeTypeName))
            .ToDictionary(
                group => group.Key,
                group => RegularAccrualCalculator.Deserialize(group.First().CalculationDetailsJson));
        var reasonLookup = (worksheetData.Reasons ?? [])
            .GroupBy(item => (item.AccountingMonth, item.IncomeTypeId, item.IncomeTypeName))
            .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(item => item.Reason).Distinct()));
        var appliedIncomeLookup = worksheetData.Allocations
            .Where(allocation =>
                !annualAccrualIds.Contains(allocation.AccrualId) &&
                allocation.AccrualAccountingMonth >= monthFrom &&
                allocation.AccrualAccountingMonth <= monthTo)
            .GroupBy(allocation => (
                allocation.AccrualAccountingMonth,
                allocation.IncomeTypeId,
                allocation.IncomeTypeName))
            .ToDictionary(group => group.Key, group => MoneyMath.RoundMoney(group.Sum(allocation => allocation.Amount)));
        var appliedPaymentLookup = worksheetData.Allocations
            .Where(allocation =>
                allocation.PaymentAccountingMonth >= monthFrom &&
                allocation.PaymentAccountingMonth <= monthTo)
            .GroupBy(allocation => (allocation.PaymentAccountingMonth, allocation.IncomeTypeId))
            .ToDictionary(group => group.Key, group => MoneyMath.RoundMoney(group.Sum(allocation => allocation.Amount)));
        var advanceLookup = worksheetData.IncomeBuckets.ToDictionary(
            bucket => (bucket.AccountingMonth, bucket.IncomeTypeId),
            bucket => MoneyMath.RoundMoney(Math.Max(
                bucket.Amount - appliedPaymentLookup.GetValueOrDefault((bucket.AccountingMonth, bucket.IncomeTypeId)),
                0m)));
        var annualObligationKeys = worksheetData.AnnualAccruals
            .Select(accrual => (accrual.AccountingYear, accrual.IncomeTypeId))
            .ToHashSet();
        var requiredMeterBuckets = defaultMonthTo >= monthFrom && defaultMonthTo <= monthTo
            ? worksheetData.MeterIncomeTypes.Select(incomeType => new GarageIncomeWorksheetBucketData(
                defaultMonthTo,
                incomeType.IncomeTypeId,
                incomeType.IncomeTypeName,
                incomeType.IncomeTypeCode,
                0m))
            : [];
        var incomeBucketsWithAdvance = worksheetData.IncomeBuckets
            .Where(bucket => advanceLookup.GetValueOrDefault((bucket.AccountingMonth, bucket.IncomeTypeId)) > 0m)
            .ToList();
        var advanceDisplayKeys = incomeBucketsWithAdvance
            .Select(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId, bucket.IncomeTypeName))
            .ToHashSet();
        var keys = worksheetData.AccrualBuckets
            .Concat(incomeBucketsWithAdvance)
            .Concat(requiredMeterBuckets)
            .Where(bucket => !annualObligationKeys.Contains((bucket.AccountingMonth.Year, bucket.IncomeTypeId)))
            .GroupBy(bucket => (
                bucket.AccountingMonth,
                bucket.IncomeTypeId,
                bucket.IncomeTypeName,
                bucket.IrregularPaymentId,
                bucket.IrregularPaymentIsAvailable))
            .Select(group => group.First())
            .OrderByDescending(bucket => bucket.AccountingMonth)
            .ThenBy(bucket => bucket.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = keys.Select(key =>
        {
            var accrualAmount = MoneyMath.RoundMoney(accrualLookup
                .GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId, key.IncomeTypeName))?.Amount ?? 0m);
            var incomeAmount = appliedIncomeLookup
                .GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId, key.IncomeTypeName));
            var advanceAmount = advanceDisplayKeys.Contains((key.AccountingMonth, key.IncomeTypeId, key.IncomeTypeName))
                ? advanceLookup.GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId))
                : 0m;
            var debt = MoneyMath.RoundMoney(Math.Max(accrualAmount - incomeAmount, 0m));
            var meterKind = meterKindByIncomeTypeId.GetValueOrDefault(key.IncomeTypeId)
                ?? InferMeterKind(key.IncomeTypeName, key.IncomeTypeCode);
            meterReadingByMonthKind.TryGetValue((key.AccountingMonth, meterKind ?? string.Empty), out var reading);
            return new GarageIncomeWorksheetRowDto(
                key.AccountingMonth,
                key.IncomeTypeId,
                key.IncomeTypeName,
                null,
                meterKind,
                reading?.Id,
                reading?.Version,
                reading?.ReadingDate,
                reading?.CurrentValue,
                reading?.Consumption,
                accrualAmount,
                accrualAmount,
                incomeAmount,
                advanceAmount,
                debt,
                IrregularPaymentId: key.IrregularPaymentId,
                IrregularPaymentRemainingAmount: key.IrregularPaymentId.HasValue ? debt : null,
                CalculationDetails: calculationLookup.GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId, key.IncomeTypeName)),
                Reason: reasonLookup.GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId, key.IncomeTypeName)));
        }).ToList();

        foreach (var annualAccrual in worksheetData.AnnualAccruals)
        {
            var yearStart = new DateOnly(annualAccrual.AccountingYear, 1, 1);
            var yearEnd = new DateOnly(annualAccrual.AccountingYear, 12, 1);
            var displayFrom = monthFrom > yearStart ? monthFrom : yearStart;
            var displayTo = monthTo < yearEnd ? monthTo : yearEnd;
            if (displayFrom > displayTo)
            {
                continue;
            }

            var allocations = annualAllocations.GetValueOrDefault(annualAccrual.AccrualId) ?? [];
            for (var month = displayFrom; month <= displayTo; month = month.AddMonths(1))
            {
                var allocatedBeforeMonth = MoneyMath.RoundMoney(allocations
                    .Where(allocation => allocation.PaymentAccountingMonth < month)
                    .Sum(allocation => allocation.Amount));
                if (AnnualAccrualPolicy.IsFullyPaid(annualAccrual.Amount, allocatedBeforeMonth))
                {
                    break;
                }

                var allocatedInMonth = MoneyMath.RoundMoney(allocations
                    .Where(allocation => allocation.PaymentAccountingMonth == month)
                    .Sum(allocation => allocation.Amount));
                var allocatedThroughMonth = MoneyMath.RoundMoney(allocatedBeforeMonth + allocatedInMonth);
                rows.Add(new GarageIncomeWorksheetRowDto(
                    month,
                    annualAccrual.IncomeTypeId,
                    annualAccrual.IncomeTypeName,
                    annualAccrual.AccrualId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    annualAccrual.AccountingMonth == month ? MoneyMath.RoundMoney(annualAccrual.Amount) : 0m,
                    MoneyMath.RoundMoney(Math.Max(annualAccrual.Amount - allocatedBeforeMonth, 0m)),
                    allocatedInMonth,
                    advanceLookup.GetValueOrDefault((month, annualAccrual.IncomeTypeId)),
                    MoneyMath.RoundMoney(Math.Max(annualAccrual.Amount - allocatedThroughMonth, 0m))));
            }
        }

        rows = rows
            .Select(row =>
            {
                var option = feeCampaignOptions.FirstOrDefault(item =>
                    item.Accrual is not null &&
                    item.Accrual.AccountingMonth == row.AccountingMonth &&
                    item.Campaign.IncomeTypeId == row.IncomeTypeId &&
                    string.Equals(item.Campaign.Name, row.IncomeTypeName, StringComparison.Ordinal));
                return option is null
                    ? row
                    : row with
                    {
                        FeeCampaignId = option.Campaign.Id,
                        FeeCampaignRemainingAmount = MoneyMath.RoundMoney(Math.Max(option.Campaign.TargetAmount - option.CollectedAmount, 0m))
                    };
            })
            .Where(row => !row.FeeCampaignId.HasValue || row.FeeCampaignRemainingAmount > 0m)
            .Where(row => !row.IrregularPaymentId.HasValue ||
                (worksheetData.AccrualBuckets.Any(bucket =>
                     bucket.AccountingMonth == row.AccountingMonth &&
                     bucket.IncomeTypeId == row.IncomeTypeId &&
                     bucket.IrregularPaymentId == row.IrregularPaymentId &&
                     bucket.IrregularPaymentIsAvailable) &&
                 row.IrregularPaymentRemainingAmount > 0m))
            .OrderByDescending(row => row.AccountingMonth)
            .ThenBy(row => row.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accrualTotal = MoneyMath.RoundMoney(worksheetData.AccrualBuckets.Sum(bucket => bucket.Amount));
        var incomeTotal = MoneyMath.RoundMoney(worksheetData.IncomeBuckets.Sum(bucket => bucket.Amount));
        var closingBalance = MoneyMath.RoundMoney(openingBalance + accrualTotal - incomeTotal);
        var advanceTotal = MoneyMath.RoundMoney(worksheetData.Advances.Sum(advance => Math.Max(advance.Amount, 0m)));
        var closingDebt = MoneyMath.RoundMoney(Math.Max(closingBalance, 0m));
        var debtTotal = closingDebt;
        return FinanceResult<GarageIncomeWorksheetDto>.Success(new GarageIncomeWorksheetDto(
            worksheetData.GarageId,
            worksheetData.GarageNumber,
            worksheetData.OwnerName,
            monthFrom,
            monthTo,
            openingBalance,
            openingDebt,
            unrepresentedOpeningDebt,
            accrualTotal,
            incomeTotal,
            advanceTotal,
            debtTotal,
            closingBalance,
            closingDebt,
            rows));
    }

    public async Task<FinanceResult<GarageFullPaymentQuoteDto>> GetGarageFullPaymentQuoteAsync(
        Guid garageId,
        CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindActiveWithOwnerAsync(garageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<GarageFullPaymentQuoteDto>.Failure(
                "garage_not_found",
                "Гараж для расчёта полной оплаты не найден.");
        }

        var accruals = await accrualRepository.GetOutstandingDebtDetailsAsync(garageId, cancellationToken);
        var totals = await garageRepository.GetBalanceTotalsAsync([garageId], cancellationToken);
        var unallocatedIncome = Math.Max(
            totals.IncomeTotals.GetValueOrDefault(garageId) - totals.AllocatedIncomeTotals.GetValueOrDefault(garageId),
            0m);
        var openingOriginal = Math.Max(garage.StartingBalance, 0m);
        var openingOutstanding = MoneyMath.RoundMoney(Math.Max(openingOriginal - unallocatedIncome, 0m));
        var remainingCredit = MoneyMath.RoundMoney(
            Math.Max(unallocatedIncome - openingOriginal, 0m) +
            Math.Max(-garage.StartingBalance, 0m) +
            accruals.Sum(accrual => accrual.ExcessPaidAmount));
        var accountingMonth = accruals.Count > 0
            ? accruals.Min(accrual => accrual.AccountingMonth)
            : GetCurrentAccountingMonth();
        var lines = new List<GarageFullPaymentQuoteLineDto>(accruals.Count + 1);

        if (openingOutstanding > 0m)
        {
            lines.Add(new GarageFullPaymentQuoteLineDto(
                null,
                "Входящий долг",
                accountingMonth,
                openingOutstanding,
                IsOpeningDebt: true));
        }

        foreach (var accrual in accruals)
        {
            if (accrual.OutstandingAmount <= 0m)
            {
                continue;
            }

            var creditApplied = Math.Min(remainingCredit, accrual.OutstandingAmount);
            remainingCredit = MoneyMath.RoundMoney(remainingCredit - creditApplied);
            var outstanding = MoneyMath.RoundMoney(accrual.OutstandingAmount - creditApplied);
            if (outstanding <= 0m)
            {
                continue;
            }

            lines.Add(new GarageFullPaymentQuoteLineDto(
                accrual.IncomeTypeId,
                accrual.IncomeTypeName,
                accrual.AccountingMonth,
                outstanding,
                FeeCampaignId: accrual.FeeCampaignId,
                IrregularPaymentId: accrual.IrregularPaymentId));
        }

        var consolidatedLines = lines
            .GroupBy(line => (
                line.IsOpeningDebt,
                line.IncomeTypeId,
                line.IncomeTypeName,
                line.AccountingMonth,
                line.FeeCampaignId,
                line.IrregularPaymentId))
            .Select(group => new GarageFullPaymentQuoteLineDto(
                group.Key.IncomeTypeId,
                group.Key.IncomeTypeName,
                group.Key.AccountingMonth,
                MoneyMath.RoundMoney(group.Sum(line => line.OutstandingAmount)),
                group.Key.IsOpeningDebt,
                group.Key.FeeCampaignId,
                group.Key.IrregularPaymentId))
            .ToList();

        return FinanceResult<GarageFullPaymentQuoteDto>.Success(new GarageFullPaymentQuoteDto(
            garage.Id,
            garage.Number,
            garage.Owner?.FullName,
            MoneyMath.RoundMoney(consolidatedLines.Sum(line => line.OutstandingAmount)),
            consolidatedLines));
    }

    public async Task<FinanceResult<GarageIncomeWorksheetDto>> CalculateGarageIncomeWorksheetAsync(
        Guid garageId,
        GarageIncomeWorksheetRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var defaultMonth = GetCurrentAccountingMonth();
        var monthTo = MonthPeriod.Normalize(request.MonthTo ?? defaultMonth);
        var monthFrom = MonthPeriod.Normalize(request.MonthFrom ?? monthTo);
        if (monthFrom > monthTo)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure(
                "income_worksheet_period_invalid",
                "Месяц начала формы поступлений не может быть позже месяца окончания.");
        }

        var monthCount = ((monthTo.Year - monthFrom.Year) * 12) + monthTo.Month - monthFrom.Month + 1;
        if (monthCount > MaxBalanceHistoryMonths)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure(
                "income_worksheet_period_too_large",
                $"Форму поступлений можно построить максимум за {MaxBalanceHistoryMonths} месяцев.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(garageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure(
                "garage_not_found",
                "Гараж для расчета поступлений не найден.");
        }

        await using var garageWorksheetLock =
            await accrualPaymentAllocationRepository.AcquireGarageIncomeWorksheetLockAsync(
                garage.Id,
                cancellationToken);

        var settings = (await chargeServiceSettingRepository.GetActiveRegularAsync(monthTo, cancellationToken))
            .Where(setting => setting.IncomeTypeId.HasValue)
            .GroupBy(setting => setting.IncomeTypeId!.Value)
            .Select(group => group.First())
            .ToArray();
        var incomeTypes = (await incomeTypeRepository.GetActiveByIdsAsync(
                settings.Select(setting => setting.IncomeTypeId!.Value).ToArray(),
                cancellationToken))
            .ToDictionary(incomeType => incomeType.Id);
        var meterKinds = settings
            .Select(setting => MeterKinds.IsValid(setting.MeterKind) ? setting.MeterKind : null)
            .Where(meterKind => meterKind is not null)
            .Select(meterKind => meterKind!)
            .Append(MeterKinds.Water)
            .Append(MeterKinds.Electricity)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var meterReadings = (await meterReadingRepository.GetActiveForGaragePeriodAsync(
                garage.Id,
                monthFrom,
                monthTo,
                meterKinds,
                cancellationToken))
            .ToDictionary(reading => (reading.AccountingMonth, reading.MeterKind));
        var existingAccruals = await accrualRepository.GetActiveRegularForGarageForUpdateAsync(
            garage.Id,
            monthFrom,
            monthTo,
            cancellationToken);
        var allocationKeys = settings
            .Where(setting => incomeTypes.ContainsKey(setting.IncomeTypeId!.Value))
            .Select(setting => new AccrualPaymentAllocationKey(garage.Id, setting.IncomeTypeId!.Value))
            .Concat(existingAccruals.Select(accrual => new AccrualPaymentAllocationKey(garage.Id, accrual.IncomeTypeId)))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            allocationKeys,
            cancellationToken);

        var paidAccrualIds = await accrualPaymentAllocationRepository.GetActivelyAllocatedAccrualIdsAsync(
            existingAccruals.Select(accrual => accrual.Id).ToArray(),
            cancellationToken);
        var monthlyAccruals = existingAccruals
            .Where(accrual => !accrual.AccountingYear.HasValue)
            .GroupBy(accrual => (accrual.AccountingMonth, accrual.IncomeTypeId))
            .ToDictionary(group => group.Key, group => group.First());
        var annualAccruals = existingAccruals
            .Where(accrual => accrual.AccountingYear.HasValue)
            .GroupBy(accrual => (AccountingYear: accrual.AccountingYear!.Value, accrual.IncomeTypeId))
            .ToDictionary(group => group.Key, group => group.First());
        var changedKeys = new HashSet<AccrualPaymentAllocationKey>();

        for (var month = monthFrom; month <= monthTo; month = month.AddMonths(1))
        {
            foreach (var setting in settings)
            {
                if (!setting.IncomeTypeId.HasValue ||
                    !incomeTypes.TryGetValue(setting.IncomeTypeId.Value, out var incomeType) ||
                    !IsChargeServiceDueForMonth(setting, month))
                {
                    continue;
                }

                var tariff = SelectTariffForMonth(setting, month);
                if (tariff is null)
                {
                    continue;
                }

                var accountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month);
                var existing = accountingYear.HasValue
                    ? annualAccruals.GetValueOrDefault((accountingYear.Value, incomeType.Id)) ??
                      monthlyAccruals.GetValueOrDefault((month, incomeType.Id))
                    : monthlyAccruals.GetValueOrDefault((month, incomeType.Id));
                if (existing is not null && paidAccrualIds.Contains(existing.Id))
                {
                    continue;
                }

                var segments = BuildRegularAccrualSegments(month, setting, tariff);
                var meteredBase = segments
                    .Select(segment => segment.CalculationBase)
                    .FirstOrDefault(calculationBase =>
                        calculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity);
                var meterKind = meteredBase is not null && MeterKinds.IsValid(setting.MeterKind)
                    ? setting.MeterKind
                    : meteredBase switch
                    {
                        TariffCalculationBases.MeterWater => MeterKinds.Water,
                        TariffCalculationBases.MeterElectricity => MeterKinds.Electricity,
                        _ => null
                    };
                var meterReading = meterKind is null
                    ? null
                    : meterReadings.GetValueOrDefault((month, meterKind));
                var calculation = RegularAccrualCalculator.Calculate(garage, month, meterReading, segments);
                if (!calculation.Succeeded)
                {
                    // Missing calculation inputs (for example, a meter reading) must not erase an
                    // already issued unpaid obligation. Keep it visible until a successful
                    // recalculation can replace the amount.
                    continue;
                }

                if (calculation.Amount <= 0m)
                {
                    if (existing is not null)
                    {
                        existing.IsCanceled = true;
                        existing.UpdatedAtUtc = timeProvider.GetUtcNow();
                        AddAudit(
                            actorUserId,
                            "finance.regular_accrual_excluded_from_worksheet_recalculation",
                            existing,
                            $"Неоплаченное начисление исключено при повторном расчете за {month:MM.yyyy}: сумма стала нулевой.");
                        changedKeys.Add(new AccrualPaymentAllocationKey(garage.Id, incomeType.Id));
                    }

                    continue;
                }

                var dueDates = AccrualDueDates.ForGarage(month, incomeType.Code, setting, GetGarageRegistrationDate(garage));
                var detailsJson = RegularAccrualCalculator.Serialize(calculation.Details!);
                var useTieredTariff = segments.Any(segment => segment.Tiers.Count > 0);
                if (existing is null)
                {
                    existing = new Accrual
                    {
                        GarageId = garage.Id,
                        Garage = garage,
                        IncomeTypeId = incomeType.Id,
                        IncomeType = incomeType,
                        TariffId = tariff.Id,
                        AccountingMonth = month,
                        AccountingYear = accountingYear,
                        DueDate = dueDates.DueDate,
                        OverdueFromDate = dueDates.OverdueFromDate,
                        Amount = calculation.Amount,
                        RequiresMeterReading = calculation.Details!.RequiresMeter,
                        CalculationMeterKind = calculation.Details.RequiresMeter ? meterKind : null,
                        CalculationDetailsJson = detailsJson,
                        Source = AccrualSources.Regular,
                        Comment = BuildRegularAccrualComment(
                            tariff,
                            "Расчет из формы платежей гаража",
                            useTieredTariff)
                    };
                    accrualRepository.Add(existing);
                    if (accountingYear.HasValue)
                    {
                        annualAccruals[(accountingYear.Value, incomeType.Id)] = existing;
                    }
                    else
                    {
                        monthlyAccruals[(month, incomeType.Id)] = existing;
                    }

                    AddAudit(
                        actorUserId,
                        "finance.regular_accrual_calculated_for_garage_worksheet",
                        existing,
                        $"Для гаража {garage.Number} рассчитано регулярное начисление «{setting.Name}» за {month:MM.yyyy}: {MoneyFormatting.Format(calculation.Amount)}.");
                    changedKeys.Add(new AccrualPaymentAllocationKey(garage.Id, incomeType.Id));
                    continue;
                }

                var accountingYearChanged = existing.AccountingYear != accountingYear;
                if (accountingYearChanged)
                {
                    existing.AccountingYear = accountingYear;
                    if (accountingYear.HasValue)
                    {
                        annualAccruals[(accountingYear.Value, incomeType.Id)] = existing;
                    }
                }

                if (!accountingYearChanged &&
                    existing.Amount == calculation.Amount &&
                    existing.TariffId == tariff.Id &&
                    existing.DueDate == dueDates.DueDate &&
                    existing.OverdueFromDate == dueDates.OverdueFromDate &&
                    string.Equals(existing.CalculationDetailsJson, detailsJson, StringComparison.Ordinal))
                {
                    continue;
                }

                var oldAmount = existing.Amount;
                existing.TariffId = tariff.Id;
                existing.Tariff = null;
                existing.DueDate = dueDates.DueDate;
                existing.OverdueFromDate = dueDates.OverdueFromDate;
                existing.Amount = calculation.Amount;
                existing.RequiresMeterReading = calculation.Details!.RequiresMeter;
                existing.CalculationMeterKind = calculation.Details.RequiresMeter ? meterKind : null;
                existing.CalculationDetailsJson = detailsJson;
                existing.Comment = BuildRegularAccrualComment(
                    tariff,
                    "Повторный расчет из формы платежей гаража",
                    useTieredTariff);
                existing.UpdatedAtUtc = timeProvider.GetUtcNow();
                AddAudit(
                    actorUserId,
                    "finance.regular_accrual_recalculated_for_garage_worksheet",
                    existing,
                    $"Неоплаченное начисление «{setting.Name}» за {month:MM.yyyy} пересчитано: {MoneyFormatting.Format(oldAmount)} → {MoneyFormatting.Format(calculation.Amount)}.",
                    new Dictionary<string, object?> { ["amount"] = oldAmount },
                    new Dictionary<string, object?> { ["amount"] = calculation.Amount });
                changedKeys.Add(new AccrualPaymentAllocationKey(garage.Id, incomeType.Id));
            }
        }

        if (changedKeys.Count > 0)
        {
            await RebuildPaymentAllocationsAsync(
                changedKeys.ToArray(),
                actorUserId,
                "Расчет ведомости поступлений гаража",
                garage.Id,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var worksheet = await GetGarageIncomeWorksheetAsync(garageId, request, cancellationToken);
        if (!worksheet.Succeeded || worksheet.Value is null)
        {
            return worksheet;
        }

        var rows = worksheet.Value.Rows.ToList();
        for (var month = monthFrom; month <= monthTo; month = month.AddMonths(1))
        {
            foreach (var setting in settings)
            {
                if (!setting.IncomeTypeId.HasValue ||
                    !incomeTypes.TryGetValue(setting.IncomeTypeId.Value, out var incomeType) ||
                    !IsChargeServiceDueForMonth(setting, month))
                {
                    continue;
                }

                var tariff = SelectTariffForMonth(setting, month);
                if (tariff is null)
                {
                    continue;
                }

                var segments = BuildRegularAccrualSegments(month, setting, tariff);
                var meteredBase = segments
                    .Select(segment => segment.CalculationBase)
                    .FirstOrDefault(calculationBase =>
                        calculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity);
                if (meteredBase is null || rows.Any(row =>
                        row.AccountingMonth == month &&
                        row.IncomeTypeId == incomeType.Id))
                {
                    continue;
                }

                var meterKind = MeterKinds.IsValid(setting.MeterKind)
                    ? setting.MeterKind
                    : meteredBase switch
                    {
                        TariffCalculationBases.MeterWater => MeterKinds.Water,
                        TariffCalculationBases.MeterElectricity => MeterKinds.Electricity,
                        _ => null
                    };
                rows.Add(new GarageIncomeWorksheetRowDto(
                    month,
                    incomeType.Id,
                    incomeType.Name,
                    null,
                    meterKind,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0m,
                    0m,
                    0m,
                    0m,
                    0m));
            }
        }

        return FinanceResult<GarageIncomeWorksheetDto>.Success(worksheet.Value with
        {
            Rows = rows
                .OrderByDescending(row => row.AccountingMonth)
                .ThenBy(row => row.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        });
    }

    public async Task<FinanceResult<ExpenseWorksheetDto>> GetExpenseWorksheetAsync(ExpenseWorksheetRequest request, CancellationToken cancellationToken)
    {
        var defaultMonth = MonthPeriod.Normalize(request.AccountingMonth ?? businessDateProvider.Today);
        var monthFrom = MonthPeriod.Normalize(request.MonthFrom ?? defaultMonth);
        var monthTo = MonthPeriod.Normalize(request.MonthTo ?? request.MonthFrom ?? defaultMonth);
        if (monthFrom > monthTo)
        {
            return FinanceResult<ExpenseWorksheetDto>.Failure("expense_worksheet_period_invalid", "Дата начала формы выплат не может быть позже даты окончания.");
        }

        var monthCount = ((monthTo.Year - monthFrom.Year) * 12) + monthTo.Month - monthFrom.Month + 1;
        if (monthCount > MaxBalanceHistoryMonths)
        {
            return FinanceResult<ExpenseWorksheetDto>.Failure("expense_worksheet_period_too_large", $"Форму выплат можно построить максимум за {MaxBalanceHistoryMonths} месяцев.");
        }

        var worksheetData = await expenseWorksheetQuery.GetAsync(
            monthFrom,
            monthTo,
            CashExpenseTypeCodes,
            CashExpenseTypeNames,
            cancellationToken);

        var rows = new List<ExpenseWorksheetRowDto>();
        var supplierAccruals = worksheetData.SupplierAccruals
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierExpenses = worksheetData.SupplierExpenses
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierOpeningAccruals = worksheetData.SupplierOpeningAccruals
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierOpeningExpenses = worksheetData.SupplierOpeningExpenses
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierStartingBalances = worksheetData.SupplierStartingBalances
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierFunds = worksheetData.SupplierFunds
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierKeys = supplierAccruals.Keys
            .Concat(supplierExpenses.Keys)
            .Concat(supplierOpeningAccruals.Keys)
            .Concat(supplierOpeningExpenses.Keys)
            .Concat(supplierStartingBalances.Keys)
            .Distinct()
            .ToList();

        foreach (var key in supplierKeys)
        {
            supplierAccruals.TryGetValue(key, out var accrual);
            supplierExpenses.TryGetValue(key, out var expense);
            supplierOpeningAccruals.TryGetValue(key, out var openingAccrual);
            supplierOpeningExpenses.TryGetValue(key, out var openingExpense);
            supplierStartingBalances.TryGetValue(key, out var startingBalance);
            supplierFunds.TryGetValue(key, out var expenseFund);
            var sample = accrual ?? expense ?? openingAccrual ?? openingExpense ?? startingBalance!;
            var accrualAmount = MoneyMath.RoundMoney(accrual?.Amount ?? 0m);
            var expenseAmount = MoneyMath.RoundMoney(expense?.Amount ?? 0m);
            var balance = MoneyMath.RoundMoney(Math.Max(accrualAmount - expenseAmount, 0m));
            var openingBalance = MoneyMath.RoundMoney(
                (startingBalance?.Amount ?? 0m) +
                (openingAccrual?.Amount ?? 0m) -
                (openingExpense?.Amount ?? 0m));
            var closingBalance = MoneyMath.RoundMoney(openingBalance + accrualAmount - expenseAmount);
            decimal? collected = expenseFund is null
                ? null
                : MoneyMath.RoundMoney(expenseFund.AvailableBalance + expenseAmount);
            decimal? difference = expenseFund is null
                ? null
                : MoneyMath.RoundMoney(expenseFund.AvailableBalance);
            rows.Add(new ExpenseWorksheetRowDto(
                "supplier",
                sample.SupplierId,
                null,
                sample.SupplierName,
                sample.ExpenseTypeId,
                sample.ExpenseTypeName,
                accrualAmount,
                expenseAmount,
                balance,
                collected,
                difference)
            {
                OpeningBalance = openingBalance,
                OpeningDebt = MoneyMath.RoundMoney(Math.Max(openingBalance, 0m)),
                OpeningAdvance = MoneyMath.RoundMoney(Math.Max(-openingBalance, 0m)),
                ClosingDebt = MoneyMath.RoundMoney(Math.Max(closingBalance, 0m)),
                ClosingAdvance = MoneyMath.RoundMoney(Math.Max(-closingBalance, 0m)),
                ExpenseFundId = expenseFund?.ExpenseFundId,
                ExpenseFundName = expenseFund?.ExpenseFundName
            });
        }

        var staffExpenses = worksheetData.StaffExpenses
            .ToDictionary(item => (item.StaffMemberId, item.ExpenseTypeId), item => item.Amount);
        var staffOpeningExpenses = worksheetData.StaffOpeningExpenses
            .ToDictionary(item => (item.StaffMemberId, item.ExpenseTypeId));
        var staffBonuses = worksheetData.StaffBonuses
            .ToDictionary(item => item.StaffMemberId, item => item.Amount);
        var staffPenalties = worksheetData.StaffPenalties
            .ToDictionary(item => item.StaffMemberId, item => item.Amount);
        var staffOpeningBonuses = worksheetData.StaffOpeningBonuses
            .ToDictionary(item => item.StaffMemberId, item => item.Amount);
        var staffOpeningPenalties = worksheetData.StaffOpeningPenalties
            .ToDictionary(item => item.StaffMemberId, item => item.Amount);
        foreach (var staffMember in worksheetData.StaffMembers)
        {
            var key = (staffMember.StaffMemberId, staffMember.ExpenseTypeId);
            staffExpenses.TryGetValue(key, out var staffExpenseAmount);
            staffOpeningExpenses.TryGetValue(key, out var staffOpeningExpense);
            staffBonuses.TryGetValue(staffMember.StaffMemberId, out var bonusAmount);
            staffPenalties.TryGetValue(staffMember.StaffMemberId, out var penaltyAmount);
            staffOpeningBonuses.TryGetValue(staffMember.StaffMemberId, out var openingBonusAmount);
            staffOpeningPenalties.TryGetValue(staffMember.StaffMemberId, out var openingPenaltyAmount);
            var staffCreatedMonth = new DateOnly(
                staffMember.CreatedAtUtc.UtcDateTime.Year,
                staffMember.CreatedAtUtc.UtcDateTime.Month,
                1);
            var salaryStartMonth = staffCreatedMonth > monthFrom ? staffCreatedMonth : monthFrom;
            var salaryMonthCount = worksheetData.SalaryAccrualMonthTo is { } salaryMonthTo && salaryMonthTo >= salaryStartMonth
                ? ((salaryMonthTo.Year - salaryStartMonth.Year) * 12) + salaryMonthTo.Month - salaryStartMonth.Month + 1
                : 0;
            var baseAccrualAmount = MoneyMath.RoundMoney(staffMember.Rate * salaryMonthCount);
            var accrualAmount = MoneyMath.RoundMoney(baseAccrualAmount + bonusAmount - penaltyAmount);
            var expenseAmount = MoneyMath.RoundMoney(staffExpenseAmount);
            var historyStartMonth = staffOpeningExpense?.FirstAccountingMonth is { } firstExpenseMonth && firstExpenseMonth < staffCreatedMonth
                ? firstExpenseMonth
                : staffCreatedMonth;
            var historyMonthCount = Math.Max(
                0,
                ((monthFrom.Year - historyStartMonth.Year) * 12) + monthFrom.Month - historyStartMonth.Month);
            var openingBalance = MoneyMath.RoundMoney(
                (staffMember.Rate * historyMonthCount) +
                openingBonusAmount -
                openingPenaltyAmount -
                (staffOpeningExpense?.Amount ?? 0m));
            var closingBalance = MoneyMath.RoundMoney(openingBalance + accrualAmount - expenseAmount);
            rows.Add(new ExpenseWorksheetRowDto(
                "staff",
                null,
                staffMember.StaffMemberId,
                staffMember.FullName,
                staffMember.ExpenseTypeId,
                staffMember.ExpenseTypeName,
                accrualAmount,
                expenseAmount,
                MoneyMath.RoundMoney(Math.Max(accrualAmount - expenseAmount, 0m)),
                null,
                null)
            {
                BaseAccrualAmount = baseAccrualAmount,
                BonusAmount = MoneyMath.RoundMoney(bonusAmount),
                PenaltyAmount = MoneyMath.RoundMoney(penaltyAmount),
                OpeningBalance = openingBalance,
                OpeningDebt = MoneyMath.RoundMoney(Math.Max(openingBalance, 0m)),
                OpeningAdvance = MoneyMath.RoundMoney(Math.Max(-openingBalance, 0m)),
                ClosingDebt = MoneyMath.RoundMoney(Math.Max(closingBalance, 0m)),
                ClosingAdvance = MoneyMath.RoundMoney(Math.Max(-closingBalance, 0m))
            });
        }

        rows = rows
            .OrderBy(row => row.RowKind == "supplier" ? 0 : 1)
            .ThenBy(row => row.CounterpartyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ExpenseTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accrualTotal = MoneyMath.RoundMoney(rows.Sum(row => row.AccrualAmount));
        var expenseTotal = MoneyMath.RoundMoney(rows.Sum(row => row.ExpenseAmount));
        var balanceTotal = MoneyMath.RoundMoney(rows.Sum(row => row.Balance));
        var openingBalanceTotal = MoneyMath.RoundMoney(rows.Sum(row => row.OpeningBalance));
        var openingDebtTotal = MoneyMath.RoundMoney(rows.Sum(row => row.OpeningDebt));
        var openingAdvanceTotal = MoneyMath.RoundMoney(rows.Sum(row => row.OpeningAdvance));
        var closingDebtTotal = MoneyMath.RoundMoney(rows.Sum(row => row.ClosingDebt));
        var closingAdvanceTotal = MoneyMath.RoundMoney(rows.Sum(row => row.ClosingAdvance));
        var supplierFundTotals = rows
            .Where(row => row.ExpenseFundId.HasValue)
            .GroupBy(row => row.ExpenseFundId!.Value)
            .Select(group => new
            {
                Collected = MoneyMath.RoundMoney(
                    group.First().Difference!.Value + group.Sum(row => row.ExpenseAmount)),
                Difference = group.First().Difference!.Value
            })
            .ToList();
        var collectedTotal = MoneyMath.RoundMoney(
            supplierFundTotals.Sum(item => item.Collected) +
            rows.Where(row => !row.ExpenseFundId.HasValue).Sum(row => row.CollectedAmount ?? 0m));
        var differenceTotal = MoneyMath.RoundMoney(
            supplierFundTotals.Sum(item => item.Difference) +
            rows.Where(row => !row.ExpenseFundId.HasValue).Sum(row => row.Difference ?? 0m));
        var availableAmounts = CalculateAvailableAmounts(worksheetData.AvailableBalance);

        return FinanceResult<ExpenseWorksheetDto>.Success(new ExpenseWorksheetDto(
            monthTo,
            accrualTotal,
            expenseTotal,
            balanceTotal,
            collectedTotal,
            differenceTotal,
            availableAmounts.BankAmount,
            availableAmounts.CashAmount,
            rows)
        {
            MonthFrom = monthFrom,
            MonthTo = monthTo,
            OpeningBalanceTotal = openingBalanceTotal,
            OpeningDebtTotal = openingDebtTotal,
            OpeningAdvanceTotal = openingAdvanceTotal,
            ClosingDebtTotal = closingDebtTotal,
            ClosingAdvanceTotal = closingAdvanceTotal
        });
    }

    private static int NormalizeListLimit(int? limit)
    {
        return QueryLimits.NormalizeListSize(limit);
    }

    private static int NormalizeListOffset(int? offset)
    {
        return offset is null or < 0 ? 0 : offset.Value;
    }

    public async Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var totals = await financeTotalsQuery.GetAsync(
            request.DateFrom,
            request.DateTo,
            NormalizeOptional(request.OperationKind),
            NormalizeSearch(request.Search),
            request.GarageId,
            request.SupplierId,
            request.StaffMemberId,
            cancellationToken);
        return new FinanceSummaryDto(
            totals.IncomeTotal,
            totals.ExpenseTotal,
            totals.AccrualTotal,
            totals.IncomeTotal - totals.ExpenseTotal,
            totals.AccrualTotal - totals.IncomeTotal,
            totals.OperationCount,
            totals.AccrualCount,
            totals.MeterReadingCount)
        {
            IncomeCount = totals.IncomeCount,
            ExpenseCount = totals.ExpenseCount,
            SupplierAccrualCount = totals.SupplierAccrualCount
        };
    }

    public async Task<FinanceResult<SupplierOpeningBalanceDto>> GetSupplierOpeningBalanceAsync(
        Guid supplierId,
        SupplierOpeningBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var monthFrom = MonthPeriod.Normalize(request.MonthFrom ?? businessDateProvider.Today);
        var data = await supplierRepository.GetOpeningBalanceAsync(supplierId, monthFrom, cancellationToken);
        if (data is null)
        {
            return FinanceResult<SupplierOpeningBalanceDto>.Failure("supplier_not_found", "Поставщик для финансового отчёта не найден.");
        }

        var startingBalance = MoneyMath.RoundMoney(data.StartingBalance);
        var priorAccrualTotal = MoneyMath.RoundMoney(data.PriorAccrualTotal);
        var priorPaymentTotal = MoneyMath.RoundMoney(data.PriorPaymentTotal);
        return FinanceResult<SupplierOpeningBalanceDto>.Success(new SupplierOpeningBalanceDto(
            supplierId,
            monthFrom,
            startingBalance,
            priorAccrualTotal,
            priorPaymentTotal,
            MoneyMath.RoundMoney(startingBalance + priorAccrualTotal - priorPaymentTotal)));
    }

    public async Task<FinanceResult<FinancialReportPeriodDto>> GetFinancialReportPeriodAsync(
        FinancialReportPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var targetCount = (request.GarageId.HasValue ? 1 : 0) +
            (request.SupplierId.HasValue ? 1 : 0) +
            (request.StaffMemberId.HasValue ? 1 : 0);
        if (targetCount != 1)
        {
            return FinanceResult<FinancialReportPeriodDto>.Failure(
                "financial_report_target_invalid",
                "Для финансового отчёта нужно выбрать ровно один гараж, поставщика или сотрудника.");
        }

        var data = await financialReportPeriodQuery.GetAsync(
            request.GarageId,
            request.SupplierId,
            request.StaffMemberId,
            cancellationToken);
        if (data is null)
        {
            return FinanceResult<FinancialReportPeriodDto>.Failure(
                "financial_report_target_not_found",
                "Запись для финансового отчёта не найдена.");
        }

        var currentMonth = MonthPeriod.Normalize(businessDateProvider.Today);
        var months = new[]
        {
            data.AccrualMonthFrom,
            data.OperationMonthFrom,
            data.AccrualMonthTo,
            data.OperationMonthTo,
            currentMonth
        };
        var monthFrom = months.Where(month => month.HasValue).Min()!.Value;
        var monthTo = months.Where(month => month.HasValue).Max()!.Value;
        var defaultMonthFrom = request.GarageId.HasValue
            ? data.FirstUnpaidAccrualMonth is { } firstUnpaidMonth && firstUnpaidMonth <= currentMonth
                ? firstUnpaidMonth
                : currentMonth
            : (DateOnly?)null;
        return FinanceResult<FinancialReportPeriodDto>.Success(new FinancialReportPeriodDto(
            monthFrom,
            monthTo,
            defaultMonthFrom,
            request.GarageId.HasValue ? currentMonth : null));
    }

    public async Task<FinanceResult<IncomePaymentWarningDto>> GetIncomePaymentWarningAsync(
        IncomePaymentWarningRequest request,
        CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<IncomePaymentWarningDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<IncomePaymentWarningDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        if (!string.Equals(incomeType.Code, MeterKinds.Electricity, StringComparison.OrdinalIgnoreCase))
        {
            return FinanceResult<IncomePaymentWarningDto>.Success(new IncomePaymentWarningDto(false, null, null, false));
        }

        var previousPaymentDate = await financialOperationRepository.GetPreviousActiveIncomeDateAsync(
            garage.Id,
            incomeType.Id,
            request.OperationDate,
            request.ExcludedOperationId,
            cancellationToken);
        if (!previousPaymentDate.HasValue)
        {
            return FinanceResult<IncomePaymentWarningDto>.Success(new IncomePaymentWarningDto(true, null, null, false));
        }

        var daysSincePreviousPayment = request.OperationDate.DayNumber - previousPaymentDate.Value.DayNumber;
        return FinanceResult<IncomePaymentWarningDto>.Success(new IncomePaymentWarningDto(
            true,
            previousPaymentDate,
            daysSincePreviousPayment,
            daysSincePreviousPayment < EarlyElectricityPaymentWarningDays));
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateIncomeAsync(CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (request.FeeCampaignId.HasValue && request.IrregularPaymentId.HasValue)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "income_payment_target_conflict",
                "Платёж не может одновременно относиться к сбору и нерегулярному начислению.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        IrregularPayment? irregularPayment = null;
        if (request.IrregularPaymentId.HasValue)
        {
            irregularPayment = await irregularPaymentRepository.FindActiveAsync(request.IrregularPaymentId.Value, cancellationToken);
            if (irregularPayment is null || !irregularPayment.IsActive)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "irregular_payment_inactive",
                    "Нерегулярный платёж деактивирован. Обновите расчёт платежей.");
            }

            if (!string.Equals(incomeType.Code, OtherPaymentsIncomeTypeCode, StringComparison.OrdinalIgnoreCase))
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "irregular_payment_income_type_invalid",
                    "Нерегулярное начисление не связано с выбранным видом поступления.");
            }
        }

        await using var campaignLock = request.FeeCampaignId.HasValue
            ? await feeCampaignRepository.AcquirePaymentLockAsync(request.FeeCampaignId.Value, cancellationToken)
            : null;
        FeeCampaign? feeCampaign = null;
        decimal collectedBefore = 0m;
        if (request.FeeCampaignId.HasValue)
        {
            feeCampaign = await feeCampaignRepository.FindActiveForAccrualGenerationAsync(request.FeeCampaignId.Value, cancellationToken);
            if (feeCampaign is null || feeCampaign.ClosedAtUtc.HasValue || feeCampaign.IsArchived)
            {
                return FinanceResult<FinancialOperationDto>.Failure("fee_campaign_closed", "Сбор уже закрыт. Обновите расчёт платежей.");
            }
            if (feeCampaign.IncomeTypeId != incomeType.Id ||
                (!feeCampaign.AppliesToAllGarages && feeCampaign.ParticipantGarages.All(item => item.GarageId != garage.Id)))
            {
                return FinanceResult<FinancialOperationDto>.Failure("fee_campaign_payment_invalid", "Сбор недоступен выбранному гаражу.");
            }
            collectedBefore = MoneyMath.RoundMoney(await feeCampaignRepository.GetCollectedAmountAsync(feeCampaign.Id, cancellationToken));
            var available = MoneyMath.RoundMoney(Math.Max(feeCampaign.TargetAmount - collectedBefore, 0m));
            if (available <= 0m || MoneyMath.RoundMoney(request.Amount) > available)
            {
                return FinanceResult<FinancialOperationDto>.Failure("fee_campaign_amount_exceeds_remaining", $"По сбору осталось оплатить не более {MoneyFormatting.Format(available)}.");
            }

            var accruals = await feeCampaignRepository.GetAccrualsForSettlementAsync(feeCampaign.Id, cancellationToken);
            var garageAccrual = accruals.FirstOrDefault(item => item.GarageId == garage.Id);
            var paidByGarage = await feeCampaignRepository.GetPaidAmountsByGarageAsync(feeCampaign.Id, cancellationToken);
            var requiredAmount = MoneyMath.RoundMoney(paidByGarage.GetValueOrDefault(garage.Id) + request.Amount);
            if (garageAccrual is null)
            {
                var month = MonthPeriod.Normalize(request.AccountingMonth);
                var dueDates = AccrualDueDates.ForFeeCampaign(month, feeCampaign.EndsOn, feeCampaign.OverdueGraceDays);
                garageAccrual = new Accrual
                {
                    GarageId = garage.Id,
                    Garage = garage,
                    IncomeTypeId = incomeType.Id,
                    IncomeType = incomeType,
                    FeeCampaignId = feeCampaign.Id,
                    FeeCampaign = feeCampaign,
                    AccountingMonth = month,
                    DueDate = dueDates.DueDate,
                    OverdueFromDate = dueDates.OverdueFromDate,
                    Amount = requiredAmount,
                    Source = AccrualSources.FeeCampaign,
                    Basis = feeCampaign.Name,
                    Comment = BuildFeeCampaignAccrualComment(feeCampaign, null)
                };
                accrualRepository.Add(garageAccrual);
            }
            else if (garageAccrual.Amount < requiredAmount)
            {
                garageAccrual.Amount = requiredAmount;
                garageAccrual.Basis = feeCampaign.Name;
                garageAccrual.UpdatedAtUtc = timeProvider.GetUtcNow();
            }
        }

        await using var fundAssignmentLock = await incomeFundAssignmentService.AcquireUpdateLockAsync(cancellationToken);
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            FinanceBalanceAccounts.Cash,
            cancellationToken);
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(garage.Id, incomeType.Id)],
            cancellationToken);

        if (irregularPayment is not null)
        {
            var state = await accrualRepository.GetIrregularPaymentStateAsync(
                garage.Id,
                irregularPayment.Id,
                MonthPeriod.Normalize(request.AccountingMonth),
                cancellationToken);
            if (state is null || !state.IsAvailable)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "irregular_payment_accrual_not_found",
                    "Нерегулярное начисление недоступно для выбранного гаража и месяца.");
            }

            var remaining = MoneyMath.RoundMoney(state.OutstandingAmount);
            if (remaining <= 0m || MoneyMath.RoundMoney(request.Amount) > remaining)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "irregular_payment_amount_exceeds_remaining",
                    $"По нерегулярному начислению осталось оплатить не более {MoneyFormatting.Format(remaining)}.");
            }
        }

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Income, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        if (request.ReceiptBatchId is Guid receiptBatchId &&
            await financialOperationRepository.ReceiptBatchConflictExistsAsync(
                receiptBatchId,
                garage.Id,
                request.OperationDate,
                cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "receipt_batch_conflict",
                "Пакет единой квитанции уже связан с другим гаражом или датой платежа.");
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = request.OperationDate,
            AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth),
            Amount = MoneyMath.RoundMoney(request.Amount),
            ReceiptBatchId = request.ReceiptBatchId,
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            FeeCampaignId = feeCampaign?.Id,
            FeeCampaign = feeCampaign,
            IrregularPaymentId = irregularPayment?.Id,
            IrregularPayment = irregularPayment
        };

        financialOperationRepository.Add(operation);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
            actorUserId,
            "Создание поступления",
            operation.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.income_created", operation, FormatIncomeCreatedAuditSummary(operation));
        var assignmentResult = await incomeFundAssignmentService.CreateAsync(operation, actorUserId, cancellationToken);
        if (!assignmentResult.Succeeded)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                assignmentResult.ErrorCode!,
                assignmentResult.ErrorMessage!);
        }
        if (feeCampaign is not null)
        {
            var paidByGarage = (await feeCampaignRepository.GetPaidAmountsByGarageAsync(feeCampaign.Id, cancellationToken)).ToDictionary(item => item.Key, item => item.Value);
            paidByGarage[garage.Id] = MoneyMath.RoundMoney(paidByGarage.GetValueOrDefault(garage.Id) + operation.Amount);
            var settlementAccruals = await feeCampaignRepository.GetAccrualsForSettlementAsync(feeCampaign.Id, cancellationToken);
            foreach (var group in settlementAccruals.GroupBy(item => item.GarageId))
            {
                var paid = paidByGarage.GetValueOrDefault(group.Key);
                var primary = group.First();
                if (paid <= 0m)
                {
                    foreach (var accrual in group) accrual.IsCanceled = true;
                    continue;
                }
                primary.Amount = paid;
                primary.Basis = feeCampaign.Name;
                foreach (var duplicateAccrual in group.Skip(1)) duplicateAccrual.IsCanceled = true;
            }
            if (MoneyMath.RoundMoney(collectedBefore + operation.Amount) >= feeCampaign.TargetAmount)
            {
                feeCampaign.ClosedAtUtc = timeProvider.GetUtcNow();
                feeCampaign.ClosedByUserId = actorUserId;
                feeCampaign.IsClosedEarly = false;
                feeCampaign.ClosureComment = "Сбор закрыт автоматически после достижения полной суммы.";
                feeCampaign.UpdatedAtUtc = feeCampaign.ClosedAtUtc.Value;
                AddAudit(actorUserId, "dictionary.fee_campaign_closed_automatically", "fee_campaign", feeCampaign.Id,
                    $"Сбор {feeCampaign.Name} закрыт автоматически: собрано {feeCampaign.TargetAmount:F2}.");
            }
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FullGaragePaymentDto>> CreateFullGaragePaymentAsync(
        CreateFullGaragePaymentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (request.Lines is null || request.Lines.Count is < 1 or > 100)
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "full_payment_lines_invalid",
                "Полная оплата должна содержать от 1 до 100 строк.");
        }

        var normalizedLines = request.Lines
            .Select(line => line with
            {
                AccountingMonth = MonthPeriod.Normalize(line.AccountingMonth),
                Amount = MoneyMath.RoundMoney(line.Amount),
                Comment = NormalizeOptional(line.Comment)
            })
            .ToList();
        if (normalizedLines.Any(line => line.Amount <= 0))
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "full_payment_amount_invalid",
                "Сумма каждой строки полной оплаты должна быть больше нуля.");
        }

        if (normalizedLines.Any(line => line.FeeCampaignId.HasValue && line.IrregularPaymentId.HasValue))
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "income_payment_target_conflict",
                "Строка полной оплаты не может одновременно относиться к сбору и нерегулярному начислению.");
        }

        if (normalizedLines.Any(line => line.IsOpeningDebt == line.IncomeTypeId.HasValue))
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "full_payment_line_kind_invalid",
                "Для обычной строки укажите вид поступления, а для входящего долга не указывайте его.");
        }

        if (normalizedLines.Count(line => line.IsOpeningDebt) > 1 ||
            normalizedLines
                .GroupBy(line => (
                    line.IsOpeningDebt,
                    line.IncomeTypeId,
                    line.AccountingMonth,
                    line.FeeCampaignId,
                    line.IrregularPaymentId))
                .Any(group => group.Count() > 1))
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "full_payment_line_duplicate",
                "В полной оплате не должно быть повторяющихся строк одного вида и месяца.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FullGaragePaymentDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeTypeIds = normalizedLines
            .Where(line => line.IncomeTypeId.HasValue)
            .Select(line => line.IncomeTypeId!.Value)
            .Distinct()
            .ToArray();
        var incomeTypes = (await incomeTypeRepository.GetActiveByIdsAsync(incomeTypeIds, cancellationToken))
            .ToDictionary(incomeType => incomeType.Id);
        if (incomeTypes.Count != incomeTypeIds.Length)
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "income_type_not_found",
                "Один из видов поступления полной оплаты не найден или архивирован.");
        }

        await using var fundAssignmentLock = await incomeFundAssignmentService.AcquireUpdateLockAsync(cancellationToken);
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            FinanceBalanceAccounts.Cash,
            cancellationToken);

        var shouldValidateIrregularPayments = !request.ReceiptBatchId.HasValue ||
            (await financialOperationRepository.GetByReceiptBatchIdAsync(request.ReceiptBatchId.Value, cancellationToken)).Count == 0;
        var irregularPayments = new Dictionary<Guid, IrregularPayment>();
        foreach (var line in normalizedLines.Where(item =>
                     item.IrregularPaymentId.HasValue && shouldValidateIrregularPayments))
        {
            var irregularPaymentId = line.IrregularPaymentId!.Value;
            if (!irregularPayments.TryGetValue(irregularPaymentId, out var irregularPayment))
            {
                irregularPayment = await irregularPaymentRepository.FindActiveAsync(irregularPaymentId, cancellationToken);
                if (irregularPayment is null || !irregularPayment.IsActive)
                {
                    return FinanceResult<FullGaragePaymentDto>.Failure(
                        "irregular_payment_inactive",
                        "Одно из нерегулярных начислений деактивировано. Обновите расчёт платежей.");
                }
                irregularPayments.Add(irregularPaymentId, irregularPayment);
            }

            var incomeType = incomeTypes[line.IncomeTypeId!.Value];
            var state = await accrualRepository.GetIrregularPaymentStateAsync(
                garage.Id,
                irregularPaymentId,
                line.AccountingMonth,
                cancellationToken);
            if (!string.Equals(incomeType.Code, OtherPaymentsIncomeTypeCode, StringComparison.OrdinalIgnoreCase) ||
                state is null || !state.IsAvailable)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    "irregular_payment_accrual_not_found",
                    "Одно из нерегулярных начислений недоступно для выбранного гаража и месяца.");
            }
            var remaining = MoneyMath.RoundMoney(state.OutstandingAmount);
            if (remaining <= 0m || line.Amount > remaining)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    "irregular_payment_amount_exceeds_remaining",
                    $"По нерегулярному начислению осталось оплатить не более {MoneyFormatting.Format(remaining)}.");
            }
        }

        var receiptBatchId = request.ReceiptBatchId ?? Guid.NewGuid();
        var existingBatch = await financialOperationRepository.GetByReceiptBatchIdAsync(
            receiptBatchId,
            cancellationToken);
        if (existingBatch.Count > 0)
        {
            var orderedExistingBatch = MatchFullPaymentBatch(existingBatch, normalizedLines);
            if (existingBatch.Any(operation =>
                    operation.GarageId != garage.Id ||
                    operation.OperationDate != request.OperationDate) ||
                orderedExistingBatch is null)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    "receipt_batch_conflict",
                    "Пакет единой квитанции уже связан с другой полной оплатой.");
            }

            var existingOperationDtos = await ToOperationDtosAsync(orderedExistingBatch, cancellationToken);
            return FinanceResult<FullGaragePaymentDto>.Success(new FullGaragePaymentDto(
                receiptBatchId,
                MoneyMath.RoundMoney(orderedExistingBatch.Sum(operation => operation.Amount)),
                existingOperationDtos));
        }

        if (await financialOperationRepository.ReceiptBatchConflictExistsAsync(
            receiptBatchId,
            garage.Id,
            request.OperationDate,
            cancellationToken))
        {
            return FinanceResult<FullGaragePaymentDto>.Failure(
                "receipt_batch_conflict",
                "Пакет единой квитанции уже связан с другим гаражом или датой платежа.");
        }

        IncomeType? openingDebtIncomeType = null;
        var openingDebtLine = normalizedLines.SingleOrDefault(line => line.IsOpeningDebt);
        if (openingDebtLine is not null)
        {
            var availableOpeningDebt = await CalculateAvailableOpeningDebtAsync(
                garage,
                openingDebtLine.AccountingMonth,
                cancellationToken);
            if (availableOpeningDebt <= 0)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    "debt_payment_opening_debt_not_found",
                    "На начало выбранного периода нет входящего долга для оплаты.");
            }

            if (openingDebtLine.Amount > availableOpeningDebt)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    "debt_payment_amount_exceeds_opening_debt",
                    $"Сумма оплаты входящего долга не может превышать {MoneyFormatting.Format(availableOpeningDebt)}.");
            }

            openingDebtIncomeType = await GetOrCreateDebtTransferIncomeTypeAsync(cancellationToken);
        }

        var operations = new List<FinancialOperation>(normalizedLines.Count);
        var batchCreatedAtUtc = DateTimeOffset.UtcNow;
        foreach (var line in normalizedLines)
        {
            var incomeType = line.IsOpeningDebt ? openingDebtIncomeType! : incomeTypes[line.IncomeTypeId!.Value];
            var comment = line.IsOpeningDebt
                ? line.Comment is null
                    ? "Оплата входящего долга периода"
                    : $"Оплата входящего долга периода: {line.Comment}"
                : line.Comment;
            var operation = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = request.OperationDate,
                AccountingMonth = line.AccountingMonth,
                Amount = line.Amount,
                ReceiptBatchId = receiptBatchId,
                Comment = comment,
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                FeeCampaignId = line.FeeCampaignId,
                IrregularPaymentId = line.IrregularPaymentId,
                IrregularPayment = line.IrregularPaymentId.HasValue && irregularPayments.TryGetValue(line.IrregularPaymentId.Value, out var irregularPayment)
                    ? irregularPayment
                    : null,
                CreatedAtUtc = batchCreatedAtUtc.AddTicks(operations.Count * TimeSpan.TicksPerMicrosecond),
                UpdatedAtUtc = batchCreatedAtUtc.AddTicks(operations.Count * TimeSpan.TicksPerMicrosecond)
            };
            financialOperationRepository.Add(operation);
            operations.Add(operation);
        }

        var allocationKeys = operations
            .Select(operation => new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            allocationKeys,
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            allocationKeys,
            actorUserId,
            "Полная оплата гаража",
            receiptBatchId,
            cancellationToken);

        foreach (var operation in operations)
        {
            AddAudit(actorUserId, "finance.income_created", operation, FormatIncomeCreatedAuditSummary(operation));
            var assignmentResult = await incomeFundAssignmentService.CreateAsync(operation, actorUserId, cancellationToken);
            if (!assignmentResult.Succeeded)
            {
                return FinanceResult<FullGaragePaymentDto>.Failure(
                    assignmentResult.ErrorCode!,
                    assignmentResult.ErrorMessage!);
            }
        }

        var totalAmount = MoneyMath.RoundMoney(operations.Sum(operation => operation.Amount));
        AddAudit(
            actorUserId,
            "finance.full_garage_payment_created",
            "receipt_batch",
            receiptBatchId,
            $"Полная оплата гаража {garage.Number}: {operations.Count} строк на сумму {MoneyFormatting.Format(totalAmount)}.",
            relatedDocumentId: receiptBatchId.ToString(),
            relatedDocumentNumber: $"ПАКЕТ-{receiptBatchId:N}",
            relatedGarageId: garage.Id.ToString(),
            relatedGarageNumber: garage.Number,
            metadata: new Dictionary<string, object?>
            {
                ["lineCount"] = operations.Count,
                ["totalAmount"] = totalAmount
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var operationDtos = await ToOperationDtosAsync(operations, cancellationToken);
        return FinanceResult<FullGaragePaymentDto>.Success(new FullGaragePaymentDto(
            receiptBatchId,
            totalAmount,
            operationDtos));
    }

    private static IReadOnlyList<FinancialOperation>? MatchFullPaymentBatch(
        IReadOnlyList<FinancialOperation> existingBatch,
        IReadOnlyList<CreateFullGaragePaymentLineRequest> requestedLines)
    {
        if (existingBatch.Count != requestedLines.Count || existingBatch.Any(operation => operation.IsCanceled))
        {
            return null;
        }

        var unmatched = existingBatch.ToList();
        var orderedMatches = new List<FinancialOperation>(requestedLines.Count);
        foreach (var line in requestedLines)
        {
            var expectedComment = line.IsOpeningDebt
                ? line.Comment is null
                    ? "Оплата входящего долга периода"
                    : $"Оплата входящего долга периода: {line.Comment}"
                : line.Comment;
            var matchIndex = unmatched.FindIndex(operation =>
                operation.AccountingMonth == line.AccountingMonth &&
                operation.Amount == line.Amount &&
                string.Equals(operation.Comment, expectedComment, StringComparison.Ordinal) &&
                operation.FeeCampaignId == line.FeeCampaignId &&
                operation.IrregularPaymentId == line.IrregularPaymentId &&
                (line.IsOpeningDebt
                    ? string.Equals(operation.IncomeType?.Code, DebtTransferIncomeTypeCode, StringComparison.OrdinalIgnoreCase)
                    : operation.IncomeTypeId == line.IncomeTypeId));
            if (matchIndex < 0)
            {
                return null;
            }

            orderedMatches.Add(unmatched[matchIndex]);
            unmatched.RemoveAt(matchIndex);
        }

        return unmatched.Count == 0 ? orderedMatches : null;
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateGarageDebtPaymentAsync(CreateGarageDebtPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var amount = MoneyMath.RoundMoney(request.Amount);
        if (amount <= 0)
        {
            return FinanceResult<FinancialOperationDto>.Failure("debt_payment_amount_invalid", "Сумма оплаты входящего долга должна быть больше нуля.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж для оплаты входящего долга не найден.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var availableOpeningDebt = await CalculateAvailableOpeningDebtAsync(garage, accountingMonth, cancellationToken);
        if (availableOpeningDebt <= 0)
        {
            return FinanceResult<FinancialOperationDto>.Failure("debt_payment_opening_debt_not_found", "На начало выбранного периода нет входящего долга для оплаты.");
        }

        if (amount > availableOpeningDebt)
        {
            return FinanceResult<FinancialOperationDto>.Failure("debt_payment_amount_exceeds_opening_debt", $"Сумма оплаты входящего долга не может превышать {MoneyFormatting.Format(availableOpeningDebt)}.");
        }

        var incomeType = await GetOrCreateDebtTransferIncomeTypeAsync(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var comment = NormalizeOptional(request.Comment);
        return await CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                request.GarageId,
                incomeType.Id,
                request.OperationDate,
                accountingMonth,
                amount,
                null,
                comment is null ? "Оплата входящего долга периода" : $"Оплата входящего долга периода: {comment}",
                request.ReceiptBatchId),
            actorUserId,
            cancellationToken);
    }

    private async Task<decimal> CalculateAvailableOpeningDebtAsync(Garage garage, DateOnly accountingMonth, CancellationToken cancellationToken)
    {
        var previousAccrualTotal = await accrualRepository.GetTotalBeforeMonthAsync(garage.Id, accountingMonth, cancellationToken);
        var previousIncomeTotal = await financialOperationRepository.GetIncomeTotalBeforeMonthAsync(garage.Id, accountingMonth, cancellationToken);
        var alreadyPaidOpeningDebt = await financialOperationRepository.GetOpeningDebtPaymentTotalAsync(
            garage.Id,
            accountingMonth,
            DebtTransferIncomeTypeCode,
            DebtTransferIncomeTypeName,
            cancellationToken);

        return MoneyMath.RoundMoney(Math.Max(garage.StartingBalance + previousAccrualTotal - previousIncomeTotal - alreadyPaidOpeningDebt, 0m));
    }

    private async Task<decimal> CalculateAvailableBankAmountAsync(CancellationToken cancellationToken)
    {
        return (await CalculateAvailableAmountsAsync(cancellationToken)).BankAmount;
    }

    private async Task<decimal> CalculateAvailableCashAmountAsync(CancellationToken cancellationToken)
    {
        return (await CalculateAvailableAmountsAsync(cancellationToken)).CashAmount;
    }

    private async Task<AvailableAmounts> CalculateAvailableAmountsAsync(CancellationToken cancellationToken)
    {
        var balance = await financeAvailableBalanceQuery.GetAsync(CashExpenseTypeCodes, CashExpenseTypeNames, cancellationToken);
        return CalculateAvailableAmounts(balance);
    }

    private static AvailableAmounts CalculateAvailableAmounts(FinanceAvailableBalanceData balance)
    {
        var bankAmount = MoneyMath.RoundMoney(Math.Max(
            balance.BankAdjustmentTotal + balance.BankDepositTotal - balance.BankExpenseTotal,
            0m));
        var cashAmount = MoneyMath.RoundMoney(Math.Max(
            balance.CashAdjustmentTotal + balance.IncomeTotal - balance.BankDepositTotal - balance.CashExpenseTotal,
            0m));

        return new AvailableAmounts(bankAmount, cashAmount);
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var expensePaymentType = NormalizeExpensePaymentType(request.ExpensePaymentType);
        if (expensePaymentType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "expense_payment_type_invalid",
                "Тип выплаты должен быть «С чеком» или «Без чека».");
        }

        var expensePaymentSource = NormalizeExpensePaymentSource(request.ExpensePaymentSource, expensePaymentType);
        if (expensePaymentSource is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "expense_payment_source_invalid",
                "Источник выплаты должен быть «Банк» или «Касса».");
        }

        var isCashExpense = expensePaymentSource == ExpensePaymentSources.Cash;
        var allowNegativeFundBalance = !isCashExpense && request.ConfirmNegativeFundBalance;
        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Услуга не найдена.");
        }

        Supplier? supplier = null;
        if (request.SupplierId.HasValue)
        {
            supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId.Value, cancellationToken);
            if (supplier is null)
            {
                return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
            }
        }

        if (!isCashExpense && supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_required_for_bank_expense", "Для выплаты с банковского счёта выберите поставщика.");
        }

        Guid? expenseFundId = null;
        Fund? expenseFund = null;
        if (supplier is not null)
        {
            var supplierExpenseTypeValidation = ValidateSupplierExpenseTypeLinkForPayment(supplier, expenseType);
            if (supplierExpenseTypeValidation is not null)
            {
                return supplierExpenseTypeValidation;
            }

            expenseFundId = GetSupplierExpenseFundId(supplier);
            expenseFund = GetSupplierExpenseFund(supplier);
            if (request.ExpenseFundId.HasValue && request.ExpenseFundId != expenseFundId)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "supplier_expense_fund_mismatch",
                    "Выплата должна использовать фонд настроенной услуги поставщика.");
            }
        }
        else if (request.ExpenseFundId.HasValue)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "episodic_expense_fund_not_allowed",
                "Эпизодическая выплата из кассы не списывает средства из фонда.");
        }

        await using var fundDisbursementLock = await expenseFundDisbursementService.AcquireUpdateLockAsync(cancellationToken);
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            isCashExpense ? FinanceBalanceAccounts.Cash : FinanceBalanceAccounts.Bank,
            cancellationToken);

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (isCashExpense)
        {
            var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
            if (amount > availableCashAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "cash_amount_insufficient",
                    $"В кассе недостаточно средств. Доступно {MoneyFormatting.Format(availableCashAmount)}.");
            }
        }
        else
        {
            var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
            if (amount > availableBankAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "bank_amount_insufficient",
                    $"На банковском счёте недостаточно средств. Доступно {MoneyFormatting.Format(availableBankAmount)}.");
            }
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = request.OperationDate,
            AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth),
            Amount = amount,
            ExpensePaymentType = expensePaymentType,
            ExpensePaymentSource = expensePaymentSource,
            CounterpartyName = supplier is null ? NormalizeOptional(request.CounterpartyName) : null,
            NegativeFundBalanceConfirmed = allowNegativeFundBalance,
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            SupplierId = supplier?.Id,
            Supplier = supplier,
            ExpenseTypeId = expenseType.Id,
            ExpenseType = expenseType,
            ExpenseFundId = expenseFundId,
            ExpenseFund = expenseFund
        };

        financialOperationRepository.Add(operation);
        if (isCashExpense && supplier is not null)
        {
            supplierAccrualRepository.Add(new SupplierAccrual
            {
                SupplierId = supplier.Id,
                Supplier = supplier,
                ExpenseTypeId = expenseType.Id,
                ExpenseType = expenseType,
                ExpenseFundId = operation.ExpenseFundId,
                ExpenseFund = operation.ExpenseFund,
                SourceFinancialOperationId = operation.Id,
                SourceFinancialOperation = operation,
                AccountingMonth = operation.AccountingMonth,
                Amount = amount,
                Source = AccrualSources.Manual,
                DocumentNumber = operation.DocumentNumber,
                Comment = operation.Comment
            });
            AddAudit(
                actorUserId,
                "finance.atomic_cash_expense_created",
                operation,
                FormatAtomicCashExpenseCreatedAuditSummary(operation));
        }
        else
        {
            AddAudit(actorUserId, "finance.expense_created", operation, FormatExpenseCreatedAuditSummary(operation));
        }

        if (expenseFundId.HasValue)
        {
            var fundDisbursementResult = await expenseFundDisbursementService.CreateAsync(
                operation,
                supplier?.Name ?? operation.CounterpartyName ?? "получатель",
                actorUserId,
                operation.NegativeFundBalanceConfirmed,
                cancellationToken);
            if (!fundDisbursementResult.Succeeded)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    fundDisbursementResult.ErrorCode!,
                    fundDisbursementResult.ErrorMessage!);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    private async Task<FinanceResult<FinancialOperationDto>> UpdateEpisodicExpenseAsync(
        FinancialOperation operation,
        CreateExpenseOperationRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (operation.SupplierId.HasValue || operation.ExpenseFundId.HasValue)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "episodic_expense_conversion_not_supported",
                "Обычную выплату поставщику нельзя преобразовать в эпизодическую. Отмените её и создайте новую.");
        }

        var expensePaymentType = NormalizeExpensePaymentType(request.ExpensePaymentType);
        var expensePaymentSource = NormalizeExpensePaymentSource(request.ExpensePaymentSource, expensePaymentType);
        if (expensePaymentType is null || expensePaymentSource != ExpensePaymentSources.Cash)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "episodic_expense_source_invalid",
                "Эпизодическая выплата без карточки поставщика проводится только из кассы.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Услуга не найдена.");
        }

        if (await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, operation.Id, cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(FinanceBalanceAccounts.Cash, cancellationToken);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var availableCashAmount = MoneyMath.RoundMoney(await CalculateAvailableCashAmountAsync(cancellationToken) + operation.Amount);
        if (amount > availableCashAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "cash_amount_insufficient",
                $"Сумма выплаты превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
        }

        var previousSnapshot = FormatExpenseOperationSnapshot(operation);
        var oldValues = new Dictionary<string, object?>
        {
            ["operationDate"] = operation.OperationDate,
            ["accountingMonth"] = operation.AccountingMonth,
            ["amount"] = operation.Amount,
            ["counterpartyName"] = operation.CounterpartyName,
            ["expenseType"] = operation.ExpenseType?.Name,
            ["documentNumber"] = operation.DocumentNumber,
            ["comment"] = operation.Comment
        };
        operation.OperationDate = request.OperationDate;
        operation.AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        operation.Amount = amount;
        operation.ExpensePaymentType = expensePaymentType;
        operation.ExpensePaymentSource = ExpensePaymentSources.Cash;
        operation.CounterpartyName = NormalizeOptional(request.CounterpartyName);
        operation.NegativeFundBalanceConfirmed = false;
        operation.DocumentNumber = NormalizeOptional(request.DocumentNumber);
        operation.Comment = NormalizeOptional(request.Comment);
        operation.ExpenseTypeId = expenseType.Id;
        operation.ExpenseType = expenseType;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var newValues = new Dictionary<string, object?>
        {
            ["operationDate"] = operation.OperationDate,
            ["accountingMonth"] = operation.AccountingMonth,
            ["amount"] = operation.Amount,
            ["counterpartyName"] = operation.CounterpartyName,
            ["expenseType"] = expenseType.Name,
            ["documentNumber"] = operation.DocumentNumber,
            ["comment"] = operation.Comment
        };
        AddAudit(actorUserId, "finance.expense_updated", operation, FormatExpenseUpdatedAuditSummary(previousSnapshot, operation), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateStaffPaymentAsync(CreateStaffPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var staffMember = await staffMemberRepository.FindActiveAsync(request.StaffMemberId, cancellationToken);
        if (staffMember is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("staff_member_not_found", "Сотрудник для выплаты не найден.");
        }

        var salaryExpenseType = await expenseTypeRepository.FindActiveByCodeAsync("salary", cancellationToken);
        if (salaryExpenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("salary_expense_type_not_found", "Системная услуга «Зарплата» не найдена.");
        }

        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            FinanceBalanceAccounts.Cash,
            cancellationToken);

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var adjustmentTotals = await staffSalaryAdjustmentRepository.GetTotalsAsync(staffMember.Id, accountingMonth, cancellationToken);
        var paidThisMonth = await financialOperationRepository.GetStaffExpenseTotalAsync(staffMember.Id, accountingMonth, cancellationToken);
        var availableAmount = MoneyMath.RoundMoney(
            staffMember.Rate + adjustmentTotals.BonusAmount - adjustmentTotals.PenaltyAmount - paidThisMonth);
        if (amount > availableAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {MoneyFormatting.Format(availableAmount)}.");
        }

        var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
        if (amount > availableCashAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "cash_amount_insufficient",
                $"Сумма выплаты превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = request.OperationDate,
            AccountingMonth = accountingMonth,
            Amount = amount,
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            StaffMemberId = staffMember.Id,
            StaffMember = staffMember,
            ExpenseTypeId = salaryExpenseType.Id,
            ExpenseType = salaryExpenseType,
            ExpensePaymentSource = ExpensePaymentSources.Cash
        };

        financialOperationRepository.Add(operation);
        AddAudit(actorUserId, "finance.staff_payment_created", operation, FormatStaffPaymentCreatedAuditSummary(operation, availableAmount));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<StaffSalaryAdjustmentDto>> CreateStaffSalaryAdjustmentAsync(
        CreateStaffSalaryAdjustmentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var staffMember = await staffMemberRepository.FindActiveAsync(request.StaffMemberId, cancellationToken);
        if (staffMember is null)
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure("staff_member_not_found", "Сотрудник для премии или штрафа не найден.");
        }

        var adjustmentType = NormalizeOptional(request.AdjustmentType)?.ToLowerInvariant();
        if (!StaffSalaryAdjustmentTypes.IsSupported(adjustmentType))
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure("staff_salary_adjustment_type_invalid", "Выберите тип корректировки «Премия» или «Штраф».");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (amount <= 0m)
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure("staff_salary_adjustment_amount_invalid", "Сумма премии или штрафа должна быть больше нуля.");
        }

        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure("staff_salary_adjustment_reason_required", "Укажите основание премии или штрафа.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var staffCreatedMonth = new DateOnly(
            staffMember.CreatedAtUtc.UtcDateTime.Year,
            staffMember.CreatedAtUtc.UtcDateTime.Month,
            1);
        if (accountingMonth < staffCreatedMonth)
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure("staff_salary_adjustment_month_invalid", "Нельзя выписать премию или штраф за месяц до начала работы сотрудника.");
        }

        var totals = await staffSalaryAdjustmentRepository.GetTotalsAsync(staffMember.Id, accountingMonth, cancellationToken);
        var paidThisMonth = await financialOperationRepository.GetStaffExpenseTotalAsync(staffMember.Id, accountingMonth, cancellationToken);
        var adjustedAccrual = MoneyMath.RoundMoney(
            staffMember.Rate +
            totals.BonusAmount -
            totals.PenaltyAmount +
            (adjustmentType == StaffSalaryAdjustmentTypes.Bonus ? amount : -amount));
        if (adjustedAccrual < paidThisMonth)
        {
            return FinanceResult<StaffSalaryAdjustmentDto>.Failure(
                "staff_penalty_exceeds_available",
                $"После штрафа начисление не может быть меньше уже выплаченной суммы {MoneyFormatting.Format(paidThisMonth)}.");
        }

        var adjustment = new StaffSalaryAdjustment
        {
            StaffMemberId = staffMember.Id,
            StaffMember = staffMember,
            AccountingMonth = accountingMonth,
            AdjustmentType = adjustmentType!,
            Amount = amount,
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Reason = reason
        };
        staffSalaryAdjustmentRepository.Add(adjustment);
        var typeName = adjustmentType == StaffSalaryAdjustmentTypes.Bonus ? "Премия" : "Штраф";
        AddAudit(
            actorUserId,
            "finance.staff_salary_adjustment_created",
            "staff_salary_adjustment",
            adjustment.Id,
            $"{typeName} сотруднику {staffMember.FullName} за {accountingMonth:MM.yyyy}: {MoneyFormatting.Format(amount)}; основание: {reason}.",
            relatedAccountingMonth: accountingMonth,
            relatedDocumentId: adjustment.Id.ToString(),
            relatedDocumentNumber: adjustment.DocumentNumber,
            relatedCounterpartyId: staffMember.Id.ToString(),
            relatedCounterpartyName: staffMember.FullName,
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "staff_salary_adjustment",
                ["staffMemberId"] = staffMember.Id,
                ["staffMemberName"] = staffMember.FullName,
                ["staffDepartmentName"] = staffMember.Department?.Name,
                ["adjustmentType"] = adjustment.AdjustmentType,
                ["amount"] = adjustment.Amount,
                ["salaryAccrualAfterAdjustment"] = adjustedAccrual
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FinanceResult<StaffSalaryAdjustmentDto>.Success(new StaffSalaryAdjustmentDto(
            adjustment.Id,
            staffMember.Id,
            staffMember.FullName,
            accountingMonth,
            adjustment.AdjustmentType,
            adjustment.Amount,
            adjustment.DocumentNumber,
            adjustment.Reason));
    }

    public async Task<FinanceResult<CashBankTransferDto>> CreateCashBankTransferAsync(
        CreateCashBankTransferRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (request.TransferDate == default)
        {
            return FinanceResult<CashBankTransferDto>.Failure(
                "cash_bank_transfer_date_required",
                "Укажите дату сдачи кассы в банк.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (amount <= 0m)
        {
            return FinanceResult<CashBankTransferDto>.Failure(
                "cash_bank_transfer_amount_invalid",
                "Сумма сдачи кассы в банк должна быть больше нуля.");
        }

        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            FinanceBalanceAccounts.Cash | FinanceBalanceAccounts.Bank,
            cancellationToken);
        var availableCash = await CalculateAvailableCashAmountAsync(cancellationToken);
        if (amount > availableCash)
        {
            return FinanceResult<CashBankTransferDto>.Failure(
                "cash_amount_insufficient",
                $"Сумма сдачи превышает доступный остаток в кассе {MoneyFormatting.Format(availableCash)}.");
        }

        var transfer = new CashBankTransfer
        {
            TransferDate = request.TransferDate,
            Amount = amount,
            Comment = NormalizeOptional(request.Comment),
            ActorUserId = actorUserId,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        cashBankTransferRepository.Add(transfer);
        AddAudit(
            actorUserId,
            "finance.cash_bank_transfer_created",
            "cash_bank_transfer",
            transfer.Id,
            $"Сдано из кассы в банк {MoneyFormatting.Format(transfer.Amount)} руб. от {transfer.TransferDate:dd.MM.yyyy}.",
            relatedDocumentId: transfer.Id.ToString(),
            metadata: new Dictionary<string, object?>
            {
                ["transferDate"] = transfer.TransferDate,
                ["amount"] = transfer.Amount,
                ["source"] = "cash",
                ["destination"] = "bank"
            },
            reason: transfer.Comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FinanceResult<CashBankTransferDto>.Success(new CashBankTransferDto(
            transfer.Id,
            transfer.TransferDate,
            transfer.Amount,
            transfer.Comment,
            transfer.CreatedAtUtc));
    }

    public async Task<FinanceResult<FinancialOperationDto>> UpdateIncomeAsync(Guid operationId, CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await financialOperationRepository.FindForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.OperationKind != FinancialOperationKinds.Income)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_kind_mismatch", "Эта операция не является поступлением.");
        }

        if (operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Отмененную операцию нельзя изменить.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("income_type_not_found", "Вид поступления не найден.");
        }

        if (await HasDocumentDuplicateAsync(FinancialOperationKinds.Income, request.DocumentNumber, request.OperationDate, operation.Id, cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        var comment = NormalizeOptional(request.Comment);
        if (IncomeOperationMatches(operation, request.OperationDate, accountingMonth, amount, documentNumber, comment, garage.Id, incomeType.Id))
        {
            return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
        }

        await using var fundAssignmentLock = await incomeFundAssignmentService.AcquireUpdateLockAsync(cancellationToken);
        await using var cashBalanceLock = amount < operation.Amount
            ? await financeAvailableBalanceQuery.AcquireUpdateLockAsync(FinanceBalanceAccounts.Cash, cancellationToken)
            : null;
        var reductionAmount = MoneyMath.RoundMoney(operation.Amount - amount);
        if (reductionAmount > 0m)
        {
            var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
            if (reductionAmount > availableCashAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "cash_amount_insufficient",
                    $"Уменьшение поступления превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
            }
        }

        var assignmentResult = await incomeFundAssignmentService.UpdateAsync(
            operation,
            incomeType.DestinationFundId,
            incomeType.Name,
            amount,
            actorUserId,
            cancellationToken);
        if (!assignmentResult.Succeeded)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                assignmentResult.ErrorCode!,
                assignmentResult.ErrorMessage!);
        }

        var oldAllocationKey = new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value);
        var previousSnapshot = FormatIncomeOperationSnapshot(operation);
        var oldValues = new Dictionary<string, object?>
        {
            ["operationDate"] = operation.OperationDate,
            ["accountingMonth"] = operation.AccountingMonth,
            ["amount"] = operation.Amount,
            ["documentNumber"] = operation.DocumentNumber,
            ["comment"] = operation.Comment,
            ["garage"] = operation.Garage?.Number,
            ["incomeType"] = operation.IncomeType?.Name
        };
        var newValues = new Dictionary<string, object?>
        {
            ["operationDate"] = request.OperationDate,
            ["accountingMonth"] = accountingMonth,
            ["amount"] = amount,
            ["documentNumber"] = documentNumber,
            ["comment"] = comment,
            ["garage"] = garage.Number,
            ["incomeType"] = incomeType.Name
        };
        operation.OperationDate = request.OperationDate;
        operation.AccountingMonth = accountingMonth;
        operation.Amount = amount;
        operation.DocumentNumber = documentNumber;
        operation.Comment = comment;
        operation.GarageId = garage.Id;
        operation.Garage = garage;
        operation.IncomeTypeId = incomeType.Id;
        operation.IncomeType = incomeType;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [oldAllocationKey, new AccrualPaymentAllocationKey(garage.Id, incomeType.Id)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [oldAllocationKey, new AccrualPaymentAllocationKey(garage.Id, incomeType.Id)],
            actorUserId,
            "Изменение поступления",
            operation.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.income_updated", operation, FormatIncomeUpdatedAuditSummary(previousSnapshot, operation), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> UpdateExpenseAsync(Guid operationId, CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await financialOperationRepository.FindForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.OperationKind != FinancialOperationKinds.Expense)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_kind_mismatch", "Эта операция не является выплатой.");
        }

        if (operation.StaffMemberId is not null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_kind_mismatch", "Выплату сотруднику нельзя изменить как выплату поставщику.");
        }

        if (operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Отмененную операцию нельзя изменить.");
        }

        if (!request.SupplierId.HasValue)
        {
            return await UpdateEpisodicExpenseAsync(operation, request, actorUserId, cancellationToken);
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId.Value, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Услуга не найдена.");
        }

        var expensePaymentType = NormalizeExpensePaymentType(request.ExpensePaymentType);
        if (expensePaymentType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "expense_payment_type_invalid",
                "Тип выплаты должен быть «С чеком» или «Без чека».");
        }

        var expensePaymentSource = NormalizeExpensePaymentSource(request.ExpensePaymentSource, expensePaymentType);
        if (expensePaymentSource is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "expense_payment_source_invalid",
                "Источник выплаты должен быть «Банк» или «Касса».");
        }

        var isCashExpense = expensePaymentSource == ExpensePaymentSources.Cash;
        var allowNegativeFundBalance = !isCashExpense && request.ConfirmNegativeFundBalance;
        var configuredExpenseFundId = GetSupplierExpenseFundId(supplier);
        var configuredExpenseFund = GetSupplierExpenseFund(supplier);
        var supplierExpenseTypeValidation = ValidateSupplierExpenseTypeLinkForPayment(supplier, expenseType);
        if (supplierExpenseTypeValidation is not null)
        {
            return supplierExpenseTypeValidation;
        }

        var expenseFundId = configuredExpenseFundId;
        var expenseFund = configuredExpenseFund;
        if (request.ExpenseFundId.HasValue && request.ExpenseFundId != expenseFundId)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "supplier_expense_fund_mismatch",
                "Выплата должна использовать фонд настроенной услуги поставщика.");
        }

        var resolvedExpenseFundId = expenseFundId;
        if (await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, operation.Id, cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        var comment = NormalizeOptional(request.Comment);
        if (ExpenseOperationMatches(
            operation,
            request.OperationDate,
            accountingMonth,
            amount,
            documentNumber,
            comment,
            supplier.Id,
            expenseType.Id,
            expensePaymentType,
            expensePaymentSource,
            resolvedExpenseFundId))
        {
            return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
        }

        var wasCashExpense = IsCashExpense(operation);
        var hadExpenseFund = operation.ExpenseFundId.HasValue;
        await using var fundDisbursementLock = await expenseFundDisbursementService.AcquireUpdateLockAsync(cancellationToken);
        var balanceAccounts = (wasCashExpense ? FinanceBalanceAccounts.Cash : FinanceBalanceAccounts.Bank) |
            (isCashExpense ? FinanceBalanceAccounts.Cash : FinanceBalanceAccounts.Bank);
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            balanceAccounts,
            cancellationToken);
        var linkedAtomicAccrual = wasCashExpense || isCashExpense
            ? await supplierAccrualRepository.FindBySourceFinancialOperationForUpdateAsync(operation.Id, cancellationToken)
            : null;
        if (wasCashExpense && linkedAtomicAccrual is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "atomic_expense_accrual_not_found",
                "Связанное начисление эпизодической кассовой выплаты не найдено. Отмените операцию и создайте ее заново.");
        }

        if (isCashExpense)
        {
            var availableCashAmount = MoneyMath.RoundMoney(await CalculateAvailableCashAmountAsync(cancellationToken) + (wasCashExpense ? operation.Amount : 0m));
            if (amount > availableCashAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "cash_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
            }
        }
        else
        {
            var availableBankAmount = MoneyMath.RoundMoney(await CalculateAvailableBankAmountAsync(cancellationToken) + (wasCashExpense ? 0m : operation.Amount));
            if (amount > availableBankAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "bank_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток на банковском счете {MoneyFormatting.Format(availableBankAmount)}.");
            }
        }

        var previousSnapshot = FormatExpenseOperationSnapshot(operation);
        var oldValues = new Dictionary<string, object?>
        {
            ["operationDate"] = operation.OperationDate,
            ["accountingMonth"] = operation.AccountingMonth,
            ["amount"] = operation.Amount,
            ["documentNumber"] = operation.DocumentNumber,
            ["comment"] = operation.Comment,
            ["supplier"] = operation.Supplier?.Name,
            ["expenseType"] = operation.ExpenseType?.Name,
            ["expenseFund"] = operation.ExpenseFund?.Name,
            ["expensePaymentType"] = operation.ExpensePaymentType,
            ["expensePaymentSource"] = operation.ExpensePaymentSource
        };
        var newValues = new Dictionary<string, object?>
        {
            ["operationDate"] = request.OperationDate,
            ["accountingMonth"] = accountingMonth,
            ["amount"] = amount,
            ["documentNumber"] = documentNumber,
            ["comment"] = comment,
            ["supplier"] = supplier.Name,
            ["expenseType"] = expenseType.Name,
            ["expenseFund"] = expenseFund?.Name,
            ["expensePaymentType"] = expensePaymentType,
            ["expensePaymentSource"] = expensePaymentSource
        };
        operation.OperationDate = request.OperationDate;
        operation.AccountingMonth = accountingMonth;
        operation.Amount = amount;
        operation.ExpensePaymentType = expensePaymentType;
        operation.ExpensePaymentSource = expensePaymentSource;
        operation.CounterpartyName = null;
        operation.NegativeFundBalanceConfirmed = allowNegativeFundBalance;
        operation.DocumentNumber = documentNumber;
        operation.Comment = comment;
        operation.SupplierId = supplier.Id;
        operation.Supplier = supplier;
        operation.ExpenseTypeId = expenseType.Id;
        operation.ExpenseType = expenseType;
        operation.ExpenseFundId = expenseFundId;
        operation.ExpenseFund = expenseFund;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (isCashExpense)
        {
            if (linkedAtomicAccrual is null)
            {
                linkedAtomicAccrual = new SupplierAccrual
                {
                    SupplierId = supplier.Id,
                    Supplier = supplier,
                    ExpenseTypeId = expenseType.Id,
                    ExpenseType = expenseType,
                    ExpenseFundId = operation.ExpenseFundId,
                    ExpenseFund = operation.ExpenseFund,
                    SourceFinancialOperationId = operation.Id,
                    SourceFinancialOperation = operation,
                    AccountingMonth = accountingMonth,
                    Amount = amount,
                    Source = AccrualSources.Manual,
                    DocumentNumber = documentNumber,
                    Comment = comment
                };
                supplierAccrualRepository.Add(linkedAtomicAccrual);
            }
            else
            {
                linkedAtomicAccrual.SupplierId = supplier.Id;
                linkedAtomicAccrual.Supplier = supplier;
                linkedAtomicAccrual.ExpenseTypeId = expenseType.Id;
                linkedAtomicAccrual.ExpenseType = expenseType;
                linkedAtomicAccrual.ExpenseFundId = operation.ExpenseFundId;
                linkedAtomicAccrual.ExpenseFund = operation.ExpenseFund;
                linkedAtomicAccrual.AccountingMonth = accountingMonth;
                linkedAtomicAccrual.Amount = amount;
                linkedAtomicAccrual.DocumentNumber = documentNumber;
                linkedAtomicAccrual.Comment = comment;
                linkedAtomicAccrual.IsCanceled = false;
                linkedAtomicAccrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        else if (linkedAtomicAccrual is not null)
        {
            linkedAtomicAccrual.IsCanceled = true;
            linkedAtomicAccrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var fundDisbursementResult = hadExpenseFund
            ? await expenseFundDisbursementService.UpdateAsync(
                operation,
                resolvedExpenseFundId!.Value,
                supplier.Name,
                amount,
                actorUserId,
                allowNegativeFundBalance,
                cancellationToken)
            : await expenseFundDisbursementService.CreateAsync(
                operation,
                supplier.Name,
                actorUserId,
                allowNegativeFundBalance,
                cancellationToken);

        if (fundDisbursementResult is { Succeeded: false })
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                fundDisbursementResult.ErrorCode!,
                fundDisbursementResult.ErrorMessage!);
        }

        AddAudit(actorUserId, "finance.expense_updated", operation, FormatExpenseUpdatedAuditSummary(previousSnapshot, operation), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> CancelOperationAsync(Guid operationId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_cancel_reason_required", "Для отмены операции нужна причина.");
        }

        var operation = await financialOperationRepository.FindForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Финансовая операция уже отменена.");
        }

        var hasSupplierExpenseFund = operation.OperationKind == FinancialOperationKinds.Expense &&
            operation.SupplierId.HasValue &&
            operation.ExpenseFundId.HasValue;
        await using var fundAssignmentLock = operation.OperationKind == FinancialOperationKinds.Income
            ? await incomeFundAssignmentService.AcquireUpdateLockAsync(cancellationToken)
            : hasSupplierExpenseFund
                ? await expenseFundDisbursementService.AcquireUpdateLockAsync(cancellationToken)
            : null;
        var balanceAccounts = operation.OperationKind == FinancialOperationKinds.Income || IsCashExpense(operation)
            ? FinanceBalanceAccounts.Cash
            : FinanceBalanceAccounts.Bank;
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            balanceAccounts,
            cancellationToken);
        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
            if (operation.Amount > availableCashAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "cash_amount_insufficient",
                    $"Поступление нельзя отменить: доступный остаток в кассе составляет {MoneyFormatting.Format(availableCashAmount)}.");
            }
        }

        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            var assignmentResult = await incomeFundAssignmentService.CancelAsync(
                operation,
                reason,
                actorUserId,
                cancellationToken);
            if (!assignmentResult.Succeeded)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    assignmentResult.ErrorCode!,
                    assignmentResult.ErrorMessage!);
            }
        }
        else if (hasSupplierExpenseFund)
        {
            var disbursementResult = await expenseFundDisbursementService.CancelAsync(
                operation,
                reason,
                actorUserId,
                cancellationToken);
            if (!disbursementResult.Succeeded)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    disbursementResult.ErrorCode!,
                    disbursementResult.ErrorMessage!);
            }
        }

        SupplierAccrual? linkedAtomicAccrualToCancel = null;
        if (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId is not null && IsCashExpense(operation))
        {
            linkedAtomicAccrualToCancel = await supplierAccrualRepository.FindBySourceFinancialOperationForUpdateAsync(operation.Id, cancellationToken);
            if (linkedAtomicAccrualToCancel is null)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "atomic_expense_accrual_not_found",
                    "Связанное начисление эпизодической кассовой выплаты не найдено. Операция не отменена.");
            }
        }

        operation.IsCanceled = true;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.Comment = AppendCancelReason(operation.Comment, reason);
        if (linkedAtomicAccrualToCancel is not null)
        {
            linkedAtomicAccrualToCancel.IsCanceled = true;
            linkedAtomicAccrualToCancel.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
                cancellationToken);
            await RebuildPaymentAllocationsAsync(
                [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
                actorUserId,
                "Отмена поступления",
                operation.Id,
                cancellationToken);
        }
        AddAudit(actorUserId, "finance.operation_canceled", operation, FormatOperationCanceledAuditSummary(operation, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await financialOperationRepository.FindForUpdateAsync(operationId, cancellationToken);
        if (operation is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Финансовая операция не найдена.");
        }

        if (!operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_canceled", "Финансовая операция уже активна.");
        }

        if (await HasDocumentDuplicateAsync(operation.OperationKind, operation.DocumentNumber, operation.OperationDate, operation.Id, cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var hasSupplierExpenseFund = operation.OperationKind == FinancialOperationKinds.Expense &&
            operation.SupplierId.HasValue &&
            operation.ExpenseFundId.HasValue;
        await using var fundAssignmentLock = operation.OperationKind == FinancialOperationKinds.Income
            ? await incomeFundAssignmentService.AcquireUpdateLockAsync(cancellationToken)
            : hasSupplierExpenseFund
                ? await expenseFundDisbursementService.AcquireUpdateLockAsync(cancellationToken)
            : null;
        var balanceAccounts = operation.OperationKind == FinancialOperationKinds.Income || IsCashExpense(operation)
            ? FinanceBalanceAccounts.Cash
            : FinanceBalanceAccounts.Bank;
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(
            balanceAccounts,
            cancellationToken);

        SupplierAccrual? linkedAtomicAccrualToRestore = null;
        if (operation.OperationKind == FinancialOperationKinds.Expense)
        {
            if (operation.StaffMemberId is not null)
            {
                var paidThisMonth = await financialOperationRepository.GetStaffExpenseTotalAsync(
                    operation.StaffMemberId.Value,
                    operation.AccountingMonth,
                    cancellationToken);
                var adjustmentTotals = await staffSalaryAdjustmentRepository.GetTotalsAsync(
                    operation.StaffMemberId.Value,
                    operation.AccountingMonth,
                    cancellationToken);
                var availableStaffAmount = MoneyMath.RoundMoney(
                    (operation.StaffMember?.Rate ?? 0m) +
                    adjustmentTotals.BonusAmount -
                    adjustmentTotals.PenaltyAmount -
                    paidThisMonth);
                if (operation.Amount > availableStaffAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {MoneyFormatting.Format(availableStaffAmount)}.");
                }
            }

            if (IsCashExpense(operation))
            {
                if (operation.SupplierId is not null)
                {
                    linkedAtomicAccrualToRestore = await supplierAccrualRepository.FindBySourceFinancialOperationForUpdateAsync(operation.Id, cancellationToken);
                    if (linkedAtomicAccrualToRestore is null)
                    {
                        return FinanceResult<FinancialOperationDto>.Failure(
                            "atomic_expense_accrual_not_found",
                            "Связанное начисление эпизодической кассовой выплаты не найдено. Операция не восстановлена.");
                    }
                }

                var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
                if (operation.Amount > availableCashAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure(
                        "cash_amount_insufficient",
                        $"Сумма выплаты превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
                }
            }
            else
            {
                var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
                if (operation.Amount > availableBankAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure(
                        "bank_amount_insufficient",
                        $"Сумма выплаты превышает доступный остаток на банковском счете {MoneyFormatting.Format(availableBankAmount)}.");
                }
            }
        }

        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            var assignmentResult = await incomeFundAssignmentService.RestoreAsync(
                operation,
                actorUserId,
                cancellationToken);
            if (!assignmentResult.Succeeded)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    assignmentResult.ErrorCode!,
                    assignmentResult.ErrorMessage!);
            }
        }
        else if (hasSupplierExpenseFund)
        {
            var disbursementResult = await expenseFundDisbursementService.RestoreAsync(
                operation,
                actorUserId,
                cancellationToken);
            if (!disbursementResult.Succeeded)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    disbursementResult.ErrorCode!,
                    disbursementResult.ErrorMessage!);
            }
        }

        operation.IsCanceled = false;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (linkedAtomicAccrualToRestore is not null)
        {
            linkedAtomicAccrualToRestore.IsCanceled = false;
            linkedAtomicAccrualToRestore.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
                cancellationToken);
            await RebuildPaymentAllocationsAsync(
                [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
                actorUserId,
                "Восстановление поступления",
                operation.Id,
                cancellationToken);
        }
        AddAudit(actorUserId, "finance.operation_restored", operation, FormatOperationRestoredAuditSummary(operation));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<AccrualDto>> CancelAccrualAsync(Guid accrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_cancel_reason_required", "Для отмены начисления нужна причина.");
        }

        var accrual = await accrualRepository.FindForUpdateAsync(accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_already_canceled", "Начисление уже отменено.");
        }

        accrual.IsCanceled = true;
        accrual.Comment = AppendCancelReason(accrual.Comment, reason);
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Отмена начисления",
            accrual.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.accrual_canceled", accrual, FormatAccrualCanceledAuditSummary(accrual, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> RestoreAccrualAsync(Guid accrualId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var accrual = await accrualRepository.FindForUpdateAsync(accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
        }

        if (!accrual.IsCanceled)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_canceled", "Начисление уже активно.");
        }

        var duplicateExists = accrual.IrregularPaymentId.HasValue
            ? await accrualRepository.ActiveIrregularDuplicateExistsAsync(
                accrual.Id,
                accrual.GarageId,
                accrual.IrregularPaymentId.Value,
                accrual.AccountingMonth,
                cancellationToken)
            : accrual.Basis is not null
                ? false
            : accrual.FeeCampaignId.HasValue
                ? await accrualRepository.ActiveFeeCampaignDuplicateExistsAsync(
                    accrual.Id,
                    accrual.GarageId,
                    accrual.FeeCampaignId.Value,
                    accrual.AccountingMonth,
                    cancellationToken)
            : await accrualRepository.ActiveDuplicateExistsAsync(
                accrual.Id,
                accrual.GarageId,
                accrual.IncomeTypeId,
                accrual.AccountingMonth,
                accrual.AccountingYear,
                accrual.Source,
                cancellationToken);
        if (duplicateExists)
        {
            var duplicateMessage = accrual.Source == AccrualSources.Regular && accrual.AccountingYear.HasValue
                ? $"Регулярное годовое начисление за {accrual.AccountingYear.Value} год уже активно."
                : "Такое начисление за месяц уже внесено.";
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", duplicateMessage);
        }

        accrual.IsCanceled = false;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Восстановление начисления",
            accrual.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.accrual_restored", accrual, FormatAccrualRestoredAuditSummary(accrual));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> CreateIrregularAccrualAsync(
        CreateIrregularAccrualRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для начисления не найден.");
        }

        var basis = NormalizeOptional(request.Basis);
        if (basis is null)
        {
            return FinanceResult<AccrualDto>.Failure("irregular_accrual_basis_required", "Укажите основание начисления.");
        }

        IrregularPayment? irregularPayment = null;
        if (request.IrregularPaymentId.HasValue)
        {
            irregularPayment = await irregularPaymentRepository.FindActiveAsync(request.IrregularPaymentId.Value, cancellationToken);
            if (irregularPayment is null || !irregularPayment.IsActive)
            {
                return FinanceResult<AccrualDto>.Failure("irregular_payment_not_found", "Активное основание из справочника не найдено.");
            }

            basis = irregularPayment.Name;
        }

        var incomeType = await incomeTypeRepository.FindFirstActiveByCodeAsync(OtherPaymentsIncomeTypeCode, cancellationToken);
        if (incomeType is null || !incomeType.IsSystem || !incomeType.DestinationFundId.HasValue)
        {
            return FinanceResult<AccrualDto>.Failure(
                "other_payments_destination_not_configured",
                "Системное назначение «Прочие оплаты» не настроено или не связано с фондом.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (irregularPayment is not null &&
            await accrualRepository.ActiveIrregularDuplicateExistsAsync(
                null,
                garage.Id,
                irregularPayment.Id,
                month,
                cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure(
                "accrual_duplicate",
                "Этот нерегулярный платёж за выбранный месяц уже начислен гаражу.");
        }

        var amount = MoneyMath.RoundMoney(irregularPayment?.Amount ?? request.Amount);
        if (amount <= 0)
        {
            return FinanceResult<AccrualDto>.Failure(
                "irregular_payment_amount_invalid",
                "Сумма нерегулярного платежа должна быть больше нуля.");
        }

        var dueDates = AccrualDueDates.ForIncomeType(month, incomeType.Code, setting: null);
        var accrual = new Accrual
        {
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            IrregularPaymentId = irregularPayment?.Id,
            IrregularPayment = irregularPayment,
            Basis = basis,
            AccountingMonth = month,
            DueDate = dueDates.DueDate,
            OverdueFromDate = dueDates.OverdueFromDate,
            Amount = amount,
            Source = AccrualSources.Manual,
            Comment = NormalizeOptional(request.Comment)
        };

        accrualRepository.Add(accrual);
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Создание нерегулярного начисления",
            accrual.Id,
            cancellationToken);
        AddAudit(
            actorUserId,
            "finance.irregular_accrual_created",
            accrual,
            $"Создано разовое начисление с основанием «{basis}» {MoneyFormatting.Format(accrual.Amount)} по гаражу {garage.Number} за {month:MM.yyyy}; назначение «{incomeType.Name}», фонд {incomeType.DestinationFundId}.");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        if (source is not AccrualSources.Manual and not AccrualSources.Regular)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_source_invalid", "Источник начисления должен быть manual или regular.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для начисления не найден.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<AccrualDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var accountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month);
        if (await accrualRepository.ActiveDuplicateExistsAsync(null, garage.Id, incomeType.Id, month, accountingYear, source, cancellationToken))
        {
            var duplicateMessage = source == AccrualSources.Regular && accountingYear.HasValue
                ? $"Регулярное годовое начисление за {accountingYear.Value} год уже внесено."
                : "Такое начисление за месяц уже внесено.";
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", duplicateMessage);
        }

        var dueDateSetting = accountingYear.HasValue
            ? SelectChargeServiceSettingForDueDates(
                await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(
                    incomeType.Id,
                    tariffId: null,
                    month,
                    cancellationToken),
                month)
            : null;
        var dueDates = AccrualDueDates.ForGarage(month, incomeType.Code, dueDateSetting, GetGarageRegistrationDate(garage));
        var accrual = new Accrual
        {
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            AccountingMonth = month,
            AccountingYear = accountingYear,
            DueDate = dueDates.DueDate,
            OverdueFromDate = dueDates.OverdueFromDate,
            Amount = MoneyMath.RoundMoney(request.Amount),
            Source = source,
            Comment = NormalizeOptional(request.Comment)
        };

        accrualRepository.Add(accrual);
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Создание начисления",
            accrual.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.accrual_created", accrual, FormatAccrualCreatedAuditSummary(accrual));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> CreateDebtTransferAsync(CreateDebtTransferRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var sourceMonth = MonthPeriod.Normalize(request.SourceMonth);
        var targetMonth = MonthPeriod.Normalize(request.TargetMonth);
        if (sourceMonth == targetMonth)
        {
            return FinanceResult<AccrualDto>.Failure("debt_transfer_months_equal", "Месяц переноса должен отличаться от исходного месяца.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (amount <= 0)
        {
            return FinanceResult<AccrualDto>.Failure("debt_transfer_amount_invalid", "Сумма переноса должна быть больше нуля.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для переноса задолженности не найден.");
        }

        var incomeType = await GetOrCreateDebtTransferIncomeTypeAsync(cancellationToken);
        var comment = BuildDebtTransferComment(sourceMonth, targetMonth, request.Comment);
        var accrual = await accrualRepository.FindActiveForUpdateAsync(
            garage.Id,
            incomeType.Id,
            targetMonth,
            AccrualSources.DebtTransfer,
            cancellationToken);

        if (accrual is null)
        {
            var dueDates = AccrualDueDates.ForChargeService(targetMonth, null);
            accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                AccountingMonth = targetMonth,
                AccountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, targetMonth),
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                Source = AccrualSources.DebtTransfer,
                Comment = comment
            };
            accrualRepository.Add(accrual);
            await using var debtTransferCreateAllocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
                cancellationToken);
            await RebuildPaymentAllocationsAsync(
                [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
                actorUserId,
                "Перенос задолженности",
                accrual.Id,
                cancellationToken);
            AddAudit(actorUserId, "finance.debt_transfer_created", accrual, FormatDebtTransferCreatedAuditSummary(accrual, sourceMonth, targetMonth));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return FinanceResult<AccrualDto>.Success(ToDto(accrual));
        }

        var before = AccrualAuditSnapshot.From(accrual);
        var oldValues = new Dictionary<string, object?>
        {
            ["garage"] = accrual.Garage.Number,
            ["incomeType"] = accrual.IncomeType.Name,
            ["accountingMonth"] = accrual.AccountingMonth,
            ["amount"] = accrual.Amount,
            ["source"] = accrual.Source,
            ["comment"] = accrual.Comment
        };

        accrual.Amount = MoneyMath.RoundMoney(accrual.Amount + amount);
        accrual.Comment = AppendDebtTransferComment(accrual.Comment, comment);
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var debtTransferUpdateAllocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Изменение переноса задолженности",
            accrual.Id,
            cancellationToken);

        var newValues = new Dictionary<string, object?>
        {
            ["garage"] = accrual.Garage.Number,
            ["incomeType"] = accrual.IncomeType.Name,
            ["accountingMonth"] = accrual.AccountingMonth,
            ["amount"] = accrual.Amount,
            ["source"] = accrual.Source,
            ["comment"] = accrual.Comment
        };
        AddAudit(actorUserId, "finance.debt_transfer_updated", accrual, FormatDebtTransferUpdatedAuditSummary(before, accrual, sourceMonth, targetMonth, amount), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> UpdateAccrualAsync(Guid accrualId, CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        if (source is not AccrualSources.Manual and not AccrualSources.Regular)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_source_invalid", "Источник начисления должен быть manual или regular.");
        }

        var accrual = await accrualRepository.FindForUpdateAsync(accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
        }

        if (accrual.IrregularPaymentId.HasValue || accrual.Basis is not null)
        {
            return FinanceResult<AccrualDto>.Failure(
                "irregular_accrual_edit_not_supported",
                "Разовое начисление нельзя переназначить как обычное. Отмените его и создайте заново.");
        }

        if (accrual.FeeCampaignId.HasValue)
        {
            return FinanceResult<AccrualDto>.Failure(
                "fee_campaign_accrual_edit_not_supported",
                "Начисление объявленного сбора нельзя переназначить как обычное. Отмените его и сформируйте сбор заново.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_already_canceled", "Отмененное начисление нельзя изменить.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для начисления не найден.");
        }

        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<AccrualDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var accountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month);
        if (await accrualRepository.ActiveDuplicateExistsAsync(
            accrual.Id,
            garage.Id,
            incomeType.Id,
            month,
            accountingYear,
            source,
            cancellationToken))
        {
            var duplicateMessage = source == AccrualSources.Regular && accountingYear.HasValue
                ? $"Регулярное годовое начисление за {accountingYear.Value} год уже внесено."
                : "Такое начисление за месяц уже внесено.";
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", duplicateMessage);
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        var comment = NormalizeOptional(request.Comment);
        if (!accrual.DueDateNeedsReview && AccrualMatches(accrual, garage.Id, incomeType.Id, month, accountingYear, amount, source, comment))
        {
            return FinanceResult<AccrualDto>.Success(ToDto(accrual));
        }

        var oldAllocationKey = new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId);
        var before = AccrualAuditSnapshot.From(accrual);
        var oldValues = new Dictionary<string, object?>
        {
            ["garage"] = accrual.Garage.Number,
            ["incomeType"] = accrual.IncomeType.Name,
            ["accountingMonth"] = accrual.AccountingMonth,
            ["accountingYear"] = accrual.AccountingYear,
            ["amount"] = accrual.Amount,
            ["source"] = accrual.Source,
            ["comment"] = accrual.Comment,
            ["dueDateNeedsReview"] = accrual.DueDateNeedsReview
        };
        var newValues = new Dictionary<string, object?>
        {
            ["garage"] = garage.Number,
            ["incomeType"] = incomeType.Name,
            ["accountingMonth"] = month,
            ["accountingYear"] = accountingYear,
            ["amount"] = amount,
            ["source"] = source,
            ["comment"] = comment,
            ["dueDateNeedsReview"] = false
        };

        accrual.GarageId = garage.Id;
        accrual.Garage = garage;
        accrual.IncomeTypeId = incomeType.Id;
        accrual.IncomeType = incomeType;
        accrual.AccountingMonth = month;
        accrual.AccountingYear = accountingYear;
        var dueDateSetting = accountingYear.HasValue
            ? SelectChargeServiceSettingForDueDates(
                await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(
                    incomeType.Id,
                    tariffId: null,
                    month,
                    cancellationToken),
                month)
            : null;
        var updatedDueDates = AccrualDueDates.ForGarage(month, incomeType.Code, dueDateSetting, GetGarageRegistrationDate(garage));
        accrual.DueDate = updatedDueDates.DueDate;
        accrual.OverdueFromDate = updatedDueDates.OverdueFromDate;
        accrual.DueDateNeedsReview = false;
        accrual.DueDateReviewReason = null;
        accrual.Amount = amount;
        accrual.Source = source;
        accrual.Comment = comment;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [oldAllocationKey, new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [oldAllocationKey, new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)],
            actorUserId,
            "Изменение начисления",
            accrual.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.accrual_updated", accrual, FormatAccrualUpdatedAuditSummary(before, accrual), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<SupplierAccrualDto>> CreateSupplierAccrualAsync(CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        if (source is not AccrualSources.Manual and not AccrualSources.Regular)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_source_invalid", "Источник начисления поставщику должен быть manual или regular.");
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_not_found", "Поставщик для начисления не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("expense_type_not_found", "Вид начисления поставщику не найден.");
        }
        var supplierExpenseTypeValidation = ValidateSupplierExpenseTypeLink(supplier, expenseType);
        if (supplierExpenseTypeValidation is not null)
        {
            return supplierExpenseTypeValidation;
        }
        var expenseFund = GetSupplierExpenseFund(supplier)!;

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        if (await supplierAccrualRepository.ActiveDuplicateExistsAsync(null, supplier.Id, expenseType.Id, month, source, documentNumber, cancellationToken))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_duplicate", "Такое начисление поставщику за месяц уже внесено.");
        }

        var accrual = new SupplierAccrual
        {
            SupplierId = supplier.Id,
            Supplier = supplier,
            ExpenseTypeId = expenseType.Id,
            ExpenseType = expenseType,
            ExpenseFundId = expenseFund.Id,
            ExpenseFund = expenseFund,
            AccountingMonth = month,
            Amount = MoneyMath.RoundMoney(request.Amount),
            Source = source,
            DocumentNumber = documentNumber,
            Comment = NormalizeOptional(request.Comment)
        };

        supplierAccrualRepository.Add(accrual);
        AddAudit(actorUserId, "finance.supplier_accrual_created", accrual, FormatSupplierAccrualCreatedAuditSummary(accrual));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<SupplierAccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<SupplierAccrualDto>> UpdateSupplierAccrualAsync(Guid supplierAccrualId, CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        if (source is not AccrualSources.Manual and not AccrualSources.Regular)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_source_invalid", "Источник начисления поставщику должен быть manual или regular.");
        }

        var accrual = await supplierAccrualRepository.FindForUpdateAsync(supplierAccrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_found", "Начисление поставщику не найдено.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_already_canceled", "Отмененное начисление поставщику нельзя изменить.");
        }

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_not_found", "Поставщик для начисления не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("expense_type_not_found", "Вид начисления поставщику не найден.");
        }
        var supplierExpenseTypeValidation = ValidateSupplierExpenseTypeLink(supplier, expenseType);
        if (supplierExpenseTypeValidation is not null)
        {
            return supplierExpenseTypeValidation;
        }
        var expenseFund = GetSupplierExpenseFund(supplier)!;

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        if (await supplierAccrualRepository.ActiveDuplicateExistsAsync(accrual.Id, supplier.Id, expenseType.Id, month, source, documentNumber, cancellationToken))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_duplicate", "Такое начисление поставщику за месяц уже внесено.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        var comment = NormalizeOptional(request.Comment);
        if (SupplierAccrualMatches(accrual, supplier.Id, expenseType.Id, month, amount, source, documentNumber, comment))
        {
            return FinanceResult<SupplierAccrualDto>.Success(ToDto(accrual));
        }

        var before = SupplierAccrualAuditSnapshot.From(accrual);
        var oldValues = new Dictionary<string, object?>
        {
            ["supplier"] = accrual.Supplier.Name,
            ["expenseType"] = accrual.ExpenseType.Name,
            ["expenseFund"] = accrual.ExpenseFund?.Name,
            ["accountingMonth"] = accrual.AccountingMonth,
            ["amount"] = accrual.Amount,
            ["source"] = accrual.Source,
            ["documentNumber"] = accrual.DocumentNumber,
            ["comment"] = accrual.Comment
        };
        var newValues = new Dictionary<string, object?>
        {
            ["supplier"] = supplier.Name,
            ["expenseType"] = expenseType.Name,
            ["expenseFund"] = expenseFund.Name,
            ["accountingMonth"] = month,
            ["amount"] = amount,
            ["source"] = source,
            ["documentNumber"] = documentNumber,
            ["comment"] = comment
        };

        accrual.SupplierId = supplier.Id;
        accrual.Supplier = supplier;
        accrual.ExpenseTypeId = expenseType.Id;
        accrual.ExpenseType = expenseType;
        accrual.ExpenseFundId = expenseFund.Id;
        accrual.ExpenseFund = expenseFund;
        accrual.AccountingMonth = month;
        accrual.Amount = amount;
        accrual.Source = source;
        accrual.DocumentNumber = documentNumber;
        accrual.Comment = comment;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "finance.supplier_accrual_updated", accrual, FormatSupplierAccrualUpdatedAuditSummary(before, accrual), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<SupplierAccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<SupplierAccrualDto>> CancelSupplierAccrualAsync(Guid supplierAccrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_cancel_reason_required", "Для отмены начисления поставщику нужна причина.");
        }

        var accrual = await supplierAccrualRepository.FindForUpdateAsync(supplierAccrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_found", "Начисление поставщику не найдено.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_already_canceled", "Начисление поставщику уже отменено.");
        }

        accrual.IsCanceled = true;
        accrual.Comment = AppendCancelReason(accrual.Comment, reason);
        AddAudit(actorUserId, "finance.supplier_accrual_canceled", accrual, FormatSupplierAccrualCanceledAuditSummary(accrual, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<SupplierAccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<SupplierAccrualDto>> RestoreSupplierAccrualAsync(Guid supplierAccrualId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var accrual = await supplierAccrualRepository.FindForUpdateAsync(supplierAccrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_found", "Начисление поставщику не найдено.");
        }

        if (!accrual.IsCanceled)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_canceled", "Начисление поставщику уже активно.");
        }

        if (await supplierAccrualRepository.ActiveDuplicateExistsAsync(
            accrual.Id,
            accrual.SupplierId,
            accrual.ExpenseTypeId,
            accrual.AccountingMonth,
            accrual.Source,
            accrual.DocumentNumber,
            cancellationToken))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_duplicate", "Такое начисление поставщику за месяц уже внесено.");
        }

        accrual.IsCanceled = false;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "finance.supplier_accrual_restored", accrual, FormatSupplierAccrualRestoredAuditSummary(accrual));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<SupplierAccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<RegularAccrualGenerationResultDto>> GenerateRegularAccrualsAsync(GenerateRegularAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var incomeType = await incomeTypeRepository.FindActiveAsync(request.IncomeTypeId, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var tariff = await tariffRepository.FindActiveAsync(request.TariffId, cancellationToken);
        if (tariff is null)
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("tariff_not_found", "Тариф для регулярного начисления не найден.");
        }

        if (tariff.EffectiveFrom > month.AddMonths(1).AddDays(-1))
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("tariff_not_effective", "Тариф еще не действует в выбранном месяце.");
        }

        var matchingSetting = SelectChargeServiceSettingForDueDates(
            await chargeServiceSettingRepository.GetActiveRegularForDueDatesAsync(
                incomeType.Id,
                tariff.Id,
                month,
                cancellationToken),
            month);
        if (matchingSetting is null && !IsIncomeTypeCompatibleWithTariff(incomeType.Code, tariff.CalculationBase))
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure(
                "regular_accrual_tariff_mismatch",
                "Выбранный тариф не подходит для этого вида регулярного начисления.");
        }

        var calculationSegments = BuildRegularAccrualSegments(month, matchingSetting, tariff);
        var useTieredElectricity = calculationSegments.Any(segment => segment.Tiers.Count > 0);
        var accountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month);
        await using var generationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(Guid.Empty, incomeType.Id)],
            cancellationToken);

        var existingAccrualCount = accountingYear.HasValue
            ? await accrualRepository.CountActiveAnnualRegularForGenerationAsync(
                incomeType.Id,
                accountingYear.Value,
                cancellationToken)
            : await accrualRepository.CountActiveForGenerationAsync(
                incomeType.Id,
                month,
                AccrualSources.Regular,
                cancellationToken);
        if (existingAccrualCount > 0)
        {
            var activeGarageCount = await garageRepository.CountActiveAsync(cancellationToken);
            if (activeGarageCount > 0 && existingAccrualCount >= activeGarageCount)
            {
                var periodLabel = accountingYear.HasValue ? $"за {accountingYear.Value} год" : $"за {month:MM.yyyy}";
                return FinanceResult<RegularAccrualGenerationResultDto>.Failure(
                    "regular_accruals_empty",
                    $"Регулярные начисления {periodLabel} уже сформированы для всех активных гаражей ({activeGarageCount}).");
            }
        }

        var garages = await garageRepository.GetAllActiveWithOwnerAsync(cancellationToken);
        IReadOnlySet<Guid> existingGarageIds = existingAccrualCount == 0
            ? new HashSet<Guid>()
            : accountingYear.HasValue
                ? await accrualRepository.GetActiveAnnualRegularGarageIdsAsync(
                    incomeType.Id,
                    accountingYear.Value,
                    cancellationToken)
                : await accrualRepository.GetActiveGarageIdsAsync(
                    incomeType.Id,
                    month,
                    AccrualSources.Regular,
                    cancellationToken);
        var pendingGarageIds = garages
            .Where(garage => !existingGarageIds.Contains(garage.Id))
            .Select(garage => garage.Id)
            .ToArray();
        var meteredCalculationBase = calculationSegments
            .Select(segment => segment.CalculationBase)
            .FirstOrDefault(calculationBase => calculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity);
        var meterKind = meteredCalculationBase is not null && MeterKinds.IsValid(matchingSetting?.MeterKind)
            ? matchingSetting!.MeterKind!
            : meteredCalculationBase switch
            {
                TariffCalculationBases.MeterWater => MeterKinds.Water,
                TariffCalculationBases.MeterElectricity => MeterKinds.Electricity,
                _ => null
            };
        var meterReadings = meterKind is null
            ? new Dictionary<Guid, MeterReading>()
            : await meterReadingRepository.GetActiveByGarageIdsAsync(pendingGarageIds, meterKind, month, cancellationToken);
        var created = new List<AccrualDto>();
        var skipped = new List<string>();

        foreach (var garage in garages)
        {
            if (existingGarageIds.Contains(garage.Id))
            {
                var periodLabel = accountingYear.HasValue ? $" за {accountingYear.Value} год" : null;
                skipped.Add($"Гараж {garage.Number}: регулярное начисление{periodLabel} уже есть.");
                continue;
            }

            meterReadings.TryGetValue(garage.Id, out var meterReading);
            var amountResult = RegularAccrualCalculator.Calculate(garage, month, meterReading, calculationSegments);
            if (!amountResult.Succeeded)
            {
                skipped.Add($"Гараж {garage.Number}: {amountResult.ErrorMessage}");
                continue;
            }

            var amount = amountResult.Amount;
            if (amount <= 0)
            {
                skipped.Add($"Гараж {garage.Number}: сумма начисления равна нулю.");
                continue;
            }

            var dueDates = AccrualDueDates.ForGarage(month, incomeType.Code, matchingSetting, GetGarageRegistrationDate(garage));

            var accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                TariffId = tariff.Id,
                Tariff = tariff,
                AccountingMonth = month,
                AccountingYear = accountingYear,
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                RequiresMeterReading = amountResult.Details!.RequiresMeter,
                CalculationMeterKind = amountResult.Details.RequiresMeter ? meterKind : null,
                CalculationDetailsJson = RegularAccrualCalculator.Serialize(amountResult.Details),
                Source = AccrualSources.Regular,
                Comment = BuildRegularAccrualComment(tariff, request.Comment, useTieredElectricity)
            };
            accrualRepository.Add(accrual);
            created.Add(ToDto(accrual));
        }

        if (created.Count == 0)
        {
            var visibleReasons = skipped.Count == 0
                ? "Нет активных гаражей для начисления."
                : string.Join(" ", skipped.Take(10));
            var remainingReasonCount = Math.Max(0, skipped.Count - 10);
            var remainingReasons = remainingReasonCount > 0 ? $" Еще причин: {remainingReasonCount}." : null;
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure(
                "regular_accruals_empty",
                $"Не создано ни одного начисления. Причины: {visibleReasons}{remainingReasons}");
        }

        AddAudit(
            actorUserId,
            "finance.regular_accruals_generated",
            "accrual",
            Guid.NewGuid(),
            FormatRegularAccrualGenerationAuditSummary(month, incomeType, tariff, created, skipped, useTieredElectricity),
            relatedAccountingMonth: month,
            relatedDocumentNumber: $"{incomeType.Name} {month:MM.yyyy}",
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "accrual",
                ["incomeTypeId"] = incomeType.Id,
                ["incomeTypeName"] = incomeType.Name,
                ["tariffId"] = tariff.Id,
                ["tariffName"] = tariff.Name,
                ["createdCount"] = created.Count,
                ["skippedCount"] = skipped.Count,
                ["totalAmount"] = created.Sum(item => item.Amount)
            });
        await RebuildPaymentAllocationsAsync(
            created.Select(item => new AccrualPaymentAllocationKey(item.GarageId, item.IncomeTypeId)).ToArray(),
            actorUserId,
            "Формирование регулярных начислений",
            incomeType.Id,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = new RegularAccrualGenerationResultDto(
            month,
            incomeType.Id,
            incomeType.Name,
            tariff.Id,
            tariff.Name,
            tariff.CalculationBase,
            created.Count,
            skipped.Count,
            created.Sum(item => item.Amount),
            created,
            skipped);
        return FinanceResult<RegularAccrualGenerationResultDto>.Success(result);
    }

    public async Task<FinanceResult<RegularCatalogAccrualGenerationResultDto>> GenerateRegularCatalogAccrualsAsync(GenerateRegularCatalogAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var settings = await chargeServiceSettingRepository.GetActiveRegularAsync(month, cancellationToken);
        if (settings.Count == 0)
        {
            return FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure(
                "regular_catalog_empty",
                "Нет активных регулярных услуг. Откройте раздел «Тарифы и сборы», добавьте регулярную услугу и свяжите ее с видом начисления и тарифом.");
        }

        var serviceResults = new List<RegularAccrualGenerationResultDto>();
        var skippedServices = new List<string>();
        foreach (var setting in settings)
        {
            if (!IsChargeServiceDueForMonth(setting, month))
            {
                skippedServices.Add($"{setting.Name}: услуга не начисляется в {month:MM.yyyy} по своей периодичности.");
                continue;
            }

            var tariff = SelectTariffForMonth(setting, month);
            if (!setting.IncomeTypeId.HasValue || tariff is null)
            {
                skippedServices.Add($"{setting.Name}: не указан вид начисления или тариф.");
                continue;
            }

            var comment = BuildRegularCatalogAccrualComment(setting.Name, request.Comment);
            var serviceResult = await GenerateRegularAccrualsAsync(
                new GenerateRegularAccrualsRequest(setting.IncomeTypeId.Value, tariff.Id, month, comment),
                actorUserId,
                cancellationToken);
            if (!serviceResult.Succeeded)
            {
                skippedServices.Add($"{setting.Name}: {serviceResult.ErrorMessage}");
                continue;
            }

            serviceResults.Add(serviceResult.Value!);
        }

        var createdCount = serviceResults.Sum(result => result.CreatedCount);
        if (createdCount == 0)
        {
            var details = skippedServices.Count == 0
                ? null
                : $" Причины: {string.Join(" ", skippedServices)}";
            return FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure(
                "regular_catalog_accruals_empty",
                $"По каталогу услуг не создано ни одного начисления.{details}");
        }

        var skippedCount = serviceResults.Sum(result => result.SkippedCount) + skippedServices.Count;
        var totalAmount = serviceResults.Sum(result => result.TotalAmount);
        AddAudit(
            actorUserId,
            "finance.regular_catalog_accruals_generated",
            "accrual",
            Guid.NewGuid(),
            $"Сформированы регулярные начисления по каталогу услуг за {month:MM.yyyy}: услуг обработано {serviceResults.Count}, создано {createdCount}, на сумму {MoneyFormatting.Format(totalAmount)}, пропущено {skippedCount}.",
            relatedAccountingMonth: month,
            relatedDocumentNumber: $"Каталог услуг {month:MM.yyyy}",
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "accrual",
                ["serviceCount"] = serviceResults.Count,
                ["createdCount"] = createdCount,
                ["skippedCount"] = skippedCount,
                ["totalAmount"] = totalAmount
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = new RegularCatalogAccrualGenerationResultDto(
            month,
            serviceResults.Count,
            createdCount,
            skippedCount,
            totalAmount,
            serviceResults,
            skippedServices);
        return FinanceResult<RegularCatalogAccrualGenerationResultDto>.Success(result);
    }

    public async Task<RegularAccrualAutomationPreviewDto> PreviewRegularAccrualAutomationAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(businessDate);
        var regularServices = await chargeServiceSettingRepository.GetActiveRegularAsync(month, cancellationToken);
        var dueServices = regularServices.Where(setting => IsChargeServiceDueForMonth(setting, month)).ToList();
        var configuredDueServiceCount = dueServices.Count(setting => setting.IncomeTypeId.HasValue && setting.TariffId.HasValue);
        var feeCampaigns = await feeCampaignRepository.GetActiveAccrualCandidatesAsync(
            month,
            MaxAutomaticFeeCampaigns + 1,
            cancellationToken);
        var activeGarageCount = await garageRepository.CountActiveAsync(cancellationToken);
        var warnings = new List<string>();
        var incompleteServiceCount = dueServices.Count - configuredDueServiceCount;
        if (incompleteServiceCount > 0)
        {
            warnings.Add($"У {incompleteServiceCount} регулярных услуг не указан вид начисления или тариф; они будут пропущены.");
        }

        if (feeCampaigns.Count > MaxAutomaticFeeCampaigns)
        {
            warnings.Add($"Найдено больше {MaxAutomaticFeeCampaigns} действующих сборов; автоматическое начисление потребует сначала архивировать завершённые сборы.");
        }

        if (activeGarageCount == 0)
        {
            warnings.Add("Нет активных гаражей: новые начисления созданы не будут.");
        }

        var boundedCampaignCount = Math.Min(feeCampaigns.Count, MaxAutomaticFeeCampaigns);
        var maximumGarageChecks = (long)activeGarageCount * (configuredDueServiceCount + boundedCampaignCount);
        return new RegularAccrualAutomationPreviewDto(
            month,
            activeGarageCount,
            regularServices.Count,
            configuredDueServiceCount,
            feeCampaigns.Count,
            (int)Math.Min(maximumGarageChecks, int.MaxValue),
            warnings);
    }

    public async Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> GenerateFeeCampaignAccrualsAsync(GenerateFeeCampaignAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var campaign = await feeCampaignRepository.FindActiveForAccrualGenerationAsync(request.FeeCampaignId, cancellationToken);
        if (campaign is null)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }
        if (campaign.ClosedAtUtc.HasValue)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_closed", "Сбор закрыт: новые начисления создавать нельзя.");
        }

        var incomeType = await incomeTypeRepository.FindFirstActiveByCodeAsync(OtherIncomeIncomeTypeCode, cancellationToken);
        if (incomeType is null || !incomeType.IsSystem || !incomeType.DestinationFundId.HasValue)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure(
                "other_income_destination_not_configured",
                "Системное назначение «Прочие доходы» не настроено или не связано с фондом.");
        }

        if (MonthPeriod.Normalize(campaign.StartsOn) > month)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_not_started", "Сбор еще не действует в выбранном месяце.");
        }

        if (campaign.EndsOn.HasValue && MonthPeriod.Normalize(campaign.EndsOn.Value) < month)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_finished", "Сбор уже завершен в выбранном месяце.");
        }

        var amount = MoneyMath.RoundMoney(campaign.ContributionAmount);
        if (amount <= 0m)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_contribution_amount_invalid", "Сумма взноса по сбору должна быть больше нуля для начисления.");
        }

        var dueDates = AccrualDueDates.ForFeeCampaign(month, campaign.EndsOn, campaign.OverdueGraceDays);

        IReadOnlyList<Garage> garages = campaign.AppliesToAllGarages
            ? await garageRepository.GetAllActiveWithOwnerAsync(cancellationToken)
            : campaign.ParticipantGarages
                .Select(participant => participant.Garage)
                .Where(garage => !garage.IsArchived)
                .OrderBy(garage => garage.Number)
                .ToList();
        if (garages.Count == 0)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_no_garages", "Нет активных гаражей для начисления сбора.");
        }

        await using var generationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            garages.Select(garage => new AccrualPaymentAllocationKey(garage.Id, incomeType.Id)).ToArray(),
            cancellationToken);

        var existingGarageIds = await accrualRepository.GetActiveFeeCampaignGarageIdsAsync(campaign.Id, month, cancellationToken);
        var fullyPaidGarageIds = await accrualRepository.GetFullyPaidFeeCampaignGarageIdsBeforeMonthAsync(
            campaign.Id,
            month,
            cancellationToken);
        var created = new List<AccrualDto>();
        var skipped = new List<string>();
        foreach (var garage in garages)
        {
            if (existingGarageIds.Contains(garage.Id))
            {
                skipped.Add($"Гараж {garage.Number}: начисление сбора уже есть.");
                continue;
            }

            if (fullyPaidGarageIds.Contains(garage.Id))
            {
                skipped.Add($"Гараж {garage.Number}: предыдущие начисления по сбору полностью оплачены.");
                continue;
            }

            var accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                FeeCampaignId = campaign.Id,
                FeeCampaign = campaign,
                AccountingMonth = month,
                AccountingYear = AnnualAccrualPolicy.ResolveAccountingYear(incomeType.Code, month),
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                Source = AccrualSources.FeeCampaign,
                Basis = campaign.Name,
                Comment = BuildFeeCampaignAccrualComment(campaign, request.Comment)
            };
            accrualRepository.Add(accrual);
            created.Add(ToDto(accrual));
        }

        if (created.Count == 0)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure(
                "fee_campaign_accruals_empty",
                "Новые начисления по сбору не требуются: выбранный месяц уже обработан или участники полностью оплатили предыдущие начисления.");
        }

        AddAudit(
            actorUserId,
            "finance.fee_campaign_accruals_generated",
            "accrual",
            campaign.Id,
            FormatFeeCampaignAccrualGenerationAuditSummary(month, campaign, incomeType, created, skipped),
            relatedAccountingMonth: month,
            relatedDocumentNumber: $"{campaign.Name} {month:MM.yyyy}",
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "accrual",
                ["feeCampaignId"] = campaign.Id,
                ["feeCampaignName"] = campaign.Name,
                ["incomeTypeId"] = incomeType.Id,
                ["incomeTypeName"] = incomeType.Name,
                ["destinationFundId"] = incomeType.DestinationFundId,
                ["createdCount"] = created.Count,
                ["skippedCount"] = skipped.Count,
                ["totalAmount"] = created.Sum(item => item.Amount)
            });
        await RebuildPaymentAllocationsAsync(
            created.Select(item => new AccrualPaymentAllocationKey(item.GarageId, item.IncomeTypeId)).ToArray(),
            actorUserId,
            "Формирование начислений по сбору",
            campaign.Id,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = new FeeCampaignAccrualGenerationResultDto(
            month,
            campaign.Id,
            campaign.Name,
            incomeType.Id,
            incomeType.Name,
            amount,
            created.Count,
            skipped.Count,
            created.Sum(item => item.Amount),
            created,
            skipped);
        return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Success(result);
    }

    public async Task<FinanceResult<ActiveFeeCampaignAccrualGenerationResultDto>> GenerateActiveFeeCampaignAccrualsAsync(
        GenerateActiveFeeCampaignAccrualsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var candidates = await feeCampaignRepository.GetActiveAccrualCandidatesAsync(
            month,
            MaxAutomaticFeeCampaigns + 1,
            cancellationToken);
        if (candidates.Count > MaxAutomaticFeeCampaigns)
        {
            return FinanceResult<ActiveFeeCampaignAccrualGenerationResultDto>.Failure(
                "active_fee_campaign_limit_exceeded",
                $"За {month:MM.yyyy} найдено больше {MaxAutomaticFeeCampaigns} действующих сборов. Архивируйте завершенные сборы или начислите их вручную.");
        }

        var campaignResults = new List<FeeCampaignAccrualGenerationResultDto>();
        var skippedCampaigns = new List<string>();
        var failedCampaigns = new List<string>();
        foreach (var candidate in candidates)
        {
            var result = await GenerateFeeCampaignAccrualsAsync(
                new GenerateFeeCampaignAccrualsRequest(candidate.Id, month, request.Comment),
                actorUserId,
                cancellationToken);
            if (result.Succeeded)
            {
                campaignResults.Add(result.Value!);
            }
            else if (result.ErrorCode == "fee_campaign_accruals_empty")
            {
                skippedCampaigns.Add($"{candidate.Name}: новые начисления за {month:MM.yyyy} не требуются — месяц уже обработан или участники полностью оплатили сбор.");
            }
            else
            {
                failedCampaigns.Add($"{candidate.Name}: {result.ErrorMessage}");
            }
        }

        var createdCount = campaignResults.Sum(result => result.CreatedCount);
        var skippedCount = campaignResults.Sum(result => result.SkippedCount) + skippedCampaigns.Count;
        return FinanceResult<ActiveFeeCampaignAccrualGenerationResultDto>.Success(
            new ActiveFeeCampaignAccrualGenerationResultDto(
                month,
                candidates.Count,
                createdCount,
                skippedCount,
                campaignResults.Sum(result => result.TotalAmount),
                campaignResults,
                skippedCampaigns,
                failedCampaigns));
    }

    public async Task<FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>> GenerateSupplierGroupSalaryAccrualsAsync(GenerateSupplierGroupSalaryAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        var comment = NormalizeOptional(request.Comment);

        var group = await supplierGroupRepository.FindActiveAsync(request.SupplierGroupId, cancellationToken);
        if (group is null)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("supplier_group_not_found", "Группа персонала не найдена.");
        }

        var salaryExpenseType = await expenseTypeRepository.FindActiveByCodeAsync("salary", cancellationToken);
        if (salaryExpenseType is null)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("salary_expense_type_not_found", "Системная услуга «Зарплата» не найдена.");
        }

        var suppliers = await supplierRepository.GetActiveByGroupAsync(group.Id, cancellationToken);
        if (suppliers.Count == 0)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("supplier_group_empty", "В выбранной группе нет активных поставщиков или сотрудников.");
        }

        var existingSupplierIds = await supplierAccrualRepository.GetActiveSupplierIdsAsync(
            salaryExpenseType.Id,
            month,
            AccrualSources.Regular,
            documentNumber,
            cancellationToken);
        var created = new List<SupplierAccrualDto>();
        var skipped = new List<string>();
        foreach (var supplier in suppliers)
        {
            if (existingSupplierIds.Contains(supplier.Id))
            {
                skipped.Add($"{supplier.Name}: зарплата за месяц уже начислена.");
                continue;
            }

            var accrual = new SupplierAccrual
            {
                SupplierId = supplier.Id,
                Supplier = supplier,
                ExpenseTypeId = salaryExpenseType.Id,
                ExpenseType = salaryExpenseType,
                AccountingMonth = month,
                Amount = amount,
                Source = AccrualSources.Regular,
                DocumentNumber = documentNumber,
                Comment = BuildSupplierGroupSalaryComment(group.Name, comment)
            };
            supplierAccrualRepository.Add(accrual);
            created.Add(ToDto(accrual));
        }

        if (created.Count == 0)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("salary_accruals_empty", "Не создано ни одного начисления зарплаты.");
        }

        AddAudit(
            actorUserId,
            "finance.supplier_group_salary_accruals_generated",
            "supplier_accrual",
            Guid.NewGuid(),
            FormatSupplierGroupSalaryAccrualGenerationAuditSummary(month, group.Name, salaryExpenseType.Name, created, skipped),
            relatedAccountingMonth: month,
            relatedDocumentNumber: documentNumber,
            relatedCounterpartyId: group.Id.ToString(),
            relatedCounterpartyName: group.Name,
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "supplier_accrual",
                ["supplierGroupId"] = group.Id,
                ["supplierGroupName"] = group.Name,
                ["expenseTypeId"] = salaryExpenseType.Id,
                ["expenseTypeName"] = salaryExpenseType.Name,
                ["createdCount"] = created.Count,
                ["skippedCount"] = skipped.Count,
                ["totalAmount"] = created.Sum(item => item.Amount)
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = new SupplierGroupSalaryAccrualGenerationResultDto(
            month,
            group.Id,
            group.Name,
            salaryExpenseType.Id,
            salaryExpenseType.Name,
            created.Count,
            skipped.Count,
            created.Sum(item => item.Amount),
            created,
            skipped);
        return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Success(result);
    }

    private static bool IsIncomeTypeCompatibleWithTariff(string? incomeTypeCode, string calculationBase)
    {
        return NormalizeIncomeTypeCode(incomeTypeCode) switch
        {
            "water" => calculationBase == TariffCalculationBases.MeterWater,
            "trash" => calculationBase == TariffCalculationBases.People,
            "electricity" => calculationBase == TariffCalculationBases.MeterElectricity,
            "membership" or "target" or "entry" or "connection" => calculationBase == TariffCalculationBases.Fixed,
            _ => true
        };
    }

    private static ChargeServiceSetting? SelectChargeServiceSettingForDueDates(
        IReadOnlyList<ChargeServiceSetting> candidates,
        DateOnly accountingMonth)
    {
        var matchingMonth = candidates.FirstOrDefault(setting => IsChargeServiceDueForMonth(setting, accountingMonth));
        return matchingMonth ?? (candidates.Count == 1 ? candidates[0] : null);
    }

    private static bool IsChargeServiceDueForMonth(ChargeServiceSetting setting, DateOnly month)
    {
        if (!setting.IsRegular || !setting.AccrualStartMonth.HasValue || !setting.PeriodicityMonths.HasValue)
        {
            return false;
        }

        var periodicity = Math.Max(1, setting.PeriodicityMonths.Value);
        if (periodicity >= 12)
        {
            return month.Month == setting.AccrualStartMonth.Value;
        }

        var monthsAfterStart = (month.Month - setting.AccrualStartMonth.Value + 12) % 12;
        return monthsAfterStart % periodicity == 0;
    }

    private static string BuildRegularCatalogAccrualComment(string serviceName, string? comment)
    {
        var prefix = $"Каталог услуг: {serviceName}";
        var normalizedComment = NormalizeOptional(comment);
        return normalizedComment is null ? prefix : $"{prefix}; {normalizedComment}";
    }

    public async Task<IReadOnlyList<MeterDeviceDto>> GetMeterDevicesAsync(
        Guid garageId,
        string meterKind,
        CancellationToken cancellationToken)
    {
        var normalizedKind = meterKind.Trim();
        if (!MeterKinds.IsValid(normalizedKind))
        {
            return [];
        }

        var devices = await meterReadingRepository.GetDevicesAsync(garageId, normalizedKind, cancellationToken);
        return devices.Select(ToDto).ToArray();
    }

    public async Task<FinanceResult<MeterDeviceReplacementDto>> ReplaceMeterDeviceAsync(
        ReplaceMeterDeviceRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (!MeterKinds.IsValid(meterKind))
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
        }

        var serialNumber = NormalizeOptional(request.NewSerialNumber);
        var reason = NormalizeOptional(request.Reason);
        if (serialNumber is null)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_serial_required", "Укажите номер нового счетчика.");
        }

        if (reason is null)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_replacement_reason_required", "Укажите причину замены счетчика.");
        }

        if (!request.NewInitialValue.HasValue || !request.CurrentValue.HasValue)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_values_required", "Укажите начальное и текущее показания нового счетчика.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        if (request.ReplacementDate.Year != accountingMonth.Year || request.ReplacementDate.Month != accountingMonth.Month)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_replacement_date_month_mismatch", "Дата замены счетчика должна относиться к выбранному учетному месяцу.");
        }

        var initialValue = MoneyMath.RoundMeterValue(request.NewInitialValue.Value);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue.Value);
        if (currentValue < initialValue)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_current_below_initial", "Текущее показание нового счетчика не может быть меньше его начального показания.");
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("garage_not_found", "Гараж для замены счетчика не найден.");
        }

        await using var meterChainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(garage.Id, meterKind)],
            cancellationToken);
        var allReadings = (await meterReadingRepository.GetAllActiveForUpdateAsync(garage.Id, meterKind, cancellationToken)).ToList();
        var activeDevice = await meterReadingRepository.GetActiveDeviceForUpdateAsync(garage.Id, meterKind, cancellationToken);
        var registerLegacyDevice = false;
        if (activeDevice is null && allReadings.Count > 0)
        {
            var firstReading = allReadings[0];
            activeDevice = new MeterDevice
            {
                GarageId = garage.Id,
                Garage = garage,
                MeterKind = meterKind,
                SerialNumber = "Без номера",
                InstalledOn = firstReading.ReadingDate,
                InitialValue = firstReading.PreviousValue
            };
            registerLegacyDevice = true;
        }

        var devices = await meterReadingRepository.GetDevicesAsync(garage.Id, meterKind, cancellationToken);
        if (devices.Any(device => string.Equals(device.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)))
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_serial_duplicate", "Счетчик с таким номером уже зарегистрирован для этого гаража.");
        }

        MeterReading? targetReading = null;
        if (request.MeterReadingId.HasValue)
        {
            targetReading = allReadings.SingleOrDefault(reading => reading.Id == request.MeterReadingId.Value);
            if (targetReading is null)
            {
                return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_reading_not_found", "Изменяемое показание не найдено.");
            }

            if (!request.ExpectedReadingVersion.HasValue || targetReading.Version != request.ExpectedReadingVersion.Value)
            {
                return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_reading_conflict", "Показание уже изменено другим пользователем. Обновите данные и повторите действие.");
            }

            if (targetReading.AccountingMonth != accountingMonth)
            {
                return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_replacement_month_mismatch", "Месяц заменяемого показания не совпадает с выбранным месяцем.");
            }
        }
        else if (allReadings.Any(reading => reading.AccountingMonth == accountingMonth))
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_reading_duplicate", "За выбранный месяц уже есть показание. Обновите таблицу и повторите замену из этой ячейки.");
        }

        if (activeDevice is not null && request.ReplacementDate <= activeDevice.InstalledOn)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_replacement_date_invalid", "Дата замены должна быть позже даты установки действующего счетчика.");
        }

        var lastOldReading = allReadings
            .Where(reading => reading.Id != targetReading?.Id && reading.ReadingDate < request.ReplacementDate)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.AccountingMonth)
            .FirstOrDefault();
        var removedFinalValue = MoneyMath.RoundMeterValue(
            request.RemovedDeviceFinalValue ?? targetReading?.CurrentValue ?? lastOldReading?.CurrentValue ?? activeDevice?.InitialValue ?? 0m);
        if (lastOldReading is not null && removedFinalValue < lastOldReading.CurrentValue)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_final_below_last_reading", "Конечное показание старого счетчика не может быть меньше его последнего сохраненного показания.");
        }
        var oldDeviceBaseline = lastOldReading?.CurrentValue ?? activeDevice?.InitialValue ?? removedFinalValue;
        var previousDeviceConsumption = MoneyMath.RoundMeterValue(removedFinalValue - oldDeviceBaseline);
        if (previousDeviceConsumption < 0)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_final_below_initial", "Конечное показание старого счетчика не может быть меньше его начального показания.");
        }

        var newDevice = new MeterDevice
        {
            GarageId = garage.Id,
            Garage = garage,
            MeterKind = meterKind,
            SerialNumber = serialNumber,
            InstalledOn = request.ReplacementDate,
            InitialValue = initialValue
        };
        var isNewReading = targetReading is null;
        var prospectiveTarget = new MeterReading
        {
            Id = targetReading?.Id ?? Guid.NewGuid(),
            GarageId = garage.Id,
            Garage = garage,
            MeterDeviceId = newDevice.Id,
            MeterDevice = newDevice,
            MeterKind = meterKind,
            AccountingMonth = accountingMonth,
            ReadingDate = request.ReplacementDate,
            CurrentValue = currentValue,
            PreviousValue = targetReading?.PreviousValue ?? initialValue,
            PreviousDeviceConsumption = previousDeviceConsumption,
            Consumption = targetReading?.Consumption ?? MoneyMath.RoundMeterValue(currentValue - initialValue),
            IsMeterReplacement = true,
            HasGapWarning = targetReading?.HasGapWarning ?? false,
            Comment = $"Замена счетчика: {reason}"
        };

        var previousReading = allReadings
            .Where(reading => reading.Id != prospectiveTarget.Id && reading.AccountingMonth < accountingMonth)
            .OrderByDescending(reading => reading.AccountingMonth)
            .FirstOrDefault();
        var prospectiveLaterReadings = allReadings
            .Where(reading => reading.Id != prospectiveTarget.Id && reading.AccountingMonth > accountingMonth)
            .Select(reading => CloneMeterReadingForChain(
                reading,
                reading.ReadingDate >= request.ReplacementDate ? newDevice : reading.MeterDevice))
            .ToArray();
        var chainReadings = new[] { prospectiveTarget }
            .Concat(prospectiveLaterReadings)
            .ToArray();
        var chainPlan = PlanMeterReadingChain(garage, meterKind, previousReading, chainReadings);
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure(chainPlan.ErrorCode!, chainPlan.ErrorMessage!);
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            garage.Id,
            meterKind,
            accountingMonth,
            chainPlan.Value!,
            missingReadingMonth: null,
            cancellationToken);
        var meteredSettings = await GetApplicableMeteredSettingsAsync(prospectiveTarget, cancellationToken);
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Concat(meteredSettings.Select(setting => new AccrualPaymentAllocationKey(garage.Id, setting.IncomeTypeId!.Value)))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(allocationKeys, cancellationToken);
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure(
                "meter_reading_accrual_paid",
                "Замена счетчика изменяет полностью или частично оплаченное начисление. Сначала исправьте оплату или оформите отдельную корректировку.");
        }

        if (registerLegacyDevice)
        {
            meterReadingRepository.Add(activeDevice!);
            foreach (var existingReading in allReadings.Where(reading => reading.ReadingDate < request.ReplacementDate))
            {
                existingReading.MeterDeviceId = activeDevice!.Id;
                existingReading.MeterDevice = activeDevice;
            }
        }

        meterReadingRepository.Add(newDevice);
        if (activeDevice is not null)
        {
            activeDevice.RemovedOn = request.ReplacementDate.AddDays(-1);
            activeDevice.FinalValue = removedFinalValue;
            activeDevice.Version = Guid.NewGuid();
            activeDevice.UpdatedAtUtc = timeProvider.GetUtcNow();
        }

        targetReading ??= new MeterReading
        {
            Id = prospectiveTarget.Id,
            GarageId = garage.Id,
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = accountingMonth
        };
        targetReading.MeterDeviceId = newDevice.Id;
        targetReading.MeterDevice = newDevice;
        targetReading.ReadingDate = request.ReplacementDate;
        targetReading.CurrentValue = currentValue;
        targetReading.PreviousDeviceConsumption = previousDeviceConsumption;
        targetReading.IsMeterReplacement = true;
        targetReading.Comment = prospectiveTarget.Comment;
        foreach (var laterReading in allReadings.Where(reading => reading.Id != targetReading.Id && reading.ReadingDate >= request.ReplacementDate))
        {
            laterReading.MeterDeviceId = newDevice.Id;
            laterReading.MeterDevice = newDevice;
        }

        var appliedChainPlan = PlanMeterReadingChain(
            garage,
            meterKind,
            previousReading,
            new[] { targetReading }
                .Concat(allReadings.Where(reading => reading.Id != targetReading.Id && reading.AccountingMonth > accountingMonth))
                .ToArray());
        if (!appliedChainPlan.Succeeded)
        {
            throw new InvalidOperationException("Validated meter replacement chain could not be applied.");
        }

        ApplyMeterReadingChainChanges(appliedChainPlan.Value!, actorUserId, targetReading.Id);
        var primaryChange = appliedChainPlan.Value![0];
        targetReading.PreviousValue = primaryChange.PreviousValue;
        targetReading.Consumption = primaryChange.Consumption;
        targetReading.HasGapWarning = primaryChange.HasGapWarning;
        targetReading.Version = Guid.NewGuid();
        targetReading.UpdatedAtUtc = timeProvider.GetUtcNow();
        if (isNewReading)
        {
            meterReadingRepository.Add(targetReading);
        }

        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Пересчет после замены счетчика");
        var createdAccrualKeys = await CreateMissingMeteredAccrualsAsync(
            garage,
            targetReading,
            meteredSettings,
            actorUserId,
            cancellationToken);
        AddAudit(
            actorUserId,
            "finance.meter_device_replaced",
            "meter_device",
            newDevice.Id,
            $"В гараже {garage.Number} счетчик {meterKind} заменен на № {serialNumber}; начальное показание {initialValue:0.###}, текущее {currentValue:0.###}. Причина: {reason}.",
            metadata: new Dictionary<string, object?>
            {
                ["garageId"] = garage.Id,
                ["meterKind"] = meterKind,
                ["removedDeviceId"] = activeDevice?.Id,
                ["newDeviceId"] = newDevice.Id,
                ["replacementDate"] = request.ReplacementDate,
                ["reason"] = reason
            });
        await RebuildPaymentAllocationsAsync(
            allocationKeys.Concat(createdAccrualKeys).Distinct().ToArray(),
            actorUserId,
            "Перераспределение после замены счетчика",
            targetReading.Id,
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConcurrencyException)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_conflict", "Данные счетчика уже изменены другим пользователем. Обновите страницу и повторите действие.");
        }
        catch (ApplicationPersistenceConflictException)
        {
            return FinanceResult<MeterDeviceReplacementDto>.Failure("meter_device_conflict", "Не удалось сохранить замену из-за конкурентного изменения. Обновите страницу и повторите действие.");
        }

        return FinanceResult<MeterDeviceReplacementDto>.Success(new MeterDeviceReplacementDto(ToDto(newDevice), ToDto(targetReading)));
    }

    public async Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (!MeterKinds.IsValid(meterKind))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
        }

        if (!request.CurrentValue.HasValue)
        {
            return ManualMeterReadingValueRequired();
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var periodOverrideReason = NormalizeOptional(request.PeriodOverrideReason);
        if (periodOverrideReason is null && month != GetCurrentAccountingMonth())
        {
            periodOverrideReason = "Ввод показания за другой месяц.";
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
        }

        await using var meterChainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(garage.Id, meterKind)],
            cancellationToken);

        if (await meterReadingRepository.ActiveDuplicateExistsAsync(null, garage.Id, meterKind, month, cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание этого счетчика за месяц уже внесено.");
        }

        var meterDevice = await meterReadingRepository.GetDeviceForDateForUpdateAsync(
            garage.Id,
            meterKind,
            request.ReadingDate,
            cancellationToken);
        var registerMeterDevice = false;
        var adjustMeterDeviceInstalledOn = false;
        if (meterDevice is null)
        {
            var activeDevice = await meterReadingRepository.GetActiveDeviceForUpdateAsync(garage.Id, meterKind, cancellationToken);
            if (activeDevice is not null && string.Equals(activeDevice.SerialNumber, "Без номера", StringComparison.Ordinal))
            {
                meterDevice = activeDevice;
                adjustMeterDeviceInstalledOn = true;
            }
        }

        if (meterDevice is null)
        {
            var initialValue = GetInitialMeterValue(garage, meterKind);
            if (!initialValue.HasValue && meterKind == MeterKinds.Water)
            {
                return WaterMeterReadingBaselineRequired();
            }

            meterDevice = new MeterDevice
            {
                GarageId = garage.Id,
                Garage = garage,
                MeterKind = meterKind,
                SerialNumber = "Без номера",
                InstalledOn = request.ReadingDate,
                InitialValue = MoneyMath.RoundMeterValue(initialValue ?? 0m)
            };
            registerMeterDevice = true;
        }

        var previousReading = await meterReadingRepository.GetPreviousActiveAsync(null, garage.Id, meterKind, month, cancellationToken);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue.Value);
        var previousBelongsToDevice = previousReading?.MeterDeviceId == meterDevice.Id;
        var previousMeterValue = previousBelongsToDevice ? previousReading!.CurrentValue : meterDevice.InitialValue;
        var previousValue = MoneyMath.RoundMeterValue(previousMeterValue);
        var consumption = MoneyMath.RoundMeterValue(currentValue - previousValue);
        if (consumption < 0)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_decreased", "Новое показание не может быть меньше предыдущего.");
        }

        var hasGapWarning = HasGapWarning(meterKind, month, previousReading);
        var reading = new MeterReading
        {
            GarageId = garage.Id,
            Garage = garage,
            MeterDeviceId = meterDevice.Id,
            MeterDevice = meterDevice,
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = request.ReadingDate,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            Consumption = consumption,
            HasGapWarning = hasGapWarning,
            Comment = NormalizeOptional(request.Comment)
        };

        var subsequentReadings = await meterReadingRepository.GetActiveFromForUpdateAsync(
            ignoredId: null,
            garage.Id,
            meterKind,
            month.AddMonths(1),
            cancellationToken);
        var chainPlan = PlanMeterReadingChain(
            garage,
            meterKind,
            previousReading,
            new[] { reading }.Concat(subsequentReadings).ToArray());
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<MeterReadingDto>.Failure(chainPlan.ErrorCode!, chainPlan.ErrorMessage!);
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            garage.Id,
            meterKind,
            month,
            chainPlan.Value!,
            missingReadingMonth: null,
            cancellationToken);

        var meteredSettings = await GetApplicableMeteredSettingsAsync(reading, cancellationToken);
        var allocationKeys = meteredSettings
            .Select(setting => new AccrualPaymentAllocationKey(garage.Id, setting.IncomeTypeId!.Value))
            .Concat(accrualRecalculations.Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId)))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            allocationKeys,
            cancellationToken);
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_accrual_paid",
                "Вставка показания изменяет уже оплаченное начисление последующего периода. Сначала исправьте оплату или оформите отдельную корректировку начисления.");
        }

        ApplyMeterReadingChainChanges(chainPlan.Value!, actorUserId, reading.Id);
        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Пересчет после вставки показания");
        if (adjustMeterDeviceInstalledOn)
        {
            meterDevice.InstalledOn = request.ReadingDate;
            meterDevice.Version = Guid.NewGuid();
            meterDevice.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
        if (registerMeterDevice)
        {
            meterReadingRepository.Add(meterDevice);
            AddAudit(
                actorUserId,
                "finance.meter_device_registered",
                "meter_device",
                meterDevice.Id,
                $"Для гаража {garage.Number} зарегистрирован счетчик {meterKind} без указанного номера со стартовым значением {meterDevice.InitialValue:0.###}.");
        }
        meterReadingRepository.Add(reading);
        var createdSummary = FormatMeterReadingCreatedAuditSummary(reading);
        if (periodOverrideReason is not null)
        {
            createdSummary = $"{createdSummary} Причина ввода вне текущего месяца: {periodOverrideReason}.";
        }
        AddAudit(actorUserId, "finance.meter_reading_created", reading, createdSummary);
        var createdAccrualKeys = await CreateMissingMeteredAccrualsAsync(
            garage,
            reading,
            meteredSettings,
            actorUserId,
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            createdAccrualKeys,
            actorUserId,
            "Применение тарифа после внесения показания",
            reading.Id,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> SavePaymentFormMeterReadingAsync(
        SavePaymentFormMeterReadingRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (!MeterKinds.IsValid(meterKind))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
        }

        if (!request.CurrentValue.HasValue)
        {
            return ManualMeterReadingValueRequired();
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var activeReading = await meterReadingRepository.GetActiveAsync(
            request.GarageId,
            meterKind,
            month,
            cancellationToken);
        var saveRequest = new CreateMeterReadingRequest(
            request.GarageId,
            meterKind,
            month,
            request.ReadingDate,
            request.CurrentValue,
            request.Comment,
            request.ExpectedVersion,
            request.PeriodOverrideReason);

        if (!request.MeterReadingId.HasValue)
        {
            if (activeReading is not null)
            {
                return MeterReadingConflict();
            }

            try
            {
                var createResult = await CreateMeterReadingAsync(saveRequest, actorUserId, cancellationToken);
                return createResult.ErrorCode == "meter_reading_duplicate"
                    ? MeterReadingConflict()
                    : createResult;
            }
            catch (ApplicationPersistenceConflictException)
            {
                return MeterReadingConflict();
            }
        }

        if (!request.ExpectedVersion.HasValue ||
            activeReading is null ||
            activeReading.Id != request.MeterReadingId.Value ||
            activeReading.Version != request.ExpectedVersion.Value)
        {
            return MeterReadingConflict();
        }

        return await UpdateMeterReadingAsync(request.MeterReadingId.Value, saveRequest, actorUserId, cancellationToken);
    }

    public Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingAsync(
        Guid meterReadingId,
        CreateMeterReadingRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken) =>
        UpdateMeterReadingCoreAsync(meterReadingId, request, actorUserId, historicalCorrectionReason: null, cancellationToken);

    public async Task<FinanceResult<MeterReadingDto>> CorrectHistoricalMeterReadingAsync(
        Guid meterReadingId,
        CorrectHistoricalMeterReadingRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason) ?? "Корректировка показания за другой месяц.";

        var reading = await meterReadingRepository.FindForUpdateAsync(meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        var currentMonth = GetCurrentAccountingMonth();
        if (reading.AccountingMonth == currentMonth)
        {
            return HistoricalMeterReadingMonthRequired();
        }

        var correction = new CreateMeterReadingRequest(
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            request.ReadingDate,
            request.CurrentValue,
            request.Comment,
            request.ExpectedVersion);
        return await UpdateMeterReadingCoreAsync(meterReadingId, correction, actorUserId, reason, cancellationToken);
    }

    private async Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingCoreAsync(
        Guid meterReadingId,
        CreateMeterReadingRequest request,
        Guid? actorUserId,
        string? historicalCorrectionReason,
        CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (!MeterKinds.IsValid(meterKind))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
        }

        if (!request.CurrentValue.HasValue)
        {
            return ManualMeterReadingValueRequired();
        }

        var reading = await meterReadingRepository.FindForUpdateAsync(meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        if (reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_already_canceled", "Отмененное показание нельзя изменить.");
        }

        await using var meterChainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(reading.GarageId, reading.MeterKind)],
            cancellationToken);
        await meterReadingRepository.ReloadForUpdateAsync(reading, cancellationToken);

        if (reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_already_canceled", "Отмененное показание нельзя изменить.");
        }

        if (request.ExpectedVersion.HasValue && reading.Version != request.ExpectedVersion.Value)
        {
            return MeterReadingConflict();
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var currentMonth = GetCurrentAccountingMonth();
        if (historicalCorrectionReason is null)
        {
            if (reading.AccountingMonth != currentMonth || month != currentMonth)
            {
                return FinanceResult<MeterReadingDto>.Failure(
                    "meter_reading_current_month_required",
                    "Обычное изменение показания разрешено только за текущий учетный месяц. Для прошлого месяца используйте историческую корректировку.");
            }
        }
        else if (reading.AccountingMonth == currentMonth || month != reading.AccountingMonth)
        {
            return HistoricalMeterReadingMonthRequired();
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
        }

        if (garage.Id != reading.GarageId || !string.Equals(meterKind, reading.MeterKind, StringComparison.Ordinal))
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_identity_immutable",
                "Гараж и тип счетчика существующего показания менять нельзя. Отмените ошибочную запись и создайте новую.");
        }

        if (await meterReadingRepository.ActiveDuplicateExistsAsync(reading.Id, garage.Id, meterKind, month, cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание этого счетчика за месяц уже внесено.");
        }

        var previousReading = await meterReadingRepository.GetPreviousActiveAsync(reading.Id, garage.Id, meterKind, month, cancellationToken);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue.Value);
        var storedPreviousValue = reading.GarageId == garage.Id &&
            string.Equals(reading.MeterKind, meterKind, StringComparison.Ordinal)
                ? reading.PreviousValue
                : (decimal?)null;
        var previousMeterValue = previousReading?.CurrentValue ?? GetInitialMeterValue(garage, meterKind) ?? storedPreviousValue;
        if (!previousMeterValue.HasValue && meterKind == MeterKinds.Water)
        {
            return WaterMeterReadingBaselineRequired();
        }

        var previousValue = MoneyMath.RoundMeterValue(previousMeterValue ?? 0m);
        var consumption = MoneyMath.RoundMeterValue(currentValue - previousValue + reading.PreviousDeviceConsumption);
        if (consumption < 0)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_decreased", "Новое показание не может быть меньше предыдущего.");
        }

        var nextReading = await meterReadingRepository.GetNextActiveAsync(reading.Id, garage.Id, meterKind, month, cancellationToken);
        if (nextReading is not null && currentValue > nextReading.CurrentValue)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_sequence_invalid", "Показание не может быть больше следующего внесенного месяца.");
        }

        var hasGapWarning = HasGapWarning(meterKind, month, previousReading);
        var comment = NormalizeOptional(request.Comment);
        var primaryMatches = MeterReadingMatches(reading, garage.Id, meterKind, month, request.ReadingDate, currentValue, previousValue, consumption, hasGapWarning, comment);

        var prospectiveReading = new MeterReading
        {
            Id = reading.Id,
            GarageId = garage.Id,
            Garage = garage,
            MeterDeviceId = reading.MeterDeviceId,
            MeterDevice = reading.MeterDevice,
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = request.ReadingDate,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            PreviousDeviceConsumption = reading.PreviousDeviceConsumption,
            Consumption = consumption,
            HasGapWarning = hasGapWarning
        };
        var subsequentReadings = await meterReadingRepository.GetActiveFromForUpdateAsync(
            reading.Id,
            garage.Id,
            meterKind,
            month.AddMonths(1),
            cancellationToken);
        var chainPlan = PlanMeterReadingChain(
            garage,
            meterKind,
            previousReading,
            new[] { prospectiveReading }.Concat(subsequentReadings).ToArray());
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<MeterReadingDto>.Failure(chainPlan.ErrorCode!, chainPlan.ErrorMessage!);
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            garage.Id,
            meterKind,
            month,
            chainPlan.Value!,
            missingReadingMonth: null,
            cancellationToken);
        if (primaryMatches && chainPlan.Value!.Skip(1).All(item => !item.Changed) && accrualRecalculations.Count == 0)
        {
            return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
        }
        var meteredSettings = await GetApplicableMeteredSettingsAsync(prospectiveReading, cancellationToken);
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Concat(meteredSettings.Select(setting => new AccrualPaymentAllocationKey(garage.Id, setting.IncomeTypeId!.Value)))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            allocationKeys,
            cancellationToken);
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_accrual_paid",
                "Связанное начисление уже полностью или частично оплачено. Изменение показания отменено; сначала исправьте оплату или согласуйте отдельную корректировку начисления.");
        }

        var oldValues = new Dictionary<string, object?>
        {
            ["garage"] = reading.Garage.Number,
            ["meterKind"] = reading.MeterKind,
            ["accountingMonth"] = reading.AccountingMonth,
            ["readingDate"] = reading.ReadingDate,
            ["currentValue"] = reading.CurrentValue,
            ["previousValue"] = reading.PreviousValue,
            ["consumption"] = reading.Consumption,
            ["hasGapWarning"] = reading.HasGapWarning,
            ["comment"] = reading.Comment
        };
        var newValues = new Dictionary<string, object?>
        {
            ["garage"] = garage.Number,
            ["meterKind"] = meterKind,
            ["accountingMonth"] = month,
            ["readingDate"] = request.ReadingDate,
            ["currentValue"] = currentValue,
            ["previousValue"] = previousValue,
            ["consumption"] = consumption,
            ["hasGapWarning"] = hasGapWarning,
            ["comment"] = comment
        };

        reading.GarageId = garage.Id;
        reading.Garage = garage;
        reading.MeterKind = meterKind;
        reading.AccountingMonth = month;
        reading.ReadingDate = request.ReadingDate;
        reading.CurrentValue = currentValue;
        var primaryChainChange = chainPlan.Value![0];
        reading.PreviousValue = primaryChainChange.PreviousValue;
        reading.Consumption = primaryChainChange.Consumption;
        reading.HasGapWarning = primaryChainChange.HasGapWarning;
        reading.Comment = comment;
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        ApplyMeterReadingChainChanges(chainPlan.Value.Skip(1).ToArray(), actorUserId, reading.Id);
        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Начисление пересчитано после изменения показания");
        var createdAccrualKeys = await CreateMissingMeteredAccrualsAsync(
            garage,
            reading,
            meteredSettings,
            actorUserId,
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            allocationKeys.Concat(createdAccrualKeys).Distinct().ToArray(),
            actorUserId,
            "Пересчет начисления после изменения показания",
            reading.Id,
            cancellationToken);
        AddAudit(
            actorUserId,
            historicalCorrectionReason is null ? "finance.meter_reading_updated" : "finance.meter_reading_historical_updated",
            reading,
            historicalCorrectionReason is null
                ? FormatMeterReadingUpdatedAuditSummary(reading)
                : FormatHistoricalMeterReadingCorrectedAuditSummary(reading),
            oldValues,
            newValues,
            historicalCorrectionReason);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConcurrencyException)
        {
            return MeterReadingConflict();
        }
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    private DateOnly GetCurrentAccountingMonth()
    {
        return MonthPeriod.Normalize(businessDateProvider.Today);
    }

    private static FinanceResult<MeterReadingDto> HistoricalMeterReadingMonthRequired() =>
        FinanceResult<MeterReadingDto>.Failure(
            "meter_reading_historical_month_required",
            "Корректировка другого периода применяется только к месяцу, отличному от текущего.");

    private static FinanceResult<MeterReadingDto> MeterReadingConflict() =>
        FinanceResult<MeterReadingDto>.Failure(
            "meter_reading_conflict",
            "Показание уже изменено другим пользователем. Обновите данные и повторите действие.");

    private static FinanceResult<MeterReadingDto> ManualMeterReadingValueRequired() =>
        FinanceResult<MeterReadingDto>.Failure(
            "meter_reading_value_required",
            "Введите показание счетчика вручную.");

    private static FinanceResult<MeterReadingDto> WaterMeterReadingBaselineRequired() =>
        FinanceResult<MeterReadingDto>.Failure(
            "water_meter_reading_baseline_required",
            "Для первого показания воды укажите стартовое значение счетчика в карточке гаража.");

    public async Task<FinanceResult<MeterReadingDto>> CancelMeterReadingAsync(Guid meterReadingId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_cancel_reason_required", "Для отмены показания счетчика нужна причина.");
        }

        var reading = await meterReadingRepository.FindForUpdateAsync(meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        if (reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_already_canceled", "Показание счетчика уже отменено.");
        }

        if (reading.IsMeterReplacement)
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_device_replacement_reading_cancel_forbidden",
                "Переходное показание замены счетчика отменять нельзя. Исправьте значения и причину в записи замены.");
        }

        await using var meterChainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(reading.GarageId, reading.MeterKind)],
            cancellationToken);
        await meterReadingRepository.ReloadForUpdateAsync(reading, cancellationToken);
        if (reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_already_canceled", "Показание счетчика уже отменено.");
        }

        var previousReading = await meterReadingRepository.GetPreviousActiveAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            cancellationToken);
        var subsequentReadings = await meterReadingRepository.GetActiveFromForUpdateAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth.AddMonths(1),
            cancellationToken);
        var chainPlan = PlanMeterReadingChain(reading.Garage, reading.MeterKind, previousReading, subsequentReadings);
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<MeterReadingDto>.Failure(chainPlan.ErrorCode!, chainPlan.ErrorMessage!);
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            chainPlan.Value!,
            reading.AccountingMonth,
            cancellationToken);
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(allocationKeys, cancellationToken);
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_accrual_paid",
                "Отмена показания изменяет полностью или частично оплаченное начисление. Сначала исправьте оплату или оформите отдельную корректировку начисления.");
        }

        ApplyMeterReadingChainChanges(chainPlan.Value!, actorUserId, reading.Id);
        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Пересчет после отмены показания");
        reading.IsCanceled = true;
        reading.Comment = AppendCancelReason(reading.Comment, reason);
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        AddAudit(actorUserId, "finance.meter_reading_canceled", reading, FormatMeterReadingCanceledAuditSummary(reading, reason));
        await RebuildPaymentAllocationsAsync(
            allocationKeys,
            actorUserId,
            "Перераспределение после отмены показания",
            reading.Id,
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConcurrencyException)
        {
            return MeterReadingConflict();
        }
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> RestoreMeterReadingAsync(Guid meterReadingId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reading = await meterReadingRepository.FindForUpdateAsync(meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        if (!reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_canceled", "Показание счетчика уже активно.");
        }

        await using var meterChainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(reading.GarageId, reading.MeterKind)],
            cancellationToken);
        await meterReadingRepository.ReloadForUpdateAsync(reading, cancellationToken);
        if (!reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_canceled", "Показание счетчика уже активно.");
        }

        if (await meterReadingRepository.ActiveDuplicateExistsAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "За этот гараж, месяц и счетчик уже есть активное показание.");
        }

        var previousReading = await meterReadingRepository.GetPreviousActiveAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            cancellationToken);
        var subsequentReadings = await meterReadingRepository.GetActiveFromForUpdateAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth.AddMonths(1),
            cancellationToken);
        var chainPlan = PlanMeterReadingChain(
            reading.Garage,
            reading.MeterKind,
            previousReading,
            new[] { reading }.Concat(subsequentReadings).ToArray());
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<MeterReadingDto>.Failure(chainPlan.ErrorCode!, chainPlan.ErrorMessage!);
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            chainPlan.Value!,
            missingReadingMonth: null,
            cancellationToken);
        var meteredSettings = await GetApplicableMeteredSettingsAsync(reading, cancellationToken);
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Concat(meteredSettings.Select(setting => new AccrualPaymentAllocationKey(reading.GarageId, setting.IncomeTypeId!.Value)))
            .Distinct()
            .ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(allocationKeys, cancellationToken);
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_accrual_paid",
                "Восстановление показания изменяет полностью или частично оплаченное начисление. Сначала исправьте оплату или оформите отдельную корректировку начисления.");
        }

        reading.IsCanceled = false;
        ApplyMeterReadingChainChanges(chainPlan.Value!, actorUserId, reading.Id);
        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Пересчет после восстановления показания");
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        var createdAccrualKeys = await CreateMissingMeteredAccrualsAsync(
            reading.Garage,
            reading,
            meteredSettings,
            actorUserId,
            cancellationToken);
        AddAudit(actorUserId, "finance.meter_reading_restored", reading, FormatMeterReadingRestoredAuditSummary(reading));
        await RebuildPaymentAllocationsAsync(
            allocationKeys.Concat(createdAccrualKeys).Distinct().ToArray(),
            actorUserId,
            "Перераспределение после восстановления показания",
            reading.Id,
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConcurrencyException)
        {
            return MeterReadingConflict();
        }
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    private static AccrualPaymentAllocationKey GetMeterChainLockKey(Guid garageId, string meterKind) =>
        new(garageId, meterKind switch
        {
            MeterKinds.Water => WaterMeterChainLockId,
            MeterKinds.Electricity => ElectricityMeterChainLockId,
            _ => MeterKinds.GetLockId(meterKind)
        });

    private static FinanceResult<IReadOnlyList<MeterReadingChainChange>> PlanMeterReadingChain(
        Garage garage,
        string meterKind,
        MeterReading? previousReading,
        IReadOnlyList<MeterReading> readings)
    {
        var changes = new List<MeterReadingChainChange>(readings.Count);
        var firstReading = readings.OrderBy(item => item.AccountingMonth).ThenBy(item => item.Id).FirstOrDefault();
        var sameDeviceAsFirst = firstReading is not null && MeterDevicesMatch(previousReading, firstReading);
        var previousValue = sameDeviceAsFirst
            ? previousReading!.CurrentValue
            : firstReading?.MeterDevice?.InitialValue ?? GetInitialMeterValue(garage, meterKind);
        if (!previousValue.HasValue && meterKind == MeterKinds.Water)
        {
            return FinanceResult<IReadOnlyList<MeterReadingChainChange>>.Failure(
                "water_meter_reading_baseline_required",
                "Для первого показания воды укажите стартовое значение счетчика в карточке гаража.");
        }

        foreach (var reading in readings.OrderBy(item => item.AccountingMonth).ThenBy(item => item.Id))
        {
            if (previousReading is not null && !MeterDevicesMatch(previousReading, reading))
            {
                previousValue = reading.MeterDevice?.InitialValue ?? GetInitialMeterValue(garage, meterKind);
            }

            var normalizedPreviousValue = MoneyMath.RoundMeterValue(previousValue ?? 0m);
            var consumption = MoneyMath.RoundMeterValue(
                reading.CurrentValue - normalizedPreviousValue + reading.PreviousDeviceConsumption);
            if (consumption < 0)
            {
                return FinanceResult<IReadOnlyList<MeterReadingChainChange>>.Failure(
                    "meter_reading_sequence_invalid",
                    $"Показание за {reading.AccountingMonth:MM.yyyy} меньше предыдущего активного показания. Проверьте последовательность или оформите замену счетчика.");
            }

            var hasGapWarning = HasGapWarning(meterKind, reading.AccountingMonth, previousReading);
            changes.Add(new MeterReadingChainChange(
                reading,
                normalizedPreviousValue,
                consumption,
                hasGapWarning,
                reading.PreviousValue != normalizedPreviousValue ||
                reading.Consumption != consumption ||
                reading.HasGapWarning != hasGapWarning));
            previousValue = reading.CurrentValue;
            previousReading = reading;
        }

        return FinanceResult<IReadOnlyList<MeterReadingChainChange>>.Success(changes);
    }

    private static bool MeterDevicesMatch(MeterReading? left, MeterReading right) =>
        left is not null && left.MeterDeviceId == right.MeterDeviceId;

    private static MeterReading CloneMeterReadingForChain(MeterReading reading, MeterDevice? meterDevice) => new()
    {
        Id = reading.Id,
        GarageId = reading.GarageId,
        Garage = reading.Garage,
        MeterDeviceId = meterDevice?.Id,
        MeterDevice = meterDevice,
        MeterKind = reading.MeterKind,
        AccountingMonth = reading.AccountingMonth,
        ReadingDate = reading.ReadingDate,
        CurrentValue = reading.CurrentValue,
        PreviousValue = reading.PreviousValue,
        PreviousDeviceConsumption = reading.PreviousDeviceConsumption,
        Consumption = reading.Consumption,
        IsMeterReplacement = reading.IsMeterReplacement,
        HasGapWarning = reading.HasGapWarning,
        Comment = reading.Comment,
        Version = reading.Version,
        CreatedAtUtc = reading.CreatedAtUtc,
        UpdatedAtUtc = reading.UpdatedAtUtc
    };

    private async Task<IReadOnlyList<MeteredAccrualRecalculation>> PlanMeteredAccrualRecalculationsAsync(
        Guid garageId,
        string meterKind,
        DateOnly accountingMonth,
        IReadOnlyList<MeterReadingChainChange> chainChanges,
        DateOnly? missingReadingMonth,
        CancellationToken cancellationToken)
    {
        var readingByMonth = chainChanges.ToDictionary(change => change.Reading.AccountingMonth);
        var accruals = await accrualRepository.GetActiveMeteredFromForUpdateAsync(
            garageId,
            accountingMonth,
            meterKind,
            cancellationToken);
        var recalculations = new List<MeteredAccrualRecalculation>();
        foreach (var accrual in accruals)
        {
            decimal? newAmount = null;
            AccrualCalculationDetailsDto? newDetails = null;
            if (missingReadingMonth.HasValue && accrual.AccountingMonth == missingReadingMonth.Value)
            {
                newAmount = 0m;
            }
            else if (readingByMonth.TryGetValue(accrual.AccountingMonth, out var change))
            {
                var prospectiveReading = new MeterReading
                {
                    GarageId = change.Reading.GarageId,
                    Garage = change.Reading.Garage,
                    MeterKind = change.Reading.MeterKind,
                    AccountingMonth = change.Reading.AccountingMonth,
                    ReadingDate = change.Reading.ReadingDate,
                    CurrentValue = change.Reading.CurrentValue,
                    PreviousValue = change.PreviousValue,
                    Consumption = change.Consumption,
                    HasGapWarning = change.HasGapWarning
                };
                var previousDetails = RegularAccrualCalculator.Deserialize(accrual.CalculationDetailsJson);
                var calculation = previousDetails is null
                    ? CalculateLegacyRegularAccrual(change.Reading.Garage, accrual, prospectiveReading)
                    : RegularAccrualCalculator.Calculate(
                        change.Reading.Garage,
                        accrual.AccountingMonth,
                        prospectiveReading,
                        RegularAccrualCalculator.FromSnapshot(previousDetails));
                if (calculation.Succeeded)
                {
                    newAmount = calculation.Amount;
                    newDetails = calculation.Details;
                }
            }

            if (newAmount.HasValue && (accrual.Amount != newAmount.Value || newDetails is not null))
            {
                recalculations.Add(new MeteredAccrualRecalculation(accrual, newAmount.Value, newDetails));
            }
        }

        return recalculations;
    }

    private void ApplyMeterReadingChainChanges(
        IReadOnlyList<MeterReadingChainChange> changes,
        Guid? actorUserId,
        Guid primaryReadingId)
    {
        foreach (var change in changes.Where(item => item.Changed))
        {
            var oldValues = new Dictionary<string, object?>
            {
                ["previousValue"] = change.Reading.PreviousValue,
                ["consumption"] = change.Reading.Consumption,
                ["hasGapWarning"] = change.Reading.HasGapWarning
            };
            change.Reading.PreviousValue = change.PreviousValue;
            change.Reading.Consumption = change.Consumption;
            change.Reading.HasGapWarning = change.HasGapWarning;
            change.Reading.Version = Guid.NewGuid();
            change.Reading.UpdatedAtUtc = timeProvider.GetUtcNow();
            if (change.Reading.Id != primaryReadingId)
            {
                AddAudit(
                    actorUserId,
                    "finance.meter_reading_chain_rebuilt",
                    change.Reading,
                    $"Цепочка показаний пересчитана за {change.Reading.AccountingMonth:MM.yyyy}: предыдущее значение {change.PreviousValue:0.###}, расход {change.Consumption:0.###}.",
                    oldValues,
                    new Dictionary<string, object?>
                    {
                        ["previousValue"] = change.PreviousValue,
                        ["consumption"] = change.Consumption,
                        ["hasGapWarning"] = change.HasGapWarning
                    });
            }
        }
    }

    private void ApplyMeteredAccrualRecalculations(
        IReadOnlyList<MeteredAccrualRecalculation> recalculations,
        Guid? actorUserId,
        string reason)
    {
        foreach (var recalculation in recalculations)
        {
            var accrual = recalculation.Accrual;
            var before = AccrualAuditSnapshot.From(accrual);
            var oldAmount = accrual.Amount;
            accrual.Amount = recalculation.NewAmount;
            if (recalculation.Details is not null)
            {
                accrual.CalculationDetailsJson = RegularAccrualCalculator.Serialize(recalculation.Details);
                accrual.RequiresMeterReading = recalculation.Details.RequiresMeter;
            }
            accrual.UpdatedAtUtc = timeProvider.GetUtcNow();
            AddAudit(
                actorUserId,
                "finance.accrual_updated_from_meter_reading",
                accrual,
                $"{reason}: было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.",
                new Dictionary<string, object?> { ["amount"] = oldAmount },
                new Dictionary<string, object?> { ["amount"] = recalculation.NewAmount });
        }
    }

    private static AmountCalculationResult CalculateRegularAccrualAmount(
        Garage garage,
        Tariff tariff,
        MeterReading? meterReading,
        bool useTieredElectricity = true)
    {
        return tariff.CalculationBase switch
        {
            TariffCalculationBases.Fixed => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate)),
            TariffCalculationBases.People => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate * garage.PeopleCount)),
            TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity => useTieredElectricity
                ? CalculateTieredMeterAmount(meterReading, tariff)
                : CalculateMeterAmount(meterReading, tariff.Rate),
            _ => AmountCalculationResult.Failure($"неподдерживаемая база расчета {tariff.CalculationBase}.")
        };
    }

    private static RegularAccrualCalculationResult CalculateLegacyRegularAccrual(
        Garage garage,
        Accrual accrual,
        MeterReading meterReading)
    {
        var tariff = accrual.Tariff!;
        var segment = CreateTariffSegment(
            accrual.AccountingMonth,
            accrual.AccountingMonth.AddMonths(1).AddDays(-1),
            tariff,
            TariffCalculationBases.GetUnitName(tariff.CalculationBase),
            UsesTieredElectricitySnapshot(accrual));

        return RegularAccrualCalculator.Calculate(garage, accrual.AccountingMonth, meterReading, [segment]);
    }

    private static IReadOnlyList<RegularAccrualSegmentDefinition> BuildRegularAccrualSegments(
        DateOnly accountingMonth,
        ChargeServiceSetting? setting,
        Tariff fallbackTariff)
    {
        var month = MonthPeriod.Normalize(accountingMonth);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var versions = setting?.TariffVersions
            .Where(version =>
                !version.IsArchived &&
                !version.Tariff.IsArchived &&
                version.EffectiveFrom <= monthEnd &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= month))
            .OrderBy(version => version.EffectiveFrom)
            .ToList() ?? [];
        if (versions.Count == 0)
        {
            var effectiveFrom = fallbackTariff.EffectiveFrom > month ? fallbackTariff.EffectiveFrom : month;
            if (effectiveFrom > monthEnd)
            {
                return [CreateMissingTariffSegment(month, monthEnd, setting?.UnitName)];
            }

            var result = new List<RegularAccrualSegmentDefinition>();
            if (effectiveFrom > month)
            {
                result.Add(CreateMissingTariffSegment(month, effectiveFrom.AddDays(-1), setting?.UnitName));
            }

            result.Add(CreateTariffSegment(
                effectiveFrom,
                monthEnd,
                fallbackTariff,
                setting?.UnitName,
                setting?.HasTieredTariff ?? ReadElectricityTiers(fallbackTariff).Count >= 2));
            return result;
        }

        var segments = new List<RegularAccrualSegmentDefinition>();
        var cursor = month;
        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            var nextVersionStart = index + 1 < versions.Count ? versions[index + 1].EffectiveFrom : (DateOnly?)null;
            var from = version.EffectiveFrom < month ? month : version.EffectiveFrom;
            var implicitTo = nextVersionStart?.AddDays(-1);
            var configuredTo = version.EffectiveTo;
            var to = configuredTo.HasValue && implicitTo.HasValue
                ? (configuredTo.Value < implicitTo.Value ? configuredTo.Value : implicitTo.Value)
                : configuredTo ?? implicitTo ?? monthEnd;
            if (to > monthEnd)
            {
                to = monthEnd;
            }
            if (from > cursor)
            {
                segments.Add(CreateMissingTariffSegment(cursor, from.AddDays(-1), setting?.UnitName));
            }

            if (to >= cursor)
            {
                var effectiveSegmentFrom = from < cursor ? cursor : from;
                segments.Add(CreateTariffSegment(
                    effectiveSegmentFrom,
                    to,
                    version.Tariff,
                    setting?.UnitName,
                    version.Tariff.Id == setting?.TariffId
                        ? setting.HasTieredTariff
                        : ReadElectricityTiers(version.Tariff).Count >= 2));
                cursor = to.AddDays(1);
            }
        }

        if (cursor <= monthEnd)
        {
            segments.Add(CreateMissingTariffSegment(cursor, monthEnd, setting?.UnitName));
        }

        return segments;
    }

    private static Tariff? SelectTariffForMonth(ChargeServiceSetting setting, DateOnly accountingMonth)
    {
        var month = MonthPeriod.Normalize(accountingMonth);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        return setting.Tariff
            ?? setting.TariffVersions
                .Where(version =>
                    !version.IsArchived &&
                    !version.Tariff.IsArchived &&
                    version.EffectiveFrom <= monthEnd &&
                    (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= month))
                .OrderBy(version => version.EffectiveFrom)
                .Select(version => version.Tariff)
                .FirstOrDefault();
    }

    private static RegularAccrualSegmentDefinition CreateTariffSegment(
        DateOnly from,
        DateOnly to,
        Tariff tariff,
        string? configuredUnitName,
        bool includeTiers)
    {
        var tiers = ReadElectricityTiers(tariff)
            .Select(tier => new RegularAccrualTariffTier(tier.UpperBound, tier.Rate))
            .ToArray();
        return new RegularAccrualSegmentDefinition(
            from,
            to,
            tariff.CalculationBase,
            tariff.Rate,
            NormalizeOptional(configuredUnitName) ?? TariffCalculationBases.GetUnitName(tariff.CalculationBase),
            includeTiers && tiers.Length >= 2 ? tiers : []);
    }

    private static RegularAccrualSegmentDefinition CreateMissingTariffSegment(
        DateOnly from,
        DateOnly to,
        string? configuredUnitName) =>
        new(from, to, null, 0m, NormalizeOptional(configuredUnitName) ?? string.Empty, []);

    private static string BuildRegularAccrualComment(Tariff tariff, string? comment, bool useTieredElectricity = true)
    {
        var snapshot = $"тариф {tariff.Name}: {FormatTariffRateSnapshot(tariff, useTieredElectricity)}, действует с {tariff.EffectiveFrom:dd.MM.yyyy}";
        var userComment = NormalizeOptional(comment);
        return userComment is null
            ? $"Автоначисление; {snapshot}."
            : $"{userComment}; {snapshot}.";
    }

    private static string BuildFeeCampaignAccrualComment(FeeCampaign campaign, string? comment)
    {
        var snapshot = $"сбор {campaign.Name}: взнос {MoneyFormatting.Format(campaign.ContributionAmount)}, цель {MoneyFormatting.Format(campaign.TargetAmount)}, действует с {campaign.StartsOn:dd.MM.yyyy}";
        if (campaign.EndsOn.HasValue)
        {
            snapshot = $"{snapshot} по {campaign.EndsOn.Value:dd.MM.yyyy}";
        }

        var goal = NormalizeOptional(campaign.Goal);
        if (goal is not null)
        {
            snapshot = $"{snapshot}, назначение: {goal}";
        }

        var userComment = NormalizeOptional(comment);
        return userComment is null
            ? $"Начисление сбора; {snapshot}."
            : $"{userComment}; {snapshot}.";
    }

    private async Task<IncomeType> GetOrCreateDebtTransferIncomeTypeAsync(CancellationToken cancellationToken)
    {
        var incomeType = await incomeTypeRepository.FindFirstActiveByCodeAsync(DebtTransferIncomeTypeCode, cancellationToken)
            ?? await incomeTypeRepository.FindFirstActiveByNameAsync(DebtTransferIncomeTypeName, cancellationToken);
        if (incomeType is not null)
        {
            if (!incomeType.IsSystem || incomeType.Code != DebtTransferIncomeTypeCode)
            {
                incomeType.IsSystem = true;
                incomeType.Code = DebtTransferIncomeTypeCode;
                incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            return incomeType;
        }

        incomeType = await incomeTypeRepository.FindFirstArchivedByCodeOrNameAsync(DebtTransferIncomeTypeCode, DebtTransferIncomeTypeName, cancellationToken);
        if (incomeType is not null)
        {
            incomeType.Name = DebtTransferIncomeTypeName;
            incomeType.Code = DebtTransferIncomeTypeCode;
            incomeType.IsSystem = true;
            incomeType.IsArchived = false;
            incomeType.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return incomeType;
        }

        incomeType = new IncomeType
        {
            Name = DebtTransferIncomeTypeName,
            Code = DebtTransferIncomeTypeCode,
            IsSystem = true
        };
        incomeTypeRepository.Add(incomeType);
        return incomeType;
    }

    private static string BuildDebtTransferComment(DateOnly sourceMonth, DateOnly targetMonth, string? comment)
    {
        var userComment = NormalizeOptional(comment);
        var transferComment = $"Перенос задолженности {sourceMonth:MM.yyyy} -> {targetMonth:MM.yyyy}";
        return userComment is null ? transferComment : $"{transferComment}: {userComment}";
    }

    private static string AppendDebtTransferComment(string? currentComment, string nextComment)
    {
        var normalized = NormalizeOptional(currentComment);
        var combined = normalized is null ? nextComment : $"{normalized}{Environment.NewLine}{nextComment}";
        return combined.Length <= 1000 ? combined : combined[^1000..];
    }

    private static string FormatDebtTransferCreatedAuditSummary(Accrual accrual, DateOnly sourceMonth, DateOnly targetMonth)
    {
        return $"Создан перенос задолженности {MoneyFormatting.Format(accrual.Amount)} по гаражу {accrual.Garage.Number} из {sourceMonth:MM.yyyy} в {targetMonth:MM.yyyy}.";
    }

    private static string FormatDebtTransferUpdatedAuditSummary(AccrualAuditSnapshot before, Accrual accrual, DateOnly sourceMonth, DateOnly targetMonth, decimal addedAmount)
    {
        return $"Дополнен перенос задолженности по гаражу {accrual.Garage.Number} из {sourceMonth:MM.yyyy} в {targetMonth:MM.yyyy}: добавлено {MoneyFormatting.Format(addedAmount)}; было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.";
    }

    private static string FormatIncomeCreatedAuditSummary(FinancialOperation operation)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Создано поступление {FormatIncomeOperationSnapshot(operation)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatIncomeUpdatedAuditSummary(string previousSnapshot, FinancialOperation operation)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Изменено поступление: было {previousSnapshot}; стало {FormatIncomeOperationSnapshot(operation)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatIncomeOperationSnapshot(FinancialOperation operation)
    {
        var amount = MoneyFormatting.Format(operation.Amount);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        return $"{amount} по гаражу {operation.Garage?.Number} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.IncomeType?.Name}; документ {document}";
    }

    private static string FormatExpenseCreatedAuditSummary(FinancialOperation operation)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Создана выплата {FormatExpenseOperationSnapshot(operation)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatStaffPaymentCreatedAuditSummary(FinancialOperation operation, decimal availableBeforePayment)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Создана выплата {FormatStaffPaymentSnapshot(operation)}; доступно до выплаты {MoneyFormatting.Format(availableBeforePayment)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatExpenseUpdatedAuditSummary(string previousSnapshot, FinancialOperation operation)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Изменена выплата: было {previousSnapshot}; стало {FormatExpenseOperationSnapshot(operation)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatExpenseOperationSnapshot(FinancialOperation operation)
    {
        var amount = MoneyFormatting.Format(operation.Amount);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        if (operation.StaffMember is not null)
        {
            return FormatStaffPaymentSnapshot(operation);
        }

        return $"{amount} получателю {GetExpenseCounterpartyName(operation)} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; услуга/статья {operation.ExpenseType?.Name}; источник {FormatExpensePaymentSource(operation)}; тип {FormatExpensePaymentType(operation.ExpensePaymentType)}; документ {document}";
    }

    private static string FormatStaffPaymentSnapshot(FinancialOperation operation)
    {
        var amount = MoneyFormatting.Format(operation.Amount);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        return $"{amount} сотруднику {operation.StaffMember?.FullName} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; отдел {operation.StaffMember?.Department?.Name}; вид {operation.ExpenseType?.Name}; документ {document}";
    }

    private static string FormatOperationCanceledAuditSummary(FinancialOperation operation, string reason)
    {
        var amount = MoneyFormatting.Format(operation.Amount);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            return $"Отменено поступление {amount} по гаражу {operation.Garage?.Number} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.IncomeType?.Name}; документ {document}. Причина: {reason}";
        }

        return operation.StaffMember is not null
            ? $"Отменена выплата {amount} сотруднику {operation.StaffMember.FullName} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}. Причина: {reason}"
            : $"Отменена выплата {amount} получателю {GetExpenseCounterpartyName(operation)} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; услуга/статья {operation.ExpenseType?.Name}; источник {FormatExpensePaymentSource(operation)}; тип {FormatExpensePaymentType(operation.ExpensePaymentType)}; документ {document}. Причина: {reason}";
    }

    private static string FormatOperationRestoredAuditSummary(FinancialOperation operation)
    {
        var amount = MoneyFormatting.Format(operation.Amount);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
            return $"Восстановлено поступление {amount} по гаражу {operation.Garage?.Number} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.IncomeType?.Name}; документ {document}.";
        }

        return operation.StaffMember is not null
            ? $"Восстановлена выплата {amount} сотруднику {operation.StaffMember.FullName} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}."
            : $"Восстановлена выплата {amount} получателю {GetExpenseCounterpartyName(operation)} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; услуга/статья {operation.ExpenseType?.Name}; источник {FormatExpensePaymentSource(operation)}; тип {FormatExpensePaymentType(operation.ExpensePaymentType)}; документ {document}.";
    }

    private static string FormatAccrualCreatedAuditSummary(Accrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var comment = NormalizeOptional(accrual.Comment);
        var accountingYear = accrual.AccountingYear.HasValue ? $"; учетный год {accrual.AccountingYear.Value}" : null;
        var summary = $"Создано начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}{accountingYear}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatAccrualUpdatedAuditSummary(AccrualAuditSnapshot before, Accrual accrual)
    {
        return $"Изменено начисление: было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.";
    }

    private static string FormatAccrualCanceledAuditSummary(Accrual accrual, string reason)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var accountingYear = accrual.AccountingYear.HasValue ? $"; учетный год {accrual.AccountingYear.Value}" : null;
        return $"Отменено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}{accountingYear}; вид {accrual.IncomeType.Name}; источник {accrual.Source}. Причина: {reason}";
    }

    private static string FormatAccrualRestoredAuditSummary(Accrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var accountingYear = accrual.AccountingYear.HasValue ? $"; учетный год {accrual.AccountingYear.Value}" : null;
        return $"Восстановлено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}{accountingYear}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
    }

    private static string FormatSupplierAccrualCreatedAuditSummary(SupplierAccrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var document = NormalizeOptional(accrual.DocumentNumber) ?? "без документа";
        var comment = NormalizeOptional(accrual.Comment);
        var summary = $"Создано начисление {amount} поставщику {accrual.Supplier.Name} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.ExpenseType.Name}; источник {accrual.Source}; документ {document}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatSupplierAccrualUpdatedAuditSummary(SupplierAccrualAuditSnapshot before, SupplierAccrual accrual)
    {
        return $"Изменено начисление поставщику: было {FormatSupplierAccrualSnapshot(before)}; стало {FormatSupplierAccrualSnapshot(SupplierAccrualAuditSnapshot.From(accrual))}.";
    }

    private static string FormatAccrualSnapshot(AccrualAuditSnapshot snapshot)
    {
        var amount = MoneyFormatting.Format(snapshot.Amount);
        var comment = NormalizeOptional(snapshot.Comment);
        var accountingYear = snapshot.AccountingYear.HasValue ? $"; учетный год {snapshot.AccountingYear.Value}" : null;
        var summary = $"{amount} по гаражу {snapshot.GarageNumber} за {snapshot.AccountingMonth:MM.yyyy}{accountingYear}; вид {snapshot.IncomeTypeName}; источник {snapshot.Source}";
        return comment is null ? summary : $"{summary}; комментарий {comment}";
    }

    private static string FormatSupplierAccrualSnapshot(SupplierAccrualAuditSnapshot snapshot)
    {
        var amount = MoneyFormatting.Format(snapshot.Amount);
        var document = NormalizeOptional(snapshot.DocumentNumber) ?? "без документа";
        var comment = NormalizeOptional(snapshot.Comment);
        var summary = $"{amount} поставщику {snapshot.SupplierName} за {snapshot.AccountingMonth:MM.yyyy}; вид {snapshot.ExpenseTypeName}; источник {snapshot.Source}; документ {document}";
        return comment is null ? summary : $"{summary}; комментарий {comment}";
    }

    private static string FormatSupplierAccrualCanceledAuditSummary(SupplierAccrual accrual, string reason)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var document = NormalizeOptional(accrual.DocumentNumber) ?? "без документа";
        return $"Отменено начисление {amount} поставщику {accrual.Supplier.Name} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.ExpenseType.Name}; источник {accrual.Source}; документ {document}. Причина: {reason}";
    }

    private static string FormatSupplierAccrualRestoredAuditSummary(SupplierAccrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var document = NormalizeOptional(accrual.DocumentNumber) ?? "без документа";
        return $"Восстановлено начисление {amount} поставщику {accrual.Supplier.Name} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.ExpenseType.Name}; источник {accrual.Source}; документ {document}.";
    }

    private static string FormatMeterReadingCanceledAuditSummary(MeterReading reading, string reason)
    {
        return $"Отменено показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; расход {reading.Consumption.ToString("0.####", RussianCulture)}. Причина: {reason}";
    }

    private static string FormatMeterReadingRestoredAuditSummary(MeterReading reading)
    {
        return $"Восстановлено показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; расход {reading.Consumption.ToString("0.####", RussianCulture)}.";
    }

    private static string FormatMeterReadingCreatedAuditSummary(MeterReading reading)
    {
        var warning = reading.HasGapWarning ? "есть предупреждение по разрыву истории" : "без предупреждения";
        var comment = NormalizeOptional(reading.Comment);
        var summary = $"Внесено показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; предыдущее {reading.PreviousValue.ToString("0.###", RussianCulture)}, текущее {reading.CurrentValue.ToString("0.###", RussianCulture)}, расход {reading.Consumption.ToString("0.###", RussianCulture)}; {warning}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatMeterReadingUpdatedAuditSummary(MeterReading reading)
    {
        var warning = reading.HasGapWarning ? "есть предупреждение по разрыву истории" : "без предупреждения";
        var comment = NormalizeOptional(reading.Comment);
        var summary = $"Изменено показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; предыдущее {reading.PreviousValue.ToString("0.###", RussianCulture)}, текущее {reading.CurrentValue.ToString("0.###", RussianCulture)}, расход {reading.Consumption.ToString("0.###", RussianCulture)}; {warning}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatRegularAccrualGenerationAuditSummary(DateOnly month, IncomeType incomeType, Tariff tariff, IReadOnlyCollection<AccrualDto> created, IReadOnlyCollection<string> skipped, bool useTieredElectricity)
    {
        var totalAmount = MoneyFormatting.Format(created.Sum(item => item.Amount));
        return $"Создано регулярных начислений: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; вид {incomeType.Name}; тариф {tariff.Name}, база {tariff.CalculationBase}, {FormatTariffRateSnapshot(tariff, useTieredElectricity)}; пропущено {skipped.Count}.";
    }

    private static string FormatFeeCampaignAccrualGenerationAuditSummary(
        DateOnly month,
        FeeCampaign campaign,
        IncomeType incomeType,
        IReadOnlyCollection<AccrualDto> created,
        IReadOnlyCollection<string> skipped)
    {
        var totalAmount = MoneyFormatting.Format(created.Sum(item => item.Amount));
        return $"Создано начислений по сбору: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; сбор {campaign.Name}; назначение {incomeType.Name}; взнос {MoneyFormatting.Format(campaign.ContributionAmount)}; пропущено {skipped.Count}.";
    }

    private static string FormatSupplierGroupSalaryAccrualGenerationAuditSummary(DateOnly month, string groupName, string expenseTypeName, IReadOnlyCollection<SupplierAccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = MoneyFormatting.Format(created.Sum(item => item.Amount));
        return $"Создано начислений зарплаты: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; группа {groupName}; вид {expenseTypeName}; пропущено {skipped.Count}.";
    }

    private static string BuildSupplierGroupSalaryComment(string groupName, string? comment)
    {
        var baseComment = $"Зарплата по группе {groupName}";
        return comment is null ? baseComment : $"{baseComment}. {comment}";
    }

    private static AmountCalculationResult CalculateMeterAmount(MeterReading? reading, decimal rate)
    {
        return reading is null
            ? AmountCalculationResult.Failure("нет показания счетчика за месяц.")
            : AmountCalculationResult.Success(MoneyMath.RoundMoney(reading.Consumption * rate));
    }

    private static AmountCalculationResult CalculateTieredMeterAmount(MeterReading? reading, Tariff tariff)
    {
        if (reading is null)
        {
            return AmountCalculationResult.Failure("нет показания счетчика за месяц.");
        }

        var tiers = ReadElectricityTiers(tariff);
        if (tiers.Count == 0)
        {
            return AmountCalculationResult.Success(MoneyMath.RoundMoney(reading.Consumption * tariff.Rate));
        }

        var activeTier = tiers.FirstOrDefault(tier =>
            !tier.UpperBound.HasValue || reading.CurrentValue <= tier.UpperBound.Value) ?? tiers[^1];
        return AmountCalculationResult.Success(MoneyMath.RoundMoney(reading.Consumption * activeTier.Rate));
    }

    private static string FormatTariffRateSnapshot(Tariff tariff, bool useTieredTariff = true)
    {
        var tiers = useTieredTariff ? ReadElectricityTiers(tariff) : [];
        if (tiers.Count == 0)
        {
            return $"ставка {MoneyFormatting.Format(tariff.Rate)}";
        }

        var unitName = TariffCalculationBases.GetUnitName(tariff.CalculationBase);
        var details = string.Join(", ", tiers.Select(tier => tier.UpperBound.HasValue
            ? $"до {tier.UpperBound.Value.ToString("0.####", RussianCulture)} {unitName} по {MoneyFormatting.Format(tier.Rate)}"
            : $"свыше по {MoneyFormatting.Format(tier.Rate)}"));
        return $"пороговый тариф по текущему показанию: {details}";
    }

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetApplicableMeteredSettingsAsync(
        MeterReading reading,
        CancellationToken cancellationToken) =>
        await GetApplicableMeteredSettingsAsync(reading.MeterKind, reading.AccountingMonth, cancellationToken);

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetApplicableMeteredSettingsAsync(
        string meterKind,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var calculationBase = meterKind switch
        {
            MeterKinds.Water => TariffCalculationBases.MeterWater,
            MeterKinds.Electricity => TariffCalculationBases.MeterElectricity,
            _ => null
        };
        var settings = calculationBase is null
            ? await chargeServiceSettingRepository.GetActiveRegularMeteredAsync(
                accountingMonth,
                MaxAutomaticMeteredServices,
                cancellationToken)
            : await chargeServiceSettingRepository.GetActiveRegularMeteredAsync(
                calculationBase,
                accountingMonth,
                MaxAutomaticMeteredServices,
                cancellationToken);
        return settings
            .Where(setting => string.Equals(setting.MeterKind, meterKind, StringComparison.Ordinal) &&
                IsChargeServiceDueForMonth(setting, accountingMonth))
            .ToArray();
    }

    private async Task<IReadOnlyList<AccrualPaymentAllocationKey>> CreateMissingMeteredAccrualsAsync(
        Garage garage,
        MeterReading reading,
        IReadOnlyList<ChargeServiceSetting> settings,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var createdKeys = new List<AccrualPaymentAllocationKey>();
        var processedIncomeTypeIds = new HashSet<Guid>();
        foreach (var setting in settings)
        {
            var incomeType = setting.IncomeType;
            var tariff = SelectTariffForMonth(setting, reading.AccountingMonth);
            if (incomeType is null || tariff is null || !setting.IncomeTypeId.HasValue)
            {
                continue;
            }

            if (!processedIncomeTypeIds.Add(incomeType.Id))
            {
                continue;
            }

            if (await accrualRepository.ActiveDuplicateExistsAsync(
                ignoredId: null,
                garage.Id,
                incomeType.Id,
                reading.AccountingMonth,
                accountingYear: null,
                AccrualSources.Regular,
                cancellationToken))
            {
                continue;
            }

            var calculationSegments = BuildRegularAccrualSegments(reading.AccountingMonth, setting, tariff);
            var useTieredTariff = calculationSegments.Any(segment => segment.Tiers.Count > 0);
            var calculation = RegularAccrualCalculator.Calculate(
                garage,
                reading.AccountingMonth,
                reading,
                calculationSegments);
            if (!calculation.Succeeded || calculation.Amount <= 0m)
            {
                continue;
            }

            var dueDates = AccrualDueDates.ForGarage(reading.AccountingMonth, incomeType.Code, setting, GetGarageRegistrationDate(garage));
            var accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                TariffId = tariff.Id,
                Tariff = tariff,
                AccountingMonth = reading.AccountingMonth,
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = calculation.Amount,
                RequiresMeterReading = calculation.Details!.RequiresMeter,
                CalculationMeterKind = calculation.Details.RequiresMeter ? reading.MeterKind : null,
                CalculationDetailsJson = RegularAccrualCalculator.Serialize(calculation.Details),
                Source = AccrualSources.Regular,
                Comment = BuildRegularAccrualComment(
                    tariff,
                    $"Начисление по показанию {reading.MeterKind}: расход {reading.Consumption.ToString("0.###", RussianCulture)}",
                    useTieredTariff)
            };
            accrualRepository.Add(accrual);
            AddAudit(
                actorUserId,
                "finance.metered_accrual_created_from_reading",
                accrual,
                $"По показанию счетчика автоматически создано начисление. {FormatAccrualCreatedAuditSummary(accrual)}");
            createdKeys.Add(new AccrualPaymentAllocationKey(garage.Id, incomeType.Id));
        }

        return createdKeys;
    }

    private static bool UsesTieredElectricitySnapshot(Accrual accrual)
    {
        return accrual.Tariff?.CalculationBase is TariffCalculationBases.MeterElectricity or TariffCalculationBases.MeterWater
            && (accrual.Comment?.Contains("пороговый тариф", StringComparison.OrdinalIgnoreCase) == true
                || accrual.Comment?.Contains("пороги электроэнергии", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static IReadOnlyList<ElectricityTierSnapshot> ReadElectricityTiers(Tariff tariff)
    {
        if (!string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<ElectricityTierSnapshot>>(
                    tariff.ElectricityTiersJson,
                    PersistedJsonOptions);
                if (stored is { Count: >= 2 })
                {
                    return stored;
                }
            }
            catch (JsonException)
            {
                // При поврежденном JSON старые поля тарифа остаются безопасным резервом.
            }
        }

        return tariff.ElectricityFirstThreshold.HasValue
            && tariff.ElectricitySecondThreshold.HasValue
            && tariff.ElectricityFirstRate.HasValue
            && tariff.ElectricitySecondRate.HasValue
            && tariff.ElectricityThirdRate.HasValue
                ?
                [
                    new ElectricityTierSnapshot(tariff.ElectricityFirstThreshold, tariff.ElectricityFirstRate.Value),
                    new ElectricityTierSnapshot(tariff.ElectricitySecondThreshold, tariff.ElectricitySecondRate.Value),
                    new ElectricityTierSnapshot(null, tariff.ElectricityThirdRate.Value)
                ]
                : [];
    }

    private sealed record ElectricityTierSnapshot(decimal? UpperBound, decimal Rate);

    private static string[] NormalizeMeterKindFilter(string? meterKind)
    {
        if (!string.IsNullOrWhiteSpace(meterKind))
        {
            var normalized = meterKind.Trim().ToLowerInvariant();
            return MeterKinds.IsValid(normalized) ? [normalized] : [];
        }

        return [MeterKinds.Water, MeterKinds.Electricity];
    }

    private static string? NormalizeSearch(string? search)
    {
        return string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
    }

    private async Task<bool> HasDocumentDuplicateAsync(string operationKind, string? documentNumber, DateOnly operationDate, CancellationToken cancellationToken)
    {
        return await HasDocumentDuplicateAsync(operationKind, documentNumber, operationDate, null, cancellationToken);
    }

    private async Task<bool> HasDocumentDuplicateAsync(string operationKind, string? documentNumber, DateOnly operationDate, Guid? excludeOperationId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOptional(documentNumber);
        return normalized is not null && await financialOperationRepository.ActiveDocumentDuplicateExistsAsync(
            excludeOperationId,
            operationKind,
            operationDate,
            normalized,
            cancellationToken);
    }

    private async Task RebuildPaymentAllocationsAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        Guid? actorUserId,
        string trigger,
        Guid sourceEntityId,
        CancellationToken cancellationToken)
    {
        var result = await accrualPaymentAllocationRepository.RebuildAsync(keys, cancellationToken);
        if (result.PreviousActiveAllocationCount == 0 && result.ActiveAllocationCount == 0)
        {
            return;
        }

        AddAudit(
            actorUserId,
            "finance.payment_allocations_rebuilt",
            "payment_allocation",
            sourceEntityId,
            $"{trigger}: перераспределены платежи по начислениям; пар учета {result.KeyCount}, активных распределений {result.ActiveAllocationCount}.",
            metadata: new Dictionary<string, object?>
            {
                ["trigger"] = trigger,
                ["keyCount"] = result.KeyCount,
                ["previousActiveAllocationCount"] = result.PreviousActiveAllocationCount,
                ["activeAllocationCount"] = result.ActiveAllocationCount
            });
    }

    private static string FormatAtomicCashExpenseCreatedAuditSummary(FinancialOperation operation)
    {
        var comment = NormalizeOptional(operation.Comment);
        var summary = $"Атомарно созданы стоимость и оплата выплаты {FormatExpenseOperationSnapshot(operation)}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatExpensePaymentType(string? expensePaymentType) =>
        expensePaymentType == ExpensePaymentTypes.WithoutReceipt ? "без чека" : "с чеком";

    private static string FormatExpensePaymentSource(FinancialOperation operation) =>
        IsCashExpense(operation) ? "касса" : "банк";

    private static string GetExpenseCounterpartyName(FinancialOperation operation) =>
        operation.Supplier?.Name ?? NormalizeOptional(operation.CounterpartyName) ?? "не указан";

    private static string FormatHistoricalMeterReadingCorrectedAuditSummary(MeterReading reading)
    {
        return $"Скорректировано показание другого периода {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; предыдущее {reading.PreviousValue.ToString("0.###", RussianCulture)}, текущее {reading.CurrentValue.ToString("0.###", RussianCulture)}, расход {reading.Consumption.ToString("0.###", RussianCulture)}.";
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        FinancialOperation operation,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null)
    {
        var relatedGarageId = operation.GarageId?.ToString();
        var relatedGarageNumber = operation.Garage?.Number;
        var relatedCounterpartyId = operation.SupplierId?.ToString() ?? operation.StaffMemberId?.ToString();
        var relatedCounterpartyName = operation.Supplier?.Name ?? operation.StaffMember?.FullName ?? operation.CounterpartyName;
        var metadata = new Dictionary<string, object?>
        {
            ["financeEntityType"] = "financial_operation",
            ["operationKind"] = operation.OperationKind,
            ["operationDate"] = operation.OperationDate,
            ["amount"] = operation.Amount,
            ["counterpartyName"] = operation.CounterpartyName,
            ["negativeFundBalanceConfirmed"] = operation.NegativeFundBalanceConfirmed
        };
        if (operation.StaffMember is not null)
        {
            metadata["staffMemberId"] = operation.StaffMember.Id;
            metadata["staffMemberName"] = operation.StaffMember.FullName;
            metadata["staffDepartmentName"] = operation.StaffMember.Department?.Name;
        }

        AddAudit(
            actorUserId,
            action,
            "financial_operation",
            operation.Id,
            summary,
            operation.AccountingMonth,
            operation.Id.ToString(),
            operation.DocumentNumber,
            relatedGarageId,
            relatedGarageNumber,
            relatedCounterpartyId,
            relatedCounterpartyName,
            metadata,
            oldValues,
            newValues);
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        Accrual accrual,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null)
    {
        AddAudit(
            actorUserId,
            action,
            "accrual",
            accrual.Id,
            summary,
            accrual.AccountingMonth,
            accrual.Id.ToString(),
            null,
            accrual.GarageId.ToString(),
            accrual.Garage.Number,
            null,
            null,
            new Dictionary<string, object?>
            {
                ["financeEntityType"] = "accrual",
                ["incomeTypeId"] = accrual.IncomeTypeId,
                ["incomeTypeName"] = accrual.IncomeType.Name,
                ["source"] = accrual.Source,
                ["amount"] = accrual.Amount
            },
            oldValues,
            newValues);
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        SupplierAccrual accrual,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null)
    {
        AddAudit(
            actorUserId,
            action,
            "supplier_accrual",
            accrual.Id,
            summary,
            accrual.AccountingMonth,
            accrual.Id.ToString(),
            accrual.DocumentNumber,
            null,
            null,
            accrual.SupplierId.ToString(),
            accrual.Supplier.Name,
            new Dictionary<string, object?>
            {
                ["financeEntityType"] = "supplier_accrual",
                ["expenseTypeId"] = accrual.ExpenseTypeId,
                ["expenseTypeName"] = accrual.ExpenseType.Name,
                ["source"] = accrual.Source,
                ["amount"] = accrual.Amount
            },
            oldValues,
            newValues);
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        MeterReading reading,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null,
        string? reason = null)
    {
        AddAudit(
            actorUserId,
            action,
            "meter_reading",
            reading.Id,
            summary,
            reading.AccountingMonth,
            reading.Id.ToString(),
            reading.MeterKind,
            reading.GarageId.ToString(),
            reading.Garage.Number,
            null,
            null,
            new Dictionary<string, object?>
            {
                ["financeEntityType"] = "meter_reading",
                ["meterKind"] = reading.MeterKind,
                ["readingDate"] = reading.ReadingDate,
                ["currentValue"] = reading.CurrentValue,
                ["previousValue"] = reading.PreviousValue,
                ["consumption"] = reading.Consumption
            },
            oldValues,
            newValues,
            reason);
    }

    private void AddAudit(
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        DateOnly? relatedAccountingMonth = null,
        string? relatedDocumentId = null,
        string? relatedDocumentNumber = null,
        string? relatedGarageId = null,
        string? relatedGarageNumber = null,
        string? relatedCounterpartyId = null,
        string? relatedCounterpartyName = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null,
        string? reason = null)
    {
        var mergedMetadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["financeEntityType"] = entityType
        };
        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                mergedMetadata[key] = value;
            }
        }

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            entityType,
            entityId.ToString(),
            Summary: summary,
            EntityDisplayName: NormalizeAuditDisplayName(summary),
            Reason: reason ?? (action.Contains("_canceled", StringComparison.Ordinal) ? "Отмена финансовой записи." : null),
            OldValues: oldValues,
            NewValues: newValues,
            FieldLabels: oldValues is null || newValues is null ? null : FinanceFieldLabels,
            Metadata: mergedMetadata,
            RelatedGarageId: relatedGarageId,
            RelatedGarageNumber: relatedGarageNumber,
            RelatedAccountingMonth: relatedAccountingMonth?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            RelatedCounterpartyId: relatedCounterpartyId,
            RelatedCounterpartyName: relatedCounterpartyName,
            RelatedDocumentId: relatedDocumentId,
            RelatedDocumentNumber: relatedDocumentNumber));
    }

    private static string NormalizeAuditDisplayName(string summary)
    {
        return summary.Trim().TrimEnd('.');
    }

    private static bool IncomeOperationMatches(FinancialOperation operation, DateOnly operationDate, DateOnly accountingMonth, decimal amount, string? documentNumber, string? comment, Guid garageId, Guid incomeTypeId)
    {
        return operation.OperationDate == operationDate &&
            operation.AccountingMonth == accountingMonth &&
            operation.Amount == amount &&
            StringEquals(operation.DocumentNumber, documentNumber) &&
            StringEquals(operation.Comment, comment) &&
            operation.GarageId == garageId &&
            operation.IncomeTypeId == incomeTypeId;
    }

    private static bool ExpenseOperationMatches(
        FinancialOperation operation,
        DateOnly operationDate,
        DateOnly accountingMonth,
        decimal amount,
        string? documentNumber,
        string? comment,
        Guid supplierId,
        Guid expenseTypeId,
        string expensePaymentType,
        string expensePaymentSource,
        Guid? expenseFundId)
    {
        return operation.OperationDate == operationDate &&
            operation.AccountingMonth == accountingMonth &&
            operation.Amount == amount &&
            StringEquals(operation.DocumentNumber, documentNumber) &&
            StringEquals(operation.Comment, comment) &&
            operation.SupplierId == supplierId &&
            operation.ExpenseTypeId == expenseTypeId &&
            operation.ExpensePaymentType == expensePaymentType &&
            NormalizeExpensePaymentSource(operation.ExpensePaymentSource, operation.ExpensePaymentType) == expensePaymentSource &&
            operation.ExpenseFundId == expenseFundId;
    }

    private static bool AccrualMatches(Accrual accrual, Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, int? accountingYear, decimal amount, string source, string? comment)
    {
        return accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.AccountingYear == accountingYear &&
            accrual.Amount == amount &&
            StringEquals(accrual.Source, source) &&
            StringEquals(accrual.Comment, comment);
    }

    private static bool SupplierAccrualMatches(SupplierAccrual accrual, Guid supplierId, Guid expenseTypeId, DateOnly accountingMonth, decimal amount, string source, string? documentNumber, string? comment)
    {
        return accrual.SupplierId == supplierId &&
            accrual.ExpenseTypeId == expenseTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Amount == amount &&
            StringEquals(accrual.Source, source) &&
            StringEquals(accrual.DocumentNumber, documentNumber) &&
            StringEquals(accrual.Comment, comment);
    }

    private static bool MeterReadingMatches(MeterReading reading, Guid garageId, string meterKind, DateOnly accountingMonth, DateOnly readingDate, decimal currentValue, decimal previousValue, decimal consumption, bool hasGapWarning, string? comment)
    {
        return reading.GarageId == garageId &&
            StringEquals(reading.MeterKind, meterKind) &&
            reading.AccountingMonth == accountingMonth &&
            reading.ReadingDate == readingDate &&
            reading.CurrentValue == currentValue &&
            reading.PreviousValue == previousValue &&
            reading.Consumption == consumption &&
            reading.HasGapWarning == hasGapWarning &&
            StringEquals(reading.Comment, comment);
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeIncomeTypeCode(string? value)
    {
        return NormalizeOptional(value)?.ToLowerInvariant();
    }

    private static string AppendCancelReason(string? comment, string reason)
    {
        var cancelComment = $"Отменено: {reason}";
        var normalized = NormalizeOptional(comment);
        return normalized is null ? cancelComment : $"{normalized}{Environment.NewLine}{cancelComment}";
    }

    private static decimal? GetInitialMeterValue(GarageBalance.Api.Domain.Dictionaries.Garage garage, string meterKind)
    {
        return meterKind switch
        {
            MeterKinds.Water => garage.InitialWaterMeterValue,
            MeterKinds.Electricity => garage.InitialElectricityMeterValue,
            _ => 0m
        };
    }

    private static bool HasGapWarning(string meterKind, DateOnly month, MeterReading? previousReading)
    {
        return meterKind != MeterKinds.Water && (previousReading is null || previousReading.AccountingMonth < month.AddMonths(-1));
    }

    private async Task<IReadOnlyList<FinancialOperationDto>> ToOperationDtosAsync(IReadOnlyList<FinancialOperation> operations, CancellationToken cancellationToken)
    {
        var calculatedOperationIds = operations
            .Where(operation =>
                (operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId is not null) ||
                (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId is not null))
            .Select(operation => operation.Id)
            .ToArray();
        var displayData = await financialOperationDisplayQuery.GetAsync(calculatedOperationIds, cancellationToken);
        var calculationsByOperationId = displayData.Calculations.ToDictionary(item => item.OperationId);
        var bucketsByCounterparty = displayData.AccrualBuckets
            .GroupBy(item => (item.CounterpartyKind, item.CounterpartyId))
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.AccountingMonth).ToList());
        var result = new List<FinancialOperationDto>(operations.Count);
        foreach (var operation in operations)
        {
            if (!calculationsByOperationId.TryGetValue(operation.Id, out var calculation))
            {
                result.Add(ToDto(operation, null, null, null, null, []));
                continue;
            }

            bucketsByCounterparty.TryGetValue(
                (calculation.CounterpartyKind, calculation.CounterpartyId),
                out var counterpartyBuckets);
            var accrualBuckets = (counterpartyBuckets ?? [])
                .Where(bucket => bucket.AccountingMonth <= operation.AccountingMonth)
                .Select(bucket => new AllocationDebtBucket(
                    "month",
                    bucket.AccountingMonth,
                    $"{bucket.AccountingMonth:MM.yyyy}",
                    bucket.Amount))
                .ToList();
            var startingBalance = operation.OperationKind == FinancialOperationKinds.Income
                ? operation.Garage!.StartingBalance
                : operation.Supplier!.StartingBalance;
            var accrualTotal = accrualBuckets.Sum(bucket => bucket.Amount);
            var debtBefore = MoneyMath.RoundMoney(startingBalance + accrualTotal - calculation.PreviousPaymentTotal);
            var allocationBuckets = new List<AllocationDebtBucket>(accrualBuckets.Count + 1);
            if (startingBalance > 0)
            {
                allocationBuckets.Add(new AllocationDebtBucket("starting_balance", null, "Стартовый баланс", startingBalance));
            }

            allocationBuckets.AddRange(accrualBuckets);
            var allocations = BuildPaymentAllocations(
                allocationBuckets,
                calculation.PreviousPaymentTotal + Math.Max(-startingBalance, 0),
                operation.Amount);
            result.Add(operation.OperationKind == FinancialOperationKinds.Income
                ? ToDto(operation, debtBefore, debtBefore - operation.Amount, null, null, allocations)
                : ToDto(operation, null, null, debtBefore, debtBefore - operation.Amount, allocations));
        }

        return result;
    }

    private async Task<FinancialOperationDto> ToDtoAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        decimal? garageDebtBefore = null;
        decimal? garageDebtAfter = null;
        decimal? supplierDebtBefore = null;
        decimal? supplierDebtAfter = null;
        IReadOnlyList<PaymentAllocationDto> paymentAllocations = [];
        if (operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId is not null)
        {
            garageDebtBefore = await CalculateGarageDebtBeforeIncomeAsync(operation, cancellationToken);
            garageDebtAfter = garageDebtBefore - operation.Amount;
            paymentAllocations = await CalculateGaragePaymentAllocationsAsync(operation, cancellationToken);
        }
        else if (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId is not null)
        {
            supplierDebtBefore = await CalculateSupplierDebtBeforeExpenseAsync(operation, cancellationToken);
            supplierDebtAfter = supplierDebtBefore - operation.Amount;
            paymentAllocations = await CalculateSupplierPaymentAllocationsAsync(operation, cancellationToken);
        }

        return ToDto(operation, garageDebtBefore, garageDebtAfter, supplierDebtBefore, supplierDebtAfter, paymentAllocations);
    }

    private async Task<decimal> CalculateGarageDebtBeforeIncomeAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var garageId = operation.GarageId!.Value;
        var startingBalance = operation.Garage?.StartingBalance ?? await garageRepository.GetStartingBalanceAsync(garageId, cancellationToken);
        var accrualTotal = await accrualRepository.GetTotalThroughMonthAsync(garageId, operation.AccountingMonth, cancellationToken);
        var previousIncomeTotal = await financialOperationRepository.GetPreviousGarageIncomeTotalAsync(
            operation.Id,
            garageId,
            operation.OperationDate,
            cancellationToken);

        return MoneyMath.RoundMoney(startingBalance + accrualTotal - previousIncomeTotal);
    }

    private async Task<IReadOnlyList<PaymentAllocationDto>> CalculateGaragePaymentAllocationsAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var garageId = operation.GarageId!.Value;
        var startingBalance = operation.Garage?.StartingBalance ?? await garageRepository.GetStartingBalanceAsync(garageId, cancellationToken);
        var previousIncomeTotal = await financialOperationRepository.GetPreviousGarageIncomeTotalAsync(
            operation.Id,
            garageId,
            operation.OperationDate,
            cancellationToken);
        var accrualBucketRows = await accrualRepository.GetMonthlyBucketsAsync(garageId, null, operation.AccountingMonth, cancellationToken);
        var accrualBuckets = accrualBucketRows
            .Select(bucket => new AllocationDebtBucket("month", bucket.AccountingMonth, $"{bucket.AccountingMonth:MM.yyyy}", bucket.Amount))
            .ToList();
        var buckets = new List<AllocationDebtBucket>(accrualBuckets.Count + 1);
        if (startingBalance > 0)
        {
            buckets.Add(new AllocationDebtBucket("starting_balance", null, "Стартовый баланс", startingBalance));
        }

        buckets.AddRange(accrualBuckets);
        return BuildPaymentAllocations(buckets, previousIncomeTotal + Math.Max(-startingBalance, 0), operation.Amount);
    }

    private async Task<decimal> CalculateSupplierDebtBeforeExpenseAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var supplierId = operation.SupplierId!.Value;
        var expenseTypeId = operation.ExpenseTypeId!.Value;
        var startingBalance = operation.Supplier?.StartingBalance ?? await supplierRepository.GetStartingBalanceAsync(supplierId, cancellationToken);
        var accrualTotal = await supplierAccrualRepository.GetTotalThroughMonthAsync(supplierId, expenseTypeId, operation.AccountingMonth, cancellationToken);
        var previousExpenseTotal = await financialOperationRepository.GetPreviousSupplierExpenseTotalAsync(
            operation.Id,
            supplierId,
            expenseTypeId,
            operation.OperationDate,
            cancellationToken);

        return MoneyMath.RoundMoney(startingBalance + accrualTotal - previousExpenseTotal);
    }

    private async Task<IReadOnlyList<PaymentAllocationDto>> CalculateSupplierPaymentAllocationsAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var supplierId = operation.SupplierId!.Value;
        var expenseTypeId = operation.ExpenseTypeId!.Value;
        var startingBalance = operation.Supplier?.StartingBalance ?? await supplierRepository.GetStartingBalanceAsync(supplierId, cancellationToken);
        var previousExpenseTotal = await financialOperationRepository.GetPreviousSupplierExpenseTotalAsync(
            operation.Id,
            supplierId,
            expenseTypeId,
            operation.OperationDate,
            cancellationToken);
        var accrualBucketRows = await supplierAccrualRepository.GetMonthlyBucketsThroughMonthAsync(
            supplierId,
            expenseTypeId,
            operation.AccountingMonth,
            cancellationToken);
        var accrualBuckets = accrualBucketRows
            .Select(bucket => new AllocationDebtBucket("month", bucket.AccountingMonth, $"{bucket.AccountingMonth:MM.yyyy}", bucket.Amount))
            .ToList();
        var buckets = new List<AllocationDebtBucket>(accrualBuckets.Count + 1);
        if (startingBalance > 0)
        {
            buckets.Add(new AllocationDebtBucket("starting_balance", null, "Стартовый баланс", startingBalance));
        }

        buckets.AddRange(accrualBuckets);
        return BuildPaymentAllocations(buckets, previousExpenseTotal + Math.Max(-startingBalance, 0), operation.Amount);
    }

    private static IReadOnlyList<PaymentAllocationDto> BuildPaymentAllocations(IReadOnlyList<AllocationDebtBucket> buckets, decimal previousPaymentTotal, decimal paymentAmount)
    {
        var remainingPreviousPayment = MoneyMath.RoundMoney(previousPaymentTotal);
        var remainingPayment = MoneyMath.RoundMoney(paymentAmount);
        var allocations = new List<PaymentAllocationDto>();

        foreach (var bucket in buckets)
        {
            var debtBeforeCurrentPayment = MoneyMath.RoundMoney(bucket.Amount);
            if (remainingPreviousPayment > 0)
            {
                var previousPaid = Math.Min(debtBeforeCurrentPayment, remainingPreviousPayment);
                debtBeforeCurrentPayment = MoneyMath.RoundMoney(debtBeforeCurrentPayment - previousPaid);
                remainingPreviousPayment = MoneyMath.RoundMoney(remainingPreviousPayment - previousPaid);
            }

            if (debtBeforeCurrentPayment <= 0 || remainingPayment <= 0)
            {
                continue;
            }

            var paidAmount = Math.Min(debtBeforeCurrentPayment, remainingPayment);
            allocations.Add(new PaymentAllocationDto(
                bucket.Kind,
                bucket.AccountingMonth,
                bucket.Label,
                debtBeforeCurrentPayment,
                paidAmount,
                MoneyMath.RoundMoney(debtBeforeCurrentPayment - paidAmount)));
            remainingPayment = MoneyMath.RoundMoney(remainingPayment - paidAmount);
        }

        if (remainingPayment > 0)
        {
            allocations.Add(new PaymentAllocationDto(
                "overpayment",
                null,
                "Переплата",
                0,
                remainingPayment,
                MoneyMath.RoundMoney(-remainingPayment)));
        }

        return allocations;
    }

    private static FinancialOperationDto ToDto(
        FinancialOperation operation,
        decimal? garageDebtBefore = null,
        decimal? garageDebtAfter = null,
        decimal? supplierDebtBefore = null,
        decimal? supplierDebtAfter = null,
        IReadOnlyList<PaymentAllocationDto>? paymentAllocations = null)
    {
        return new FinancialOperationDto(
            operation.Id,
            operation.OperationKind,
            operation.OperationDate,
            operation.AccountingMonth,
            operation.Amount,
            operation.DocumentNumber,
            operation.Comment,
            operation.GarageId,
            operation.Garage?.Number,
            operation.Garage?.Owner?.FullName,
            operation.IncomeTypeId,
            operation.IncomeType?.Name,
            operation.SupplierId,
            operation.Supplier?.Name,
            operation.ExpenseTypeId,
            operation.ExpenseType?.Name,
            garageDebtBefore,
            garageDebtAfter,
            supplierDebtBefore,
            supplierDebtAfter,
            paymentAllocations ?? [],
            operation.IsCanceled,
            operation.CreatedAtUtc,
            operation.StaffMemberId,
            operation.StaffMember?.FullName,
            operation.StaffMember?.Department?.Name,
            operation.ReceiptBatchId,
            operation.ExpensePaymentType,
            operation.ExpensePaymentSource,
            operation.ExpenseFundId,
            operation.ExpenseFund?.Name,
            operation.CounterpartyName,
            operation.NegativeFundBalanceConfirmed);
    }

    private static string? InferMeterKind(string incomeTypeName, string? incomeTypeCode)
    {
        var normalized = $"{incomeTypeCode ?? string.Empty} {incomeTypeName}".ToLower(RussianCulture);
        if (normalized.Contains("electric", StringComparison.Ordinal) || normalized.Contains("электр", StringComparison.Ordinal))
        {
            return MeterKinds.Electricity;
        }

        if (normalized.Contains("water", StringComparison.Ordinal) || normalized.Contains("вод", StringComparison.Ordinal))
        {
            return MeterKinds.Water;
        }

        return null;
    }

    private static FinanceResult<SupplierAccrualDto>? ValidateSupplierExpenseTypeLink(Supplier supplier, ExpenseType expenseType)
    {
        if (supplier.ChargeServiceSetting is null || supplier.ChargeServiceSetting.IsArchived)
        {
            return FinanceResult<SupplierAccrualDto>.Failure(
                "supplier_service_not_configured",
                $"Для поставщика «{supplier.Name}» не настроена действующая услуга.");
        }

        if (!supplier.ExpenseTypeId.HasValue)
        {
            return FinanceResult<SupplierAccrualDto>.Failure(
                "supplier_service_expense_type_not_configured",
                $"Для поставщика «{supplier.Name}» не настроена учётная услуга.");
        }

        if (supplier.ExpenseTypeId.Value != expenseType.Id)
        {
            return FinanceResult<SupplierAccrualDto>.Failure(
                "supplier_expense_type_mismatch",
                $"Поставщику «{supplier.Name}» можно начислять только услугу «{supplier.ChargeServiceSetting.Name}».");
        }

        var expenseFund = GetSupplierExpenseFund(supplier);
        if (expenseFund is null || expenseFund.IsArchived)
        {
            return FinanceResult<SupplierAccrualDto>.Failure(
                "supplier_service_expense_fund_not_configured",
                $"Для услуги «{supplier.ChargeServiceSetting.Name}» не настроен действующий фонд расходования.");
        }

        return null;
    }

    private static FinanceResult<FinancialOperationDto>? ValidateSupplierExpenseTypeLinkForPayment(Supplier supplier, ExpenseType expenseType)
    {
        if (supplier.ChargeServiceSetting is null || supplier.ChargeServiceSetting.IsArchived)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "supplier_service_not_configured",
                $"Для поставщика «{supplier.Name}» не настроена действующая услуга.");
        }

        if (!supplier.ExpenseTypeId.HasValue)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "supplier_service_expense_type_not_configured",
                $"Для поставщика «{supplier.Name}» не настроена учётная услуга.");
        }

        if (supplier.ExpenseTypeId.Value != expenseType.Id)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "supplier_expense_type_mismatch",
                $"Поставщику «{supplier.Name}» можно провести выплату только по услуге «{supplier.ChargeServiceSetting.Name}».");
        }

        var expenseFund = GetSupplierExpenseFund(supplier);
        if (expenseFund is null || expenseFund.IsArchived)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "supplier_service_expense_fund_not_configured",
                $"Для услуги «{supplier.ChargeServiceSetting.Name}» не настроен действующий фонд расходования.");
        }

        return null;
    }

    private async Task<IReadOnlyList<FeeCampaignPaymentOption>> EnsureFeeCampaignAccrualsForWorksheetAsync(
        Guid garageId,
        DateOnly monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var options = await feeCampaignRepository.GetPaymentOptionsForGarageAsync(garageId, monthFrom, monthTo, cancellationToken);
        var changedKeys = new List<AccrualPaymentAllocationKey>();
        foreach (var option in options)
        {
            if (option.Campaign.ClosedAtUtc.HasValue)
            {
                continue;
            }
            var remaining = MoneyMath.RoundMoney(Math.Max(option.Campaign.TargetAmount - option.CollectedAmount, 0m));
            if (remaining <= 0m)
            {
                continue;
            }

            var payable = MoneyMath.RoundMoney(Math.Min(option.Campaign.ContributionAmount, remaining));
            if (payable <= 0m)
            {
                continue;
            }

            var accrual = option.Accrual;
            var month = MonthPeriod.Normalize(option.Campaign.StartsOn);
            if (month < monthFrom) month = monthFrom;
            if (month > monthTo) month = monthTo;
            var dueDates = AccrualDueDates.ForFeeCampaign(month, option.Campaign.EndsOn, option.Campaign.OverdueGraceDays);
            if (accrual is null)
            {
                accrual = new Accrual
                {
                    GarageId = garageId,
                    IncomeTypeId = option.Campaign.IncomeTypeId,
                    IncomeType = option.Campaign.IncomeType,
                    FeeCampaignId = option.Campaign.Id,
                    FeeCampaign = option.Campaign,
                    AccountingMonth = month,
                    DueDate = dueDates.DueDate,
                    OverdueFromDate = dueDates.OverdueFromDate,
                    Amount = payable,
                    Source = AccrualSources.FeeCampaign,
                    Basis = option.Campaign.Name,
                    Comment = BuildFeeCampaignAccrualComment(option.Campaign, null)
                };
                accrualRepository.Add(accrual);
            }
            else
            {
                var unpaidStandardShare = Math.Max(option.Campaign.ContributionAmount - option.PaidAmount, 0m);
                var desiredAmount = MoneyMath.RoundMoney(option.PaidAmount + Math.Min(unpaidStandardShare, remaining));
                if (accrual.Amount == desiredAmount && string.Equals(accrual.Basis, option.Campaign.Name, StringComparison.Ordinal))
                {
                    continue;
                }
                accrual.Amount = desiredAmount;
                accrual.Basis = option.Campaign.Name;
                accrual.UpdatedAtUtc = timeProvider.GetUtcNow();
            }
            changedKeys.Add(new AccrualPaymentAllocationKey(garageId, option.Campaign.IncomeTypeId));
            AddAudit(
                null,
                "finance.fee_campaign_payment_calculated",
                "accrual",
                accrual.Id,
                $"Для гаража рассчитан доступный взнос по сбору {option.Campaign.Name}: {accrual.Amount:F2}.",
                relatedAccountingMonth: accrual.AccountingMonth,
                relatedDocumentId: accrual.Id.ToString(),
                relatedGarageId: garageId.ToString(),
                metadata: new Dictionary<string, object?>
                {
                    ["feeCampaignId"] = option.Campaign.Id,
                    ["feeCampaignRemainingAmount"] = remaining,
                    ["amount"] = accrual.Amount
                });
        }

        if (changedKeys.Count > 0)
        {
            var keys = changedKeys.Distinct().ToArray();
            await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(keys, cancellationToken);
            await RebuildPaymentAllocationsAsync(keys, null, "Автоматический расчёт объявленного сбора", garageId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            options = await feeCampaignRepository.GetPaymentOptionsForGarageAsync(garageId, monthFrom, monthTo, cancellationToken);
        }
        return options;
    }

    private static Guid? GetSupplierExpenseFundId(Supplier supplier) => supplier.ExpenseFundId;

    private static Fund? GetSupplierExpenseFund(Supplier supplier) => supplier.ExpenseFund;

    private static string? NormalizeExpensePaymentType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return ExpensePaymentTypes.IsSupported(normalized) ? normalized : null;
    }

    private static DateOnly GetGarageRegistrationDate(Garage garage) =>
        DateOnly.FromDateTime(garage.CreatedAtUtc.UtcDateTime);

    private static string? NormalizeExpensePaymentSource(string? value, string? expensePaymentType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return expensePaymentType == ExpensePaymentTypes.WithoutReceipt
                ? ExpensePaymentSources.Cash
                : ExpensePaymentSources.Bank;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return ExpensePaymentSources.All.Contains(normalized) ? normalized : null;
    }

    private static bool IsCashExpense(FinancialOperation operation)
    {
        if (operation.ExpensePaymentSource is not null)
        {
            return operation.ExpensePaymentSource == ExpensePaymentSources.Cash;
        }

        if (operation.ExpensePaymentType is not null)
        {
            return operation.ExpensePaymentType == ExpensePaymentTypes.WithoutReceipt;
        }

        return IsCashExpenseType(operation.ExpenseType);
    }

    private static bool IsCashExpenseType(ExpenseType? expenseType)
    {
        if (expenseType is null)
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(expenseType.Code) && CashExpenseTypeKeys.Contains(NormalizeFinanceLookupKey(expenseType.Code))) ||
            CashExpenseTypeKeys.Contains(NormalizeFinanceLookupKey(expenseType.Name));
    }

    private static string NormalizeFinanceLookupKey(string value)
    {
        return value.Trim().ToLower(RussianCulture);
    }

    private sealed record AllocationDebtBucket(string Kind, DateOnly? AccountingMonth, string Label, decimal Amount);

    private sealed record AvailableAmounts(decimal BankAmount, decimal CashAmount);

    private sealed record MeterReadingChainChange(
        MeterReading Reading,
        decimal PreviousValue,
        decimal Consumption,
        bool HasGapWarning,
        bool Changed);

    private sealed record MeteredAccrualRecalculation(
        Accrual Accrual,
        decimal NewAmount,
        AccrualCalculationDetailsDto? Details);

    private sealed record AccrualAuditSnapshot(
        string GarageNumber,
        string IncomeTypeName,
        DateOnly AccountingMonth,
        int? AccountingYear,
        decimal Amount,
        string Source,
        string? Comment)
    {
        public static AccrualAuditSnapshot From(Accrual accrual)
        {
            return new AccrualAuditSnapshot(
                accrual.Garage.Number,
                accrual.IncomeType.Name,
                accrual.AccountingMonth,
                accrual.AccountingYear,
                accrual.Amount,
                accrual.Source,
                accrual.Comment);
        }
    }

    private sealed record SupplierAccrualAuditSnapshot(
        string SupplierName,
        string ExpenseTypeName,
        DateOnly AccountingMonth,
        decimal Amount,
        string Source,
        string? DocumentNumber,
        string? Comment)
    {
        public static SupplierAccrualAuditSnapshot From(SupplierAccrual accrual)
        {
            return new SupplierAccrualAuditSnapshot(
                accrual.Supplier.Name,
                accrual.ExpenseType.Name,
                accrual.AccountingMonth,
                accrual.Amount,
                accrual.Source,
                accrual.DocumentNumber,
                accrual.Comment);
        }
    }

    private static AccrualDto ToDto(Accrual accrual)
    {
        return new AccrualDto(
            accrual.Id,
            accrual.GarageId,
            accrual.Garage.Number,
            accrual.Garage.Owner?.FullName,
            accrual.IncomeTypeId,
            accrual.IncomeType.Name,
            accrual.AccountingMonth,
            accrual.AccountingYear,
            accrual.Amount,
            accrual.Source,
            accrual.Comment,
            accrual.IsCanceled,
            accrual.DueDate,
            accrual.OverdueFromDate,
            accrual.IrregularPaymentId,
            accrual.IrregularPayment?.Name,
            accrual.Basis ?? accrual.IrregularPayment?.Name,
            accrual.FeeCampaignId,
            accrual.FeeCampaign?.Name);
    }

    private static SupplierAccrualDto ToDto(SupplierAccrual accrual)
    {
        return new SupplierAccrualDto(
            accrual.Id,
            accrual.SupplierId,
            accrual.Supplier.Name,
            accrual.ExpenseTypeId,
            accrual.ExpenseType.Name,
            accrual.AccountingMonth,
            accrual.Amount,
            accrual.Source,
            accrual.DocumentNumber,
            accrual.Comment,
            accrual.IsCanceled,
            accrual.ExpenseFundId,
            accrual.ExpenseFund?.Name);
    }

    private static MeterReadingDto ToDto(MeterReading reading)
    {
        return new MeterReadingDto(
            reading.Id,
            reading.GarageId,
            reading.Garage.Number,
            reading.Garage.Owner?.FullName,
            reading.MeterKind,
            reading.AccountingMonth,
            reading.ReadingDate,
            reading.CurrentValue,
            reading.PreviousValue,
            reading.Consumption,
            reading.HasGapWarning,
            reading.Comment,
            reading.IsCanceled,
            reading.Version,
            reading.MeterDeviceId,
            reading.MeterDevice?.SerialNumber,
            reading.PreviousDeviceConsumption,
            reading.IsMeterReplacement);
    }

    private static MeterDeviceDto ToDto(MeterDevice device) =>
        new(
            device.Id,
            device.GarageId,
            device.MeterKind,
            device.SerialNumber,
            device.InstalledOn,
            device.RemovedOn,
            device.InitialValue,
            device.FinalValue,
            device.Version);

    private readonly record struct AmountCalculationResult(bool Succeeded, decimal Value, string? ErrorMessage)
    {
        public static AmountCalculationResult Success(decimal value)
        {
            return new AmountCalculationResult(true, value, null);
        }

        public static AmountCalculationResult Failure(string errorMessage)
        {
            return new AmountCalculationResult(false, 0m, errorMessage);
        }
    }
}
