import { authenticatedApiFetch, authenticatedJsonBodyApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

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
  bankBalanceOpening: number
  bankBalanceClosing: number
  incomeBreakdown: NamedAmountTotalDto[]
  expenseBreakdown: NamedAmountTotalDto[]
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

export type GarageDetailReportRowDto = {
  accountingMonth: string
  garageId: string
  garageNumber: string
  ownerName: string | null
  incomeTypeId: string | null
  incomeTypeName: string
  accrualAmount: number
  incomeAmount: number
  difference: number
}

export type GarageDetailReportDto = {
  periodFrom: string
  periodTo: string
  accrualTotal: number
  incomeTotal: number
  difference: number
  rowCount: number
  rows: GarageDetailReportRowDto[]
  offset: number
  limit: number
}

export type GarageReportQuickListGarageDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  isArchived: boolean
}

export type GarageReportQuickListDto = {
  id: string
  name: string
  garages: GarageReportQuickListGarageDto[]
  updatedAtUtc: string
  updatedByUserId: string | null
}

export type UpsertGarageReportQuickListRequest = {
  name: string
  garageIds: string[]
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
  incomeBreakdown: NamedAmountTotalDto[]
  expenseBreakdown: NamedAmountTotalDto[]
}

export type NamedAmountTotalDto = {
  typeId: string | null
  name: string
  amount: number
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
  offset: number
  limit: number
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
  staffMemberId?: string | null
  counterpartyKind?: 'supplier' | 'staff'
}

export type ExpenseReportDto = {
  dateFrom: string
  dateTo: string
  accrualTotal: number
  expenseTotal: number
  difference: number
  rowCount: number
  rows: ExpenseReportRowDto[]
  offset: number
  limit: number
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
  offset: number
  limit: number
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
  offset: number
  limit: number
  rows: CashPaymentReportRowDto[]
}

export type BankDepositReportRowDto = {
  operationId: string
  date: string
  amount: number
  comment: string | null
}

export type BankDepositReportDto = {
  dateFrom: string
  dateTo: string
  total: number
  rowCount: number
  offset: number
  limit: number
  rows: BankDepositReportRowDto[]
}

type DatedOperationReportQuery = {
  dateFrom?: string
  dateTo?: string
  search?: string
  offset?: number
  limit?: number
  sortBy?: string
  sortDirection?: string
}

type DatedOperationReportExportQuery = Omit<DatedOperationReportQuery, 'offset' | 'limit'>

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

export type FeeReportGarageRowDto = {
  garageId: string
  garageNumber: string
  ownerName: string | null
  incomeTypeId: string
  feeName: string
  accrued: number
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
  garageRows: FeeReportGarageRowDto[]
  debtorRows: FeeReportDebtorRowDto[]
}

