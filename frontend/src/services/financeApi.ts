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
  amount: number
  source: 'manual' | 'regular' | 'debt_transfer'
  comment: string | null
  isCanceled: boolean
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

export type GarageIncomeWorksheetRowDto = {
  accountingMonth: string
  incomeTypeId: string | null
  incomeTypeName: string
  meterKind: 'water' | 'electricity' | null
  meterValue: number | null
  meterConsumption: number | null
  accrualAmount: number
  incomeAmount: number
  debt: number
}

export type GarageIncomeWorksheetDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  monthFrom: string
  monthTo: string
  accrualTotal: number
  incomeTotal: number
  debtTotal: number
  rows: GarageIncomeWorksheetRowDto[]
}

export type ExpenseWorksheetRowDto = {
  rowKind: 'supplier' | 'staff' | string
  supplierId: string | null
  staffMemberId: string | null
  counterpartyName: string | null
  expenseTypeId: string | null
  expenseTypeName: string
  accrualAmount: number
  expenseAmount: number
  balance: number
  collectedAmount: number | null
  difference: number | null
}

export type ExpenseWorksheetDto = {
  accountingMonth: string
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
  documentNumber?: string
  comment?: string
}

export type CreateExpenseOperationRequest = {
  supplierId: string
  expenseTypeId: string
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

export type CreateMeterReadingRequest = {
  garageId: string
  meterKind: 'water' | 'electricity'
  accountingMonth: string
  readingDate: string
  currentValue: number
  comment?: string
}

export type FinanceClient = {
  getOperations(accessToken: string, limit?: number): Promise<FinancialOperationDto[]>
  getOperationsPage(accessToken: string, params?: FinancePageParams & { operationKind?: 'income' | 'expense' }): Promise<FinancePagedResult<FinancialOperationDto>>
  getAccruals(accessToken: string, limit?: number): Promise<AccrualDto[]>
  getAccrualsPage(accessToken: string, params?: FinancePageParams): Promise<FinancePagedResult<AccrualDto>>
  getSupplierAccruals(accessToken: string, limit?: number): Promise<SupplierAccrualDto[]>
  getSupplierAccrualsPage(accessToken: string, params?: FinancePageParams): Promise<FinancePagedResult<SupplierAccrualDto>>
  getMeterReadings(accessToken: string, limit?: number): Promise<MeterReadingDto[]>
  getMeterReadingsPage(accessToken: string, params?: FinancePageParams & { meterKind?: 'water' | 'electricity' }): Promise<FinancePagedResult<MeterReadingDto>>
  getMissingMeterReadings(accessToken: string, params?: { accountingMonth?: string; meterKind?: 'water' | 'electricity'; search?: string; limit?: number }): Promise<MissingMeterReadingDto[]>
  getGarageBalanceHistory(accessToken: string, garageId: string, params?: { monthFrom?: string; monthTo?: string }): Promise<GarageBalanceHistoryDto>
  getGarageIncomeWorksheet(accessToken: string, garageId: string, params?: { monthFrom?: string; monthTo?: string }): Promise<GarageIncomeWorksheetDto>
  getExpenseWorksheet(accessToken: string, params?: { accountingMonth?: string }): Promise<ExpenseWorksheetDto>
  getSummary(accessToken: string, params?: FinancePageParams): Promise<FinanceSummaryDto>
  createIncome(accessToken: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  updateIncome(accessToken: string, operationId: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createExpense(accessToken: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  createStaffPayment(accessToken: string, request: CreateStaffPaymentRequest): Promise<FinancialOperationDto>
  updateExpense(accessToken: string, operationId: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFinanceEntryRequest): Promise<FinancialOperationDto>
  createAccrual(accessToken: string, request: CreateAccrualRequest): Promise<AccrualDto>
  createDebtTransfer(accessToken: string, request: CreateDebtTransferRequest): Promise<AccrualDto>
  updateAccrual(accessToken: string, accrualId: string, request: CreateAccrualRequest): Promise<AccrualDto>
  cancelAccrual(accessToken: string, accrualId: string, request: CancelFinanceEntryRequest): Promise<AccrualDto>
  createSupplierAccrual(accessToken: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  updateSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  cancelSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CancelFinanceEntryRequest): Promise<SupplierAccrualDto>
  generateRegularAccruals(accessToken: string, request: GenerateRegularAccrualsRequest): Promise<RegularAccrualGenerationResultDto>
  generateRegularCatalogAccruals(accessToken: string, request: GenerateRegularCatalogAccrualsRequest): Promise<RegularCatalogAccrualGenerationResultDto>
  generateSupplierGroupSalaryAccruals(accessToken: string, request: GenerateSupplierGroupSalaryAccrualsRequest): Promise<SupplierGroupSalaryAccrualGenerationResultDto>
  createMeterReading(accessToken: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
  updateMeterReading(accessToken: string, meterReadingId: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
  cancelMeterReading(accessToken: string, meterReadingId: string, request: CancelFinanceEntryRequest): Promise<MeterReadingDto>
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
    throw new Error(problem?.detail ?? 'Не удалось выполнить финансовую операцию.')
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
  getSupplierAccruals(accessToken, limit) {
    return requestJson(accessToken, withLimit('/api/finance/supplier-accruals', limit))
  },
  getSupplierAccrualsPage(accessToken, params = {}) {
    return requestJson(accessToken, withQuery('/api/finance/supplier-accruals/page', {
      monthFrom: toMonthStart(params.monthFrom),
      monthTo: toMonthStart(params.monthTo),
      search: params.search,
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
  createIncome(accessToken, request) {
    return requestJson(accessToken, '/api/finance/income', { method: 'POST', body: JSON.stringify(request) })
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
  updateExpense(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/expense`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  createAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals', { method: 'POST', body: JSON.stringify(request) })
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
  createSupplierAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/supplier-accruals', { method: 'POST', body: JSON.stringify(request) })
  },
  updateSupplierAccrual(accessToken, supplierAccrualId, request) {
    return requestJson(accessToken, `/api/finance/supplier-accruals/${supplierAccrualId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelSupplierAccrual(accessToken, supplierAccrualId, request) {
    return requestJson(accessToken, `/api/finance/supplier-accruals/${supplierAccrualId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
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
  createMeterReading(accessToken, request) {
    return requestJson(accessToken, '/api/finance/meter-readings', { method: 'POST', body: JSON.stringify(request) })
  },
  updateMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
}
