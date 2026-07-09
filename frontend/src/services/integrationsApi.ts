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

export type OneCFreshSyncDto = {
  auditEventId: string
  provider: string
  status: string
  statusMessage: string
  requestedAtUtc: string
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
  registeredAtUtc: string
}

export type IntegrationClient = {
  getOneCFreshStatus(accessToken: string): Promise<OneCFreshIntegrationStatusDto>
  startOneCFreshSync(accessToken: string, request: OneCFreshSyncRequest): Promise<OneCFreshSyncDto>
  getReceiptPrintingStatus(accessToken: string): Promise<ReceiptPrintingIntegrationStatusDto>
  registerReceiptPrintingAction(accessToken: string, operationId: string, request: ReceiptPrintingActionRequest): Promise<ReceiptPrintingActionDto>
}

export const integrationsApi: IntegrationClient = {
  getOneCFreshStatus(accessToken) {
    return requestJson<OneCFreshIntegrationStatusDto>(accessToken, '/api/integrations/one-c-fresh/status')
  },
  startOneCFreshSync(accessToken, request) {
    return requestJson<OneCFreshSyncDto>(accessToken, '/api/integrations/one-c-fresh/sync-runs', {
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
