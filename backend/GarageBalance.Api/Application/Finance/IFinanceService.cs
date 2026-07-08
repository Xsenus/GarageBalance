namespace GarageBalance.Api.Application.Finance;

public interface IFinanceService
{
    Task<IReadOnlyList<FinancialOperationDto>> GetOperationsAsync(FinancialOperationListRequest request, CancellationToken cancellationToken);

    Task<FinancePagedResult<FinancialOperationDto>> GetOperationsPageAsync(FinancialOperationListRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccrualDto>> GetAccrualsAsync(AccrualListRequest request, CancellationToken cancellationToken);

    Task<FinancePagedResult<AccrualDto>> GetAccrualsPageAsync(AccrualListRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierAccrualDto>> GetSupplierAccrualsAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken);

    Task<FinancePagedResult<SupplierAccrualDto>> GetSupplierAccrualsPageAsync(SupplierAccrualListRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeterReadingDto>> GetMeterReadingsAsync(MeterReadingListRequest request, CancellationToken cancellationToken);

    Task<FinancePagedResult<MeterReadingDto>> GetMeterReadingsPageAsync(MeterReadingListRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MissingMeterReadingDto>> GetMissingMeterReadingsAsync(MissingMeterReadingListRequest request, CancellationToken cancellationToken);

    Task<FinanceResult<GarageBalanceHistoryDto>> GetGarageBalanceHistoryAsync(Guid garageId, GarageBalanceHistoryRequest request, CancellationToken cancellationToken);

    Task<FinanceResult<GarageIncomeWorksheetDto>> GetGarageIncomeWorksheetAsync(Guid garageId, GarageIncomeWorksheetRequest request, CancellationToken cancellationToken);

    Task<FinanceResult<ExpenseWorksheetDto>> GetExpenseWorksheetAsync(ExpenseWorksheetRequest request, CancellationToken cancellationToken);

    Task<FinanceSummaryDto> GetSummaryAsync(FinancialOperationListRequest request, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateIncomeAsync(CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateGarageDebtPaymentAsync(CreateGarageDebtPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> UpdateIncomeAsync(Guid operationId, CreateIncomeOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateExpenseAsync(CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CreateStaffPaymentAsync(CreateStaffPaymentRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> UpdateExpenseAsync(Guid operationId, CreateExpenseOperationRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> CancelOperationAsync(Guid operationId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FinancialOperationDto>> RestoreOperationAsync(Guid operationId, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<AccrualDto>> CreateAccrualAsync(CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<AccrualDto>> CreateDebtTransferAsync(CreateDebtTransferRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<AccrualDto>> UpdateAccrualAsync(Guid accrualId, CreateAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<AccrualDto>> CancelAccrualAsync(Guid accrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<AccrualDto>> RestoreAccrualAsync(Guid accrualId, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<SupplierAccrualDto>> CreateSupplierAccrualAsync(CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<SupplierAccrualDto>> UpdateSupplierAccrualAsync(Guid supplierAccrualId, CreateSupplierAccrualRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<SupplierAccrualDto>> CancelSupplierAccrualAsync(Guid supplierAccrualId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<SupplierAccrualDto>> RestoreSupplierAccrualAsync(Guid supplierAccrualId, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<RegularAccrualGenerationResultDto>> GenerateRegularAccrualsAsync(GenerateRegularAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<RegularCatalogAccrualGenerationResultDto>> GenerateRegularCatalogAccrualsAsync(GenerateRegularCatalogAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<FeeCampaignAccrualGenerationResultDto>> GenerateFeeCampaignAccrualsAsync(GenerateFeeCampaignAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<SupplierGroupSalaryAccrualGenerationResultDto>> GenerateSupplierGroupSalaryAccrualsAsync(GenerateSupplierGroupSalaryAccrualsRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<MeterReadingDto>> CreateMeterReadingAsync(CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<MeterReadingDto>> UpdateMeterReadingAsync(Guid meterReadingId, CreateMeterReadingRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<MeterReadingDto>> CancelMeterReadingAsync(Guid meterReadingId, CancelFinanceEntryRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<FinanceResult<MeterReadingDto>> RestoreMeterReadingAsync(Guid meterReadingId, Guid? actorUserId, CancellationToken cancellationToken);
}
