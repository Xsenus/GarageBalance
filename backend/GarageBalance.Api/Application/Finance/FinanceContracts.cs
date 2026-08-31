using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed record FinancePagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);

public sealed record PaymentAllocationDto(
    string AllocationKind,
    DateOnly? AccountingMonth,
    string Label,
    decimal DebtBefore,
    decimal PaidAmount,
    decimal DebtAfter);

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
    IReadOnlyList<PaymentAllocationDto> PaymentAllocations,
    bool IsCanceled,
    DateTimeOffset CreatedAtUtc,
    Guid? StaffMemberId = null,
    string? StaffMemberName = null,
    string? StaffDepartmentName = null,
    Guid? ReceiptBatchId = null,
    string? ExpensePaymentType = null,
    string? ExpensePaymentSource = null,
    Guid? ExpenseFundId = null,
    string? ExpenseFundName = null,
    string? CounterpartyName = null,
    bool NegativeFundBalanceConfirmed = false);

public sealed record CreateIncomeOperationRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment,
    Guid? ReceiptBatchId = null,
    Guid? FeeCampaignId = null,
    Guid? IrregularPaymentId = null);

public sealed record CreateFullGaragePaymentLineRequest(
    Guid? IncomeTypeId,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(1000)] string? Comment,
    bool IsOpeningDebt = false,
    Guid? FeeCampaignId = null,
    Guid? IrregularPaymentId = null);

public sealed record CreateFullGaragePaymentRequest(
    Guid GarageId,
    DateOnly OperationDate,
    [MinLength(1), MaxLength(100)] IReadOnlyList<CreateFullGaragePaymentLineRequest> Lines,
    Guid? ReceiptBatchId = null);

public sealed record FullGaragePaymentDto(
    Guid ReceiptBatchId,
    decimal TotalAmount,
    IReadOnlyList<FinancialOperationDto> Operations);

public sealed record GarageFullPaymentQuoteLineDto(
    Guid? IncomeTypeId,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    decimal OutstandingAmount,
    bool IsOpeningDebt = false,
    Guid? FeeCampaignId = null,
    Guid? IrregularPaymentId = null);

public sealed record GarageFullPaymentQuoteDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    decimal TotalAmount,
    IReadOnlyList<GarageFullPaymentQuoteLineDto> Lines);

public sealed record IncomePaymentWarningRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly OperationDate,
    Guid? ExcludedOperationId = null);

public sealed record IncomePaymentWarningDto(
    bool IsElectricityPayment,
    DateOnly? PreviousPaymentDate,
    int? DaysSincePreviousPayment,
    bool RequiresConfirmation);

public sealed record CreateExpenseOperationRequest(
    Guid? SupplierId,
    Guid ExpenseTypeId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment,
    string ExpensePaymentType = ExpensePaymentTypes.WithReceipt,
    string? ExpensePaymentSource = null,
    Guid? ExpenseFundId = null,
    [MaxLength(200)] string? CounterpartyName = null,
    bool ConfirmNegativeFundBalance = false);

public sealed record CreateStaffPaymentRequest(
    Guid StaffMemberId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [MaxLength(1000)] string? Comment);

public sealed record CreateStaffSalaryAdjustmentRequest(
    Guid StaffMemberId,
    DateOnly AccountingMonth,
    [Required, MaxLength(20)] string AdjustmentType,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
    [ActionComment, MaxLength(1000)] string Reason);

public sealed record StaffSalaryAdjustmentDto(
    Guid Id,
    Guid StaffMemberId,
    string StaffMemberName,
    DateOnly AccountingMonth,
    string AdjustmentType,
    decimal Amount,
    string? DocumentNumber,
    string Reason);

public sealed record CreateCashBankTransferRequest(
    DateOnly TransferDate,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(1000)] string? Comment);

public sealed record CashBankTransferDto(
    Guid Id,
    DateOnly TransferDate,
    decimal Amount,
    string? Comment,
    DateTimeOffset CreatedAtUtc);

public sealed record CancelFinanceEntryRequest(
    [ActionComment, MaxLength(1000)] string Reason);

public sealed record FinancialOperationListRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? OperationKind,
    string? Search,
    int? Limit = null,
    int? Offset = null,
    Guid? GarageId = null,
    Guid? SupplierId = null,
    Guid? StaffMemberId = null);

public sealed record AccrualDto(
    Guid Id,
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    Guid IncomeTypeId,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    int? AccountingYear,
    decimal Amount,
    string Source,
    string? Comment,
    bool IsCanceled,
    DateOnly DueDate,
    DateOnly OverdueFromDate,
    Guid? IrregularPaymentId = null,
    string? IrregularPaymentName = null,
    string? Basis = null,
    Guid? FeeCampaignId = null,
    string? FeeCampaignName = null);

