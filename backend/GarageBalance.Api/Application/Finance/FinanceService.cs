using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
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
    IMeterReadingRepository meterReadingRepository,
    IFinancialOperationRepository financialOperationRepository,
    IAccrualRepository accrualRepository,
    IAccrualPaymentAllocationRepository accrualPaymentAllocationRepository,
    ISupplierAccrualRepository supplierAccrualRepository,
    ISupplierGroupRepository supplierGroupRepository,
    ISupplierRepository supplierRepository,
    IExpenseTypeRepository expenseTypeRepository,
    IIncomeTypeRepository incomeTypeRepository,
    ITariffRepository tariffRepository,
    IFeeCampaignRepository feeCampaignRepository,
    IChargeServiceSettingRepository chargeServiceSettingRepository,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter,
    TimeProvider timeProvider) : IFinanceService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int MaxBalanceHistoryMonths = 60;
    private const int EarlyElectricityPaymentWarningDays = 30;
    private const string DebtTransferIncomeTypeCode = "debt_transfer";
    private const string DebtTransferIncomeTypeName = "Перенос задолженности";
    private const string AdvancePaymentExpenseTypeName = "Авансовые выплаты";
    private const string NoReceiptPaymentExpenseTypeName = "Выплата без чека";
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] CashExpenseTypeCodes =
    [
        "advance",
        "advance_payment",
        "advance_payments",
        "cash_advance",
        "no_receipt",
        "without_receipt",
        "no_check",
        "without_check",
        "cash_no_receipt"
    ];

    private static readonly string[] CashExpenseTypeNames =
    [
        AdvancePaymentExpenseTypeName,
        NoReceiptPaymentExpenseTypeName
    ];

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
        ["expenseType"] = "Вид выплаты",
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
        if (meterKind is not ("water" or "electricity"))
        {
            return FinanceResult<MeterReadingYearPageDto>.Failure("meter_kind_invalid", "Тип счетчика должен быть water или electricity.");
        }

        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var page = await meterReadingRepository.GetYearPageAsync(
            request.Year,
            meterKind,
            normalizedOffset,
            normalizedLimit,
            cancellationToken);
        var result = new MeterReadingYearPageDto(
            page.Garages.Select(garage => new MeterReadingYearGarageDto(garage.Id, garage.Number)).ToList(),
            page.Readings.Select(reading => new MeterReadingYearValueDto(reading.Id, reading.GarageId, reading.AccountingMonth, reading.CurrentValue, reading.Version)).ToList(),
            page.TotalCount,
            normalizedOffset,
            normalizedLimit);
        return FinanceResult<MeterReadingYearPageDto>.Success(result);
    }

    public async Task<IReadOnlyList<MissingMeterReadingDto>> GetMissingMeterReadingsAsync(MissingMeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth ?? MonthPeriod.CurrentLocalMonth());
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
        var defaultMonthTo = MonthPeriod.CurrentLocalMonth();
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

        var asOfDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var accruals = await accrualRepository.GetOverdueDebtDetailsAsync(garageId, asOfDate, cancellationToken);
        var totals = await garageRepository.GetBalanceTotalsAsync([garageId], cancellationToken);
        var unallocatedIncome = Math.Max(
            totals.IncomeTotals.GetValueOrDefault(garageId) - totals.AllocatedIncomeTotals.GetValueOrDefault(garageId),
            0m);
        var openingOriginal = Math.Max(garage.StartingBalance, 0m);
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
        var defaultMonthTo = MonthPeriod.CurrentLocalMonth();
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

        var worksheetData = await garageIncomeWorksheetQuery.GetAsync(garageId, monthFrom, monthTo, cancellationToken);
        if (worksheetData is null)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure("garage_not_found", "Гараж для формы поступлений не найден.");
        }

        var openingDebt = MoneyMath.RoundMoney(Math.Max(
            worksheetData.StartingBalance + worksheetData.PreviousAccrualTotal - worksheetData.PreviousIncomeTotal,
            0m));
        var meterReadingByMonthKind = worksheetData.MeterReadings
            .GroupBy(reading => (reading.AccountingMonth, reading.MeterKind))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.UpdatedAtUtc)
                    .First());

        var accrualLookup = worksheetData.AccrualBuckets.ToDictionary(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId));
        var incomeLookup = worksheetData.IncomeBuckets.ToDictionary(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId));
        var requiredMeterBuckets = defaultMonthTo >= monthFrom && defaultMonthTo <= monthTo
            ? worksheetData.MeterIncomeTypes.Select(incomeType => new GarageIncomeWorksheetBucketData(
                defaultMonthTo,
                incomeType.IncomeTypeId,
                incomeType.IncomeTypeName,
                incomeType.IncomeTypeCode,
                0m))
            : [];
        var keys = worksheetData.AccrualBuckets
            .Concat(worksheetData.IncomeBuckets)
            .Concat(requiredMeterBuckets)
            .GroupBy(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId))
            .Select(group => group.First())
            .OrderByDescending(bucket => bucket.AccountingMonth)
            .ThenBy(bucket => bucket.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = keys.Select(key =>
        {
            var accrualAmount = MoneyMath.RoundMoney(accrualLookup.GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId))?.Amount ?? 0m);
            var incomeAmount = MoneyMath.RoundMoney(incomeLookup.GetValueOrDefault((key.AccountingMonth, key.IncomeTypeId))?.Amount ?? 0m);
            var debt = MoneyMath.RoundMoney(Math.Max(accrualAmount - incomeAmount, 0m));
            var meterKind = InferMeterKind(key.IncomeTypeName, key.IncomeTypeCode);
            meterReadingByMonthKind.TryGetValue((key.AccountingMonth, meterKind ?? string.Empty), out var reading);
            return new GarageIncomeWorksheetRowDto(
                key.AccountingMonth,
                key.IncomeTypeId,
                key.IncomeTypeName,
                meterKind,
                reading?.Id,
                reading?.Version,
                reading?.ReadingDate,
                reading?.CurrentValue,
                reading?.Consumption,
                accrualAmount,
                incomeAmount,
                debt);
        }).ToList();

        var accrualTotal = MoneyMath.RoundMoney(rows.Sum(row => row.AccrualAmount));
        var incomeTotal = MoneyMath.RoundMoney(rows.Sum(row => row.IncomeAmount));
        var closingDebt = MoneyMath.RoundMoney(Math.Max(openingDebt + accrualTotal - incomeTotal, 0m));
        var debtTotal = closingDebt;
        return FinanceResult<GarageIncomeWorksheetDto>.Success(new GarageIncomeWorksheetDto(
            worksheetData.GarageId,
            worksheetData.GarageNumber,
            worksheetData.OwnerName,
            monthFrom,
            monthTo,
            openingDebt,
            accrualTotal,
            incomeTotal,
            debtTotal,
            closingDebt,
            rows));
    }

    public async Task<FinanceResult<ExpenseWorksheetDto>> GetExpenseWorksheetAsync(ExpenseWorksheetRequest request, CancellationToken cancellationToken)
    {
        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth ?? MonthPeriod.CurrentLocalMonth());

        var worksheetData = await expenseWorksheetQuery.GetAsync(
            accountingMonth,
            CashExpenseTypeCodes,
            CashExpenseTypeNames,
            cancellationToken);

        var collectedByIncomeKey = worksheetData.Incomes
            .GroupBy(income => NormalizeFinanceLookupKey(income.IncomeTypeCode ?? income.IncomeTypeName))
            .ToDictionary(group => group.Key, group => MoneyMath.RoundMoney(group.Sum(income => income.Amount)), StringComparer.Ordinal);

        var rows = new List<ExpenseWorksheetRowDto>();
        var supplierAccruals = worksheetData.SupplierAccruals
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierExpenses = worksheetData.SupplierExpenses
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierOpeningAccruals = worksheetData.SupplierOpeningAccruals
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierOpeningExpenses = worksheetData.SupplierOpeningExpenses
            .ToDictionary(item => (item.SupplierId, item.ExpenseTypeId));
        var supplierKeys = supplierAccruals.Keys
            .Concat(supplierExpenses.Keys)
            .Concat(supplierOpeningAccruals.Keys)
            .Concat(supplierOpeningExpenses.Keys)
            .Distinct()
            .ToList();

        foreach (var key in supplierKeys)
        {
            supplierAccruals.TryGetValue(key, out var accrual);
            supplierExpenses.TryGetValue(key, out var expense);
            supplierOpeningAccruals.TryGetValue(key, out var openingAccrual);
            supplierOpeningExpenses.TryGetValue(key, out var openingExpense);
            var sample = accrual ?? expense ?? openingAccrual ?? openingExpense!;
            var accrualAmount = MoneyMath.RoundMoney(accrual?.Amount ?? 0m);
            var expenseAmount = MoneyMath.RoundMoney(expense?.Amount ?? 0m);
            var balance = MoneyMath.RoundMoney(Math.Max(accrualAmount - expenseAmount, 0m));
            var openingBalance = MoneyMath.RoundMoney((openingAccrual?.Amount ?? 0m) - (openingExpense?.Amount ?? 0m));
            var closingBalance = MoneyMath.RoundMoney(openingBalance + accrualAmount - expenseAmount);
            var collected = TryGetCollectedAmount(collectedByIncomeKey, sample.ExpenseTypeName, sample.ExpenseTypeCode);
            decimal? difference = collected.HasValue ? MoneyMath.RoundMoney(collected.Value - accrualAmount) : null;
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
                ClosingAdvance = MoneyMath.RoundMoney(Math.Max(-closingBalance, 0m))
            });
        }

        var staffExpenses = worksheetData.StaffExpenses
            .ToDictionary(item => (item.StaffMemberId, item.ExpenseTypeId), item => item.Amount);
        var staffOpeningExpenses = worksheetData.StaffOpeningExpenses
            .ToDictionary(item => (item.StaffMemberId, item.ExpenseTypeId));
        foreach (var staffMember in worksheetData.StaffMembers)
        {
            var key = (staffMember.StaffMemberId, staffMember.ExpenseTypeId);
            staffExpenses.TryGetValue(key, out var staffExpenseAmount);
            staffOpeningExpenses.TryGetValue(key, out var staffOpeningExpense);
            var accrualAmount = MoneyMath.RoundMoney(staffMember.Rate);
            var expenseAmount = MoneyMath.RoundMoney(staffExpenseAmount);
            var staffCreatedMonth = new DateOnly(
                staffMember.CreatedAtUtc.UtcDateTime.Year,
                staffMember.CreatedAtUtc.UtcDateTime.Month,
                1);
            var historyStartMonth = staffOpeningExpense?.FirstAccountingMonth is { } firstExpenseMonth && firstExpenseMonth < staffCreatedMonth
                ? firstExpenseMonth
                : staffCreatedMonth;
            var historyMonthCount = Math.Max(
                0,
                ((accountingMonth.Year - historyStartMonth.Year) * 12) + accountingMonth.Month - historyStartMonth.Month);
            var openingBalance = MoneyMath.RoundMoney(
                (staffMember.Rate * historyMonthCount) - (staffOpeningExpense?.Amount ?? 0m));
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
        var collectedTotal = MoneyMath.RoundMoney(rows.Sum(row => row.CollectedAmount ?? 0m));
        var differenceTotal = MoneyMath.RoundMoney(collectedTotal - accrualTotal);
        var availableAmounts = CalculateAvailableAmounts(worksheetData.AvailableBalance);

        return FinanceResult<ExpenseWorksheetDto>.Success(new ExpenseWorksheetDto(
            accountingMonth,
            accrualTotal,
            expenseTotal,
            balanceTotal,
            collectedTotal,
            differenceTotal,
            availableAmounts.BankAmount,
            availableAmounts.CashAmount,
            rows)
        {
            OpeningBalanceTotal = openingBalanceTotal,
            OpeningDebtTotal = openingDebtTotal,
            OpeningAdvanceTotal = openingAdvanceTotal,
            ClosingDebtTotal = closingDebtTotal,
            ClosingAdvanceTotal = closingAdvanceTotal
        });
    }

    private static int NormalizeListLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
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

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Income, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = request.OperationDate,
            AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth),
            Amount = MoneyMath.RoundMoney(request.Amount),
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType
        };

        financialOperationRepository.Add(operation);
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
            cancellationToken);
        await RebuildPaymentAllocationsAsync(
            [new AccrualPaymentAllocationKey(operation.GarageId!.Value, operation.IncomeTypeId!.Value)],
            actorUserId,
            "Создание поступления",
            operation.Id,
            cancellationToken);
        AddAudit(actorUserId, "finance.income_created", operation, FormatIncomeCreatedAuditSummary(operation));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
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
                comment is null ? "Оплата входящего долга периода" : $"Оплата входящего долга периода: {comment}"),
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
        var bankAmount = MoneyMath.RoundMoney(Math.Max(balance.BankDepositTotal - balance.BankExpenseTotal, 0m));
        var cashAmount = MoneyMath.RoundMoney(Math.Max(balance.IncomeTotal - balance.BankDepositTotal - balance.CashExpenseTotal, 0m));

        return new AvailableAmounts(bankAmount, cashAmount);
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        var isCashExpense = IsCashExpenseType(expenseType);
        await using var balanceLock = await financeAvailableBalanceQuery.AcquireUpdateLockAsync(isCashExpense, cancellationToken);

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
                    $"Сумма выплаты превышает доступный остаток в кассе {MoneyFormatting.Format(availableCashAmount)}.");
            }
        }
        else
        {
            var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
            if (amount > availableBankAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "bank_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток на банковском счете {MoneyFormatting.Format(availableBankAmount)}.");
            }
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = request.OperationDate,
            AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth),
            Amount = amount,
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ExpenseTypeId = expenseType.Id,
            ExpenseType = expenseType
        };

        financialOperationRepository.Add(operation);
        if (isCashExpense)
        {
            supplierAccrualRepository.Add(new SupplierAccrual
            {
                SupplierId = supplier.Id,
                Supplier = supplier,
                ExpenseTypeId = expenseType.Id,
                ExpenseType = expenseType,
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
            return FinanceResult<FinancialOperationDto>.Failure("salary_expense_type_not_found", "Системный вид выплаты Зарплата не найден.");
        }

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var paidThisMonth = await financialOperationRepository.GetStaffExpenseTotalAsync(staffMember.Id, accountingMonth, cancellationToken);
        var availableAmount = MoneyMath.RoundMoney(staffMember.Rate - paidThisMonth);
        if (amount > availableAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {MoneyFormatting.Format(availableAmount)}.");
        }

        var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
        if (amount > availableBankAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "bank_amount_insufficient",
                $"Сумма выплаты превышает доступный остаток на банковском счете {MoneyFormatting.Format(availableBankAmount)}.");
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
            ExpenseType = salaryExpenseType
        };

        financialOperationRepository.Add(operation);
        AddAudit(actorUserId, "finance.staff_payment_created", operation, FormatStaffPaymentCreatedAuditSummary(operation, availableAmount));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
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

        await using var cashBalanceLock = amount < operation.Amount
            ? await financeAvailableBalanceQuery.AcquireUpdateLockAsync(cashExpense: true, cancellationToken)
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

        var supplier = await supplierRepository.FindActiveWithGroupAsync(request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        if (await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, operation.Id, cancellationToken))
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var accountingMonth = MonthPeriod.Normalize(request.AccountingMonth);
        var amount = MoneyMath.RoundMoney(request.Amount);
        var documentNumber = NormalizeOptional(request.DocumentNumber);
        var comment = NormalizeOptional(request.Comment);
        if (ExpenseOperationMatches(operation, request.OperationDate, accountingMonth, amount, documentNumber, comment, supplier.Id, expenseType.Id))
        {
            return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
        }

        var wasCashExpense = IsCashExpenseType(operation.ExpenseType);
        var isCashExpense = IsCashExpenseType(expenseType);
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
            ["expenseType"] = operation.ExpenseType?.Name
        };
        var newValues = new Dictionary<string, object?>
        {
            ["operationDate"] = request.OperationDate,
            ["accountingMonth"] = accountingMonth,
            ["amount"] = amount,
            ["documentNumber"] = documentNumber,
            ["comment"] = comment,
            ["supplier"] = supplier.Name,
            ["expenseType"] = expenseType.Name
        };
        operation.OperationDate = request.OperationDate;
        operation.AccountingMonth = accountingMonth;
        operation.Amount = amount;
        operation.DocumentNumber = documentNumber;
        operation.Comment = comment;
        operation.SupplierId = supplier.Id;
        operation.Supplier = supplier;
        operation.ExpenseTypeId = expenseType.Id;
        operation.ExpenseType = expenseType;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

        await using var cashBalanceLock = operation.OperationKind == FinancialOperationKinds.Income
            ? await financeAvailableBalanceQuery.AcquireUpdateLockAsync(cashExpense: true, cancellationToken)
            : null;
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

        operation.IsCanceled = true;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.Comment = AppendCancelReason(operation.Comment, reason);
        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
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

        if (operation.OperationKind == FinancialOperationKinds.Expense)
        {
            if (operation.StaffMemberId is not null)
            {
                var paidThisMonth = await financialOperationRepository.GetStaffExpenseTotalAsync(
                    operation.StaffMemberId.Value,
                    operation.AccountingMonth,
                    cancellationToken);
                var availableStaffAmount = MoneyMath.RoundMoney((operation.StaffMember?.Rate ?? 0m) - paidThisMonth);
                if (operation.Amount > availableStaffAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {MoneyFormatting.Format(availableStaffAmount)}.");
                }
            }

            if (IsCashExpenseType(operation.ExpenseType))
            {
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

        operation.IsCanceled = false;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (operation.OperationKind == FinancialOperationKinds.Income)
        {
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

        if (await accrualRepository.ActiveDuplicateExistsAsync(
            accrual.Id,
            accrual.GarageId,
            accrual.IncomeTypeId,
            accrual.AccountingMonth,
            accrual.Source,
            cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        accrual.IsCanceled = false;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

    public async Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var source = request.Source.Trim();
        if (source == AccrualSources.Manual && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_comment_required", "Для ручного начисления нужен комментарий.");
        }

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
        if (await accrualRepository.ActiveDuplicateExistsAsync(null, garage.Id, incomeType.Id, month, source, cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        var dueDates = AccrualDueDates.ForChargeService(month, null);
        var accrual = new Accrual
        {
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            AccountingMonth = month,
            DueDate = dueDates.DueDate,
            OverdueFromDate = dueDates.OverdueFromDate,
            Amount = MoneyMath.RoundMoney(request.Amount),
            Source = source,
            Comment = NormalizeOptional(request.Comment)
        };

        accrualRepository.Add(accrual);
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
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                Source = AccrualSources.DebtTransfer,
                Comment = comment
            };
            accrualRepository.Add(accrual);
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
        if (source == AccrualSources.Manual && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_comment_required", "Для ручного начисления нужен комментарий.");
        }

        if (source == AccrualSources.Regular && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_regular_edit_comment_required", "Для изменения автоматического начисления нужен комментарий.");
        }

        if (source is not AccrualSources.Manual and not AccrualSources.Regular)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_source_invalid", "Источник начисления должен быть manual или regular.");
        }

        var accrual = await accrualRepository.FindForUpdateAsync(accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
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
        if (await accrualRepository.ActiveDuplicateExistsAsync(
            accrual.Id,
            garage.Id,
            incomeType.Id,
            month,
            source,
            cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        var comment = NormalizeOptional(request.Comment);
        if (!accrual.DueDateNeedsReview && AccrualMatches(accrual, garage.Id, incomeType.Id, month, amount, source, comment))
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
        var updatedDueDates = AccrualDueDates.ForChargeService(month, null);
        accrual.DueDate = updatedDueDates.DueDate;
        accrual.OverdueFromDate = updatedDueDates.OverdueFromDate;
        accrual.DueDateNeedsReview = false;
        accrual.DueDateReviewReason = null;
        accrual.Amount = amount;
        accrual.Source = source;
        accrual.Comment = comment;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
        if (source == AccrualSources.Manual && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_comment_required", "Для ручного начисления поставщику нужен комментарий.");
        }

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
        if (source == AccrualSources.Manual && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_comment_required", "Для ручного начисления поставщику нужен комментарий.");
        }

        if (source == AccrualSources.Regular && string.IsNullOrWhiteSpace(request.Comment))
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_regular_edit_comment_required", "Для изменения автоматического начисления поставщику нужен комментарий.");
        }

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

        if (tariff.EffectiveFrom > month)
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("tariff_not_effective", "Тариф еще не действует в выбранном месяце.");
        }

        if (!IsIncomeTypeCompatibleWithTariff(incomeType.Code, tariff.CalculationBase))
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure(
                "regular_accrual_tariff_mismatch",
                "Выбранный тариф не подходит для этого вида регулярного начисления.");
        }

        var matchingSetting = (await chargeServiceSettingRepository.GetActiveRegularAsync(cancellationToken))
            .FirstOrDefault(setting =>
                setting.IncomeTypeId == incomeType.Id &&
                setting.TariffId == tariff.Id &&
                IsChargeServiceDueForMonth(setting, month));
        var dueDates = AccrualDueDates.ForChargeService(month, matchingSetting);

        var existingAccrualCount = await accrualRepository.CountActiveForGenerationAsync(
            incomeType.Id,
            month,
            AccrualSources.Regular,
            cancellationToken);
        if (existingAccrualCount > 0)
        {
            var activeGarageCount = await garageRepository.CountActiveAsync(cancellationToken);
            if (activeGarageCount > 0 && existingAccrualCount >= activeGarageCount)
            {
                return FinanceResult<RegularAccrualGenerationResultDto>.Failure(
                    "regular_accruals_empty",
                    $"Регулярные начисления за {month:MM.yyyy} уже сформированы для всех активных гаражей ({activeGarageCount}).");
            }
        }

        var garages = await garageRepository.GetAllActiveWithOwnerAsync(cancellationToken);
        IReadOnlySet<Guid> existingGarageIds = existingAccrualCount == 0
            ? new HashSet<Guid>()
            : await accrualRepository.GetActiveGarageIdsAsync(
                incomeType.Id,
                month,
                AccrualSources.Regular,
                cancellationToken);
        var pendingGarageIds = garages
            .Where(garage => !existingGarageIds.Contains(garage.Id))
            .Select(garage => garage.Id)
            .ToArray();
        var meterKind = tariff.CalculationBase switch
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
                skipped.Add($"Гараж {garage.Number}: регулярное начисление уже есть.");
                continue;
            }

            meterReadings.TryGetValue(garage.Id, out var meterReading);
            var amountResult = CalculateRegularAccrualAmount(garage, tariff, meterReading);
            if (!amountResult.Succeeded)
            {
                skipped.Add($"Гараж {garage.Number}: {amountResult.ErrorMessage}");
                continue;
            }

            var amount = amountResult.Value;
            if (amount <= 0)
            {
                skipped.Add($"Гараж {garage.Number}: сумма начисления равна нулю.");
                continue;
            }

            var accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                TariffId = tariff.Id,
                Tariff = tariff,
                AccountingMonth = month,
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                Source = AccrualSources.Regular,
                Comment = BuildRegularAccrualComment(tariff, request.Comment)
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
            FormatRegularAccrualGenerationAuditSummary(month, incomeType, tariff, created, skipped),
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
        var settings = await chargeServiceSettingRepository.GetActiveRegularAsync(cancellationToken);
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

            if (!setting.IncomeTypeId.HasValue || !setting.TariffId.HasValue)
            {
                skippedServices.Add($"{setting.Name}: не указан вид начисления или тариф.");
                continue;
            }

            var comment = BuildRegularCatalogAccrualComment(setting.Name, request.Comment);
            var serviceResult = await GenerateRegularAccrualsAsync(
                new GenerateRegularAccrualsRequest(setting.IncomeTypeId.Value, setting.TariffId.Value, month, comment),
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

    public async Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> GenerateFeeCampaignAccrualsAsync(GenerateFeeCampaignAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth);
        var campaign = await feeCampaignRepository.FindActiveForAccrualGenerationAsync(request.FeeCampaignId, cancellationToken);
        if (campaign is null)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_not_found", "Сбор не найден.");
        }

        if (campaign.IncomeType.IsArchived)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("income_type_not_found", "Вид поступления для сбора не найден.");
        }

        if (campaign.StartsOn > month)
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

        var existingGarageIds = await accrualRepository.GetActiveGarageIdsAsync(
            campaign.IncomeTypeId,
            month,
            AccrualSources.FeeCampaign,
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

            var accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = campaign.IncomeTypeId,
                IncomeType = campaign.IncomeType,
                AccountingMonth = month,
                DueDate = dueDates.DueDate,
                OverdueFromDate = dueDates.OverdueFromDate,
                Amount = amount,
                Source = AccrualSources.FeeCampaign,
                Comment = BuildFeeCampaignAccrualComment(campaign, request.Comment)
            };
            accrualRepository.Add(accrual);
            created.Add(ToDto(accrual));
        }

        if (created.Count == 0)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_accruals_empty", "По сбору не создано ни одного начисления.");
        }

        AddAudit(
            actorUserId,
            "finance.fee_campaign_accruals_generated",
            "accrual",
            campaign.Id,
            FormatFeeCampaignAccrualGenerationAuditSummary(month, campaign, created, skipped),
            relatedAccountingMonth: month,
            relatedDocumentNumber: $"{campaign.Name} {month:MM.yyyy}",
            metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = "accrual",
                ["feeCampaignId"] = campaign.Id,
                ["feeCampaignName"] = campaign.Name,
                ["incomeTypeId"] = campaign.IncomeTypeId,
                ["incomeTypeName"] = campaign.IncomeType.Name,
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
            campaign.IncomeTypeId,
            campaign.IncomeType.Name,
            amount,
            created.Count,
            skipped.Count,
            created.Sum(item => item.Amount),
            created,
            skipped);
        return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Success(result);
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
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("salary_expense_type_not_found", "Системный вид выплаты Зарплата не найден.");
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

    public async Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (meterKind is not MeterKinds.Water and not MeterKinds.Electricity)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Тип счетчика должен быть water или electricity.");
        }

        if (!request.CurrentValue.HasValue)
        {
            return ManualMeterReadingValueRequired();
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (await meterReadingRepository.ActiveDuplicateExistsAsync(null, garage.Id, meterKind, month, cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание этого счетчика за месяц уже внесено.");
        }

        var previousReading = await meterReadingRepository.GetPreviousActiveAsync(null, garage.Id, meterKind, month, cancellationToken);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue.Value);
        var previousMeterValue = previousReading?.CurrentValue ?? GetInitialMeterValue(garage, meterKind);
        if (!previousMeterValue.HasValue && meterKind == MeterKinds.Water)
        {
            return WaterMeterReadingBaselineRequired();
        }

        var previousValue = MoneyMath.RoundMeterValue(previousMeterValue ?? 0m);
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
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = request.ReadingDate,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            Consumption = consumption,
            HasGapWarning = hasGapWarning,
            Comment = NormalizeOptional(request.Comment)
        };

        meterReadingRepository.Add(reading);
        AddAudit(actorUserId, "finance.meter_reading_created", reading, FormatMeterReadingCreatedAuditSummary(reading));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> SavePaymentFormMeterReadingAsync(
        SavePaymentFormMeterReadingRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (meterKind is not MeterKinds.Water and not MeterKinds.Electricity)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Тип счетчика должен быть water или electricity.");
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
            request.ExpectedVersion);

        if (!request.MeterReadingId.HasValue)
        {
            if (activeReading is not null)
            {
                return MeterReadingConflict();
            }

            try
            {
                return await CreateMeterReadingAsync(saveRequest, actorUserId, cancellationToken);
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
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<MeterReadingDto>.Failure(
                "meter_reading_correction_reason_required",
                "Для исторической корректировки показания нужна причина.");
        }

        var reading = await meterReadingRepository.FindForUpdateAsync(meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        var currentMonth = GetCurrentAccountingMonth();
        if (reading.AccountingMonth >= currentMonth)
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
        if (meterKind is not MeterKinds.Water and not MeterKinds.Electricity)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Тип счетчика должен быть water или electricity.");
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
        else if (reading.AccountingMonth >= currentMonth || month != reading.AccountingMonth)
        {
            return HistoricalMeterReadingMonthRequired();
        }

        var garage = await garageRepository.FindActiveWithOwnerAsync(request.GarageId, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
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
        var consumption = MoneyMath.RoundMeterValue(currentValue - previousValue);
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
        if (MeterReadingMatches(reading, garage.Id, meterKind, month, request.ReadingDate, currentValue, previousValue, consumption, hasGapWarning, comment))
        {
            return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
        }

        var linkedAccruals = await accrualRepository.GetActiveMeteredForUpdateAsync(
            garage.Id,
            month,
            meterKind,
            cancellationToken);
        var prospectiveReading = new MeterReading
        {
            GarageId = garage.Id,
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = request.ReadingDate,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
            Consumption = consumption
        };
        var accrualRecalculations = linkedAccruals
            .Select(accrual => new
            {
                Accrual = accrual,
                Calculation = CalculateRegularAccrualAmount(garage, accrual.Tariff!, prospectiveReading)
            })
            .Where(item => item.Calculation.Succeeded && item.Calculation.Value != item.Accrual.Amount)
            .Select(item => (item.Accrual, NewAmount: item.Calculation.Value))
            .ToArray();
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
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
        reading.PreviousValue = previousValue;
        reading.Consumption = consumption;
        reading.HasGapWarning = hasGapWarning;
        reading.Comment = comment;
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        foreach (var (accrual, newAmount) in accrualRecalculations)
        {
            var before = AccrualAuditSnapshot.From(accrual);
            var oldAccrualValues = new Dictionary<string, object?> { ["amount"] = accrual.Amount };
            var newAccrualValues = new Dictionary<string, object?> { ["amount"] = newAmount };
            accrual.Amount = newAmount;
            accrual.UpdatedAtUtc = timeProvider.GetUtcNow();
            AddAudit(
                actorUserId,
                "finance.accrual_updated_from_meter_reading",
                accrual,
                $"Начисление пересчитано после изменения показания: было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.",
                oldAccrualValues,
                newAccrualValues);
        }
        await RebuildPaymentAllocationsAsync(
            allocationKeys,
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
        return MonthPeriod.Normalize(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));
    }

    private static FinanceResult<MeterReadingDto> HistoricalMeterReadingMonthRequired() =>
        FinanceResult<MeterReadingDto>.Failure(
            "meter_reading_historical_month_required",
            "Историческая корректировка разрешена только для учетного месяца раньше текущего.");

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

        reading.IsCanceled = true;
        reading.Comment = AppendCancelReason(reading.Comment, reason);
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        AddAudit(actorUserId, "finance.meter_reading_canceled", reading, FormatMeterReadingCanceledAuditSummary(reading, reason));
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

        if (await meterReadingRepository.ActiveDuplicateExistsAsync(
            reading.Id,
            reading.GarageId,
            reading.MeterKind,
            reading.AccountingMonth,
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "За этот гараж, месяц и счетчик уже есть активное показание.");
        }

        reading.IsCanceled = false;
        reading.Version = Guid.NewGuid();
        reading.UpdatedAtUtc = timeProvider.GetUtcNow();
        AddAudit(actorUserId, "finance.meter_reading_restored", reading, FormatMeterReadingRestoredAuditSummary(reading));
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

    private static AmountCalculationResult CalculateRegularAccrualAmount(Garage garage, Tariff tariff, MeterReading? meterReading)
    {
        return tariff.CalculationBase switch
        {
            TariffCalculationBases.Fixed => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate)),
            TariffCalculationBases.People => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate * garage.PeopleCount)),
            TariffCalculationBases.MeterWater => CalculateMeterAmount(meterReading, tariff.Rate),
            TariffCalculationBases.MeterElectricity => CalculateElectricityMeterAmount(meterReading, tariff),
            _ => AmountCalculationResult.Failure($"неподдерживаемая база расчета {tariff.CalculationBase}.")
        };
    }

    private static string BuildRegularAccrualComment(Tariff tariff, string? comment)
    {
        var snapshot = $"тариф {tariff.Name}: {FormatTariffRateSnapshot(tariff)}, действует с {tariff.EffectiveFrom:dd.MM.yyyy}";
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

        return $"{amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}";
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
            : $"Отменена выплата {amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}. Причина: {reason}";
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
            : $"Восстановлена выплата {amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}.";
    }

    private static string FormatAccrualCreatedAuditSummary(Accrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        var comment = NormalizeOptional(accrual.Comment);
        var summary = $"Создано начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatAccrualUpdatedAuditSummary(AccrualAuditSnapshot before, Accrual accrual)
    {
        return $"Изменено начисление: было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.";
    }

    private static string FormatAccrualCanceledAuditSummary(Accrual accrual, string reason)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        return $"Отменено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}. Причина: {reason}";
    }

    private static string FormatAccrualRestoredAuditSummary(Accrual accrual)
    {
        var amount = MoneyFormatting.Format(accrual.Amount);
        return $"Восстановлено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
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
        var summary = $"{amount} по гаражу {snapshot.GarageNumber} за {snapshot.AccountingMonth:MM.yyyy}; вид {snapshot.IncomeTypeName}; источник {snapshot.Source}";
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

    private static string FormatRegularAccrualGenerationAuditSummary(DateOnly month, IncomeType incomeType, Tariff tariff, IReadOnlyCollection<AccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = MoneyFormatting.Format(created.Sum(item => item.Amount));
        return $"Создано регулярных начислений: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; вид {incomeType.Name}; тариф {tariff.Name}, база {tariff.CalculationBase}, {FormatTariffRateSnapshot(tariff)}; пропущено {skipped.Count}.";
    }

    private static string FormatFeeCampaignAccrualGenerationAuditSummary(DateOnly month, FeeCampaign campaign, IReadOnlyCollection<AccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = MoneyFormatting.Format(created.Sum(item => item.Amount));
        return $"Создано начислений по сбору: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; сбор {campaign.Name}; вид {campaign.IncomeType.Name}; взнос {MoneyFormatting.Format(campaign.ContributionAmount)}; пропущено {skipped.Count}.";
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

    private static AmountCalculationResult CalculateElectricityMeterAmount(MeterReading? reading, Tariff tariff)
    {
        if (reading is null)
        {
            return AmountCalculationResult.Failure("нет показания счетчика за месяц.");
        }

        if (!HasElectricityTiers(tariff))
        {
            return AmountCalculationResult.Success(MoneyMath.RoundMoney(reading.Consumption * tariff.Rate));
        }

        var firstThreshold = tariff.ElectricityFirstThreshold!.Value;
        var secondThreshold = tariff.ElectricitySecondThreshold!.Value;
        var firstVolume = Math.Min(reading.Consumption, firstThreshold);
        var secondVolume = Math.Min(Math.Max(reading.Consumption - firstThreshold, 0m), secondThreshold - firstThreshold);
        var thirdVolume = Math.Max(reading.Consumption - secondThreshold, 0m);
        var amount =
            firstVolume * tariff.ElectricityFirstRate!.Value +
            secondVolume * tariff.ElectricitySecondRate!.Value +
            thirdVolume * tariff.ElectricityThirdRate!.Value;

        return AmountCalculationResult.Success(MoneyMath.RoundMoney(amount));
    }

    private static string FormatTariffRateSnapshot(Tariff tariff)
    {
        if (!HasElectricityTiers(tariff))
        {
            return $"ставка {MoneyFormatting.Format(tariff.Rate)}";
        }

        return $"пороги электроэнергии до {tariff.ElectricityFirstThreshold!.Value.ToString("0.####", RussianCulture)} кВт по {MoneyFormatting.Format(tariff.ElectricityFirstRate!.Value)}, до {tariff.ElectricitySecondThreshold!.Value.ToString("0.####", RussianCulture)} кВт по {MoneyFormatting.Format(tariff.ElectricitySecondRate!.Value)}, свыше по {MoneyFormatting.Format(tariff.ElectricityThirdRate!.Value)}";
    }

    private static bool HasElectricityTiers(Tariff tariff)
    {
        return tariff.ElectricityFirstThreshold.HasValue
            && tariff.ElectricitySecondThreshold.HasValue
            && tariff.ElectricityFirstRate.HasValue
            && tariff.ElectricitySecondRate.HasValue
            && tariff.ElectricityThirdRate.HasValue;
    }

    private static string[] NormalizeMeterKindFilter(string? meterKind)
    {
        if (string.IsNullOrWhiteSpace(meterKind))
        {
            return [MeterKinds.Water, MeterKinds.Electricity];
        }

        var normalized = meterKind.Trim();
        return normalized is MeterKinds.Water or MeterKinds.Electricity ? [normalized] : [];
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

    private static string FormatHistoricalMeterReadingCorrectedAuditSummary(MeterReading reading)
    {
        return $"Исторически скорректировано показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; предыдущее {reading.PreviousValue.ToString("0.###", RussianCulture)}, текущее {reading.CurrentValue.ToString("0.###", RussianCulture)}, расход {reading.Consumption.ToString("0.###", RussianCulture)}.";
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
        var relatedCounterpartyName = operation.Supplier?.Name ?? operation.StaffMember?.FullName;
        var metadata = new Dictionary<string, object?>
        {
            ["financeEntityType"] = "financial_operation",
            ["operationKind"] = operation.OperationKind,
            ["operationDate"] = operation.OperationDate,
            ["amount"] = operation.Amount
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

    private static bool ExpenseOperationMatches(FinancialOperation operation, DateOnly operationDate, DateOnly accountingMonth, decimal amount, string? documentNumber, string? comment, Guid supplierId, Guid expenseTypeId)
    {
        return operation.OperationDate == operationDate &&
            operation.AccountingMonth == accountingMonth &&
            operation.Amount == amount &&
            StringEquals(operation.DocumentNumber, documentNumber) &&
            StringEquals(operation.Comment, comment) &&
            operation.SupplierId == supplierId &&
            operation.ExpenseTypeId == expenseTypeId;
    }

    private static bool AccrualMatches(Accrual accrual, Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, decimal amount, string source, string? comment)
    {
        return accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
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
        return meterKind == MeterKinds.Water ? garage.InitialWaterMeterValue : garage.InitialElectricityMeterValue;
    }

    private static bool HasGapWarning(string meterKind, DateOnly month, MeterReading? previousReading)
    {
        return meterKind == MeterKinds.Electricity && (previousReading is null || previousReading.AccountingMonth < month.AddMonths(-1));
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
        var startingBalance = operation.Supplier?.StartingBalance ?? await supplierRepository.GetStartingBalanceAsync(supplierId, cancellationToken);
        var accrualTotal = await supplierAccrualRepository.GetTotalThroughMonthAsync(supplierId, operation.AccountingMonth, cancellationToken);
        var previousExpenseTotal = await financialOperationRepository.GetPreviousSupplierExpenseTotalAsync(
            operation.Id,
            supplierId,
            operation.OperationDate,
            cancellationToken);

        return MoneyMath.RoundMoney(startingBalance + accrualTotal - previousExpenseTotal);
    }

    private async Task<IReadOnlyList<PaymentAllocationDto>> CalculateSupplierPaymentAllocationsAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var supplierId = operation.SupplierId!.Value;
        var startingBalance = operation.Supplier?.StartingBalance ?? await supplierRepository.GetStartingBalanceAsync(supplierId, cancellationToken);
        var previousExpenseTotal = await financialOperationRepository.GetPreviousSupplierExpenseTotalAsync(
            operation.Id,
            supplierId,
            operation.OperationDate,
            cancellationToken);
        var accrualBucketRows = await supplierAccrualRepository.GetMonthlyBucketsThroughMonthAsync(supplierId, operation.AccountingMonth, cancellationToken);
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
            operation.StaffMember?.Department?.Name);
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

    private static decimal? TryGetCollectedAmount(IReadOnlyDictionary<string, decimal> collectedByIncomeKey, string expenseTypeName, string? expenseTypeCode)
    {
        if (!string.IsNullOrWhiteSpace(expenseTypeCode) && collectedByIncomeKey.TryGetValue(NormalizeFinanceLookupKey(expenseTypeCode), out var byCode))
        {
            return byCode;
        }

        return collectedByIncomeKey.TryGetValue(NormalizeFinanceLookupKey(expenseTypeName), out var byName) ? byName : null;
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

    private sealed record AccrualAuditSnapshot(
        string GarageNumber,
        string IncomeTypeName,
        DateOnly AccountingMonth,
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
            accrual.Amount,
            accrual.Source,
            accrual.Comment,
            accrual.IsCanceled,
            accrual.DueDate,
            accrual.OverdueFromDate);
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
            accrual.IsCanceled);
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
            reading.Version);
    }

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
