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

export type ReportClient = {
  getConsolidatedReport(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string }): Promise<ConsolidatedReportDto>
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
    },
  ): Promise<ExpenseReportDto>
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

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'

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

async function requestBlob(accessToken: string, path: string): Promise<Blob> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
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
  for (const supplierId of params.supplierIds ?? []) {
    searchParams.append('supplierIds', supplierId)
  }
  for (const expenseTypeId of params.expenseTypeIds ?? []) {
    searchParams.append('expenseTypeIds', expenseTypeId)
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
    return requestBlob(accessToken, `/api/reports/consolidated/export/xlsx${query ? `?${query}` : ''}`)
  },
  exportConsolidatedReportPdf(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestBlob(accessToken, `/api/reports/consolidated/export/pdf${query ? `?${query}` : ''}`)
  },
  getIncomeReport(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestJson(accessToken, `/api/reports/income${query ? `?${query}` : ''}`)
  },
  exportIncomeReportXlsx(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/xlsx${query ? `?${query}` : ''}`)
  },
  exportIncomeReportPdf(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/pdf${query ? `?${query}` : ''}`)
  },
  getExpenseReport(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestJson(accessToken, `/api/reports/expense${query ? `?${query}` : ''}`)
  },
  exportExpenseReportXlsx(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestBlob(accessToken, `/api/reports/expense/export/xlsx${query ? `?${query}` : ''}`)
  },
  exportExpenseReportPdf(accessToken, params = {}) {
    const query = buildExpenseReportQuery(params)
    return requestBlob(accessToken, `/api/reports/expense/export/pdf${query ? `?${query}` : ''}`)
  },
}
