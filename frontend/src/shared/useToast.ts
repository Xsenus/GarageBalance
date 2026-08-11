import { useCallback, useEffect, useState } from 'react'
import type { ToastKind, ToastMessage } from './Toast'

export function useToast(autoDismissMilliseconds = 4200) {
  const [toast, setToast] = useState<ToastMessage | null>(null)

  const showToast = useCallback((text: string, kind: ToastKind = 'success', title?: string) => {
    setToast({ id: Date.now(), text, kind, title })
  }, [])

  const dismissToast = useCallback(() => setToast(null), [])

  useEffect(() => {
    if (!toast || autoDismissMilliseconds <= 0) return undefined
    const timeoutId = window.setTimeout(dismissToast, autoDismissMilliseconds)
    return () => window.clearTimeout(timeoutId)
  }, [autoDismissMilliseconds, dismissToast, toast])

  return { toast, showToast, dismissToast }
}
