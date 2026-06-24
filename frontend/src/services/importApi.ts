export type AccessImportCheckDto = {
  code: string
  title: string
  status: 'passed' | 'warning' | 'error'
  message: string
}

export type AccessImportRunDto = {
  id: string
  mode: string
  status: 'completed' | 'blocked'
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

export type ImportClient = {
  getAccessRuns(accessToken: string): Promise<AccessImportRunDto[]>
  getAccessRunLog(accessToken: string, runId: string): Promise<AccessImportRunLogEntryDto[]>
  getOpenQuarantineItems(accessToken: string, accessImportRunId?: string): Promise<AccessImportQuarantineItemDto[]>
  dryRunAccess(accessToken: string, file: File): Promise<AccessImportRunDto>
  downloadAccessRunReport(accessToken: string, runId: string): Promise<Blob>
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

async function requestBlob(accessToken: string, path: string): Promise<Blob> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось скачать отчет импорта.')
  }

  return response.blob()
}

export const importApi: ImportClient = {
  getAccessRuns(accessToken) {
    return requestJson(accessToken, '/api/import/access/runs')
  },
  getAccessRunLog(accessToken, runId) {
    return requestJson(accessToken, `/api/import/access/runs/${runId}/log`)
  },
  getOpenQuarantineItems(accessToken, accessImportRunId) {
    const query = accessImportRunId ? `?accessImportRunId=${encodeURIComponent(accessImportRunId)}` : ''
    return requestJson(accessToken, `/api/import/access/quarantine${query}`)
  },
  dryRunAccess(accessToken, file) {
    const formData = new FormData()
    formData.append('file', file)
    return requestJson(accessToken, '/api/import/access/dry-run', { method: 'POST', body: formData })
  },
  downloadAccessRunReport(accessToken, runId) {
    return requestBlob(accessToken, `/api/import/access/runs/${runId}/report`)
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
