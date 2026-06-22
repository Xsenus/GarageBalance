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

export type ImportClient = {
  getAccessRuns(accessToken: string): Promise<AccessImportRunDto[]>
  dryRunAccess(accessToken: string, file: File): Promise<AccessImportRunDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080'

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

export const importApi: ImportClient = {
  getAccessRuns(accessToken) {
    return requestJson(accessToken, '/api/import/access/runs')
  },
  dryRunAccess(accessToken, file) {
    const formData = new FormData()
    formData.append('file', file)
    return requestJson(accessToken, '/api/import/access/dry-run', { method: 'POST', body: formData })
  },
}
