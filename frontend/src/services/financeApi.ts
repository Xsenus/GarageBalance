export type PaymentAllocationDto = {
  allocationKind: 'starting_balance' | 'month' | 'overpayment'
  accountingMonth: string | null
  label: string
  debtBefore: number
  paidAmount: number
  debtAfter: number
}

export type FinancialOperationDto = {
  id: string
  operationKind: 'income' | 'expense'
  operationDate: string
  accountingMonth: string
  amount: number
  receiptBatchId?: string | null
  expensePaymentType?: ExpensePaymentType | null
  documentNumber: string | null
  comment: string | null
  garageId: string | null
  garageNumber: string | null
  ownerName: string | null
  incomeTypeId: string | null
  incomeTypeName: string | null
  supplierId: string | null
  supplierName: string | null
  expenseTypeId: string | null
  expenseTypeName: string | null
  garageDebtBefore: number | null
  garageDebtAfter: number | null
  supplierDebtBefore: number | null
  supplierDebtAfter: number | null
  paymentAllocations: PaymentAllocationDto[]
  isCanceled: boolean
  staffMemberId: string | null
  staffMemberName: string | null
  staffDepartmentName: string | null
  createdAtUtc: string
}

export type FinanceSummaryDto = {
  incomeTotal: number
  expenseTotal: number
  accrualTotal: number
  balance: number
  debt: number
  operationCount: number
  accrualCount: number
  meterReadingCount: number
  incomeCount?: number
  expenseCount?: number
  supplierAccrualCount?: number
}

export type ExpensePaymentType = 'with_receipt' | 'without_receipt'

export class FinanceApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(
    code: string,
    message: string,
    status: number,
  ) {
    super(message)
    this.name = 'FinanceApiError'
    this.code = code
    this.status = status
  }
}

export type FinancePagedResult<TItem> = {
  items: TItem[]
  totalCount: number
  offset: number
  limit: number
}

export type FinancePageParams = {
  monthFrom?: string
  monthTo?: string
  search?: string
  garageId?: string
  supplierId?: string
  staffMemberId?: string
  offset?: number
  limit?: number
}

export type AccrualDto = {
  id: string
  garageId: string
  garageNumber: string
  ownerName: string | null
  incomeTypeId: string
  incomeTypeName: string
  accountingMonth: string
  accountingYear: number | null
  amount: number
  source: 'manual' | 'regular' | 'debt_transfer' | 'fee_campaign'
  comment: string | null
  isCanceled: boolean
  dueDate: string
  overdueFromDate: string
  irregularPaymentId: string | null
  irregularPaymentName: string | null
  feeCampaignId: string | null
  feeCampaignName: string | null
}

export type AccrualDueDateReviewDto = {
  accrualId: string
  garageNumber: string
  incomeTypeName: string
  accountingMonth: string
  amount: number
  source: string
  temporaryDueDate: string
  temporaryOverdueFromDate: string
  reasonCode: string
}

export type SupplierAccrualDto = {
  id: string
  supplierId: string
  supplierName: string
  expenseTypeId: string
  expenseTypeName: string
  accountingMonth: string
  amount: number
  source: 'manual' | 'regular'
  documentNumber: string | null
  comment: string | null
  isCanceled: boolean
}

export type MeterReadingDto = {
  id: string
  garageId: string
  garageNumber: string
  ownerName: string | null
  meterKind: 'water' | 'electricity'
  accountingMonth: string
  readingDate: string
  currentValue: number
  previousValue: number
  consumption: number
  hasGapWarning: boolean
  comment: string | null
  isCanceled: boolean
  version: string
}

export type MeterReadingYearGarageDto = {
  id: string
  number: string
}

export type MeterReadingYearValueDto = {
  id: string
  garageId: string
  accountingMonth: string
  currentValue: number
  version: string
}

export type SupplierOpeningBalanceDto = {
  supplierId: string
  monthFrom: string
  startingBalance: number
  priorAccrualTotal: number
  priorPaymentTotal: number
  openingBalance: number
}

export type FinancialReportPeriodDto = {
  monthFrom: string
  monthTo: string
}

export type MeterReadingYearPageDto = {
  garages: MeterReadingYearGarageDto[]
  readings: MeterReadingYearValueDto[]
  totalCount: number
  offset: number
  limit: number
}

