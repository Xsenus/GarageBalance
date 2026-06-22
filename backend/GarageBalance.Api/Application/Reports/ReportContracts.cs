namespace GarageBalance.Api.Application.Reports;

public sealed record ConsolidatedReportRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? Search);

public sealed record IncomeReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    IReadOnlyCollection<Guid> GarageIds,
    IReadOnlyCollection<Guid> OwnerIds,
    IReadOnlyCollection<Guid> IncomeTypeIds,
    string? RowMode);

public sealed record ConsolidatedReportDto(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    decimal Balance,
    decimal Debt,
    int OperationCount,
    int AccrualCount,
    int MeterReadingCount,
    IReadOnlyList<MonthlyReportRowDto> MonthlyRows,
    IReadOnlyList<GarageReportRowDto> GarageRows);

public sealed record MonthlyReportRowDto(
    DateOnly AccountingMonth,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    decimal Balance,
    decimal Debt,
    int OperationCount,
    int AccrualCount,
    int MeterReadingCount);

public sealed record GarageReportRowDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    decimal IncomeTotal,
    decimal AccrualTotal,
    decimal Debt,
    int MeterReadingCount);

public sealed record IncomeReportDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    decimal AccrualTotal,
    decimal IncomeTotal,
    decimal Debt,
    int RowCount,
    IReadOnlyList<IncomeReportRowDto> Rows);

public sealed record IncomeReportRowDto(
    string RowType,
    DateOnly Date,
    DateOnly AccountingMonth,
    Guid GarageId,
    string GarageNumber,
    Guid? OwnerId,
    string? OwnerName,
    Guid IncomeTypeId,
    string IncomeTypeName,
    decimal AccrualAmount,
    decimal IncomeAmount,
    decimal Debt,
    string? DocumentNumber,
    string? Comment);