public sealed record AccrualDueDateReviewDto(
    Guid AccrualId,
    string GarageNumber,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    decimal Amount,
    string Source,
    DateOnly TemporaryDueDate,
    DateOnly TemporaryOverdueFromDate,
    string ReasonCode);

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
    bool IsCanceled,
    Guid? ExpenseFundId = null,
    string? ExpenseFundName = null);

public sealed record CreateAccrualRequest(
    Guid GarageId,
    Guid IncomeTypeId,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [Required, MaxLength(40)] string Source,
    [MaxLength(1000)] string? Comment);

public sealed record CreateIrregularAccrualRequest(
    Guid GarageId,
    Guid? IrregularPaymentId,
    [Required, MaxLength(200)] string Basis,
    [Range(0.01, 999999999)] decimal Amount,
    DateOnly AccountingMonth,
    [MaxLength(1000)] string? Comment);

public sealed record CreateDebtTransferRequest(
    Guid GarageId,
    DateOnly SourceMonth,
    DateOnly TargetMonth,
    [Range(0.01, 999999999)] decimal Amount,
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

public sealed record GenerateRegularCatalogAccrualsRequest(
    DateOnly AccountingMonth,
    [MaxLength(1000)] string? Comment);

public sealed record GenerateFeeCampaignAccrualsRequest(
    Guid FeeCampaignId,
    DateOnly AccountingMonth,
    [MaxLength(1000)] string? Comment);

public sealed record GenerateActiveFeeCampaignAccrualsRequest(
    DateOnly AccountingMonth,
    [MaxLength(1000)] string? Comment);

public sealed record RegularAccrualAutomationPreviewDto(
    DateOnly AccountingMonth,
    int ActiveGarageCount,
    int ActiveRegularServiceCount,
    int DueRegularServiceCount,
    int ActiveFeeCampaignCount,
    int MaximumGarageChecks,
    IReadOnlyList<string> Warnings);

public sealed record GenerateSupplierGroupSalaryAccrualsRequest(
    Guid SupplierGroupId,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(120)] string? DocumentNumber,
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

public sealed record RegularCatalogAccrualGenerationResultDto(
    DateOnly AccountingMonth,
    int ServiceCount,
    int CreatedCount,
    int SkippedCount,
    decimal TotalAmount,
    IReadOnlyList<RegularAccrualGenerationResultDto> ServiceResults,
    IReadOnlyList<string> SkippedServices);

public sealed record FeeCampaignAccrualGenerationResultDto(
    DateOnly AccountingMonth,
    Guid FeeCampaignId,
    string FeeCampaignName,
    Guid IncomeTypeId,
    string IncomeTypeName,
    decimal ContributionAmount,
    int CreatedCount,
    int SkippedCount,
    decimal TotalAmount,
    IReadOnlyList<AccrualDto> CreatedAccruals,
    IReadOnlyList<string> SkippedGarages);

public sealed record ActiveFeeCampaignAccrualGenerationResultDto(
    DateOnly AccountingMonth,
    int CampaignCount,
    int CreatedCount,
    int SkippedCount,
    decimal TotalAmount,
    IReadOnlyList<FeeCampaignAccrualGenerationResultDto> CampaignResults,
    IReadOnlyList<string> SkippedCampaigns,
    IReadOnlyList<string> FailedCampaigns);

public sealed record SupplierGroupSalaryAccrualGenerationResultDto(
    DateOnly AccountingMonth,
    Guid SupplierGroupId,
    string SupplierGroupName,
    Guid ExpenseTypeId,
    string ExpenseTypeName,
    int CreatedCount,
    int SkippedCount,
    decimal TotalAmount,
    IReadOnlyList<SupplierAccrualDto> CreatedAccruals,
    IReadOnlyList<string> SkippedSuppliers);

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
    int? Offset = null,
    Guid? SupplierId = null);

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
    bool IsCanceled,
    Guid Version,
    Guid? MeterDeviceId = null,
    string? MeterDeviceSerialNumber = null,
    decimal PreviousDeviceConsumption = 0m,
    bool IsMeterReplacement = false);

public sealed record MeterDeviceDto(
    Guid Id,
    Guid GarageId,
    string MeterKind,
    string SerialNumber,
    DateOnly InstalledOn,
    DateOnly? RemovedOn,
    decimal InitialValue,
    decimal? FinalValue,
    Guid Version);

public sealed record ReplaceMeterDeviceRequest(
    Guid GarageId,
    [Required, MaxLength(40)] string MeterKind,
    DateOnly AccountingMonth,
    DateOnly ReplacementDate,
    [Required, MaxLength(100)] string NewSerialNumber,
    [Required, Range(0, 999999999)] decimal? NewInitialValue,
    [Required, Range(0, 999999999)] decimal? CurrentValue,
    [Range(0, 999999999)] decimal? RemovedDeviceFinalValue,
    [ActionComment, MaxLength(500)] string Reason,
    Guid? MeterReadingId = null,
    Guid? ExpectedReadingVersion = null);

