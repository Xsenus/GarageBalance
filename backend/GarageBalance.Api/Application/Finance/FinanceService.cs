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

    public async Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken)
    {
        var operations = ApplyFilters(dbContext.FinancialOperations.AsNoTracking().Where(operation => !operation.IsCanceled), request);
        var incomeTotal = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var expenseTotal = await operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .SumAsync(operation => operation.Amount, cancellationToken);
        var operationCount = await operations.CountAsync(cancellationToken);

        return new FinanceSummaryDto(incomeTotal, expenseTotal, incomeTotal - expenseTotal, operationCount);
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
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = "financial_operation",
            EntityId = operationId.ToString(),
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
}
