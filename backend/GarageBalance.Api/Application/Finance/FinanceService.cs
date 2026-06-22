using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Finance;

public sealed class FinanceService(GarageBalanceDbContext dbContext) : IFinanceService
{
    private const int ListLimit = 100;

    public async Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        return await ApplyFilters(QueryOperations(), request)
            .OrderByDescending(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .Take(ListLimit)
            .Select(operation => ToDto(operation))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken)
    {
        return await ApplyAccrualFilters(QueryAccruals(), request)
            .OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .Take(ListLimit)
            .Select(accrual => ToDto(accrual))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken)
    {
        return await ApplyMeterReadingFilters(QueryMeterReadings(), request)
            .OrderByDescending(reading => reading.AccountingMonth)
            .ThenBy(reading => reading.Garage.Number)
            .ThenBy(reading => reading.MeterKind)
            .Take(ListLimit)
            .Select(reading => ToDto(reading))
            .ToListAsync(cancellationToken);
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
            AccountingMonth = NormalizeMonth(request.AccountingMonth),
            Amount = request.Amount,
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
        return FinanceResult<FinancialOperationDto>.Success(ToDto(operation));
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
            AccountingMonth = NormalizeMonth(request.AccountingMonth),
            Amount = request.Amount,
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
        return FinanceResult<FinancialOperationDto>.Success(ToDto(operation));
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

        var month = NormalizeMonth(request.AccountingMonth);
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
            Amount = request.Amount,
            Source = source,
            Comment = NormalizeOptional(request.Comment)
        };

        dbContext.Accruals.Add(accrual);
        AddAudit(actorUserId, "finance.accrual_created", "accrual", accrual.Id, $"Создано начисление {accrual.Amount:N2} по гаражу {garage.Number} за {accrual.AccountingMonth:MM.yyyy}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return FinanceResult<AccrualDto>.Success(ToDto(accrual));
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

        var month = NormalizeMonth(request.AccountingMonth);
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
        var previousValue = previousReading?.CurrentValue ?? GetInitialMeterValue(garage, meterKind) ?? 0m;
        var consumption = request.CurrentValue - previousValue;
        if (consumption < 0)
        {
            return FinanceResult<MeterReadingDto>.Failure("meter_reading_decreased", "Новое показание не может быть меньше предыдущего.");
        }

        var reading = new MeterReading
        {
            GarageId = garage.Id,
            Garage = garage,
            MeterKind = meterKind,
            AccountingMonth = month,
            ReadingDate = request.ReadingDate,
            CurrentValue = request.CurrentValue,
            PreviousValue = previousValue,
            Consumption = consumption,
            Comment = NormalizeOptional(request.Comment)
        };

        dbContext.MeterReadings.Add(reading);
        AddAudit(actorUserId, "finance.meter_reading_created", "meter_reading", reading.Id, $"Внесено показание {meterKind} по гаражу {garage.Number}: расход {consumption:N3}.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return FinanceResult<MeterReadingDto>.Success(ToDto(reading));
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
            query = query.Where(reading => reading.AccountingMonth >= NormalizeMonth(request.MonthFrom.Value));
        }

        if (request.MonthTo is not null)
        {
            query = query.Where(reading => reading.AccountingMonth <= NormalizeMonth(request.MonthTo.Value));
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
            query = query.Where(accrual => accrual.AccountingMonth >= NormalizeMonth(request.MonthFrom.Value));
        }

        if (request.MonthTo is not null)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= NormalizeMonth(request.MonthTo.Value));
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

    private static DateOnly NormalizeMonth(DateOnly value)
    {
        return new DateOnly(value.Year, value.Month, 1);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static decimal? GetInitialMeterValue(GarageBalance.Api.Domain.Dictionaries.Garage garage, string meterKind)
    {
        return meterKind == MeterKinds.Water ? garage.InitialWaterMeterValue : garage.InitialElectricityMeterValue;
    }

    private static bool HasGapWarning(MeterReading reading)
    {
        return reading.MeterKind == MeterKinds.Electricity && reading.PreviousValue == 0m && reading.Consumption > 0m;
    }

    private static FinancialOperationDto ToDto(FinancialOperation operation)
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
            HasGapWarning(reading),
            reading.Comment,
            reading.IsCanceled);
    }
}
