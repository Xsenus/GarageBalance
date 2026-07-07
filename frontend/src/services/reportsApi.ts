export type MonthlyReportRowDto = {
  accountingMonth: string
  incomeTotal: number
  expenseTotal: number
  accrualTotal: number
  balance: number
  debt: number
  operationCount: number
  accrualCount: number
  meterReadingCount: number
}

export type GarageReportRowDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  incomeTotal: number
  accrualTotal: number
  debt: number
  meterReadingCount: number
}

export type ConsolidatedReportDto = {
  periodFrom: string
  periodTo: string
  incomeTotal: number
  expenseTotal: number
  accrualTotal: number
  balance: number
  debt: number
  operationCount: number
  accrualCount: number
  meterReadingCount: number
  monthlyRows: MonthlyReportRowDto[]
  garageRowCount: number
  garageRows: GarageReportRowDto[]
}

export type IncomeReportRowDto = {
  rowType: string
  date: string
  accountingMonth: string
  garageId: string
  garageNumber: string
  ownerId: string | null
  ownerName: string | null
  incomeTypeId: string
  incomeTypeName: string
  accrualAmount: number
  incomeAmount: number
  debt: number
  documentNumber: string | null
  comment: string | null
  createdAtUtc: string | null
  debtAfterPayment?: number | null
}

export type IncomeReportDto = {
  dateFrom: string
  dateTo: string
  accrualTotal: number
  incomeTotal: number
  debt: number
  rowCount: number
  rows: IncomeReportRowDto[]
}

export type ExpenseReportRowDto = {
  rowType: string
  date: string
  accountingMonth: string
  supplierId: string
  supplierName: string
  expenseTypeId: string
  expenseTypeName: string
  accrualAmount: number
  expenseAmount: number
  difference: number
  documentNumber: string | null
  comment: string | null
}

export type ExpenseReportDto = {
  dateFrom: string
  dateTo: string
  accrualTotal: number
  expenseTotal: number
  difference: number
  rowCount: number
  rows: ExpenseReportRowDto[]
}

export type FundChangeReportRowDto = {
  operationId: string
  fundId: string
  fundName: string
  date: string
  changeKind: string
  changeName: string
  amount: number
  balanceBefore: number
  balanceAfter: number
  actorUserId: string | null
  actorDisplayName: string | null
  reason: string
}

export type FundChangeReportDto = {
  dateFrom: string
  dateTo: string
  depositTotal: number
  withdrawalTotal: number
  rowCount: number
  rows: FundChangeReportRowDto[]
}

export type CashPaymentReportRowDto = {
  operationId: string
  date: string
  amount: number
  hasReceipt: boolean
  purpose: string
  supplierName: string | null
  expenseTypeName: string | null
  documentNumber: string | null
  comment: string | null
}

export type CashPaymentReportDto = {
  dateFrom: string
  dateTo: string
  total: number
  rowCount: number
  rows: CashPaymentReportRowDto[]
}

export type BankDepositReportRowDto = {
  operationId: string
  date: string
  amount: number
  fundName: string | null
  comment: string
}

export type BankDepositReportDto = {
  dateFrom: string
  dateTo: string
  total: number
  rowCount: number
  rows: BankDepositReportRowDto[]
}

export type FeeReportSummaryRowDto = {
  incomeTypeId: string
  name: string
  goal: string
  feeAmount: number
  collected: number
}

export type FeeReportDebtorRowDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  incomeTypeId: string
  feeName: string
  paid: number
  lastPaymentDate: string | null
  debt: number
}

export type FeeReportDto = {
  variation: string
  accruedTotal: number
  collectedTotal: number
  debtTotal: number
  rowCount: number
  summaryRows: FeeReportSummaryRowDto[]
  debtorRows: FeeReportDebtorRowDto[]
}

export type ReportClient = {
  getConsolidatedReport(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string; limit?: number }): Promise<ConsolidatedReportDto>
  exportConsolidatedReportXlsx(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string }): Promise<Blob>
  exportConsolidatedReportPdf(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string }): Promise<Blob>
  getIncomeReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      garageIds?: string[]
      ownerIds?: string[]
      incomeTypeIds?: string[]
      rowMode?: string
      limit?: number
    },
  ): Promise<IncomeReportDto>
  exportIncomeReportXlsx(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      garageIds?: string[]
      ownerIds?: string[]
      incomeTypeIds?: string[]
      rowMode?: string
    },
  ): Promise<Blob>
  exportIncomeReportPdf(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      garageIds?: string[]
      ownerIds?: string[]
      incomeTypeIds?: string[]
      rowMode?: string
    },
  ): Promise<Blob>
  getExpenseReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
      limit?: number
    },
  ): Promise<ExpenseReportDto>
  getFundChangeReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      limit?: number
    },
  ): Promise<FundChangeReportDto>
  getCashPaymentReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      limit?: number
    },
  ): Promise<CashPaymentReportDto>
  exportCashPaymentReportXlsx(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
    },
  ): Promise<Blob>
  exportCashPaymentReportPdf(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
    },
  ): Promise<Blob>
  getBankDepositReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      limit?: number
    },
  ): Promise<BankDepositReportDto>
  exportBankDepositReportXlsx(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
    },
  ): Promise<Blob>
  exportBankDepositReportPdf(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
    },
  ): Promise<Blob>
  getFeeReport(
    accessToken: string,
    params?: {
      variation?: string
      limit?: number
    },
  ): Promise<FeeReportDto>
  exportExpenseReportXlsx(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
    },
  ): Promise<Blob>
  exportExpenseReportPdf(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
    },
  ): Promise<Blob>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось сформировать отчет.')
  }

  return response.json()
}