public sealed record MeterDeviceReplacementDto(MeterDeviceDto Device, MeterReadingDto Reading);

public sealed record MeterReadingYearGarageDto(Guid Id, string Number);

public sealed record MeterReadingYearValueDto(
    Guid Id,
    Guid GarageId,
    DateOnly AccountingMonth,
    decimal CurrentValue,
    Guid Version,
    Guid? MeterDeviceId,
    string? MeterDeviceSerialNumber,
    bool IsMeterReplacement);

public sealed record MeterReadingYearPageDto(
    IReadOnlyList<MeterReadingYearGarageDto> Garages,
    IReadOnlyList<MeterReadingYearValueDto> Readings,
    int TotalCount,
    int Offset,
    int Limit,
    DateOnly CurrentAccountingMonth = default);

public sealed record MeterReadingYearRequest(
    int Year,
    string? MeterKind,
    int? Limit = null,
    int? Offset = null);

public sealed record MissingMeterReadingDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    string MeterKind,
    DateOnly AccountingMonth);

public sealed record CreateMeterReadingRequest(
    Guid GarageId,
    [Required, MaxLength(40)] string MeterKind,
    DateOnly AccountingMonth,
    DateOnly ReadingDate,
    [Required, Range(0, 999999999)] decimal? CurrentValue,
    [MaxLength(1000)] string? Comment,
    Guid? ExpectedVersion = null,
    [MaxLength(1000)] string? PeriodOverrideReason = null);

public sealed record SavePaymentFormMeterReadingRequest(
    Guid GarageId,
    [Required, MaxLength(40)] string MeterKind,
    DateOnly AccountingMonth,
    DateOnly ReadingDate,
    [Required, Range(0, 999999999)] decimal? CurrentValue,
    [MaxLength(1000)] string? Comment,
    Guid? MeterReadingId = null,
    Guid? ExpectedVersion = null,
    [MaxLength(1000)] string? PeriodOverrideReason = null);

public sealed record CorrectHistoricalMeterReadingRequest(
    DateOnly ReadingDate,
    [Required, Range(0, 999999999)] decimal? CurrentValue,
    [MaxLength(1000)] string? Comment,
    [MaxLength(500)] string? Reason,
    Guid ExpectedVersion);

public sealed record MeterReadingListRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    string? MeterKind,
    string? Search,
    int? Limit = null,
    int? Offset = null);

public sealed record MissingMeterReadingListRequest(
    DateOnly? AccountingMonth,
    string? MeterKind,
    string? Search,
    int? Limit = null);

public sealed record GarageBalanceHistoryRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo);

public sealed record GarageBalanceHistoryRowDto(
    DateOnly AccountingMonth,
    decimal OpeningDebt,
    decimal AccrualAmount,
    decimal IncomeAmount,
    decimal ClosingDebt);

public sealed record GarageBalanceHistoryDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    DateOnly MonthFrom,
    DateOnly MonthTo,
    decimal StartingBalance,
    decimal AccrualTotal,
    decimal IncomeTotal,
    decimal Debt,
    IReadOnlyList<GarageBalanceHistoryRowDto> Rows);

public sealed record GarageOverdueDebtRowDto(
    string RowKind,
    Guid? IncomeTypeId,
    string IncomeTypeName,
    DateOnly? AccountingMonth,
    DateOnly? DueDate,
    DateOnly? OverdueFromDate,
    decimal OriginalAmount,
    decimal PaidAmount,
    decimal OutstandingAmount);

public sealed record GarageOverdueDebtDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    DateOnly AsOfDate,
    decimal Total,
    IReadOnlyList<GarageOverdueDebtRowDto> Rows);

public sealed record GarageIncomeWorksheetRequest(
    DateOnly? MonthFrom,
    DateOnly? MonthTo);

public sealed record GarageIncomeWorksheetRowDto(
    DateOnly AccountingMonth,
    Guid? IncomeTypeId,
    string IncomeTypeName,
    Guid? AnnualAccrualId,
    string? MeterKind,
    Guid? MeterReadingId,
    Guid? MeterReadingVersion,
    DateOnly? MeterReadingDate,
    decimal? MeterValue,
    decimal? MeterConsumption,
    decimal AccrualAmount,
    decimal PayableAmount,
    decimal IncomeAmount,
    decimal AdvanceAmount,
    decimal Debt,
    Guid? FeeCampaignId = null,
    decimal? FeeCampaignRemainingAmount = null,
    Guid? IrregularPaymentId = null,
    decimal? IrregularPaymentRemainingAmount = null,
    AccrualCalculationDetailsDto? CalculationDetails = null,
    string? Reason = null,
    string? IncomeTypeCode = null);

