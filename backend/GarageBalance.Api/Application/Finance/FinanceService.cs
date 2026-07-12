using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceService(
    GarageBalanceDbContext dbContext,
    IStaffMemberRepository staffMemberRepository,
    IExpenseTypeRepository expenseTypeRepository,
    IApplicationUnitOfWork unitOfWork,
    IAuditEventWriter auditEventWriter) : IFinanceService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int MaxBalanceHistoryMonths = 60;
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
        ["hasGapWarning"] = "Разрыв истории"
    };

    public FinanceService(GarageBalanceDbContext dbContext)
        : this(dbContext, new EfStaffMemberRepository(dbContext), new EfExpenseTypeRepository(dbContext), new EfApplicationUnitOfWork(dbContext), new AuditEventWriter(dbContext))
    {
    }

    public async Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var operations = await ApplyFilters(QueryOperations(), request)
            .OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .Take(NormalizeListLimit(request.Limit))
            .ToListAsync(cancellationToken);
        return await ToOperationDtosAsync(operations, cancellationToken);
    }

    public async Task<FinancePagedResult<FinancialOperationDto>> GetOperationsPageAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var query = ApplyFilters(QueryOperations(), request);
        var totalCount = await query.CountAsync(cancellationToken);
        var operations = await query
            .OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
        return new FinancePagedResult<FinancialOperationDto>(await ToOperationDtosAsync(operations, cancellationToken), totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken)
    {
        return await ApplyAccrualFilters(QueryAccruals(), request)
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .Take(NormalizeListLimit(request.Limit))
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancePagedResult<AccrualDto>> GetAccrualsPageAsync(AccrualListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var query = ApplyAccrualFilters(QueryAccruals(), request);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
        return new FinancePagedResult<AccrualDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
    {
        return await ApplySupplierAccrualFilters(QuerySupplierAccruals(), request)
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Supplier.Name)
            .Take(NormalizeListLimit(request.Limit))
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancePagedResult<SupplierAccrualDto>> GetSupplierAccrualsPageAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var query = ApplySupplierAccrualFilters(QuerySupplierAccruals(), request);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Supplier.Name)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
        return new FinancePagedResult<SupplierAccrualDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
    {
        return await ApplyMeterReadingFilters(QueryMeterReadings(), request)
            .OrderByDescending(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Garage.Number)
            .ThenBy(reading => reading.MeterKind)
            .Take(NormalizeListLimit(request.Limit))
            .Select(reading => ToDto(reading))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancePagedResult<MeterReadingDto>> GetMeterReadingsPageAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeListOffset(request.Offset);
        var normalizedLimit = NormalizeListLimit(request.Limit);
        var query = ApplyMeterReadingFilters(QueryMeterReadings(), request);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Garage.Number)
            .ThenBy(reading => reading.MeterKind)
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(reading => ToDto(reading))
            .ToListAsync(cancellationToken);
        return new FinancePagedResult<MeterReadingDto>(items, totalCount, normalizedOffset, normalizedLimit);
    }

    public async Task<IReadOnlyList<MissingMeterReadingDto>> GetMissingMeterReadingsAsync(MissingMeterReadingListRequest request, CancellationToken cancellationToken)
    {
        var month = MonthPeriod.Normalize(request.AccountingMonth ?? MonthPeriod.CurrentLocalMonth());
        var meterKinds = NormalizeMeterKindFilter(request.MeterKind);
        var search = NormalizeSearch(request.Search);
        var limit = NormalizeListLimit(request.Limit);

        var garageQuery = dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived);
        if (search is not null)
        {
            garageQuery = garageQuery.Where(garage =>
                garage.Number.ToLower().Contains(search) ||
                (garage.Owner != null && (
                    garage.Owner.LastName.ToLower().Contains(search) ||
                    garage.Owner.FirstName.ToLower().Contains(search) ||
                    (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(search)) ||
                    (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(search))));
        }

        var garages = await garageQuery
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        var existingReadings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.AccountingMonth == month && meterKinds.Contains(reading.MeterKind))
            .Select(reading => new { reading.GarageId, reading.MeterKind })
            .ToListAsync(cancellationToken);
        var existingKeys = existingReadings
            .Select(reading => (reading.GarageId, reading.MeterKind))
            .ToHashSet();

        return garages
            .SelectMany(garage => meterKinds
                .Where(meterKind => !existingKeys.Contains((garage.Id, meterKind)))
                .Select(meterKind => new MissingMeterReadingDto(
                    garage.Id,
                    garage.Number,
                    garage.Owner?.FullName,
                    meterKind,
                    month)))
            .Take(limit)
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

        var garage = await dbContext.Garages.AsNoTracking()
            .Include(item => item.Owner)
            .SingleOrDefaultAsync(item => item.Id == garageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<GarageBalanceHistoryDto>.Failure("garage_not_found", "Гараж для истории баланса не найден.");
        }

        var previousAccrualTotal = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth < monthFrom)
            .SumAsync(accrual => accrual.Amount, cancellationToken);
        var previousIncomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < monthFrom)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var accrualBuckets = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth >= monthFrom && accrual.AccountingMonth <= monthTo)
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .ToDictionaryAsync(item => item.AccountingMonth, item => item.Amount, cancellationToken);
        var incomeBuckets = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => operation.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToDictionaryAsync(item => item.AccountingMonth, item => item.Amount, cancellationToken);

        var rows = new List<GarageBalanceHistoryRowDto>(monthCount);
        var openingDebt = MoneyMath.RoundMoney(garage.StartingBalance + previousAccrualTotal - previousIncomeTotal);
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
            garage.Id,
            garage.Number,
            garage.Owner?.FullName,
            monthFrom,
            monthTo,
            garage.StartingBalance,
            accrualTotal,
            incomeTotal,
            rows.Count == 0 ? openingDebt : rows[^1].ClosingDebt,
            rows);
        return FinanceResult<GarageBalanceHistoryDto>.Success(dto);
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

        var garage = await dbContext.Garages.AsNoTracking()
            .Include(item => item.Owner)
            .SingleOrDefaultAsync(item => item.Id == garageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<GarageIncomeWorksheetDto>.Failure("garage_not_found", "Гараж для формы поступлений не найден.");
        }

        var previousAccrualTotal = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth < monthFrom)
            .SumAsync(accrual => accrual.Amount, cancellationToken);
        var previousIncomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.AccountingMonth < monthFrom)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var openingDebt = MoneyMath.RoundMoney(Math.Max(garage.StartingBalance + previousAccrualTotal - previousIncomeTotal, 0m));

        var accrualBuckets = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth >= monthFrom &&
                accrual.AccountingMonth <= monthTo)
            .GroupBy(accrual => new
            {
                accrual.AccountingMonth,
                accrual.IncomeTypeId,
                accrual.IncomeType.Name,
                accrual.IncomeType.Code
            })
            .Select(group => new IncomeWorksheetBucket(
                group.Key.AccountingMonth,
                group.Key.IncomeTypeId,
                group.Key.Name,
                group.Key.Code,
                group.Sum(accrual => accrual.Amount)))
            .ToListAsync(cancellationToken);

        var incomeBuckets = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garageId &&
                operation.IncomeTypeId != null &&
                operation.AccountingMonth >= monthFrom &&
                operation.AccountingMonth <= monthTo)
            .GroupBy(operation => new
            {
                operation.AccountingMonth,
                IncomeTypeId = operation.IncomeTypeId!.Value,
                operation.IncomeType!.Name,
                operation.IncomeType.Code
            })
            .Select(group => new IncomeWorksheetBucket(
                group.Key.AccountingMonth,
                group.Key.IncomeTypeId,
                group.Key.Name,
                group.Key.Code,
                group.Sum(operation => operation.Amount)))
            .ToListAsync(cancellationToken);

        var meterReadings = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                reading.GarageId == garageId &&
                reading.AccountingMonth >= monthFrom &&
                reading.AccountingMonth <= monthTo)
            .ToListAsync(cancellationToken);
        var meterReadingByMonthKind = meterReadings
            .GroupBy(reading => (reading.AccountingMonth, reading.MeterKind))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.UpdatedAtUtc)
                    .First());

        var accrualLookup = accrualBuckets.ToDictionary(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId));
        var incomeLookup = incomeBuckets.ToDictionary(bucket => (bucket.AccountingMonth, bucket.IncomeTypeId));
        var keys = accrualBuckets
            .Concat(incomeBuckets)
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
            garage.Id,
            garage.Number,
            garage.Owner?.FullName,
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

        var supplierAccruals = await dbContext.SupplierAccruals.AsNoTracking()
            .Include(accrual => accrual.Supplier)
            .Include(accrual => accrual.ExpenseType)
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth == accountingMonth)
            .ToListAsync(cancellationToken);

        var expenseOperations = await dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Supplier)
            .Include(operation => operation.StaffMember)
                .ThenInclude(staffMember => staffMember!.Department)
            .Include(operation => operation.ExpenseType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.AccountingMonth == accountingMonth)
            .ToListAsync(cancellationToken);

        var incomeBuckets = await dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.IncomeType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeTypeId != null)
            .ToListAsync(cancellationToken);

        var activeStaffMembers = await staffMemberRepository.GetActiveForExpenseWorksheetAsync(cancellationToken);

        var collectedByIncomeKey = incomeBuckets
            .Where(operation => operation.IncomeType is not null)
            .GroupBy(operation => NormalizeFinanceLookupKey(operation.IncomeType!.Code ?? operation.IncomeType.Name))
            .ToDictionary(group => group.Key, group => MoneyMath.RoundMoney(group.Sum(operation => operation.Amount)), StringComparer.Ordinal);

        var rows = new List<ExpenseWorksheetRowDto>();
        var supplierKeys = supplierAccruals
            .Select(accrual => (accrual.SupplierId, accrual.ExpenseTypeId))
            .Concat(expenseOperations
                .Where(operation => operation.SupplierId is not null && operation.ExpenseTypeId is not null)
                .Select(operation => (SupplierId: operation.SupplierId!.Value, ExpenseTypeId: operation.ExpenseTypeId!.Value)))
            .Distinct()
            .ToList();

        foreach (var key in supplierKeys)
        {
            var accrualGroup = supplierAccruals
                .Where(accrual => accrual.SupplierId == key.SupplierId && accrual.ExpenseTypeId == key.ExpenseTypeId)
                .ToList();
            var expenseGroup = expenseOperations
                .Where(operation => operation.SupplierId == key.SupplierId && operation.ExpenseTypeId == key.ExpenseTypeId)
                .ToList();
            var sampleAccrual = accrualGroup.FirstOrDefault();
            var sampleExpense = expenseGroup.FirstOrDefault();
            var supplierId = sampleAccrual?.SupplierId ?? sampleExpense!.SupplierId!.Value;
            var supplierName = sampleAccrual?.Supplier.Name ?? sampleExpense!.Supplier!.Name;
            var expenseTypeId = sampleAccrual?.ExpenseTypeId ?? sampleExpense!.ExpenseTypeId!.Value;
            var expenseTypeName = sampleAccrual?.ExpenseType.Name ?? sampleExpense!.ExpenseType!.Name;
            var expenseTypeCode = sampleAccrual?.ExpenseType.Code ?? sampleExpense!.ExpenseType?.Code;
            var accrualAmount = MoneyMath.RoundMoney(accrualGroup.Sum(accrual => accrual.Amount));
            var expenseAmount = MoneyMath.RoundMoney(expenseGroup.Sum(operation => operation.Amount));
            var balance = MoneyMath.RoundMoney(Math.Max(accrualAmount - expenseAmount, 0m));
            var collected = TryGetCollectedAmount(collectedByIncomeKey, expenseTypeName, expenseTypeCode);
            decimal? difference = collected.HasValue ? MoneyMath.RoundMoney(collected.Value - accrualAmount) : null;
            rows.Add(new ExpenseWorksheetRowDto(
                "supplier",
                supplierId,
                null,
                supplierName,
                expenseTypeId,
                expenseTypeName,
                accrualAmount,
                expenseAmount,
                balance,
                collected,
                difference));
        }

        foreach (var staffMember in activeStaffMembers)
        {
            var payments = expenseOperations.Where(operation => operation.StaffMemberId == staffMember.Id).ToList();
            var accrualAmount = MoneyMath.RoundMoney(staffMember.Rate);
            var expenseAmount = MoneyMath.RoundMoney(payments.Sum(operation => operation.Amount));
            rows.Add(new ExpenseWorksheetRowDto(
                "staff",
                null,
                staffMember.Id,
                staffMember.FullName,
                null,
                staffMember.Department.Name,
                accrualAmount,
                expenseAmount,
                MoneyMath.RoundMoney(Math.Max(accrualAmount - expenseAmount, 0m)),
                null,
                null));
        }

        rows = rows
            .OrderBy(row => row.RowKind == "supplier" ? 0 : 1)
            .ThenBy(row => row.CounterpartyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ExpenseTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accrualTotal = MoneyMath.RoundMoney(rows.Sum(row => row.AccrualAmount));
        var expenseTotal = MoneyMath.RoundMoney(rows.Sum(row => row.ExpenseAmount));
        var balanceTotal = MoneyMath.RoundMoney(rows.Sum(row => row.Balance));
        var collectedTotal = MoneyMath.RoundMoney(rows.Sum(row => row.CollectedAmount ?? 0m));
        var differenceTotal = MoneyMath.RoundMoney(collectedTotal - accrualTotal);
        var bankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
        var cashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);

        return FinanceResult<ExpenseWorksheetDto>.Success(new ExpenseWorksheetDto(
            accountingMonth,
            accrualTotal,
            expenseTotal,
            balanceTotal,
            collectedTotal,
            differenceTotal,
            bankAmount,
            cashAmount,
            rows));
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
        var operations = ApplyFilters(dbContext.FinancialOperations.AsNoTracking().Where(operation => !operation.IsCanceled), request);
        var accruals = ApplyAccrualFilters(dbContext.Accruals.AsNoTracking().Where(accrual => !accrual.IsCanceled), new AccrualListRequest(request.DateFrom, request.DateTo, request.Search));
        var meterReadings = ApplyMeterReadingFilters(dbContext.MeterReadings.AsNoTracking().Where(reading => !reading.IsCanceled), new MeterReadingListRequest(request.DateFrom, request.DateTo, null, request.Search));
        var incomeTotal = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var expenseTotal = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var accrualTotal = await accruals.SumAsync(accrual => accrual.Amount, cancellationToken);
        var operationCount = await operations.CountAsync(cancellationToken);
        var accrualCount = await accruals.CountAsync(cancellationToken);
        var meterReadingCount = await meterReadings.CountAsync(cancellationToken);

        return new FinanceSummaryDto(incomeTotal, expenseTotal, accrualTotal, incomeTotal - expenseTotal, accrualTotal - incomeTotal, operationCount, accrualCount, meterReadingCount);
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateIncomeAsync(CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
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

        dbContext.FinancialOperations.Add(operation);
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

        var garage = await dbContext.Garages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
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
            return FinanceResult<FinancialOperationDto>.Failure("debt_payment_amount_exceeds_opening_debt", $"Сумма оплаты входящего долга не может превышать {availableOpeningDebt.ToString("0.00", RussianCulture)}.");
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
        var previousAccrualTotal = await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garage.Id &&
                accrual.AccountingMonth < accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);
        var previousIncomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garage.Id &&
                operation.AccountingMonth < accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var alreadyPaidOpeningDebt = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId == garage.Id &&
                operation.AccountingMonth == accountingMonth &&
                operation.IncomeType != null &&
                (operation.IncomeType.Code == DebtTransferIncomeTypeCode || operation.IncomeType.Name == DebtTransferIncomeTypeName))
            .SumAsync(operation => operation.Amount, cancellationToken);

        return MoneyMath.RoundMoney(Math.Max(garage.StartingBalance + previousAccrualTotal - previousIncomeTotal - alreadyPaidOpeningDebt, 0m));
    }

    private async Task<decimal> CalculateAvailableBankAmountAsync(CancellationToken cancellationToken)
    {
        var bankDepositsTotal = await dbContext.FundOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FundOperationKinds.Deposit)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var bankExpenseTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                (operation.ExpenseType == null ||
                    !((operation.ExpenseType.Code != null && CashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                    CashExpenseTypeNames.Contains(operation.ExpenseType.Name))))
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;

        return MoneyMath.RoundMoney(Math.Max(bankDepositsTotal - bankExpenseTotal, 0m));
    }

    private async Task<decimal> CalculateAvailableCashAmountAsync(CancellationToken cancellationToken)
    {
        var incomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var bankDepositsTotal = await dbContext.FundOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FundOperationKinds.Deposit)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
        var cashExpenseTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.ExpenseType != null &&
                ((operation.ExpenseType.Code != null && CashExpenseTypeCodes.Contains(operation.ExpenseType.Code)) ||
                    CashExpenseTypeNames.Contains(operation.ExpenseType.Name)))
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;

        return MoneyMath.RoundMoney(Math.Max(incomeTotal - bankDepositsTotal - cashExpenseTotal, 0m));
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await expenseTypeRepository.FindActiveAsync(request.ExpenseTypeId, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        if (IsCashExpenseType(expenseType))
        {
            var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
            if (amount > availableCashAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "cash_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток в кассе {availableCashAmount.ToString("0.00", RussianCulture)}.");
            }
        }
        else
        {
            var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
            if (amount > availableBankAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "bank_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток на банковском счете {availableBankAmount.ToString("0.00", RussianCulture)}.");
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

        dbContext.FinancialOperations.Add(operation);
        AddAudit(actorUserId, "finance.expense_created", operation, FormatExpenseCreatedAuditSummary(operation));
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
        var paidThisMonth = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.StaffMemberId == staffMember.Id &&
                operation.AccountingMonth == accountingMonth)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var availableAmount = MoneyMath.RoundMoney(staffMember.Rate - paidThisMonth);
        if (amount > availableAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {availableAmount.ToString("0.00", RussianCulture)}.");
        }

        var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
        if (amount > availableBankAmount)
        {
            return FinanceResult<FinancialOperationDto>.Failure(
                "bank_amount_insufficient",
                $"Сумма выплаты превышает доступный остаток на банковском счете {availableBankAmount.ToString("0.00", RussianCulture)}.");
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

        dbContext.FinancialOperations.Add(operation);
        AddAudit(actorUserId, "finance.staff_payment_created", operation, FormatStaffPaymentCreatedAuditSummary(operation, availableAmount));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> UpdateIncomeAsync(Guid operationId, CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await dbContext.FinancialOperations
            .Include(item => item.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
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

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("garage_not_found", "Гараж для поступления не найден.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
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
        AddAudit(actorUserId, "finance.income_updated", operation, FormatIncomeUpdatedAuditSummary(previousSnapshot, operation), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> UpdateExpenseAsync(Guid operationId, CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await dbContext.FinancialOperations
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
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

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
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
                    $"Сумма выплаты превышает доступный остаток в кассе {availableCashAmount.ToString("0.00", RussianCulture)}.");
            }
        }
        else
        {
            var availableBankAmount = MoneyMath.RoundMoney(await CalculateAvailableBankAmountAsync(cancellationToken) + (wasCashExpense ? 0m : operation.Amount));
            if (amount > availableBankAmount)
            {
                return FinanceResult<FinancialOperationDto>.Failure(
                    "bank_amount_insufficient",
                    $"Сумма выплаты превышает доступный остаток на банковском счете {availableBankAmount.ToString("0.00", RussianCulture)}.");
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

        var operation = await dbContext.FinancialOperations
            .Include(item => item.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .Include(item => item.Supplier)
            .Include(item => item.StaffMember)
            .ThenInclude(staffMember => staffMember!.Department)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_not_found", "Финансовая операция не найдена.");
        }

        if (operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Финансовая операция уже отменена.");
        }

        operation.IsCanceled = true;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.Comment = AppendCancelReason(operation.Comment, reason);
        AddAudit(actorUserId, "finance.operation_canceled", operation, FormatOperationCanceledAuditSummary(operation, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var operation = await dbContext.FinancialOperations
            .Include(item => item.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(item => item.IncomeType)
            .Include(item => item.Supplier)
            .Include(item => item.StaffMember)
            .ThenInclude(staffMember => staffMember!.Department)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
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
                var paidThisMonth = await dbContext.FinancialOperations.AsNoTracking()
                    .Where(item =>
                        !item.IsCanceled &&
                        item.OperationKind == FinancialOperationKinds.Expense &&
                        item.StaffMemberId == operation.StaffMemberId &&
                        item.AccountingMonth == operation.AccountingMonth)
                    .SumAsync(item => item.Amount, cancellationToken);
                var availableStaffAmount = MoneyMath.RoundMoney((operation.StaffMember?.Rate ?? 0m) - paidThisMonth);
                if (operation.Amount > availableStaffAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure("staff_payment_amount_exceeds_available", $"Сумма выплаты превышает доступный остаток по сотруднику {availableStaffAmount.ToString("0.00", RussianCulture)}.");
                }
            }

            if (IsCashExpenseType(operation.ExpenseType))
            {
                var availableCashAmount = await CalculateAvailableCashAmountAsync(cancellationToken);
                if (operation.Amount > availableCashAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure(
                        "cash_amount_insufficient",
                        $"Сумма выплаты превышает доступный остаток в кассе {availableCashAmount.ToString("0.00", RussianCulture)}.");
                }
            }
            else
            {
                var availableBankAmount = await CalculateAvailableBankAmountAsync(cancellationToken);
                if (operation.Amount > availableBankAmount)
                {
                    return FinanceResult<FinancialOperationDto>.Failure(
                        "bank_amount_insufficient",
                        $"Сумма выплаты превышает доступный остаток на банковском счете {availableBankAmount.ToString("0.00", RussianCulture)}.");
                }
            }
        }

        operation.IsCanceled = false;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

        var accrual = await dbContext.Accruals
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(item => item.IncomeType)
            .SingleOrDefaultAsync(item => item.Id == accrualId, cancellationToken);
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
        AddAudit(actorUserId, "finance.accrual_canceled", accrual, FormatAccrualCanceledAuditSummary(accrual, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
    }

    public async Task<FinanceResult<AccrualDto>> RestoreAccrualAsync(Guid accrualId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var accrual = await dbContext.Accruals
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(item => item.IncomeType)
            .SingleOrDefaultAsync(item => item.Id == accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
        }

        if (!accrual.IsCanceled)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_canceled", "Начисление уже активно.");
        }

        if (await dbContext.Accruals.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != accrual.Id &&
            item.GarageId == accrual.GarageId &&
            item.IncomeTypeId == accrual.IncomeTypeId &&
            item.AccountingMonth == accrual.AccountingMonth &&
            item.Source == accrual.Source,
            cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        accrual.IsCanceled = false;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для начисления не найден.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<AccrualDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (await dbContext.Accruals.AnyAsync(
            accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garage.Id &&
                accrual.IncomeTypeId == incomeType.Id &&
                accrual.AccountingMonth == month &&
                accrual.Source == source,
            cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        var accrual = new Accrual
        {
            GarageId = garage.Id,
            Garage = garage,
            IncomeTypeId = incomeType.Id,
            IncomeType = incomeType,
            AccountingMonth = month,
            Amount = MoneyMath.RoundMoney(request.Amount),
            Source = source,
            Comment = NormalizeOptional(request.Comment)
        };

        dbContext.Accruals.Add(accrual);
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

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для переноса задолженности не найден.");
        }

        var incomeType = await GetOrCreateDebtTransferIncomeTypeAsync(cancellationToken);
        var comment = BuildDebtTransferComment(sourceMonth, targetMonth, request.Comment);
        var accrual = await dbContext.Accruals
            .Include(item => item.Garage)
            .ThenInclude(item => item.Owner)
            .Include(item => item.IncomeType)
            .SingleOrDefaultAsync(
                item =>
                    !item.IsCanceled &&
                    item.GarageId == garage.Id &&
                    item.IncomeTypeId == incomeType.Id &&
                    item.AccountingMonth == targetMonth &&
                    item.Source == AccrualSources.DebtTransfer,
                cancellationToken);

        if (accrual is null)
        {
            accrual = new Accrual
            {
                GarageId = garage.Id,
                Garage = garage,
                IncomeTypeId = incomeType.Id,
                IncomeType = incomeType,
                AccountingMonth = targetMonth,
                Amount = amount,
                Source = AccrualSources.DebtTransfer,
                Comment = comment
            };
            dbContext.Accruals.Add(accrual);
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

        var accrual = await dbContext.Accruals
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(item => item.IncomeType)
            .SingleOrDefaultAsync(item => item.Id == accrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_not_found", "Начисление не найдено.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<AccrualDto>.Failure("accrual_already_canceled", "Отмененное начисление нельзя изменить.");
        }

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<AccrualDto>.Failure("garage_not_found", "Гараж для начисления не найден.");
        }

        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<AccrualDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (await dbContext.Accruals.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != accrual.Id &&
            item.GarageId == garage.Id &&
            item.IncomeTypeId == incomeType.Id &&
            item.AccountingMonth == month &&
            item.Source == source,
            cancellationToken))
        {
            return FinanceResult<AccrualDto>.Failure("accrual_duplicate", "Такое начисление за месяц уже внесено.");
        }

        var amount = MoneyMath.RoundMoney(request.Amount);
        var comment = NormalizeOptional(request.Comment);
        if (AccrualMatches(accrual, garage.Id, incomeType.Id, month, amount, source, comment))
        {
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
        var newValues = new Dictionary<string, object?>
        {
            ["garage"] = garage.Number,
            ["incomeType"] = incomeType.Name,
            ["accountingMonth"] = month,
            ["amount"] = amount,
            ["source"] = source,
            ["comment"] = comment
        };

        accrual.GarageId = garage.Id;
        accrual.Garage = garage;
        accrual.IncomeTypeId = incomeType.Id;
        accrual.IncomeType = incomeType;
        accrual.AccountingMonth = month;
        accrual.Amount = amount;
        accrual.Source = source;
        accrual.Comment = comment;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
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

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
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
        if (await dbContext.SupplierAccruals.AnyAsync(
            accrual =>
                !accrual.IsCanceled &&
                accrual.SupplierId == supplier.Id &&
                accrual.ExpenseTypeId == expenseType.Id &&
                accrual.AccountingMonth == month &&
                accrual.Source == source &&
                accrual.DocumentNumber == documentNumber,
            cancellationToken))
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

        dbContext.SupplierAccruals.Add(accrual);
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

        var accrual = await dbContext.SupplierAccruals
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == supplierAccrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_found", "Начисление поставщику не найдено.");
        }

        if (accrual.IsCanceled)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_already_canceled", "Отмененное начисление поставщику нельзя изменить.");
        }

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
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
        if (await dbContext.SupplierAccruals.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != accrual.Id &&
            item.SupplierId == supplier.Id &&
            item.ExpenseTypeId == expenseType.Id &&
            item.AccountingMonth == month &&
            item.Source == source &&
            item.DocumentNumber == documentNumber,
            cancellationToken))
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

        var accrual = await dbContext.SupplierAccruals
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == supplierAccrualId, cancellationToken);
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
        var accrual = await dbContext.SupplierAccruals
            .Include(item => item.Supplier)
            .Include(item => item.ExpenseType)
            .SingleOrDefaultAsync(item => item.Id == supplierAccrualId, cancellationToken);
        if (accrual is null)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_found", "Начисление поставщику не найдено.");
        }

        if (!accrual.IsCanceled)
        {
            return FinanceResult<SupplierAccrualDto>.Failure("supplier_accrual_not_canceled", "Начисление поставщику уже активно.");
        }

        if (await dbContext.SupplierAccruals.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != accrual.Id &&
            item.SupplierId == accrual.SupplierId &&
            item.ExpenseTypeId == accrual.ExpenseTypeId &&
            item.AccountingMonth == accrual.AccountingMonth &&
            item.Source == accrual.Source &&
            item.DocumentNumber == accrual.DocumentNumber,
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
        var incomeType = await dbContext.IncomeTypes.SingleOrDefaultAsync(item => item.Id == request.IncomeTypeId && !item.IsArchived, cancellationToken);
        if (incomeType is null)
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("income_type_not_found", "Вид начисления не найден.");
        }

        var tariff = await dbContext.Tariffs.SingleOrDefaultAsync(item => item.Id == request.TariffId && !item.IsArchived, cancellationToken);
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

        var garages = await dbContext.Garages
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
        var created = new List<AccrualDto>();
        var skipped = new List<string>();

        foreach (var garage in garages)
        {
            var amountResult = await CalculateRegularAccrualAmountAsync(garage, tariff, month, cancellationToken);
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

            var duplicate = await dbContext.Accruals.AnyAsync(
                accrual =>
                    !accrual.IsCanceled &&
                    accrual.GarageId == garage.Id &&
                    accrual.IncomeTypeId == incomeType.Id &&
                    accrual.AccountingMonth == month &&
                    accrual.Source == AccrualSources.Regular,
                cancellationToken);
            if (duplicate)
            {
                skipped.Add($"Гараж {garage.Number}: регулярное начисление уже есть.");
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
                Amount = amount,
                Source = AccrualSources.Regular,
                Comment = BuildRegularAccrualComment(tariff, request.Comment)
            };
            dbContext.Accruals.Add(accrual);
            created.Add(ToDto(accrual));
        }

        if (created.Count == 0)
        {
            return FinanceResult<RegularAccrualGenerationResultDto>.Failure("regular_accruals_empty", "Не создано ни одного начисления.");
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
        var settings = await dbContext.ChargeServiceSettings
            .AsNoTracking()
            .Where(setting => !setting.IsArchived && setting.IsRegular)
            .OrderBy(setting => setting.Name)
            .ToListAsync(cancellationToken);
        if (settings.Count == 0)
        {
            return FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure("regular_catalog_empty", "В каталоге нет активных регулярных услуг.");
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
            return FinanceResult<RegularCatalogAccrualGenerationResultDto>.Failure("regular_catalog_accruals_empty", "По каталогу услуг не создано ни одного начисления.");
        }

        var skippedCount = serviceResults.Sum(result => result.SkippedCount) + skippedServices.Count;
        var totalAmount = serviceResults.Sum(result => result.TotalAmount);
        AddAudit(
            actorUserId,
            "finance.regular_catalog_accruals_generated",
            "accrual",
            Guid.NewGuid(),
            $"Сформированы регулярные начисления по каталогу услуг за {month:MM.yyyy}: услуг обработано {serviceResults.Count}, создано {createdCount}, на сумму {totalAmount.ToString("N2", CultureInfo.InvariantCulture)}, пропущено {skippedCount}.",
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
        var campaign = await dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
                    .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == request.FeeCampaignId && !item.IsArchived, cancellationToken);
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

        var garages = campaign.AppliesToAllGarages
            ? await dbContext.Garages
                .Include(garage => garage.Owner)
                .Where(garage => !garage.IsArchived)
                .OrderBy(garage => garage.Number)
                .ToListAsync(cancellationToken)
            : campaign.ParticipantGarages
                .Select(participant => participant.Garage)
                .Where(garage => !garage.IsArchived)
                .OrderBy(garage => garage.Number)
                .ToList();
        if (garages.Count == 0)
        {
            return FinanceResult<FeeCampaignAccrualGenerationResultDto>.Failure("fee_campaign_no_garages", "Нет активных гаражей для начисления сбора.");
        }

        var created = new List<AccrualDto>();
        var skipped = new List<string>();
        foreach (var garage in garages)
        {
            var duplicate = await dbContext.Accruals.AnyAsync(
                accrual =>
                    !accrual.IsCanceled &&
                    accrual.GarageId == garage.Id &&
                    accrual.IncomeTypeId == campaign.IncomeTypeId &&
                    accrual.AccountingMonth == month &&
                    accrual.Source == AccrualSources.FeeCampaign,
                cancellationToken);
            if (duplicate)
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
                Amount = amount,
                Source = AccrualSources.FeeCampaign,
                Comment = BuildFeeCampaignAccrualComment(campaign, request.Comment)
            };
            dbContext.Accruals.Add(accrual);
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

        var group = await dbContext.SupplierGroups.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.SupplierGroupId && !item.IsArchived, cancellationToken);
        if (group is null)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("supplier_group_not_found", "Группа персонала не найдена.");
        }

        var salaryExpenseType = await expenseTypeRepository.FindActiveByCodeAsync("salary", cancellationToken);
        if (salaryExpenseType is null)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("salary_expense_type_not_found", "Системный вид выплаты Зарплата не найден.");
        }

        var suppliers = await dbContext.Suppliers
            .Where(item => !item.IsArchived && item.GroupId == group.Id)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        if (suppliers.Count == 0)
        {
            return FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>.Failure("supplier_group_empty", "В выбранной группе нет активных поставщиков или сотрудников.");
        }

        var created = new List<SupplierAccrualDto>();
        var skipped = new List<string>();
        foreach (var supplier in suppliers)
        {
            var duplicate = await dbContext.SupplierAccruals.AnyAsync(
                accrual =>
                    !accrual.IsCanceled &&
                    accrual.SupplierId == supplier.Id &&
                    accrual.ExpenseTypeId == salaryExpenseType.Id &&
                    accrual.AccountingMonth == month &&
                    accrual.Source == AccrualSources.Regular &&
                    accrual.DocumentNumber == documentNumber,
                cancellationToken);
            if (duplicate)
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
            dbContext.SupplierAccruals.Add(accrual);
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

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (await dbContext.MeterReadings.AnyAsync(
            reading => !reading.IsCanceled && reading.GarageId == garage.Id && reading.MeterKind == meterKind && reading.AccountingMonth == month,
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание этого счетчика за месяц уже внесено.");
        }

        var previousReading = await dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.GarageId == garage.Id && reading.MeterKind == meterKind && reading.AccountingMonth < month)
            .OrderByDescending(reading => reading.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue);
        var previousValue = MoneyMath.RoundMeterValue(previousReading?.CurrentValue ?? GetInitialMeterValue(garage, meterKind) ?? 0m);
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

        dbContext.MeterReadings.Add(reading);
        AddAudit(actorUserId, "finance.meter_reading_created", reading, FormatMeterReadingCreatedAuditSummary(reading));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingAsync(Guid meterReadingId, CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var meterKind = request.MeterKind.Trim();
        if (meterKind is not MeterKinds.Water and not MeterKinds.Electricity)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_kind_invalid", "Тип счетчика должен быть water или electricity.");
        }

        var reading = await dbContext.MeterReadings
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        if (reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_already_canceled", "Отмененное показание нельзя изменить.");
        }

        var garage = await dbContext.Garages.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == request.GarageId && !item.IsArchived, cancellationToken);
        if (garage is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("garage_not_found", "Гараж для показания счетчика не найден.");
        }

        var month = MonthPeriod.Normalize(request.AccountingMonth);
        if (await dbContext.MeterReadings.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != reading.Id &&
            item.GarageId == garage.Id &&
            item.MeterKind == meterKind &&
            item.AccountingMonth == month,
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "Показание этого счетчика за месяц уже внесено.");
        }

        var previousReading = await dbContext.MeterReadings.AsNoTracking()
            .Where(item => !item.IsCanceled && item.Id != reading.Id && item.GarageId == garage.Id && item.MeterKind == meterKind && item.AccountingMonth < month)
            .OrderByDescending(item => item.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);
        var currentValue = MoneyMath.RoundMeterValue(request.CurrentValue);
        var previousValue = MoneyMath.RoundMeterValue(previousReading?.CurrentValue ?? GetInitialMeterValue(garage, meterKind) ?? 0m);
        var consumption = MoneyMath.RoundMeterValue(currentValue - previousValue);
        if (consumption < 0)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_decreased", "Новое показание не может быть меньше предыдущего.");
        }

        var nextReading = await dbContext.MeterReadings.AsNoTracking()
            .Where(item => !item.IsCanceled && item.Id != reading.Id && item.GarageId == garage.Id && item.MeterKind == meterKind && item.AccountingMonth > month)
            .OrderBy(item => item.AccountingMonth)
            .FirstOrDefaultAsync(cancellationToken);
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
        reading.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "finance.meter_reading_updated", reading, FormatMeterReadingUpdatedAuditSummary(reading), oldValues, newValues);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> CancelMeterReadingAsync(Guid meterReadingId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_cancel_reason_required", "Для отмены показания счетчика нужна причина.");
        }

        var reading = await dbContext.MeterReadings
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == meterReadingId, cancellationToken);
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
        AddAudit(actorUserId, "finance.meter_reading_canceled", reading, FormatMeterReadingCanceledAuditSummary(reading, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    public async Task<FinanceResult<MeterReadingDto>> RestoreMeterReadingAsync(Guid meterReadingId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var reading = await dbContext.MeterReadings
            .Include(item => item.Garage)
            .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == meterReadingId, cancellationToken);
        if (reading is null)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_found", "Показание счетчика не найдено.");
        }

        if (!reading.IsCanceled)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_not_canceled", "Показание счетчика уже активно.");
        }

        if (await dbContext.MeterReadings.AnyAsync(item =>
            !item.IsCanceled &&
            item.Id != reading.Id &&
            item.GarageId == reading.GarageId &&
            item.MeterKind == reading.MeterKind &&
            item.AccountingMonth == reading.AccountingMonth,
            cancellationToken))
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_duplicate", "За этот гараж, месяц и счетчик уже есть активное показание.");
        }

        reading.IsCanceled = false;
        reading.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "finance.meter_reading_restored", reading, FormatMeterReadingRestoredAuditSummary(reading));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
    }

    private async Task<AmountCalculationResult> CalculateRegularAccrualAmountAsync(Garage garage, Tariff tariff, DateOnly month, CancellationToken cancellationToken)
    {
        return tariff.CalculationBase switch
        {
            TariffCalculationBases.Fixed => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate)),
            TariffCalculationBases.People => AmountCalculationResult.Success(MoneyMath.RoundMoney(tariff.Rate * garage.PeopleCount)),
            TariffCalculationBases.MeterWater => await CalculateMeterAmountAsync(garage.Id, MeterKinds.Water, tariff.Rate, month, cancellationToken),
            TariffCalculationBases.MeterElectricity => await CalculateElectricityMeterAmountAsync(garage.Id, tariff, month, cancellationToken),
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
        var snapshot = $"сбор {campaign.Name}: взнос {campaign.ContributionAmount.ToString("0.00", RussianCulture)}, цель {campaign.TargetAmount.ToString("0.00", RussianCulture)}, действует с {campaign.StartsOn:dd.MM.yyyy}";
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
        var incomeType = await dbContext.IncomeTypes
            .FirstOrDefaultAsync(item => !item.IsArchived && item.Code == DebtTransferIncomeTypeCode, cancellationToken)
            ?? await dbContext.IncomeTypes.FirstOrDefaultAsync(item => !item.IsArchived && item.Name == DebtTransferIncomeTypeName, cancellationToken);
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

        incomeType = await dbContext.IncomeTypes
            .FirstOrDefaultAsync(item => item.IsArchived && (item.Code == DebtTransferIncomeTypeCode || item.Name == DebtTransferIncomeTypeName), cancellationToken);
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
        dbContext.IncomeTypes.Add(incomeType);
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
        return $"Создан перенос задолженности {accrual.Amount.ToString("0.00", RussianCulture)} по гаражу {accrual.Garage.Number} из {sourceMonth:MM.yyyy} в {targetMonth:MM.yyyy}.";
    }

    private static string FormatDebtTransferUpdatedAuditSummary(AccrualAuditSnapshot before, Accrual accrual, DateOnly sourceMonth, DateOnly targetMonth, decimal addedAmount)
    {
        return $"Дополнен перенос задолженности по гаражу {accrual.Garage.Number} из {sourceMonth:MM.yyyy} в {targetMonth:MM.yyyy}: добавлено {addedAmount.ToString("0.00", RussianCulture)}; было {FormatAccrualSnapshot(before)}; стало {FormatAccrualSnapshot(AccrualAuditSnapshot.From(accrual))}.";
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
        var amount = operation.Amount.ToString("0.00", RussianCulture);
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
        var summary = $"Создана выплата {FormatStaffPaymentSnapshot(operation)}; доступно до выплаты {availableBeforePayment.ToString("0.00", RussianCulture)}.";
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
        var amount = operation.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        if (operation.StaffMember is not null)
        {
            return FormatStaffPaymentSnapshot(operation);
        }

        return $"{amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}";
    }

    private static string FormatStaffPaymentSnapshot(FinancialOperation operation)
    {
        var amount = operation.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        return $"{amount} сотруднику {operation.StaffMember?.FullName} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; отдел {operation.StaffMember?.Department?.Name}; вид {operation.ExpenseType?.Name}; документ {document}";
    }

    private static string FormatOperationCanceledAuditSummary(FinancialOperation operation, string reason)
    {
        var amount = operation.Amount.ToString("0.00", RussianCulture);
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
        var amount = operation.Amount.ToString("0.00", RussianCulture);
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
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
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
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
        return $"Отменено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}. Причина: {reason}";
    }

    private static string FormatAccrualRestoredAuditSummary(Accrual accrual)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
        return $"Восстановлено начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
    }

    private static string FormatSupplierAccrualCreatedAuditSummary(SupplierAccrual accrual)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
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
        var amount = snapshot.Amount.ToString("0.00", RussianCulture);
        var comment = NormalizeOptional(snapshot.Comment);
        var summary = $"{amount} по гаражу {snapshot.GarageNumber} за {snapshot.AccountingMonth:MM.yyyy}; вид {snapshot.IncomeTypeName}; источник {snapshot.Source}";
        return comment is null ? summary : $"{summary}; комментарий {comment}";
    }

    private static string FormatSupplierAccrualSnapshot(SupplierAccrualAuditSnapshot snapshot)
    {
        var amount = snapshot.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(snapshot.DocumentNumber) ?? "без документа";
        var comment = NormalizeOptional(snapshot.Comment);
        var summary = $"{amount} поставщику {snapshot.SupplierName} за {snapshot.AccountingMonth:MM.yyyy}; вид {snapshot.ExpenseTypeName}; источник {snapshot.Source}; документ {document}";
        return comment is null ? summary : $"{summary}; комментарий {comment}";
    }

    private static string FormatSupplierAccrualCanceledAuditSummary(SupplierAccrual accrual, string reason)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(accrual.DocumentNumber) ?? "без документа";
        return $"Отменено начисление {amount} поставщику {accrual.Supplier.Name} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.ExpenseType.Name}; источник {accrual.Source}; документ {document}. Причина: {reason}";
    }

    private static string FormatSupplierAccrualRestoredAuditSummary(SupplierAccrual accrual)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
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
        var totalAmount = created.Sum(item => item.Amount).ToString("0.00", RussianCulture);
        return $"Создано регулярных начислений: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; вид {incomeType.Name}; тариф {tariff.Name}, база {tariff.CalculationBase}, {FormatTariffRateSnapshot(tariff)}; пропущено {skipped.Count}.";
    }

    private static string FormatFeeCampaignAccrualGenerationAuditSummary(DateOnly month, FeeCampaign campaign, IReadOnlyCollection<AccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = created.Sum(item => item.Amount).ToString("0.00", RussianCulture);
        return $"Создано начислений по сбору: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; сбор {campaign.Name}; вид {campaign.IncomeType.Name}; взнос {campaign.ContributionAmount.ToString("0.00", RussianCulture)}; пропущено {skipped.Count}.";
    }

    private static string FormatSupplierGroupSalaryAccrualGenerationAuditSummary(DateOnly month, string groupName, string expenseTypeName, IReadOnlyCollection<SupplierAccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = created.Sum(item => item.Amount).ToString("0.00", RussianCulture);
        return $"Создано начислений зарплаты: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; группа {groupName}; вид {expenseTypeName}; пропущено {skipped.Count}.";
    }

    private static string BuildSupplierGroupSalaryComment(string groupName, string? comment)
    {
        var baseComment = $"Зарплата по группе {groupName}";
        return comment is null ? baseComment : $"{baseComment}. {comment}";
    }

    private async Task<AmountCalculationResult> CalculateMeterAmountAsync(Guid garageId, string meterKind, decimal rate, DateOnly month, CancellationToken cancellationToken)
    {
        var reading = await dbContext.MeterReadings.AsNoTracking().SingleOrDefaultAsync(
            item => !item.IsCanceled && item.GarageId == garageId && item.MeterKind == meterKind && item.AccountingMonth == month,
            cancellationToken);
        return reading is null
            ? AmountCalculationResult.Failure("нет показания счетчика за месяц.")
            : AmountCalculationResult.Success(MoneyMath.RoundMoney(reading.Consumption * rate));
    }

    private async Task<AmountCalculationResult> CalculateElectricityMeterAmountAsync(Guid garageId, Tariff tariff, DateOnly month, CancellationToken cancellationToken)
    {
        var reading = await dbContext.MeterReadings.AsNoTracking().SingleOrDefaultAsync(
            item => !item.IsCanceled && item.GarageId == garageId && item.MeterKind == MeterKinds.Electricity && item.AccountingMonth == month,
            cancellationToken);
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
            return $"ставка {tariff.Rate.ToString("0.####", RussianCulture)}";
        }

        return $"пороги электроэнергии до {tariff.ElectricityFirstThreshold!.Value.ToString("0.####", RussianCulture)} кВт по {tariff.ElectricityFirstRate!.Value.ToString("0.####", RussianCulture)}, до {tariff.ElectricitySecondThreshold!.Value.ToString("0.####", RussianCulture)} кВт по {tariff.ElectricitySecondRate!.Value.ToString("0.####", RussianCulture)}, свыше по {tariff.ElectricityThirdRate!.Value.ToString("0.####", RussianCulture)}";
    }

    private static bool HasElectricityTiers(Tariff tariff)
    {
        return tariff.ElectricityFirstThreshold.HasValue
            && tariff.ElectricitySecondThreshold.HasValue
            && tariff.ElectricityFirstRate.HasValue
            && tariff.ElectricitySecondRate.HasValue
            && tariff.ElectricityThirdRate.HasValue;
    }

    private IQueryable<FinancialOperation> QueryOperations()
    {
        return dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Garage)
            .ThenInclude(garage => garage!.Owner)
            .Include(operation => operation.IncomeType)
            .Include(operation => operation.Supplier)
            .Include(operation => operation.StaffMember)
            .ThenInclude(staffMember => staffMember!.Department)
            .Include(operation => operation.ExpenseType)
            .Where(operation => !operation.IsCanceled);
    }

    private IQueryable<Accrual> QueryAccruals()
    {
        return dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(accrual => accrual.IncomeType)
            .Where(accrual => !accrual.IsCanceled);
    }

    private IQueryable<SupplierAccrual> QuerySupplierAccruals()
    {
        return dbContext.SupplierAccruals.AsNoTracking()
            .Include(accrual => accrual.Supplier)
            .Include(accrual => accrual.ExpenseType)
            .Where(accrual => !accrual.IsCanceled);
    }

    private IQueryable<MeterReading> QueryMeterReadings()
    {
        return dbContext.MeterReadings.AsNoTracking()
            .Include(reading => reading.Garage)
            .ThenInclude(garage => garage.Owner)
            .Where(reading => !reading.IsCanceled);
    }

    private static IQueryable<FinancialOperation> ApplyFilters(IQueryable<FinancialOperation> query, FinancialOperationListRequest request)
    {
        if (request.DateFrom is not null)
        {
            query = query.Where(operation => operation.OperationDate >= request.DateFrom);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(operation => operation.OperationDate <= request.DateTo);
        }

        if (!string.IsNullOrWhiteSpace(request.OperationKind))
        {
            var kind = request.OperationKind.Trim();
            query = query.Where(operation => operation.OperationKind == kind);
        }

        if (request.GarageId is not null)
        {
            query = query.Where(operation => operation.GarageId == request.GarageId);
        }

        if (request.SupplierId is not null)
        {
            query = query.Where(operation => operation.SupplierId == request.SupplierId);
        }

        if (request.StaffMemberId is not null)
        {
            query = query.Where(operation => operation.StaffMemberId == request.StaffMemberId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(operation =>
                (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(search)) ||
                (operation.Comment != null && operation.Comment.ToLower().Contains(search)) ||
                (operation.Garage != null && operation.Garage.Number.ToLower().Contains(search)) ||
                (operation.Supplier != null && operation.Supplier.Name.ToLower().Contains(search)) ||
                (operation.StaffMember != null && operation.StaffMember.FullName.ToLower().Contains(search)));
        }

        return query;
    }

    private static IQueryable<MeterReading> ApplyMeterReadingFilters(IQueryable<MeterReading> query, MeterReadingListRequest request)
    {
        if (request.MonthFrom is not null)
        {
            query = query.Where(reading => reading.AccountingMonth >= MonthPeriod.Normalize(request.MonthFrom.Value));
        }

        if (request.MonthTo is not null)
        {
            query = query.Where(reading => reading.AccountingMonth <= MonthPeriod.Normalize(request.MonthTo.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.MeterKind))
        {
            var meterKind = request.MeterKind.Trim();
            query = query.Where(reading => reading.MeterKind == meterKind);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(reading =>
                reading.Garage.Number.ToLower().Contains(search) ||
                (reading.Comment != null && reading.Comment.ToLower().Contains(search)));
        }

        return query;
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

    private static IQueryable<Accrual> ApplyAccrualFilters(IQueryable<Accrual> query, AccrualListRequest request)
    {
        if (request.MonthFrom is not null)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= MonthPeriod.Normalize(request.MonthFrom.Value));
        }

        if (request.MonthTo is not null)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= MonthPeriod.Normalize(request.MonthTo.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(accrual =>
                accrual.Garage.Number.ToLower().Contains(search) ||
                accrual.IncomeType.Name.ToLower().Contains(search) ||
                (accrual.Comment != null && accrual.Comment.ToLower().Contains(search)));
        }

        return query;
    }

    private static IQueryable<SupplierAccrual> ApplySupplierAccrualFilters(IQueryable<SupplierAccrual> query, SupplierAccrualListRequest request)
    {
        if (request.MonthFrom is not null)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= MonthPeriod.Normalize(request.MonthFrom.Value));
        }

        if (request.MonthTo is not null)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= MonthPeriod.Normalize(request.MonthTo.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(accrual =>
                accrual.Supplier.Name.ToLower().Contains(search) ||
                accrual.ExpenseType.Name.ToLower().Contains(search) ||
                (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(search)) ||
                (accrual.Comment != null && accrual.Comment.ToLower().Contains(search)));
        }

        if (request.SupplierId is not null)
        {
            query = query.Where(accrual => accrual.SupplierId == request.SupplierId);
        }

        return query;
    }

    private async Task<bool> HasDocumentDuplicateAsync(string operationKind, string? documentNumber, DateOnly operationDate, CancellationToken cancellationToken)
    {
        return await HasDocumentDuplicateAsync(operationKind, documentNumber, operationDate, null, cancellationToken);
    }

    private async Task<bool> HasDocumentDuplicateAsync(string operationKind, string? documentNumber, DateOnly operationDate, Guid? excludeOperationId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOptional(documentNumber);
        return normalized is not null && await dbContext.FinancialOperations.AnyAsync(
            operation =>
                !operation.IsCanceled &&
                operation.Id != excludeOperationId &&
                operation.OperationKind == operationKind &&
                operation.OperationDate == operationDate &&
                operation.DocumentNumber == normalized,
            cancellationToken);
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
        IReadOnlyDictionary<string, object?>? newValues = null)
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
            newValues);
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
        IReadOnlyDictionary<string, object?>? newValues = null)
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
            Reason: action.Contains("_canceled", StringComparison.Ordinal) ? "Отмена финансовой записи." : null,
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
        var result = new List<FinancialOperationDto>(operations.Count);
        foreach (var operation in operations)
        {
            result.Add(await ToDtoAsync(operation, cancellationToken));
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
        var startingBalance = operation.Garage?.StartingBalance ?? await dbContext.Garages
            .Where(garage => garage.Id == garageId)
            .Select(garage => garage.StartingBalance)
            .SingleAsync(cancellationToken);
        var accrualTotal = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= operation.AccountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);
        var previousIncomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(previous =>
                !previous.IsCanceled &&
                previous.Id != operation.Id &&
                previous.OperationKind == FinancialOperationKinds.Income &&
                previous.GarageId == garageId &&
                previous.OperationDate < operation.OperationDate)
            .SumAsync(previous => previous.Amount, cancellationToken);

        return MoneyMath.RoundMoney(startingBalance + accrualTotal - previousIncomeTotal);
    }

    private async Task<IReadOnlyList<PaymentAllocationDto>> CalculateGaragePaymentAllocationsAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var garageId = operation.GarageId!.Value;
        var startingBalance = operation.Garage?.StartingBalance ?? await dbContext.Garages
            .Where(garage => garage.Id == garageId)
            .Select(garage => garage.StartingBalance)
            .SingleAsync(cancellationToken);
        var previousIncomeTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(previous =>
                !previous.IsCanceled &&
                previous.Id != operation.Id &&
                previous.OperationKind == FinancialOperationKinds.Income &&
                previous.GarageId == garageId &&
                previous.OperationDate < operation.OperationDate)
            .SumAsync(previous => previous.Amount, cancellationToken);
        var accrualBucketRows = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= operation.AccountingMonth)
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .OrderBy(bucket => bucket.AccountingMonth)
            .ToListAsync(cancellationToken);
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
        var startingBalance = operation.Supplier?.StartingBalance ?? await dbContext.Suppliers
            .Where(supplier => supplier.Id == supplierId)
            .Select(supplier => supplier.StartingBalance)
            .SingleAsync(cancellationToken);
        var accrualTotal = await dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.SupplierId == supplierId && accrual.AccountingMonth <= operation.AccountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);
        var previousExpenseTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(previous =>
                !previous.IsCanceled &&
                previous.Id != operation.Id &&
                previous.OperationKind == FinancialOperationKinds.Expense &&
                previous.SupplierId == supplierId &&
                previous.OperationDate < operation.OperationDate)
            .SumAsync(previous => previous.Amount, cancellationToken);

        return MoneyMath.RoundMoney(startingBalance + accrualTotal - previousExpenseTotal);
    }

    private async Task<IReadOnlyList<PaymentAllocationDto>> CalculateSupplierPaymentAllocationsAsync(FinancialOperation operation, CancellationToken cancellationToken)
    {
        var supplierId = operation.SupplierId!.Value;
        var startingBalance = operation.Supplier?.StartingBalance ?? await dbContext.Suppliers
            .Where(supplier => supplier.Id == supplierId)
            .Select(supplier => supplier.StartingBalance)
            .SingleAsync(cancellationToken);
        var previousExpenseTotal = await dbContext.FinancialOperations.AsNoTracking()
            .Where(previous =>
                !previous.IsCanceled &&
                previous.Id != operation.Id &&
                previous.OperationKind == FinancialOperationKinds.Expense &&
                previous.SupplierId == supplierId &&
                previous.OperationDate < operation.OperationDate)
            .SumAsync(previous => previous.Amount, cancellationToken);
        var accrualBucketRows = await dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.SupplierId == supplierId && accrual.AccountingMonth <= operation.AccountingMonth)
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .OrderBy(bucket => bucket.AccountingMonth)
            .ToListAsync(cancellationToken);
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
            operation.StaffMemberId,
            operation.StaffMember?.FullName,
            operation.StaffMember?.Department?.Name,
            operation.CreatedAtUtc);
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

    private sealed record IncomeWorksheetBucket(DateOnly AccountingMonth, Guid IncomeTypeId, string IncomeTypeName, string? IncomeTypeCode, decimal Amount);

    private sealed record AllocationDebtBucket(string Kind, DateOnly? AccountingMonth, string Label, decimal Amount);

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
            accrual.IsCanceled);
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
            reading.IsCanceled);
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
