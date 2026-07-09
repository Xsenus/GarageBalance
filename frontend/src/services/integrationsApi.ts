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

export type IntegrationClient = {
  getOneCFreshStatus(accessToken: string): Promise<OneCFreshIntegrationStatusDto>
  getReceiptPrintingStatus(accessToken: string): Promise<ReceiptPrintingIntegrationStatusDto>
}

export const integrationsApi: IntegrationClient = {
  getOneCFreshStatus(accessToken) {
    return requestJson<OneCFreshIntegrationStatusDto>(accessToken, '/api/integrations/one-c-fresh/status')
  },
  getReceiptPrintingStatus(accessToken) {
    return requestJson<ReceiptPrintingIntegrationStatusDto>(accessToken, '/api/integrations/receipt-printing/status')
  },
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

async function requestJson<TResponse>(accessToken: string, path: string): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })

  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || 'Не удалось получить данные интеграции.')
  }

  return response.json() as Promise<TResponse>
}
