export type ClientErrorReport = {
  clientErrorId: string
  errorName: string
  message: string
  componentStack: string | null
  route: string | null
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export async function reportClientError(accessToken: string, report: ClientErrorReport): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/diagnostics/client-errors`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
    body: JSON.stringify(report),
  })
  if (!response.ok) {
    throw new Error('Не удалось передать диагностический отчет.')
  }
}

export function createClientErrorId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }
  return `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

export const diagnosticsApi = { reportClientError }
