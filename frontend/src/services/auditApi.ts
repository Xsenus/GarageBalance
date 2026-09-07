import { authenticatedApiFetch, readApiErrorMessage } from './authenticatedApiFetch'

export type AuditEventDto = {
  id: string
  createdAtUtc: string
  actorUserId: string | null
  actorDisplayName?: string | null
  actorEmail?: string | null
  action: string
  entityType: string
  entityId: string | null
  entityDisplayName?: string | null
  relatedGarageId?: string | null
  relatedGarageNumber?: string | null
  relatedAccountingMonth?: string | null
  relatedCounterpartyId?: string | null
  relatedCounterpartyName?: string | null
  relatedDocumentId?: string | null
  relatedDocumentNumber?: string | null
  summary: string
  section?: string | null
  actionKind?: string | null
  fieldName?: string | null
  oldValue?: string | null
  newValue?: string | null
  reason?: string | null
  metadata?: Record<string, string> | null
}

export type AuditEventPageDto = {
  items: AuditEventDto[]
  totalCount: number
  offset: number
  limit: number
}

export type AuditEventQuery = { dateFrom?: string; dateTo?: string; action?: string; search?: string; offset?: number; limit?: number; section?: string; actionKind?: string; entityType?: string; actorUserId?: string; quickFilter?: string; relatedGarage?: string; relatedAccountingMonth?: string; relatedCounterparty?: string; relatedDocument?: string }

export type AuditClient = {
  getEvents(accessToken: string, params?: AuditEventQuery, signal?: AbortSignal): Promise<AuditEventDto[]>
  getEventsPage(accessToken: string, params?: AuditEventQuery, signal?: AbortSignal): Promise<AuditEventPageDto>
  getEvent(accessToken: string, id: string, signal?: AbortSignal): Promise<AuditEventDto>
  exportEvents(accessToken: string, params?: AuditEventQuery): Promise<Blob>
  exportEventsXlsx(accessToken: string, params?: AuditEventQuery): Promise<Blob>
}

async function requestJson<TResponse>(accessToken: string, path: string, signal?: AbortSignal): Promise<TResponse> {
  const response = await authenticatedApiFetch(accessToken, path, {
    signal,
  })

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось загрузить историю изменений.'))
  }

  return response.json()
}

async function requestBlob(accessToken: string, path: string): Promise<Blob> {
  const response = await authenticatedApiFetch(accessToken, path)

  if (!response.ok) {
    throw new Error(await readApiErrorMessage(response, 'Не удалось скачать историю изменений.'))
  }

  return response.blob()
}

function getAuditDateBoundary(value: string, end: boolean) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return value
  const instant = new Date(`${value}T${end ? '23:59:59.999' : '00:00:00.000'}`).toISOString()
  // Include the final microsecond stored by PostgreSQL, preserving the local calendar day.
  return end ? instant.replace('.999Z', '.999999Z') : instant
}

function buildQuery(params: AuditEventQuery = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', getAuditDateBoundary(params.dateFrom, false))
  }
  if (params.dateTo) {
    searchParams.set('dateTo', getAuditDateBoundary(params.dateTo, true))
  }
  for (const key of ['action', 'search', 'limit', 'section', 'actionKind', 'entityType', 'actorUserId', 'quickFilter', 'relatedGarage', 'relatedAccountingMonth', 'relatedCounterparty', 'relatedDocument'] as const) {
    const value = params[key]
    if (value) searchParams.set(key, String(value))
  }
  if (params.offset !== undefined) searchParams.set('offset', String(params.offset))
  return searchParams.toString()
}

export const auditApi: AuditClient = {
  getEvents(accessToken, params, signal) {
    const query = buildQuery(params)
    return requestJson(accessToken, `/api/audit/events${query ? `?${query}` : ''}`, signal)
  },
  getEventsPage(accessToken, params, signal) {
    const query = buildQuery(params)
    return requestJson(accessToken, `/api/audit/events/page${query ? `?${query}` : ''}`, signal)
  },
  getEvent(accessToken, id, signal) {
    return requestJson(accessToken, `/api/audit/events/${encodeURIComponent(id)}`, signal)
  },
  exportEvents(accessToken, params) {
    const query = buildQuery(params)
    return requestBlob(accessToken, `/api/audit/events/export${query ? `?${query}` : ''}`)
  },
  exportEventsXlsx(accessToken, params) {
    const query = buildQuery(params)
    return requestBlob(accessToken, `/api/audit/events/export/xlsx${query ? `?${query}` : ''}`)
  },
}
