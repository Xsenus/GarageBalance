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
    params?: DatedOperationReportQuery & { fundIds?: string[] },
    signal?: AbortSignal,
  ): Promise<FundChangeReportDto>
  exportFundChangeReportXlsx(
    accessToken: string,
    params?: DatedOperationReportExportQuery & { fundIds?: string[] },
  ): Promise<Blob>
  exportFundChangeReportPdf(
    accessToken: string,
    params?: DatedOperationReportExportQuery & { fundIds?: string[] },
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

function buildReportQuery(
  params: Record<string, string | number | boolean | string[] | undefined>,
  keys: string[],
) {
  const query = new URLSearchParams()
  for (const key of keys) {
    const value = params[key]
    if (Array.isArray(value)) {
      for (const id of value) query.append(key, id)
    } else if (value || value === false || (key === 'offset' && value === 0)) {
      query.set(key, String(value))
    }
  }
  appendReportSort(query, params)
  return query.toString()
}

function buildIncomeReportQuery(params: Parameters<ReportClient['getIncomeReport']>[1] = {}) {
  return buildReportQuery(params, ['dateFrom', 'dateTo', 'search', 'rowMode', 'groupPayments', 'limit', 'offset', 'garageIds', 'ownerIds', 'incomeTypeIds'])
}

function buildConsolidatedReportQuery(params: Parameters<ReportClient['getConsolidatedReport']>[1] = {}) {
  return buildReportQuery(params, ['monthFrom', 'monthTo', 'search', 'limit', 'offset'])
}

function buildGarageReportQuery(params: Parameters<ReportClient['getGarageReport']>[1] = {}) {
  return buildReportQuery(params, ['monthFrom', 'monthTo', 'search', 'groupAccruals', 'offset', 'limit', 'garageIds', 'ownerIds', 'incomeTypeIds'])
}

function buildExpenseReportQuery(params: Parameters<ReportClient['getExpenseReport']>[1] = {}) {
  return buildReportQuery(params, ['dateFrom', 'dateTo', 'search', 'rowMode', 'limit', 'offset', 'supplierIds', 'staffMemberIds', 'expenseTypeIds'])
}

function buildDatedOperationReportQuery(params: DatedOperationReportQuery & { fundIds?: string[] } = {}) {
  return buildReportQuery(params, ['dateFrom', 'dateTo', 'search', 'offset', 'limit', 'fundIds'])
}

function buildFeeReportQuery(params: Parameters<ReportClient['getFeeReport']>[1] = {}) {
  return buildReportQuery(params, ['search', 'feeEntryIds', 'limit', 'offset'])
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
