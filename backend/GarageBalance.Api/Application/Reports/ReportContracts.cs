namespace GarageBalance.Api.Application.Reports;

public sealed record ConsolidatedReportRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? Search);

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