public sealed record GarageIncomeWorksheetDto(
    Guid GarageId,
    string GarageNumber,
    string? OwnerName,
    DateOnly MonthFrom,
    DateOnly MonthTo,
    decimal OpeningBalance,
    decimal OpeningDebt,
    decimal UnrepresentedOpeningDebt,
    decimal AccrualTotal,
    decimal IncomeTotal,
    decimal AdvanceTotal,
    decimal DebtTotal,
    decimal ClosingBalance,
    decimal ClosingDebt,
    IReadOnlyList<GarageIncomeWorksheetRowDto> Rows);

public sealed record CreateGarageDebtPaymentRequest(
    Guid GarageId,
    DateOnly OperationDate,
    DateOnly AccountingMonth,
    [Range(0.01, 999999999)] decimal Amount,
    [MaxLength(1000)] string? Comment,
    Guid? ReceiptBatchId = null);

public sealed record ExpenseWorksheetRequest(
    DateOnly? AccountingMonth,
    DateOnly? MonthFrom = null,
    DateOnly? MonthTo = null);

public sealed record ExpenseWorksheetSupplierBreakdownRequest(
    Guid SupplierId,
    Guid ExpenseTypeId,
    DateOnly? MonthFrom,
    DateOnly? MonthTo,
    int? Offset = null,
    int? Limit = null);

public sealed record ExpenseWorksheetSupplierBreakdownEntryDto(
    Guid Id,
    string EntryKind,
    DateOnly AccountingMonth,
    DateOnly? OperationDate,
    decimal Amount,
    string? DocumentNumber,
    string? Comment,
    string? Source);

public sealed record ExpenseWorksheetSupplierBreakdownDto(
    Guid SupplierId,
    Guid ExpenseTypeId,
    DateOnly MonthFrom,
    DateOnly MonthTo,
    decimal AccrualTotal,
    decimal ExpenseTotal,
    IReadOnlyList<ExpenseWorksheetSupplierBreakdownEntryDto> Items,
    int TotalCount,
    int Offset,
    int Limit);

public sealed record SupplierOpeningBalanceRequest(
    DateOnly? MonthFrom);

public sealed record FinancialReportPeriodRequest(
    Guid? GarageId,
    Guid? SupplierId,
    Guid? StaffMemberId);

public sealed record FinancialReportPeriodDto(
    DateOnly MonthFrom,
    DateOnly MonthTo,
    DateOnly? DefaultMonthFrom = null,
    DateOnly? DefaultMonthTo = null);

public sealed record SupplierOpeningBalanceDto(
    Guid SupplierId,
    DateOnly MonthFrom,
    decimal StartingBalance,
    decimal PriorAccrualTotal,
    decimal PriorPaymentTotal,
    decimal OpeningBalance);

public sealed record ExpenseWorksheetRowDto(
    string RowKind,
    Guid? SupplierId,
    Guid? StaffMemberId,
    string? CounterpartyName,
    Guid? ExpenseTypeId,
    string ExpenseTypeName,
    decimal AccrualAmount,
    decimal ExpenseAmount,
    decimal Balance,
    decimal? CollectedAmount,
    decimal? Difference)
{
    public decimal BaseAccrualAmount { get; init; }

    public decimal BonusAmount { get; init; }

    public decimal PenaltyAmount { get; init; }

    public decimal OpeningBalance { get; init; }

    public decimal OpeningDebt { get; init; }

    public decimal OpeningAdvance { get; init; }

    public decimal ClosingDebt { get; init; }

    public decimal ClosingAdvance { get; init; }

    public Guid? ExpenseFundId { get; init; }

    public string? ExpenseFundName { get; init; }
}

public sealed record ExpenseWorksheetDto(
    DateOnly AccountingMonth,
    decimal AccrualTotal,
    decimal ExpenseTotal,
    decimal BalanceTotal,
    decimal CollectedTotal,
    decimal DifferenceTotal,
    decimal BankAmount,
    decimal CashAmount,
    IReadOnlyList<ExpenseWorksheetRowDto> Rows)
{
    public DateOnly MonthFrom { get; init; }

    public DateOnly MonthTo { get; init; }

    public decimal OpeningBalanceTotal { get; init; }

    public decimal OpeningDebtTotal { get; init; }

    public decimal OpeningAdvanceTotal { get; init; }

    public decimal ClosingDebtTotal { get; init; }

    public decimal ClosingAdvanceTotal { get; init; }
}

public sealed record FinanceSummaryDto(
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    decimal Balance,
    decimal Debt,
    int OperationCount,
    int AccrualCount,
    int MeterReadingCount)
{
    public int IncomeCount { get; init; }

    public int ExpenseCount { get; init; }

    public int SupplierAccrualCount { get; init; }
}
