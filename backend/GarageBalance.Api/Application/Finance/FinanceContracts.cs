using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Finance;

public sealed record FinancialOperationDto(
    Guid Id,
    string OperationKind,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    decimal Amount,
    string? DocumentNumber,
    string? Comment,
    Guid? GarageId,
    string? GarageNumber,
    string? OwnerName,
    Guid? IncomeTypeId,
    string? IncomeTypeName,
    Guid? SupplierId,
    string? SupplierName,
    Guid? ExpenseTypeId,
    string? ExpenseTypeName,
    bool IsCanceled);

public sealed record CreateIncomeOperationRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [property: Range(0.01, 999999999)] decimal Amount,
    [property: MaxLength(120)] string? DocumentNumber,
    [property: MaxLength(1000)] string? Comment);

public sealed record CreateExpenseOperationRequest(
    Guid SupplierId,
    Guid ExpenseTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [property: Range(0.01, 999999999)] decimal Amount,
    [property: MaxLength(120)] string? DocumentNumber,
    [property: MaxLength(1000)] string? Comment);

public sealed record FinancialOperationListRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? OperationKind,
    string? Search);

public sealed record FinanceSummaryDto(
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal Balance,
    int OperationCount);
