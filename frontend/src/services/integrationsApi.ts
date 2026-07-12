export type OneCFreshIntegrationStatusDto = {
  provider: string
  displayName: string
  isConfigured: boolean
  canSynchronize: boolean
  status: string
  statusMessage: string
  requiredSettings: string[]
  configuredSettings: string[]
  lastProtectedSettingUpdatedAtUtc: string | null
}

export type OneCFreshSyncRequest = {
  comment?: string | null
}

export type OneCFreshSyncPreviewCountDto = {
  objectType: string
  operation: string
  count: number
}

export type OneCFreshSyncPreviewNoticeDto = {
  code: string
  message: string
}

export type OneCFreshSyncPreviewDto = {
  auditEventId: string
  provider: string
  mode: string
  direction: string
  status: string
  statusMessage: string
  requestedAtUtc: string
  periodSummary: string
  snapshotHash: string
  canApply: boolean
  counts: OneCFreshSyncPreviewCountDto[]
  warnings: OneCFreshSyncPreviewNoticeDto[]
  conflicts: OneCFreshSyncPreviewNoticeDto[]
}

export type OneCFreshSyncDto = {
  auditEventId: string
  provider: string
  status: string
  statusMessage: string
  requestedAtUtc: string
  isRetry: boolean
  canRetry: boolean
  hasConflict: boolean
  errorCode: string | null
  externalRunId: string | null
  recoveryAction: string | null
}

export type ReceiptPrintingIntegrationStatusDto = {
  provider: string
  displayName: string
  isConfigured: boolean
  canPrint: boolean
  status: string
  statusMessage: string
  requiredSettings: string[]
  configuredSettings: string[]
  plannedActions: string[]
  lastProtectedSettingUpdatedAtUtc: string | null
}

export type ReceiptPrintingActionKind = 'print' | 'cancel' | 'reprint'

export type ReceiptPrintingActionRequest = {
  action: ReceiptPrintingActionKind
  reason?: string | null
}

export type ReceiptPrintingActionDto = {
  auditEventId: string
  financialOperationId: string
  action: ReceiptPrintingActionKind
  status: string
  statusMessage: string
  documentNumber: string | null
  isCopy: boolean
  copyMark: string | null
  registeredAtUtc: string
}

export type IntegrationSecretSettingDto = {
  id: string
  provider: string
  settingKey: string
  purpose: string
  updatedAtUtc: string
  updatedByUserId: string | null
  hasProtectedValue: boolean
}

export type IntegrationClient = {
  getOneCFreshStatus(accessToken: string): Promise<OneCFreshIntegrationStatusDto>
  previewOneCFreshSync(accessToken: string, request: OneCFreshSyncRequest): Promise<OneCFreshSyncPreviewDto>
  startOneCFreshSync(accessToken: string, request: OneCFreshSyncRequest): Promise<OneCFreshSyncDto>
  retryOneCFreshSync(accessToken: string, request: OneCFreshSyncRequest): Promise<OneCFreshSyncDto>
  getReceiptPrintingStatus(accessToken: string): Promise<ReceiptPrintingIntegrationStatusDto>
  registerReceiptPrintingAction(accessToken: string, operationId: string, request: ReceiptPrintingActionRequest): Promise<ReceiptPrintingActionDto>
  updateProtectedSetting(accessToken: string, provider: string, settingKey: string, plaintextValue: string): Promise<IntegrationSecretSettingDto>
}

export const integrationsApi: IntegrationClient = {
  getOneCFreshStatus(accessToken) {
    return requestJson<OneCFreshIntegrationStatusDto>(accessToken, '/api/integrations/one-c-fresh/status')
  },
  previewOneCFreshSync(accessToken, request) {
    return requestJson<OneCFreshSyncPreviewDto>(accessToken, '/api/integrations/one-c-fresh/sync-runs/preview', {
      method: 'POST',
      body: JSON.stringify(request),
    })
  },
  startOneCFreshSync(accessToken, request) {
    return requestJson<OneCFreshSyncDto>(accessToken, '/api/integrations/one-c-fresh/sync-runs', {
      method: 'POST',
      body: JSON.stringify(request),
    })
  },
  retryOneCFreshSync(accessToken, request) {
    return requestJson<OneCFreshSyncDto>(accessToken, '/api/integrations/one-c-fresh/sync-runs/retry', {
      method: 'POST',
      body: JSON.stringify(request),
    })
  },
  getReceiptPrintingStatus(accessToken) {
    return requestJson<ReceiptPrintingIntegrationStatusDto>(accessToken, '/api/integrations/receipt-printing/status')
  },
  registerReceiptPrintingAction(accessToken, operationId, request) {
    return requestJson<ReceiptPrintingActionDto>(
      accessToken,
      `/api/integrations/receipt-printing/operations/${encodeURIComponent(operationId)}/actions`,
      {
        method: 'POST',
        body: JSON.stringify(request),
      },
    )
  },
  updateProtectedSetting(accessToken, provider, settingKey, plaintextValue) {
    return requestJson<IntegrationSecretSettingDto>(
      accessToken,
      `/api/integrations/settings/${encodeURIComponent(provider)}/${encodeURIComponent(settingKey)}`,
      {
        method: 'PUT',
        body: JSON.stringify({ plaintextValue }),
      },
    )
  },
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string, init: RequestInit = {}): Promise<TResponse> {
  const headers = new Headers(init.headers)
  headers.set('Authorization', `Bearer ${accessToken}`)
  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers,
  })

  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || 'Не удалось получить данные интеграции.')
  }

  return response.json() as Promise<TResponse>
}
