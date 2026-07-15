export type PaymentDisplaySettingsDto = {
  showAllGarageOperationsByDefault: boolean
}

export type DatabaseBackupFileDto = {
  fileName: string
  sizeBytes: number
  createdAtUtc: string
  kind: 'manual' | 'automatic' | 'pre_update'
}

export type DatabaseBackupStatusDto = {
  enabled: boolean
  automaticEnabled: boolean
  intervalHours: number
  retentionCount: number
  directory: string
  isRunning: boolean
  lastSuccessfulBackupAtUtc: string | null
  lastError: string | null
  backups: DatabaseBackupFileDto[]
}

export type ApplicationSettingsClient = {
  getPaymentDisplaySettings(accessToken: string): Promise<PaymentDisplaySettingsDto>
  updatePaymentDisplaySettings(accessToken: string, request: PaymentDisplaySettingsDto): Promise<PaymentDisplaySettingsDto>
  getDatabaseBackups(accessToken: string): Promise<DatabaseBackupStatusDto>
  createDatabaseBackup(accessToken: string, request: { reason: string }): Promise<DatabaseBackupFileDto>
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string, init?: RequestInit): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? 'Не удалось загрузить настройки отображения.')
  }

  return response.json()
}

export const settingsApi: ApplicationSettingsClient = {
  getPaymentDisplaySettings(accessToken) {
    return requestJson(accessToken, '/api/settings/payments/display')
  },
  updatePaymentDisplaySettings(accessToken, request) {
    return requestJson(accessToken, '/api/settings/payments/display', { method: 'PUT', body: JSON.stringify(request) })
  },
  getDatabaseBackups(accessToken) {
    return requestJson(accessToken, '/api/settings/backups')
  },
  createDatabaseBackup(accessToken, request) {
    return requestJson(accessToken, '/api/settings/backups', { method: 'POST', body: JSON.stringify(request) })
  },
}