export type ReportClient = {
  getGarageReportQuickLists(accessToken: string, signal?: AbortSignal): Promise<GarageReportQuickListDto[]>
  createGarageReportQuickList(accessToken: string, request: UpsertGarageReportQuickListRequest): Promise<GarageReportQuickListDto>
  updateGarageReportQuickList(accessToken: string, id: string, request: UpsertGarageReportQuickListRequest): Promise<GarageReportQuickListDto>
  deleteGarageReportQuickList(accessToken: string, id: string, reason: string): Promise<void>
  getConsolidatedReport(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string; limit?: number; offset?: number; sortBy?: string; sortDirection?: string }, signal?: AbortSignal): Promise<ConsolidatedReportDto>
  getGarageReport(
    accessToken: string,
    params?: { monthFrom?: string; monthTo?: string; search?: string; garageIds?: string[]; ownerIds?: string[]; incomeTypeIds?: string[]; groupAccruals?: boolean; offset?: number; limit?: number; sortBy?: string; sortDirection?: string },
    signal?: AbortSignal,
  ): Promise<GarageDetailReportDto>
  exportGarageReportXlsx(
    accessToken: string,
    params?: { monthFrom?: string; monthTo?: string; search?: string; garageIds?: string[]; ownerIds?: string[]; incomeTypeIds?: string[]; groupAccruals?: boolean; sortBy?: string; sortDirection?: string },
  ): Promise<Blob>
  exportGarageReportPdf(
    accessToken: string,
    params?: { monthFrom?: string; monthTo?: string; search?: string; garageIds?: string[]; ownerIds?: string[]; incomeTypeIds?: string[]; groupAccruals?: boolean; sortBy?: string; sortDirection?: string },
  ): Promise<Blob>
  exportConsolidatedReportXlsx(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string; sortBy?: string; sortDirection?: string }): Promise<Blob>
  exportConsolidatedReportPdf(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string; sortBy?: string; sortDirection?: string }): Promise<Blob>
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
      groupPayments?: boolean
      limit?: number
      offset?: number
      sortBy?: string
      sortDirection?: string
    },
    signal?: AbortSignal,
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
      groupPayments?: boolean
      sortBy?: string
      sortDirection?: string
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
      groupPayments?: boolean
      sortBy?: string
      sortDirection?: string
    },
  ): Promise<Blob>
  getExpenseReport(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      staffMemberIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
      limit?: number
      offset?: number
      sortBy?: string
      sortDirection?: string
    },
    signal?: AbortSignal,
  ): Promise<ExpenseReportDto>
  getFundChangeReport(
    accessToken: string,
    params?: DatedOperationReportQuery,
    signal?: AbortSignal,
  ): Promise<FundChangeReportDto>
  exportFundChangeReportXlsx(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  exportFundChangeReportPdf(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  getCashPaymentReport(
    accessToken: string,
    params?: DatedOperationReportQuery,
    signal?: AbortSignal,
  ): Promise<CashPaymentReportDto>
  exportCashPaymentReportXlsx(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  exportCashPaymentReportPdf(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  getBankDepositReport(
    accessToken: string,
    params?: DatedOperationReportQuery,
    signal?: AbortSignal,
  ): Promise<BankDepositReportDto>
  exportBankDepositReportXlsx(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  exportBankDepositReportPdf(
    accessToken: string,
    params?: DatedOperationReportExportQuery,
  ): Promise<Blob>
  getFeeReport(
    accessToken: string,
    params?: {
      variation?: string
      feeEntryIds?: string[]
      limit?: number
      offset?: number
      sortBy?: string
      sortDirection?: string
    },
    signal?: AbortSignal,
  ): Promise<FeeReportDto>
  exportFeeReportXlsx(
    accessToken: string,
    params?: {
      variation?: string
      feeEntryIds?: string[]
      sortBy?: string
      sortDirection?: string
    },
  ): Promise<Blob>
  exportFeeReportPdf(
    accessToken: string,
    params?: {
      variation?: string
      feeEntryIds?: string[]
      sortBy?: string
      sortDirection?: string
    },
  ): Promise<Blob>
  exportExpenseReportXlsx(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      staffMemberIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
      sortBy?: string
      sortDirection?: string
    },
  ): Promise<Blob>
  exportExpenseReportPdf(
    accessToken: string,
    params?: {
      dateFrom?: string
      dateTo?: string
      search?: string
      supplierIds?: string[]
      staffMemberIds?: string[]
      expenseTypeIds?: string[]
      rowMode?: string
      sortBy?: string
      sortDirection?: string
    },
  ): Promise<Blob>
}

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await authenticatedJsonBodyApiFetch(accessToken, path, init)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось сформировать отчет.'))
  }

  return response.status === 204 ? undefined as TResponse : response.json()
}

async function requestBlob(accessToken: string, path: string, init?: RequestInit): Promise<Blob> {
  const response = await authenticatedApiFetch(accessToken, path, init)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось выгрузить отчет.'))
  }

  return response.blob()
}

function appendReportSort(searchParams: URLSearchParams, params: { sortBy?: string; sortDirection?: string }) {
  if (params.sortBy) {
    searchParams.set('sortBy', params.sortBy)
  }
  if (params.sortDirection) {
    searchParams.set('sortDirection', params.sortDirection)
  }
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
  if (params.groupPayments !== undefined) {
    searchParams.set('groupPayments', String(params.groupPayments))
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
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
  appendReportSort(searchParams, params)
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
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
  }
  appendReportSort(searchParams, params)
  return searchParams.toString()
}

function buildGarageReportQuery(params: Parameters<ReportClient['getGarageReport']>[1] = {}) {
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
  if (params.groupAccruals !== undefined) {
    searchParams.set('groupAccruals', String(params.groupAccruals))
  }
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
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
  appendReportSort(searchParams, params)
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
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
  }
  for (const supplierId of params.supplierIds ?? []) {
    searchParams.append('supplierIds', supplierId)
  }
  for (const staffMemberId of params.staffMemberIds ?? []) {
    searchParams.append('staffMemberIds', staffMemberId)
  }
  for (const expenseTypeId of params.expenseTypeIds ?? []) {
    searchParams.append('expenseTypeIds', expenseTypeId)
  }
  appendReportSort(searchParams, params)
  return searchParams.toString()
}

function buildDatedOperationReportQuery(params: DatedOperationReportQuery = {}) {
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
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  appendReportSort(searchParams, params)
  return searchParams.toString()
}

