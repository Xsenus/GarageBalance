export type AuditEventDto = {
  id: string
  createdAtUtc: string
  actorUserId: string | null
  action: string
  entityType: string
  entityId: string | null
  summary: string
}

export type AuditEventQuery = { dateFrom?: string; dateTo?: string; action?: string; search?: string; limit?: number; section?: string; actionKind?: string; entityType?: string; actorUserId?: string; quickFilter?: string }

export type AuditClient = {
  getEvents(accessToken: string, params?: AuditEventQuery): Promise<AuditEventDto[]>
  exportEvents(accessToken: string, params?: AuditEventQuery): Promise<Blob>
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
    throw new Error(problem?.detail ?? 'Не удалось загрузить audit-журнал.')
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
    throw new Error(problem?.detail ?? 'Не удалось скачать audit-журнал.')
  }

  return response.blob()
}

function buildQuery(params: AuditEventQuery = {}) {
  const searchParams = new URLSearchParams()
  if (params.dateFrom) {
    searchParams.set('dateFrom', params.dateFrom)
  }
  if (params.dateTo) {
    searchParams.set('dateTo', params.dateTo)
  }
  if (params.action) {
    searchParams.set('action', params.action)
  }
  if (params.search) {
    searchParams.set('search', params.search)
  }
  if (params.limit) {
    searchParams.set('limit', String(params.limit))
  }
  if (params.section) {
    searchParams.set('section', params.section)
  }
  if (params.actionKind) {
    searchParams.set('actionKind', params.actionKind)
  }
  if (params.entityType) {
    searchParams.set('entityType', params.entityType)
  }
  if (params.actorUserId) {
    searchParams.set('actorUserId', params.actorUserId)
  }
  if (params.quickFilter) {
    searchParams.set('quickFilter', params.quickFilter)
  }
  return searchParams.toString()
}

export const auditApi: AuditClient = {
  getEvents(accessToken, params) {
    const query = buildQuery(params)
    return requestJson(accessToken, `/api/audit/events${query ? `?${query}` : ''}`)
  },
  exportEvents(accessToken, params) {
    const query = buildQuery(params)
    return requestBlob(accessToken, `/api/audit/events/export${query ? `?${query}` : ''}`)
  },
}