export type MissingMeterReadingDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  meterKind: 'water' | 'electricity'
  accountingMonth: string
}

export type GarageBalanceHistoryRowDto = {
  accountingMonth: string
  openingDebt: number
  accrualAmount: number
  incomeAmount: number
  closingDebt: number
}

export type GarageBalanceHistoryDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  monthFrom: string
  monthTo: string
  startingBalance: number
  accrualTotal: number
  incomeTotal: number
  debt: number
  rows: GarageBalanceHistoryRowDto[]
}

export type GarageOverdueDebtRowDto = {
  rowKind: 'opening_balance' | 'accrual'
  incomeTypeId: string | null
  incomeTypeName: string
  accountingMonth: string | null
  dueDate: string | null
  overdueFromDate: string | null
  originalAmount: number
  paidAmount: number
  outstandingAmount: number
}

export type GarageOverdueDebtDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  asOfDate: string
  total: number
  rows: GarageOverdueDebtRowDto[]
}

export type GarageIncomeWorksheetRowDto = {
  accountingMonth: string
  incomeTypeId: string | null
  incomeTypeName: string
  annualAccrualId?: string | null
  meterKind: 'water' | 'electricity' | null
  meterReadingId?: string | null
  meterReadingVersion?: string | null
  meterReadingDate?: string | null
  meterValue: number | null
  meterConsumption: number | null
  accrualAmount: number
  payableAmount?: number
  incomeAmount: number
  advanceAmount?: number
  debt: number
}

export type GarageIncomeWorksheetDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  monthFrom: string
  monthTo: string
  openingDebt: number
  unrepresentedOpeningDebt?: number
  accrualTotal: number
  incomeTotal: number
  advanceTotal?: number
  debtTotal: number
  closingDebt: number
  rows: GarageIncomeWorksheetRowDto[]
}

export type ExpenseWorksheetRowDto = {
  rowKind: 'supplier' | 'staff' | string
  supplierId: string | null
  staffMemberId: string | null
  counterpartyName: string | null
  expenseTypeId: string | null
  expenseTypeName: string
  openingBalance: number
  openingDebt?: number
  openingAdvance?: number
  closingDebt?: number
  closingAdvance?: number
  accrualAmount: number
  baseAccrualAmount?: number
  bonusAmount?: number
  penaltyAmount?: number
  expenseAmount: number
  balance: number
  collectedAmount: number | null
  difference: number | null
}

export type ExpenseWorksheetDto = {
  accountingMonth: string
  openingBalanceTotal: number
  openingDebtTotal?: number
  openingAdvanceTotal?: number
  closingDebtTotal?: number
  closingAdvanceTotal?: number
  accrualTotal: number
  expenseTotal: number
  balanceTotal: number
  collectedTotal: number
  differenceTotal: number
  bankAmount: number
  cashAmount: number
  rows: ExpenseWorksheetRowDto[]
}

export type CreateIncomeOperationRequest = {
  garageId: string
  incomeTypeId: string
  operationDate: string
  accountingMonth: string
  amount: number
  receiptBatchId?: string
  documentNumber?: string
  comment?: string
}

export type IncomePaymentWarningRequest = {
  garageId: string
  incomeTypeId: string
  operationDate: string
  excludedOperationId?: string
}

export type IncomePaymentWarningDto = {
  isElectricityPayment: boolean
  previousPaymentDate: string | null
  daysSincePreviousPayment: number | null
  requiresConfirmation: boolean
}

export type CreateGarageDebtPaymentRequest = {
  garageId: string
  operationDate: string
  accountingMonth: string
  amount: number
  comment?: string
  receiptBatchId?: string
}

export type CreateExpenseOperationRequest = {
  supplierId: string
  expenseTypeId: string
  expensePaymentType: ExpensePaymentType
  operationDate: string
  accountingMonth: string
  amount: number
  documentNumber?: string
  comment?: string
}

export type CreateStaffPaymentRequest = {
  staffMemberId: string
  operationDate: string
  accountingMonth: string
  amount: number
  documentNumber?: string
  comment?: string
}

export type CancelFinanceEntryRequest = {
  reason: string
}

export type CreateAccrualRequest = {
  garageId: string
  incomeTypeId: string
  accountingMonth: string
  amount: number
  source: 'manual' | 'regular'
  comment?: string
}

