import { useCallback, useEffect, useRef } from 'react'
import { scheduleDelayedAction } from './debouncedRequest'

export const intentPreloadDelayMs = 120

export function useIntentPreload<T>(preload: (target: T) => void, delay = intentPreloadDelayMs) {
  const cancelRef = useRef<(() => void) | null>(null)
  const cancel = useCallback(() => {
    cancelRef.current?.()
    cancelRef.current = null
  }, [])
  const schedule = useCallback((target: T) => {
    cancel()
    cancelRef.current = scheduleDelayedAction(() => {
      cancelRef.current = null
      preload(target)
    }, delay)
  }, [cancel, delay, preload])
  const runNow = useCallback((target: T) => {
    cancel()
    preload(target)
  }, [cancel, preload])

  useEffect(() => cancel, [cancel])
  return { schedule, cancel, runNow }
}
