export type AuditEventDto = {
  id: string
  createdAtUtc: string
  actorUserId: string | null
  action: string
  entityType: string
  entityId: string | null
  summary: string
}

export type AuditClient = {
  getEvents(accessToken: string, params?: { dateFrom?: string; dateTo?: string; action?: string; search?: string }): Promise<AuditEventDto[]>
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
    throw new Error(problem?.detail ?? 'Не удалось загрузить audit-журнал.')
  }

  return response.json()
}

function buildQuery(params: Parameters<AuditClient['getEvents']>[1] = {}) {
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
  return searchParams.toString()
}

export const auditApi: AuditClient = {
  getEvents(accessToken, params) {
    const query = buildQuery(params)
    return requestJson(accessToken, `/api/audit/events${query ? `?${query}` : ''}`)
  },
}