function buildFeeReportQuery(params: Parameters<ReportClient['getFeeReport']>[1] = {}) {
  const searchParams = new URLSearchParams()
  if (params.variation) {
    searchParams.set('variation', params.variation)
  }
  params.feeEntryIds?.forEach((feeEntryId) => searchParams.append('feeEntryIds', feeEntryId))
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  if (params.offset !== undefined) {
    searchParams.set('offset', String(params.offset))
  }
  appendReportSort(searchParams, params)
  return searchParams.toString()
}

export const reportsApi: ReportClient = {
  getGarageReportQuickLists(accessToken, signal) {
    return requestJson(accessToken, '/api/reports/garage-quick-lists', { signal })
  },
  createGarageReportQuickList(accessToken, request) {
    return requestJson(accessToken, '/api/reports/garage-quick-lists', { method: 'POST', body: JSON.stringify(request) })
  },
  updateGarageReportQuickList(accessToken, id, request) {
    return requestJson(accessToken, `/api/reports/garage-quick-lists/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(request) })
  },
  deleteGarageReportQuickList(accessToken, id, reason) {
    return requestJson(accessToken, `/api/reports/garage-quick-lists/${encodeURIComponent(id)}`, { method: 'DELETE', body: JSON.stringify({ reason }) })
  },
  getConsolidatedReport(accessToken, params = {}, signal) {
    const query = buildConsolidatedReportQuery(params)
    return requestJson(accessToken, `/api/reports/consolidated${query ? `?${query}` : ''}`, { signal })
  },
  getGarageReport(accessToken, params = {}, signal) {
    const query = buildGarageReportQuery(params)
    return requestJson(accessToken, `/api/reports/garages${query ? `?${query}` : ''}`, { signal })
  },
  exportGarageReportXlsx(accessToken, params = {}) {
    const query = buildGarageReportQuery(params)
    return requestBlob(accessToken, `/api/reports/garages/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportGarageReportPdf(accessToken, params = {}) {
    const query = buildGarageReportQuery(params)
    return requestBlob(accessToken, `/api/reports/garages/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportConsolidatedReportXlsx(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestBlob(accessToken, `/api/reports/consolidated/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportConsolidatedReportPdf(accessToken, params = {}) {
    const query = buildConsolidatedReportQuery(params)
    return requestBlob(accessToken, `/api/reports/consolidated/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getIncomeReport(accessToken, params = {}, signal) {
    const query = buildIncomeReportQuery(params)
    return requestJson(accessToken, `/api/reports/income${query ? `?${query}` : ''}`, { signal })
  },
  exportIncomeReportXlsx(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportIncomeReportPdf(accessToken, params = {}) {
    const query = buildIncomeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/income/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getExpenseReport(accessToken, params = {}, signal) {
    const query = buildExpenseReportQuery(params)
    return requestJson(accessToken, `/api/reports/expense${query ? `?${query}` : ''}`, { signal })
  },
  getFundChangeReport(accessToken, params = {}, signal) {
    const query = buildDatedOperationReportQuery(params)
    return requestJson(accessToken, `/api/reports/fund-changes${query ? `?${query}` : ''}`, { signal })
  },
  exportFundChangeReportXlsx(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/fund-changes/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportFundChangeReportPdf(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/fund-changes/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getCashPaymentReport(accessToken, params = {}, signal) {
    const query = buildDatedOperationReportQuery(params)
    return requestJson(accessToken, `/api/reports/cash-payments${query ? `?${query}` : ''}`, { signal })
  },
  exportCashPaymentReportXlsx(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/cash-payments/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportCashPaymentReportPdf(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/cash-payments/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getBankDepositReport(accessToken, params = {}, signal) {
    const query = buildDatedOperationReportQuery(params)
    return requestJson(accessToken, `/api/reports/bank-deposits${query ? `?${query}` : ''}`, { signal })
  },
  exportBankDepositReportXlsx(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/bank-deposits/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportBankDepositReportPdf(accessToken, params = {}) {
    const query = buildDatedOperationReportQuery(params)
    return requestBlob(accessToken, `/api/reports/bank-deposits/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  getFeeReport(accessToken, params = {}, signal) {
    const query = buildFeeReportQuery(params)
    return requestJson(accessToken, `/api/reports/fees${query ? `?${query}` : ''}`, { signal })
  },
  exportFeeReportXlsx(accessToken, params = {}) {
    const query = buildFeeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/fees/export/xlsx${query ? `?${query}` : ''}`, { method: 'POST' })
  },
  exportFeeReportPdf(accessToken, params = {}) {
    const query = buildFeeReportQuery(params)
    return requestBlob(accessToken, `/api/reports/fees/export/pdf${query ? `?${query}` : ''}`, { method: 'POST' })
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
