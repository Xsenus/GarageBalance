import { authenticatedApiFetch, authenticatedJsonApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

export type ServiceReportColumn = { id: string; name: string; serviceIds: string[] }
export type ServiceReportScope = 'payments' | 'accrued' | 'overdue'
export type ServiceReportColumns = { version: string; report?: ServiceReportScope; columns: ServiceReportColumn[]; services: { id: string; name: string; incomeTypeId: string | null; isArchived: boolean }[] }
export type ServiceReportQuery = { dateFrom?: string; dateTo?: string; garageId?: string; offset?: number; limit?: number; overdueOnly?: boolean }
export type ServiceReport = { dateFrom: string | null; dateTo: string; columns: ServiceReportColumn[]; rows: { date: string | null; garageId: string; garageNumber: string; amounts: number[]; total: number }[]; days: { date: string; amounts: number[]; total: number }[]; totals: number[]; total: number; rowCount: number; offset: number; limit: number }
export type ServiceReportKind = 'payments' | 'debt'
export interface ServiceReportsClient {
  getColumns(token: string, signal?: AbortSignal, report?: ServiceReportScope): Promise<ServiceReportColumns>
  saveColumns(token: string, request: Pick<ServiceReportColumns, 'version' | 'columns' | 'report'>, signal?: AbortSignal): Promise<ServiceReportColumns>
  getReport(token: string, kind: ServiceReportKind, query: ServiceReportQuery, signal?: AbortSignal): Promise<ServiceReport>
  exportReport(token: string, kind: ServiceReportKind, query: ServiceReportQuery, format: 'xlsx' | 'pdf', signal?: AbortSignal): Promise<Blob>
}
function queryString(query: ServiceReportQuery) {
  const parameters = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => { if (value !== undefined && value !== '') parameters.set(key, String(value)) })
  return parameters.toString()
}
async function json<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(await readApiErrorMessage(response, 'Не удалось получить отчёт или сохранить колонки.'))
  return response.json() as Promise<T>
}
export const serviceReportsApi: ServiceReportsClient = {
  async getColumns(token, signal, report) { return json(await authenticatedApiFetch(token, `/api/reports/services/columns${report ? `?report=${report}` : ''}`, { signal })) },
  async saveColumns(token, request, signal) { return json(await authenticatedJsonApiFetch(token, '/api/reports/services/columns', { method: 'PUT', body: JSON.stringify(request), signal })) },
  async getReport(token, kind, query, signal) { return json(await authenticatedApiFetch(token, `/api/reports/services/${kind}?${queryString(query)}`, { signal })) },
  async exportReport(token, kind, query, format, signal) {
    const response = await authenticatedApiFetch(token, `/api/reports/services/${kind}/export/${format}?${queryString({ ...query, offset: undefined, limit: undefined })}`, { method: 'POST', signal })
    if (!response.ok) throw new Error(await readApiErrorMessage(response, 'Не удалось выгрузить отчёт.'))
    return response.blob()
  },
}
