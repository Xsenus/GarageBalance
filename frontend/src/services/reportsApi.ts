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

export type ReportClient = {
  getConsolidatedReport(accessToken: string, params?: { monthFrom?: string; monthTo?: string; search?: string }): Promise<ConsolidatedReportDto>
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

export const reportsApi: ReportClient = {
  getConsolidatedReport(accessToken, params = {}) {
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
    const query = searchParams.toString()
    return requestJson(accessToken, `/api/reports/consolidated${query ? `?${query}` : ''}`)
  },
}