export type CreateDebtTransferRequest = {
  garageId: string
  sourceMonth: string
  targetMonth: string
  amount: number
  comment?: string
}

export type CreateSupplierAccrualRequest = {
  supplierId: string
  expenseTypeId: string
  accountingMonth: string
  amount: number
  source: 'manual' | 'regular'
  documentNumber?: string
  comment?: string
}

export type GenerateRegularAccrualsRequest = {
  incomeTypeId: string
  tariffId: string
  accountingMonth: string
  comment?: string
}

export type GenerateRegularCatalogAccrualsRequest = {
  accountingMonth: string
  comment?: string
}

export type GenerateSupplierGroupSalaryAccrualsRequest = {
  supplierGroupId: string
  accountingMonth: string
  amount: number
  documentNumber?: string
  comment?: string
}

export type GenerateFeeCampaignAccrualsRequest = {
  feeCampaignId: string
  accountingMonth: string
  comment?: string
}

export type RegularAccrualGenerationResultDto = {
  accountingMonth: string
  incomeTypeId: string
  incomeTypeName: string
  tariffId: string
  tariffName: string
  calculationBase: string
  createdCount: number
  skippedCount: number
  totalAmount: number
  createdAccruals: AccrualDto[]
  skippedGarages: string[]
}

export type RegularCatalogAccrualGenerationResultDto = {
  accountingMonth: string
  serviceCount: number
  createdCount: number
  skippedCount: number
  totalAmount: number
  serviceResults: RegularAccrualGenerationResultDto[]
  skippedServices: string[]
}

export type SupplierGroupSalaryAccrualGenerationResultDto = {
  accountingMonth: string
  supplierGroupId: string
  supplierGroupName: string
  expenseTypeId: string
  expenseTypeName: string
  createdCount: number
  skippedCount: number
  totalAmount: number
  createdAccruals: SupplierAccrualDto[]
  skippedSuppliers: string[]
}

export type FeeCampaignAccrualGenerationResultDto = {
  accountingMonth: string
  feeCampaignId: string
  feeCampaignName: string
  incomeTypeId: string
  incomeTypeName: string
  contributionAmount: number
  createdCount: number
  skippedCount: number
  totalAmount: number
  createdAccruals: AccrualDto[]
  skippedGarages: string[]
}

export type CreateMeterReadingRequest = {
  garageId: string
  meterKind: 'water' | 'electricity'
  accountingMonth: string
  readingDate: string
  currentValue: number
  comment?: string
  expectedVersion?: string
}

export type CreateIrregularAccrualRequest = {
  garageId: string
  irregularPaymentId: string
  accountingMonth: string
  comment?: string
}

export type StaffSalaryAdjustmentType = 'bonus' | 'penalty'

export type CreateStaffSalaryAdjustmentRequest = {
  staffMemberId: string
  accountingMonth: string
  adjustmentType: StaffSalaryAdjustmentType
  amount: number
  documentNumber?: string
  reason: string
}

export type StaffSalaryAdjustmentDto = {
  id: string
  staffMemberId: string
  staffMemberName: string
  accountingMonth: string
  adjustmentType: StaffSalaryAdjustmentType
  amount: number
  documentNumber: string | null
  reason: string
}

export type SavePaymentFormMeterReadingRequest = CreateMeterReadingRequest & {
  meterReadingId?: string
}

export type CorrectHistoricalMeterReadingRequest = {
  readingDate: string
  currentValue: number
  comment?: string
  reason: string
  expectedVersion: string
}

