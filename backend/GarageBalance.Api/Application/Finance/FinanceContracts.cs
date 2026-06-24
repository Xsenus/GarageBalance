using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Finance;

public sealed record FinancePagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);

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
    decimal? GarageDebtBefore,
    decimal? GarageDebtAfter,
    decimal? SupplierDebtBefore,
    decimal? SupplierDebtAfter,
    bool IsCanceled);

public sealed record CreateIncomeOperationRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment);

public sealed record CreateExpenseOperationRequest(
    Guid SupplierId,
    Guid ExpenseTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment);

public sealed record CancelFinanceEntryRequest(
    [Required, MaxLength(1000)] string Reason);

public sealed record FinancialOperationListRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? OperationKind,
    string? Search,
    int? Limit = null,
    int? Offset = null);

public sealed record AccrualDto(
    Guid Id,
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    Guid IncomeTypeId,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    decimal Amount,
    string Source,
    string? Comment,
    bool IsCanceled);

public sealed record SupplierAccrualDto(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    Guid ExpenseTypeId,
    string ExpenseTypeName,
    DateOnly AccountingMonth,
    decimal Amount,
    string Source,
    string? DocumentNumber,
    string? Comment,
    bool IsCanceled);

public sealed record CreateAccrualRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(40)] string Source,
    [MaxLength(1000)] string? Comment);

public sealed record CreateSupplierAccrualRequest(
    Guid SupplierId,
    Guid ExpenseTypeId,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(40)] string Source,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment);

public sealed record GenerateRegularAccrualsRequest(
    Guid IncomeTypeId,
    Guid TariffId,
    DateOnly AccountingMonth,
    [MaxLength(1000)] string? Comment);

public sealed record RegularAccrualGenerationResultDto(
    DateOnly AccountingMonth,
    Guid IncomeTypeId,
    string IncomeTypeName,
    Guid TariffId,
    string TariffName,
    string CalculationBase,
    int CreatedCount,
    int SkippedCount,
    decimal TotalAmount,
    IReadOnlyList<AccrualDto> CreatedAccruals,
    IReadOnlyList<string> SkippedGarages);

public sealed record AccrualListRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? Search,
    int? Limit = null,
    int? Offset = null);

public sealed record SupplierAccrualListRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? Search,
    int? Limit = null,
    int? Offset = null);

public sealed record MeterReadingDto(
    Guid Id,
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    string MeterKind,
    DateOnly AccountingMonth,
    DateOnly ReadingDate,
    decimal CurrentValue,
    decimal PreviousValue,
    decimal Consumption,
    bool HasGapWarning,
    string? Comment,
    bool IsCanceled);

public sealed record CreateMeterReadingRequest(
    Guid GarageId,
    [Required, MaxLength(40)] string MeterKind,
    DateOnly AccountingMonth,
    DateOnly ReadingDate,
    [Range(0, 999999999)] decimal CurrentValue,
    [MaxLength(1000)] string? Comment);

public sealed record MeterReadingListRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? MeterKind,
    string? Search,
    int? Limit = null,
    int? Offset = null);

public sealed record FinanceSummaryDto(
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    decimal Balance,
    decimal Debt,
    int OperationCount,
    int AccrualCount,
    int MeterReadingCount);
