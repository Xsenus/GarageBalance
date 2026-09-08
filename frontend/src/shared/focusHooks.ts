import { useEffect, useEffectEvent, useRef } from 'react'
import type { Dispatch, KeyboardEvent, SetStateAction } from 'react'

export function useCloseOnOutsidePointer<TElement extends HTMLElement>(enabled: boolean, setOpen: Dispatch<SetStateAction<boolean>>) {
  const ref = useRef<TElement | null>(null)

  useEffect(() => {
    if (!enabled) return
    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', closeOnOutsidePointer, true)
    return () => document.removeEventListener('pointerdown', closeOnOutsidePointer, true)
  }, [enabled, setOpen])

  return ref
}

export function useDismissOnWindowClick<TValue>(enabled: boolean, setValue: Dispatch<SetStateAction<TValue | null>>) {
  useEffect(() => {
    if (!enabled) return
    const dismiss = () => setValue(null)
    window.addEventListener('click', dismiss)
    return () => window.removeEventListener('click', dismiss)
  }, [enabled, setValue])
}

export function useEscapeKey(enabled: boolean, onEscape: () => void) {
  const handleEscape = useEffectEvent(onEscape)

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        handleEscape()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [enabled])
}

export function useFocusOnOpen<TElement extends HTMLElement>(enabled: boolean) {
  const ref = useRef<TElement | null>(null)

  useEffect(() => {
    if (enabled) {
      ref.current?.focus()
    }
  }, [enabled])

  return ref
}

export function focusAfterDomUpdate(trigger: HTMLElement | null) {
  window.setTimeout(() => {
    if (trigger?.isConnected) trigger.focus()
  }, 0)
}

export function fitContextMenuToViewport(x: number, y: number, width = 200, height = 190) {
  const margin = 8
  return {
    x: Math.max(margin, Math.min(x, window.innerWidth - width - margin)),
    y: Math.max(margin, Math.min(y, window.innerHeight - height - margin)),
  }
}

export function handleMenuArrowNavigation(event: KeyboardEvent<HTMLElement>) {
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
  const items = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)'))
  if (items.length === 0) return
  event.preventDefault()
  const current = items.indexOf(document.activeElement as HTMLButtonElement)
  const index = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : event.key === 'ArrowDown' ? (current + 1) % items.length : (current <= 0 ? items.length : current) - 1
  items[index]?.focus()
}

export function restoreFocusAfterClose<TElement extends HTMLElement>(triggerRef: { current: TElement | null }) {
  focusAfterDomUpdate(triggerRef.current)
  triggerRef.current = null
}

export function useRestoreFocusOnClose(enabled: boolean) {
  const previousFocusRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null

    return () => {
      const previousFocus = previousFocusRef.current
      previousFocusRef.current = null
      if (previousFocus?.isConnected) {
        previousFocus.focus()
      }
    }
  }, [enabled])
}

export function useFocusTrap<TElement extends HTMLElement>(enabled: boolean) {
  const ref = useRef<TElement | null>(null)

  useEffect(() => {
    if (!enabled) {
      return undefined
    }

    function getFocusableElements() {
      const container = ref.current
      if (!container) {
        return []
      }

      return Array.from(
        container.querySelectorAll<HTMLElement>('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'),
      )
    }

    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key !== 'Tab') {
        return
      }

      const focusableElements = getFocusableElements()
      if (focusableElements.length === 0) {
        event.preventDefault()
        return
      }

      const firstElement = focusableElements[0]
      const lastElement = focusableElements[focusableElements.length - 1]

      if (event.shiftKey && document.activeElement === firstElement) {
        event.preventDefault()
        lastElement.focus()
        return
      }

      if (!event.shiftKey && document.activeElement === lastElement) {
        event.preventDefault()
        firstElement.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [enabled])

  return ref
}
