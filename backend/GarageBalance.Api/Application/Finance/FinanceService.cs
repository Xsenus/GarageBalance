using System.Globalization;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceService(
    GarageBalanceDbContext dbContext,
    IAuditEventWriter auditEventWriter) : IFinanceService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int MaxBalanceHistoryMonths = 60;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public FinanceService(GarageBalanceDbContext dbContext)
        : this(dbContext, new AuditEventWriter(dbContext))
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
        AddAudit(actorUserId, "finance.income_created", operation.Id, FormatIncomeCreatedAuditSummary(operation));
        await dbContext.SaveChangesAsync(cancellationToken);
        return FinanceResult<FinancialOperationDto>.Success(await ToDtoAsync(operation, cancellationToken));
    }

    public async Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == request.ExpenseTypeId && !item.IsArchived, cancellationToken);
        if (expenseType is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("expense_type_not_found", "Вид выплаты не найден.");
        }

        var duplicate = await HasDocumentDuplicateAsync(FinancialOperationKinds.Expense, request.DocumentNumber, request.OperationDate, cancellationToken);
        if (duplicate)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_duplicate", "Операция с таким документом и датой уже внесена.");
        }

        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = request.OperationDate,
            AccountingMonth = MonthPeriod.Normalize(request.AccountingMonth),
            Amount = MoneyMath.RoundMoney(request.Amount),
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            Comment = NormalizeOptional(request.Comment),
            SupplierId = supplier.Id,
            Supplier = supplier,
            ExpenseTypeId = expenseType.Id,
            ExpenseType = expenseType
        };

        dbContext.FinancialOperations.Add(operation);
        AddAudit(actorUserId, "finance.expense_created", operation.Id, FormatExpenseCreatedAuditSummary(operation));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.income_updated", operation.Id, FormatIncomeUpdatedAuditSummary(previousSnapshot, operation));
        await dbContext.SaveChangesAsync(cancellationToken);
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

        if (operation.IsCanceled)
        {
            return FinanceResult<FinancialOperationDto>.Failure("operation_already_canceled", "Отмененную операцию нельзя изменить.");
        }

        var supplier = await dbContext.Suppliers.SingleOrDefaultAsync(item => item.Id == request.SupplierId && !item.IsArchived, cancellationToken);
        if (supplier is null)
        {
            return FinanceResult<FinancialOperationDto>.Failure("supplier_not_found", "Поставщик для выплаты не найден.");
        }

        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == request.ExpenseTypeId && !item.IsArchived, cancellationToken);
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

        var previousSnapshot = FormatExpenseOperationSnapshot(operation);
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
        AddAudit(actorUserId, "finance.expense_updated", operation.Id, FormatExpenseUpdatedAuditSummary(previousSnapshot, operation));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.operation_canceled", operation.Id, FormatOperationCanceledAuditSummary(operation, reason));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.accrual_canceled", "accrual", accrual.Id, FormatAccrualCanceledAuditSummary(accrual, reason));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.accrual_created", "accrual", accrual.Id, FormatAccrualCreatedAuditSummary(accrual));
        await dbContext.SaveChangesAsync(cancellationToken);
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

        accrual.GarageId = garage.Id;
        accrual.Garage = garage;
        accrual.IncomeTypeId = incomeType.Id;
        accrual.IncomeType = incomeType;
        accrual.AccountingMonth = month;
        accrual.Amount = amount;
        accrual.Source = source;
        accrual.Comment = comment;
        accrual.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "finance.accrual_updated", "accrual", accrual.Id, FormatAccrualUpdatedAuditSummary(before, accrual));
        await dbContext.SaveChangesAsync(cancellationToken);
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

        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == request.ExpenseTypeId && !item.IsArchived, cancellationToken);
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
        AddAudit(actorUserId, "finance.supplier_accrual_created", "supplier_accrual", accrual.Id, FormatSupplierAccrualCreatedAuditSummary(accrual));
        await dbContext.SaveChangesAsync(cancellationToken);
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

        var expenseType = await dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == request.ExpenseTypeId && !item.IsArchived, cancellationToken);
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
        AddAudit(actorUserId, "finance.supplier_accrual_updated", "supplier_accrual", accrual.Id, FormatSupplierAccrualUpdatedAuditSummary(before, accrual));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.supplier_accrual_canceled", "supplier_accrual", accrual.Id, FormatSupplierAccrualCanceledAuditSummary(accrual, reason));
        await dbContext.SaveChangesAsync(cancellationToken);
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

        AddAudit(actorUserId, "finance.regular_accruals_generated", "accrual", Guid.NewGuid(), FormatRegularAccrualGenerationAuditSummary(month, incomeType, tariff, created, skipped));
        await dbContext.SaveChangesAsync(cancellationToken);

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

        var salaryExpenseType = await dbContext.ExpenseTypes
            .SingleOrDefaultAsync(item => item.Code == "salary" && !item.IsArchived, cancellationToken);
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

        AddAudit(actorUserId, "finance.supplier_group_salary_accruals_generated", "supplier_accrual", Guid.NewGuid(), FormatSupplierGroupSalaryAccrualGenerationAuditSummary(month, group.Name, salaryExpenseType.Name, created, skipped));
        await dbContext.SaveChangesAsync(cancellationToken);

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
        AddAudit(actorUserId, "finance.meter_reading_created", "meter_reading", reading.Id, FormatMeterReadingCreatedAuditSummary(reading));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.meter_reading_updated", "meter_reading", reading.Id, FormatMeterReadingUpdatedAuditSummary(reading));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        AddAudit(actorUserId, "finance.meter_reading_canceled", "meter_reading", reading.Id, FormatMeterReadingCanceledAuditSummary(reading, reason));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        return $"{amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}";
    }

    private static string FormatOperationCanceledAuditSummary(FinancialOperation operation, string reason)
    {
        var amount = operation.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(operation.DocumentNumber) ?? "без документа";
        return operation.OperationKind == FinancialOperationKinds.Income
            ? $"Отменено поступление {amount} по гаражу {operation.Garage?.Number} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.IncomeType?.Name}; документ {document}. Причина: {reason}"
            : $"Отменена выплата {amount} поставщику {operation.Supplier?.Name} от {operation.OperationDate:dd.MM.yyyy} за {operation.AccountingMonth:MM.yyyy}; вид {operation.ExpenseType?.Name}; документ {document}. Причина: {reason}";
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

    private static string FormatMeterReadingCanceledAuditSummary(MeterReading reading, string reason)
    {
        return $"Отменено показание {reading.MeterKind} по гаражу {reading.Garage.Number} за {reading.AccountingMonth:MM.yyyy}; дата {reading.ReadingDate:dd.MM.yyyy}; расход {reading.Consumption.ToString("0.####", RussianCulture)}. Причина: {reason}";
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

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(operation =>
                (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(search)) ||
                (operation.Comment != null && operation.Comment.ToLower().Contains(search)) ||
                (operation.Garage != null && operation.Garage.Number.ToLower().Contains(search)) ||
                (operation.Supplier != null && operation.Supplier.Name.ToLower().Contains(search)));
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

    private void AddAudit(Guid? actorUserId, string action, Guid operationId, string summary)
    {
        AddAudit(actorUserId, action, "financial_operation", operationId, summary);
    }

    private void AddAudit(Guid? actorUserId, string action, string entityType, Guid entityId, string summary)
    {
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            entityType,
            entityId.ToString(),
            Summary: summary,
            EntityDisplayName: NormalizeAuditDisplayName(summary),
            Reason: action.Contains("_canceled", StringComparison.Ordinal) ? "Отмена финансовой записи." : null,
            Metadata: new Dictionary<string, object?>
            {
                ["financeEntityType"] = entityType
            }));
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
            operation.IsCanceled);
    }

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
