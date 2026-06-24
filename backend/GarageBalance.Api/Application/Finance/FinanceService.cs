using System.Globalization;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceService(GarageBalanceDbContext dbContext) : IFinanceService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var operations = await ApplyFilters(QueryOperations(), request)
            .OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .Take(NormalizeListLimit(request.Limit))
            .ToListAsync(cancellationToken);
        return await ToOperationDtosAsync(operations, cancellationToken);
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

    public async Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken)
    {
        return await ApplySupplierAccrualFilters(QuerySupplierAccruals(), request)
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Supplier.Name)
            .Take(NormalizeListLimit(request.Limit))
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
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

    private static int NormalizeListLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
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
        AddAudit(actorUserId, "finance.income_created", operation.Id, $"Создано поступление {operation.Amount:N2} по гаражу {garage.Number}.");
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
        AddAudit(actorUserId, "finance.expense_created", operation.Id, $"Создана выплата {operation.Amount:N2} поставщику {supplier.Name}.");
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
        AddAudit(actorUserId, "finance.operation_canceled", operation.Id, $"Отменена операция {operation.DocumentNumber ?? operation.Id.ToString()} на сумму {operation.Amount:N2}. Причина: {reason}");
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
        AddAudit(actorUserId, "finance.accrual_canceled", "accrual", accrual.Id, $"Отменено начисление {accrual.Amount:N2} по гаражу {accrual.Garage.Number}. Причина: {reason}");
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
        AddAudit(actorUserId, "finance.supplier_accrual_canceled", "supplier_accrual", accrual.Id, $"Отменено начисление {accrual.Amount:N2} поставщику {accrual.Supplier.Name}. Причина: {reason}");
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
        AddAudit(actorUserId, "finance.meter_reading_created", "meter_reading", reading.Id, $"Внесено показание {meterKind} по гаражу {garage.Number}: расход {consumption:N3}.");
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
        AddAudit(actorUserId, "finance.meter_reading_canceled", "meter_reading", reading.Id, $"Отменено показание {reading.MeterKind} по гаражу {reading.Garage.Number}. Причина: {reason}");
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
            TariffCalculationBases.MeterElectricity => await CalculateMeterAmountAsync(garage.Id, MeterKinds.Electricity, tariff.Rate, month, cancellationToken),
            _ => AmountCalculationResult.Failure($"неподдерживаемая база расчета {tariff.CalculationBase}.")
        };
    }

    private static string BuildRegularAccrualComment(Tariff tariff, string? comment)
    {
        var tariffRate = tariff.Rate.ToString("0.####", RussianCulture);
        var snapshot = $"тариф {tariff.Name}: ставка {tariffRate}, действует с {tariff.EffectiveFrom:dd.MM.yyyy}";
        var userComment = NormalizeOptional(comment);
        return userComment is null
            ? $"Автоначисление; {snapshot}."
            : $"{userComment}; {snapshot}.";
    }

    private static string FormatAccrualCreatedAuditSummary(Accrual accrual)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
        var comment = NormalizeOptional(accrual.Comment);
        var summary = $"Создано начисление {amount} по гаражу {accrual.Garage.Number} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.IncomeType.Name}; источник {accrual.Source}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatSupplierAccrualCreatedAuditSummary(SupplierAccrual accrual)
    {
        var amount = accrual.Amount.ToString("0.00", RussianCulture);
        var document = NormalizeOptional(accrual.DocumentNumber) ?? "без документа";
        var comment = NormalizeOptional(accrual.Comment);
        var summary = $"Создано начисление {amount} поставщику {accrual.Supplier.Name} за {accrual.AccountingMonth:MM.yyyy}; вид {accrual.ExpenseType.Name}; источник {accrual.Source}; документ {document}.";
        return comment is null ? summary : $"{summary} Комментарий: {comment}";
    }

    private static string FormatRegularAccrualGenerationAuditSummary(DateOnly month, IncomeType incomeType, Tariff tariff, IReadOnlyCollection<AccrualDto> created, IReadOnlyCollection<string> skipped)
    {
        var totalAmount = created.Sum(item => item.Amount).ToString("0.00", RussianCulture);
        var tariffRate = tariff.Rate.ToString("0.####", RussianCulture);
        return $"Создано регулярных начислений: {created.Count} на сумму {totalAmount} за {month:MM.yyyy}; вид {incomeType.Name}; тариф {tariff.Name}, база {tariff.CalculationBase}, ставка {tariffRate}; пропущено {skipped.Count}.";
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
        var normalized = NormalizeOptional(documentNumber);
        return normalized is not null && await dbContext.FinancialOperations.AnyAsync(
            operation =>
                !operation.IsCanceled &&
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
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Summary = summary
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
        if (operation.OperationKind == FinancialOperationKinds.Income && operation.GarageId is not null)
        {
            garageDebtBefore = await CalculateGarageDebtBeforeIncomeAsync(operation, cancellationToken);
            garageDebtAfter = garageDebtBefore - operation.Amount;
        }
        else if (operation.OperationKind == FinancialOperationKinds.Expense && operation.SupplierId is not null)
        {
            supplierDebtBefore = await CalculateSupplierDebtBeforeExpenseAsync(operation, cancellationToken);
            supplierDebtAfter = supplierDebtBefore - operation.Amount;
        }

        return ToDto(operation, garageDebtBefore, garageDebtAfter, supplierDebtBefore, supplierDebtAfter);
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

    private static FinancialOperationDto ToDto(
        FinancialOperation operation,
        decimal? garageDebtBefore = null,
        decimal? garageDebtAfter = null,
        decimal? supplierDebtBefore = null,
        decimal? supplierDebtAfter = null)
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
            operation.IsCanceled);
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
