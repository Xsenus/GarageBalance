export type AccessImportCheckDto = {
  code: string
  title: string
  status: 'passed' | 'warning' | 'error'
  message: string
}

export type AccessImportRunDto = {
  id: string
  mode: string
  status: 'completed' | 'blocked' | 'rollback_requested' | 'import_requested' | 'import_request_cancelled'
  originalFileName: string
  fileExtension: string
  fileSizeBytes: number
  contentSha256: string
  startedAtUtc: string
  finishedAtUtc: string | null
  totalChecks: number
  passedChecks: number
  warningCount: number
  errorCount: number
  summary: string
  checks: AccessImportCheckDto[]
}

export type AccessImportQuarantineItemDto = {
  id: string
  accessImportRunId: string | null
  sourceSystem: string
  entityType: string
  externalId: string | null
  rowHash: string
  reasonCode: string
  reasonMessage: string
  severity: 'error' | 'warning'
  status: 'open' | 'resolved'
  createdAtUtc: string
  createdByUserId: string | null
  resolvedAtUtc: string | null
  resolvedByUserId: string | null
  resolutionComment: string | null
}

export type AccessImportRunLogEntryDto = {
  id: string
  accessImportRunId: string
  createdAtUtc: string
  level: 'info' | 'warning' | 'error'
  stepCode: string
  message: string
}

export type AccessImportCreatedRecordDto = {
  id: string
  accessImportRunId: string
  sourceSystem: string
  sourceEntityType: string
  sourceExternalId: string | null
  sourceRowHash: string
  targetEntityType: string
  targetEntityId: string
  targetDisplayName: string | null
  rollbackStatus: 'created' | 'rollback_requested' | 'rolled_back' | 'rollback_failed'
  createdAtUtc: string
  createdByUserId: string | null
  rolledBackAtUtc: string | null
  rolledBackByUserId: string | null
  rollbackReason: string | null
}

export type ImportClient = {
  getAccessRuns(accessToken: string, limit?: number): Promise<AccessImportRunDto[]>
  getAccessRunLog(accessToken: string, runId: string, limit?: number): Promise<AccessImportRunLogEntryDto[]>
  getAccessCreatedRecords(accessToken: string, runId: string, limit?: number): Promise<AccessImportCreatedRecordDto[]>
  getOpenQuarantineItems(accessToken: string, accessImportRunId?: string, limit?: number): Promise<AccessImportQuarantineItemDto[]>
  dryRunAccess(accessToken: string, file: File): Promise<AccessImportRunDto>
  downloadAccessRunReport(accessToken: string, runId: string): Promise<Blob>
  requestAccessImportApply(accessToken: string, runId: string, reason: string, backupConfirmed: boolean): Promise<AccessImportRunDto>
  cancelAccessImportApplyRequest(accessToken: string, runId: string, reason: string): Promise<AccessImportRunDto>
  requestAccessImportRollback(accessToken: string, runId: string, reason: string): Promise<AccessImportRunDto>
  resolveQuarantineItem(accessToken: string, itemId: string, resolutionComment?: string): Promise<AccessImportQuarantineItemDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось выполнить импорт.')
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
    throw new Error(problem?.detail ?? 'Не удалось скачать отчет импорта.')
  }

  return response.blob()
}

export const importApi: ImportClient = {
  getAccessRuns(accessToken, limit = 50) {
    return requestJson(accessToken, `/api/import/access/runs?limit=${encodeURIComponent(limit)}`)
  },
  getAccessRunLog(accessToken, runId, limit = 100) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/log?limit=${encodeURIComponent(limit)}`)
  },
  getAccessCreatedRecords(accessToken, runId, limit = 100) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/created-records?limit=${encodeURIComponent(limit)}`)
  },
  getOpenQuarantineItems(accessToken, accessImportRunId, limit = 50) {
    const params = new URLSearchParams()
    if (accessImportRunId) {
      params.set('accessImportRunId', accessImportRunId)
    }
    params.set('limit', String(limit))
    return requestJson(accessToken, `/api/import/access/quarantine?${params.toString()}`)
  },
  dryRunAccess(accessToken, file) {
    const formData = new FormData()
    formData.append('file', file)
    return requestJson(accessToken, '/api/import/access/dry-run', { method: 'POST', body: formData })
  },
  downloadAccessRunReport(accessToken, runId) {
    return requestBlob(accessToken, `/api/import/access/runs/${runId}/report`, { method: 'POST' })
  },
  requestAccessImportApply(accessToken, runId, reason, backupConfirmed) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/apply`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason, backupConfirmed }),
    })
  },
  cancelAccessImportApplyRequest(accessToken, runId, reason) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/apply/cancel`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason }),
    })
  },
  requestAccessImportRollback(accessToken, runId, reason) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/rollback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ reason }),
    })
  },
  resolveQuarantineItem(accessToken, itemId, resolutionComment) {
    return requestJson(accessToken, `/api/import/access/quarantine/${itemId}/resolve`, {
      method: 'PATCH',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ resolutionComment }),
    })
  },
}
