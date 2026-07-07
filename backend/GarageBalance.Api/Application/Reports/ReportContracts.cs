namespace GarageBalance.Api.Application.Reports;

public sealed record ConsolidatedReportRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? Search,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record IncomeReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    IReadOnlyCollection<Guid> GarageIds,
    IReadOnlyCollection<Guid> OwnerIds,
    IReadOnlyCollection<Guid> IncomeTypeIds,
    string? RowMode,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record ExpenseReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    IReadOnlyCollection<Guid> SupplierIds,
    IReadOnlyCollection<Guid> ExpenseTypeIds,
    string? RowMode,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record FundChangeReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record CashPaymentReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record BankDepositReportRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Search,
    int? Limit = null,
    Guid? ActorUserId = null);

public sealed record FeeReportRequest(
    string? Variation,
    int? Limit = null,
    Guid? ActorUserId = null);

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
    int GarageRowCount,
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
    string? Comment,
    DateTimeOffset? CreatedAtUtc = null,
    decimal? DebtAfterPayment = null);

public sealed record ExpenseReportDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    decimal AccrualTotal,
    decimal ExpenseTotal,
    decimal Difference,
    int RowCount,
    IReadOnlyList<ExpenseReportRowDto> Rows);

public sealed record ExpenseReportRowDto(
    string RowType,
    DateOnly Date,
    DateOnly AccountingMonth,
    Guid SupplierId,
    string SupplierName,
    Guid ExpenseTypeId,
    string ExpenseTypeName,
    decimal AccrualAmount,
    decimal ExpenseAmount,
    decimal Difference,
    string? DocumentNumber,
    string? Comment);

public sealed record FundChangeReportDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    decimal DepositTotal,
    decimal WithdrawalTotal,
    int RowCount,
    IReadOnlyList<FundChangeReportRowDto> Rows);

public sealed record FundChangeReportRowDto(
    Guid OperationId,
    Guid FundId,
    string FundName,
    DateOnly Date,
    string ChangeKind,
    string ChangeName,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Reason);

public sealed record CashPaymentReportDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    decimal Total,
    int RowCount,
    IReadOnlyList<CashPaymentReportRowDto> Rows);

public sealed record CashPaymentReportRowDto(
    Guid OperationId,
    DateOnly Date,
    decimal Amount,
    bool HasReceipt,
    string Purpose,
    string? SupplierName,
    string? ExpenseTypeName,
    string? DocumentNumber,
    string? Comment);

public sealed record BankDepositReportDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    decimal Total,
    int RowCount,
    IReadOnlyList<BankDepositReportRowDto> Rows);

public sealed record BankDepositReportRowDto(
    Guid OperationId,
    DateOnly Date,
    decimal Amount,
    string? FundName,
    string Comment);

public sealed record FeeReportDto(
    string Variation,
    decimal AccruedTotal,
    decimal CollectedTotal,
    decimal DebtTotal,
    int RowCount,
    IReadOnlyList<FeeReportSummaryRowDto> SummaryRows,
    IReadOnlyList<FeeReportDebtorRowDto> DebtorRows);

public sealed record FeeReportSummaryRowDto(
    Guid IncomeTypeId,
    string Name,
    string Goal,
    decimal FeeAmount,
    decimal Collected);

public sealed record FeeReportDebtorRowDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    Guid IncomeTypeId,
    string FeeName,
    decimal Paid,
    DateOnly? LastPaymentDate,
    decimal Debt);

public sealed record ReportExportFileDto(
    string FileName,
    string ContentType,
    byte[] Content);
