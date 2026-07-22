import { useEffect } from 'react'

export function useSessionExpiration(expiresAtUtc: string | undefined, onExpired: () => void) {
  useEffect(() => {
    if (!expiresAtUtc) {
      return
    }

    const remainingMilliseconds = Date.parse(expiresAtUtc) - Date.now()
    const timeoutId = window.setTimeout(onExpired, Math.max(0, remainingMilliseconds))

    return () => window.clearTimeout(timeoutId)
  }, [expiresAtUtc, onExpired])
}
