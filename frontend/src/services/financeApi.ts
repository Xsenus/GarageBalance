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
  getOperations(accessToken: string): Promise<FinancialOperationDto[]>
  getAccruals(accessToken: string): Promise<AccrualDto[]>
  getSupplierAccruals(accessToken: string): Promise<SupplierAccrualDto[]>
  getMeterReadings(accessToken: string): Promise<MeterReadingDto[]>
  getSummary(accessToken: string): Promise<FinanceSummaryDto>
  createIncome(accessToken: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createExpense(accessToken: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  cancelOperation(accessToken: string, operationId: string, request: CancelFinanceEntryRequest): Promise<FinancialOperationDto>
  createAccrual(accessToken: string, request: CreateAccrualRequest): Promise<AccrualDto>
  cancelAccrual(accessToken: string, accrualId: string, request: CancelFinanceEntryRequest): Promise<AccrualDto>
  createSupplierAccrual(accessToken: string, request: CreateSupplierAccrualRequest): Promise<SupplierAccrualDto>
  cancelSupplierAccrual(accessToken: string, supplierAccrualId: string, request: CancelFinanceEntryRequest): Promise<SupplierAccrualDto>
  generateRegularAccruals(accessToken: string, request: GenerateRegularAccrualsRequest): Promise<RegularAccrualGenerationResultDto>
  createMeterReading(accessToken: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
  cancelMeterReading(accessToken: string, meterReadingId: string, request: CancelFinanceEntryRequest): Promise<MeterReadingDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

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
  getOperations(accessToken) {
    return requestJson(accessToken, '/api/finance/operations')
  },
  getAccruals(accessToken) {
    return requestJson(accessToken, '/api/finance/accruals')
  },
  getSupplierAccruals(accessToken) {
    return requestJson(accessToken, '/api/finance/supplier-accruals')
  },
  getMeterReadings(accessToken) {
    return requestJson(accessToken, '/api/finance/meter-readings')
  },
  getSummary(accessToken) {
    return requestJson(accessToken, '/api/finance/summary')
  },
  createIncome(accessToken, request) {
    return requestJson(accessToken, '/api/finance/income', { method: 'POST', body: JSON.stringify(request) })
  },
  createExpense(accessToken, request) {
    return requestJson(accessToken, '/api/finance/expense', { method: 'POST', body: JSON.stringify(request) })
  },
  cancelOperation(accessToken, operationId, request) {
    return requestJson(accessToken, `/api/finance/operations/${operationId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  createAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals', { method: 'POST', body: JSON.stringify(request) })
  },
  cancelAccrual(accessToken, accrualId, request) {
    return requestJson(accessToken, `/api/finance/accruals/${accrualId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
  createSupplierAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/supplier-accruals', { method: 'POST', body: JSON.stringify(request) })
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
  cancelMeterReading(accessToken, meterReadingId, request) {
    return requestJson(accessToken, `/api/finance/meter-readings/${meterReadingId}/cancel`, { method: 'POST', body: JSON.stringify(request) })
  },
}