export type FinanceClient = {
  getOperations(accessToken: string, limit?: number): Promise<FinancialOperationDto[]>
  getOperationsPage(accessToken: string, params?: FinancePageParams & { operationKind?: 'income' | 'expense' }): Promise<FinancePagedResult<FinancialOperationDto>>
  getAccruals(accessToken: string, limit?: number): Promise<AccrualDto[]>
  getAccrualsPage(accessToken: string, params?: FinancePageParams): Promise<FinancePagedResult<AccrualDto>>
  getAccrualDueDateReviewPage?(accessToken: string, params?: Pick<FinancePageParams, 'offset' | 'limit'>): Promise<FinancePagedResult<AccrualDueDateReviewDto>>
  getSupplierAccruals(accessToken: string, limit?: number): Promise<SupplierAccrualDto[]>
  getSupplierAccrualsPage(accessToken: string, params?: FinancePageParams): Promise<FinancePagedResult<SupplierAccrualDto>>
  getSupplierOpeningBalance(accessToken: string, supplierId: string, monthFrom: string): Promise<SupplierOpeningBalanceDto>
  getFinancialReportPeriod(accessToken: string, params: { garageId?: string; supplierId?: string; staffMemberId?: string }): Promise<FinancialReportPeriodDto>
  getMeterReadings(accessToken: string, limit?: number): Promise<MeterReadingDto[]>
  getMeterReadingsPage(accessToken: string, params?: FinancePageParams & { meterKind?: 'water' | 'electricity' }): Promise<FinancePagedResult<MeterReadingDto>>
  getMeterReadingYearPage(accessToken: string, params: { year: number; meterKind: 'water' | 'electricity'; offset?: number; limit?: number }): Promise<MeterReadingYearPageDto>
  getMissingMeterReadings(accessToken: string, params?: { accountingMonth?: string; meterKind?: 'water' | 'electricity'; search?: string; limit?: number }): Promise<MissingMeterReadingDto[]>
  getGarageBalanceHistory(accessToken: string, garageId: string, params?: { monthFrom?: string; monthTo?: string }): Promise<GarageBalanceHistoryDto>
  getGarageOverdueDebt(accessToken: string, garageId: string): Promise<GarageOverdueDebtDto>
  getGarageIncomeWorksheet(accessToken: string, garageId: string, params?: { monthFrom?: string; monthTo?: string }): Promise<GarageIncomeWorksheetDto>
  getExpenseWorksheet(accessToken: string, params?: { accountingMonth?: string }): Promise<ExpenseWorksheetDto>
  getSummary(accessToken: string, params?: FinancePageParams): Promise<FinanceSummaryDto>
  getIncomePaymentWarning(accessToken: string, request: IncomePaymentWarningRequest): Promise<IncomePaymentWarningDto>
  createIncome(accessToken: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createGarageDebtPayment(accessToken: string, request: CreateGarageDebtPaymentRequest): Promise<FinancialOperationDto>
  updateIncome(accessToken: string, operationId: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createExpense(accessToken: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  createStaffPayment(accessToken: string, request: CreateStaffPaymentRequest): Promise<FinancialOperationDto>
  createStaffSalaryAdjustment(accessToken: string, request: CreateStaffSalaryAdjustmentRequest): Promise<StaffSalaryAdjustmentDto>
  updateExpense(accessToken: string, operationId: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFinanceEntryRequest): Promise<FinancialOperationDto>
  restoreOperation(accessToken: string, operationId: string): Promise<FinancialOperationDto>
  createAccrual(accessToken: string, request: CreateAccrualRequest): Promise<AccrualDto>
  createIrregularAccrual(accessToken: string, request: CreateIrregularAccrualRequest): Promise<AccrualDto>
  createDebtTransfer(accessToken: string, request: CreateDebtTransferRequest): Promise<AccrualDto>
  updateAccrual(accessToken: string, accrualId: string, request: CreateAccrualRequest): Promise<AccrualDto>
  cancelAccrual(accessToken: string, accrualId: string, request: CancelFinanceEntryRequest): Promise<AccrualDto>
  restoreAccrual(accessToken: string, accrualId: string): Promise<AccrualDto>
  createSupplierAccrual(accessToken: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  updateSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  cancelSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CancelFinanceEntryRequest): Promise<SupplierAccrualDto>
  restoreSupplierAccrual(accessToken: string, supplierAccrualId: string): Promise<SupplierAccrualDto>
  generateRegularAccruals(accessToken: string, request: GenerateRegularAccrualsRequest): Promise<RegularAccrualGenerationResultDto>
  generateRegularCatalogAccruals(accessToken: string, request: GenerateRegularCatalogAccrualsRequest): Promise<RegularCatalogAccrualGenerationResultDto>
  generateSupplierGroupSalaryAccruals(accessToken: string, request: GenerateSupplierGroupSalaryAccrualsRequest): Promise<SupplierGroupSalaryAccrualGenerationResultDto>
  generateFeeCampaignAccruals(accessToken: string, request: GenerateFeeCampaignAccrualsRequest): Promise<FeeCampaignAccrualGenerationResultDto>
  createMeterReading(accessToken: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
  savePaymentFormMeterReading?(accessToken: string, request: SavePaymentFormMeterReadingRequest): Promise<MeterReadingDto>
  updateMeterReading(accessToken: string, meterReadingId: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
  correctHistoricalMeterReading?(accessToken: string, meterReadingId: string, request: CorrectHistoricalMeterReadingRequest): Promise<MeterReadingDto>
  cancelMeterReading(accessToken: string, meterReadingId: string, request: CancelFinanceEntryRequest): Promise<MeterReadingDto>
  restoreMeterReading(accessToken: string, meterReadingId: string): Promise<MeterReadingDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''
const defaultFinanceListLimit = 50

function withLimit(path: string, limit = defaultFinanceListLimit): string {
  return `${path}?limit=${encodeURIComponent(limit)}`
}

function withQuery(path: string, params: Record<string, string | number | undefined>): string {
  const searchParams = new URLSearchParams()
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== '') {
      searchParams.set(key, String(value))
    }
  })
  const query = searchParams.toString()
  return query ? `${path}?${query}` : path
}

function toMonthStart(value?: string): string | undefined {
  return value ? `${value.slice(0, 7)}-01` : undefined
}

function toMonthEnd(value?: string): string | undefined {
  if (!value) {
    return undefined
  }
  const [year, month] = value.slice(0, 7).split('-').map(Number)
  const day = new Date(year, month, 0).getDate()
  return `${value.slice(0, 7)}-${String(day).padStart(2, '0')}`
}

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new FinanceApiError(
      problem?.code ?? 'finance_request_failed',
      problem?.detail ?? 'Не удалось выполнить финансовую операцию.',
      response.status,
    )
  }

  return response.json()
}

export const financeApi: FinanceClient = {
  getOperations(accessToken, limit) {
    return requestJson(accessToken, withLimit('/api/finance/operations', limit))
  },
  getOperationsPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/operations/page', {
      dateFrom: toMonthStart(params.monthFrom),
      dateTo: toMonthEnd(params.monthTo),
      operationKind: params.operationKind,
      search: params.search,
      garageId: params.garageId,
      supplierId: params.supplierId,
      staffMemberId: params.staffMemberId,
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getAccruals(accessToken, limit) {
    return requestJson(accessToken, withLimit('/api/finance/accruals', limit))
  },
  getAccrualsPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/accruals/page', {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
      search: params.search,
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getAccrualDueDateReviewPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/accruals/due-date-review', {
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getSupplierAccruals(accessToken, limit) {
    return requestJson(accessToken, withLimit('/api/finance/supplier-accruals', limit))
  },
  getSupplierAccrualsPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/supplier-accruals/page', {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
      search: params.search,
      supplierId: params.supplierId,
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getMeterReadings(accessToken, limit) {
    return requestJson(accessToken, withLimit('/api/finance/meter-readings', limit))
  },
  getMeterReadingsPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/meter-readings/page', {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
      meterKind: params.meterKind,
      search: params.search,
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getMeterReadingYearPage(accessToken, params) {
    return requestJson(accessToken, withQuery('/api/finance/meter-readings/year', {
      year: params.year,
      meterKind: params.meterKind,
      offset: params.offset,
      limit: params.limit,
    }))
  },
  getMissingMeterReadings(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/meter-readings/missing', {
      accountingMonth: toMonthStart(params.accountingMonth),
      meterKind: params.meterKind,
      search: params.search,
      limit: params.limit,
    }))
  },
  getGarageBalanceHistory(accessToken, garageId, params = {}) {
    return requestJson(accessToken, withQuery(`/api/finance/garages/${garageId}/balance-history`, {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
    }))
  },
  getGarageOverdueDebt(accessToken, garageId) {
    return requestJson(accessToken, `/api/finance/garages/${garageId}/overdue-debt`)
  },
  getGarageIncomeWorksheet(accessToken, garageId, params = {}) {
    return requestJson(accessToken, withQuery(`/api/finance/garages/${garageId}/income-worksheet`, {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
    }))
  },
  getExpenseWorksheet(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/expenses-worksheet', {
      accountingMonth: toMonthStart(params.accountingMonth),
    }))
  },
  getSummary(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/summary', {
      dateFrom: toMonthStart(params.monthFrom),
      dateTo: toMonthEnd(params.monthTo),
      search: params.search,
    }))
  },
  getSupplierOpeningBalance(accessToken, supplierId, monthFrom) {
    return requestJson(accessToken, withQuery(`/api/finance/suppliers/${supplierId}/opening-balance`, {
      monthFrom: toMonthStart(monthFrom),
    }))
  },
  getFinancialReportPeriod(accessToken, params) {
    return requestJson(accessToken, withQuery('/api/finance/financial-report-period', params))
  },
  getIncomePaymentWarning(accessToken, request) {
    return requestJson(accessToken, '/api/finance/income/payment-warning', { method: 'POST', body: JSON.stringify(request) })
  },
  createIncome(accessToken, request) {
    return requestJson(accessToken, '/api/finance/income', { method: 'POST', body: JSON.stringify(request) })
  },
  createGarageDebtPayment(accessToken, request) {
    return requestJson(accessToken, '/api/finance/income/debt-payment', { method: 'POST', body: JSON.stringify(request) })
  },
  updateIncome(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/income`, { method: 'PUT', body: JSON.stringify(request) })
  },
  createExpense(accessToken, request) {
    return requestJson(accessToken, '/api/finance/expense', { method: 'POST', body: JSON.stringify(request) })
  },
  createStaffPayment(accessToken, request) {
    return requestJson(accessToken, '/api/finance/staff-payments', { method: 'POST', body: JSON.stringify(request) })
  },
  createStaffSalaryAdjustment(accessToken, request) {
    return requestJson(accessToken, '/api/finance/staff-salary-adjustments', { method: 'POST', body: JSON.stringify(request) })
  },
  updateExpense(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/expense`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  restoreOperation(accessToken, operationId) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/restore`, { method: 'POST' })
  },
  createAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals', { method: 'POST', body: JSON.stringify(request) })
  },
  createIrregularAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/irregular', { method: 'POST', body: JSON.stringify(request) })
  },
  createDebtTransfer(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/debt-transfer', { method: 'POST', body: JSON.stringify(request) })
  },
  updateAccrual(accessToken, accrualId, request) {
    return requestJson(accessToken, `/api/finance/accruals/${accrualId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelAccrual(accessToken, accrualId, request) {
    return requestJson(accessToken, `/api/finance/accruals/${accrualId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  restoreAccrual(accessToken, accrualId) {
    return requestJson(accessToken, `/api/finance/accruals/${accrualId}/restore`, { method: 'POST' })
  },
  createSupplierAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/supplier-accruals', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplierAccrual(accessToken, supplierAccrualId, request) {
    return requestJson(accessToken, `/api/finance/supplier-accruals/${supplierAccrualId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelSupplierAccrual(accessToken, supplierAccrualId, request) {
    return requestJson(accessToken, `/api/finance/supplier-accruals/${supplierAccrualId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  restoreSupplierAccrual(accessToken, supplierAccrualId) {
    return requestJson(accessToken, `/api/finance/supplier-accruals/${supplierAccrualId}/restore`, { method: 'POST' })
  },
  generateRegularAccruals(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/generate-regular', { method: 'POST', body: JSON.stringify(request) })
  },
  generateRegularCatalogAccruals(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/generate-regular-catalog', { method: 'POST', body: JSON.stringify(request) })
  },
  generateSupplierGroupSalaryAccruals(accessToken, request) {
    return requestJson(accessToken, '/api/finance/supplier-accruals/generate-salary', { method: 'POST', body: JSON.stringify(request) })
  },
  generateFeeCampaignAccruals(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/generate-fee-campaign', { method: 'POST', body: JSON.stringify(request) })
  },
  createMeterReading(accessToken, request) {
    return requestJson(accessToken, '/api/finance/meter-readings', { method: 'POST', body: JSON.stringify(request) })
  },
  savePaymentFormMeterReading(accessToken, request) {
    return requestJson(accessToken, '/api/finance/payment-form/meter-reading', { method: 'PUT', body: JSON.stringify(request) })
  },
  updateMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  correctHistoricalMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}/historical-correction`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  restoreMeterReading(accessToken, meterReadingId) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}/restore`, { method: 'POST' })
  },
}
