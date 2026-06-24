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
  isCanceled: boolean
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
  source: 'manual' | 'regular'
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
  getSummary(accessToken: string, params?: FinancePageParams): Promise<FinanceSummaryDto>
  createIncome(accessToken: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  updateIncome(accessToken: string, operationId: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createExpense(accessToken: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  updateExpense(accessToken: string, operationId: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFinanceEntryRequest): Promise<FinancialOperationDto>
  createAccrual(accessToken: string, request: CreateAccrualRequest): Promise<AccrualDto>
  updateAccrual(accessToken: string, accrualId: string, request: CreateAccrualRequest): Promise<AccrualDto>
  cancelAccrual(accessToken: string, accrualId: string, request: CancelFinanceEntryRequest): Promise<AccrualDto>
  createSupplierAccrual(accessToken: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  updateSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  cancelSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CancelFinanceEntryRequest): Promise<SupplierAccrualDto>
  generateRegularAccruals(accessToken: string, request: GenerateRegularAccrualsRequest): Promise<RegularAccrualGenerationResultDto>
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
  updateExpense(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/expense`, { method: 'PUT', body: JSON.stringify(request) })
  },
  cancelOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  createAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals', { method: 'POST', body: JSON.stringify(request) })
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
