import { useEffect } from 'react'
import { createClientErrorId, diagnosticsApi } from '../services/diagnosticsApi'

export function useClientErrorReporting(accessToken: string | null) {
  useEffect(() => {
    if (!accessToken) return undefined

    const report = (error: unknown, source: string) => {
      const normalized = error instanceof Error ? error : new Error(typeof error === 'string' ? error : 'Неизвестная ошибка интерфейса.')
      void diagnosticsApi.reportClientError(accessToken, {
        clientErrorId: createClientErrorId(),
        errorName: normalized.name || source,
        message: 'Ошибка интерфейса; подробности определяются по коду и стеку вызовов.',
        componentStack: normalized.stack ?? null,
        route: window.location.pathname,
      }).catch(() => undefined)
    }
    const handleWindowError = (event: ErrorEvent) => report(event.error ?? event.message, 'WindowError')
    const handleUnhandledRejection = (event: PromiseRejectionEvent) => report(event.reason, 'UnhandledPromiseRejection')
    window.addEventListener('error', handleWindowError)
    window.addEventListener('unhandledrejection', handleUnhandledRejection)
    return () => {
      window.removeEventListener('error', handleWindowError)
      window.removeEventListener('unhandledrejection', handleUnhandledRejection)
    }
  }, [accessToken])
}