async function requestBlob(accessToken: string, path: string, init?: RequestInit): Promise<Blob> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось выгрузить отчет.')
  }

  return response.blob()
}

function buildIncomeReportQuery(params: Parameters<ReportClient['getIncomeReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.rowMode) {
    searchParams.set('rowMode', params.rowMode)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  for (const garageId of params.garageIds ?? []) {
    searchParams.append('garageIds', garageId)
  }
  for (const ownerId of params.ownerIds ?? []) {
    searchParams.append('ownerIds', ownerId)
  }
  for (const incomeTypeId of params.incomeTypeIds ?? []) {
    searchParams.append('incomeTypeIds', incomeTypeId)
  }
  return searchParams.toString()
}

function buildConsolidatedReportQuery(params: Parameters<ReportClient['getConsolidatedReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.monthFrom) {
    searchParams.set('monthFrom', params.monthFrom)
  }
  if (params.monthTo) {
    searchParams.set('monthTo', params.monthTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  return searchParams.toString()
}

function buildExpenseReportQuery(params: Parameters<ReportClient['getExpenseReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.rowMode) {
    searchParams.set('rowMode', params.rowMode)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  for (const supplierId of params.supplierIds ?? []) {
    searchParams.append('supplierIds', supplierId)
  }
  for (const expenseTypeId of params.expenseTypeIds ?? []) {
    searchParams.append('expenseTypeIds', expenseTypeId)
  }
  return searchParams.toString()
}

function buildFundChangeReportQuery(params: Parameters<ReportClient['getFundChangeReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  return searchParams.toString()
}

function buildCashPaymentReportQuery(params: Parameters<ReportClient['getCashPaymentReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  return searchParams.toString()
}

function buildBankDepositReportQuery(params: Parameters<ReportClient['getBankDepositReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  return searchParams.toString()
}

function buildFeeReportQuery(params: Parameters<ReportClient['getFeeReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.variation) {
    searchParams.set('variation', params.variation)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  return searchParams.toString()
}

export const reportsApi: ReportClient = {
  getConsolidatedReport(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestJson(accessToken, `/api/reports/consolidated${query ? `?${query}` : ''}`)
  },
  exportConsolidatedReportXlsx(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestBlob(accessToken, `/api/reports/consolidated/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportConsolidatedReportPdf(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestBlob(accessToken, `/api/reports/consolidated/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getIncomeReport(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestJson(accessToken, `/api/reports/income${query ? `?${query}` : ''}`)
  },
  exportIncomeReportXlsx(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportIncomeReportPdf(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getExpenseReport(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestJson(accessToken, `/api/reports/expense${query ? `?${query}` : ''}`)
  },
  getFundChangeReport(accessToken, params = {}) {
    const query = buildFundChangeReportQuery(params)
    return requestJson(accessToken, `/api/reports/fund-changes${query ? `?${query}` : ''}`)
  },
  getCashPaymentReport(accessToken, params = {}) {
    const query = buildCashPaymentReportQuery(params)
    return requestJson(accessToken, `/api/reports/cash-payments${query ? `?${query}` : ''}`)
  },
  exportCashPaymentReportXlsx(accessToken, params = {}) {
    const query = buildCashPaymentReportQuery(params)
    return requestBlob(accessToken, `/api/reports/cash-payments/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportCashPaymentReportPdf(accessToken, params = {}) {
    const query = buildCashPaymentReportQuery(params)
    return requestBlob(accessToken, `/api/reports/cash-payments/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getBankDepositReport(accessToken, params = {}) {
    const query = buildBankDepositReportQuery(params)
    return requestJson(accessToken, `/api/reports/bank-deposits${query ? `?${query}` : ''}`)
  },
  exportBankDepositReportXlsx(accessToken, params = {}) {
    const query = buildBankDepositReportQuery(params)
    return requestBlob(accessToken, `/api/reports/bank-deposits/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportBankDepositReportPdf(accessToken, params = {}) {
    const query = buildBankDepositReportQuery(params)
    return requestBlob(accessToken, `/api/reports/bank-deposits/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getFeeReport(accessToken, params = {}) {
    const query = buildFeeReportQuery(params)
    return requestJson(accessToken, `/api/reports/fees${query ? `?${query}` : ''}`)
  },
  exportExpenseReportXlsx(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestBlob(accessToken, `/api/reports/expense/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportExpenseReportPdf(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestBlob(accessToken, `/api/reports/expense/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
}
