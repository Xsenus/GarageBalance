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

export type CreateAccrualRequest = {
  garageId: string
  incomeTypeId: string
  accountingMonth: string
  amount: number
  source: 'manual' | 'regular'
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
  getMeterReadings(accessToken: string): Promise<MeterReadingDto[]>
  getSummary(accessToken: string): Promise<FinanceSummaryDto>
  createIncome(accessToken: string, request: CreateIncomeOperationRequest): Promise<FinancialOperationDto>
  createExpense(accessToken: string, request: CreateExpenseOperationRequest): Promise<FinancialOperationDto>
  createAccrual(accessToken: string, request: CreateAccrualRequest): Promise<AccrualDto>
  generateRegularAccruals(accessToken: string, request: GenerateRegularAccrualsRequest): Promise<RegularAccrualGenerationResultDto>
  createMeterReading(accessToken: string, request: CreateMeterReadingRequest): Promise<MeterReadingDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'

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
  createAccrual(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals', { method: 'POST', body: JSON.stringify(request) })
  },
  generateRegularAccruals(accessToken, request) {
    return requestJson(accessToken, '/api/finance/accruals/generate-regular', { method: 'POST', body: JSON.stringify(request) })
  },
  createMeterReading(accessToken, request) {
    return requestJson(accessToken, '/api/finance/meter-readings', { method: 'POST', body: JSON.stringify(request) })
  },
}
