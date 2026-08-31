import { useLayoutEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'

const modalDialogSelector = '[aria-modal="true"][role="dialog"], [aria-modal="true"][role="alertdialog"]'

function getForegroundDialog() {
  const dialogs = document.querySelectorAll<HTMLElement>(modalDialogSelector)
  return dialogs.item(dialogs.length - 1)
}

function insertErrorHost(dialog: HTMLElement, host: HTMLElement) {
  const header = Array.from(dialog.children).find((child) => child.classList.contains('detail-dialog-header'))
  if (header) {
    header.insertAdjacentElement('afterend', host)
  } else {
    dialog.prepend(host)
  }
}

/**
 * Keeps an error on its natural place unless another modal dialog is above it.
 * In that case the whole error surface (including retry controls) is moved into
 * the foreground dialog so it cannot be hidden by the modal backdrop.
 */
export function ForegroundDialogError({ children }: { children: ReactNode }) {
  const surfaceRef = useRef<HTMLDivElement>(null)
  const hostRef = useRef<HTMLDivElement>(null)
  const [portalHost, setPortalHost] = useState<HTMLDivElement | null>(null)

  useLayoutEffect(() => {
    const synchronize = () => {
      const foregroundDialog = getForegroundDialog()
      const currentHost = hostRef.current
      const surface = surfaceRef.current
      const ownAlert = surface?.querySelector<HTMLElement>('[role="alert"]')
      const ownAlertText = ownAlert?.textContent?.trim()
      const equivalentAlertExists = Boolean(foregroundDialog && ownAlertText && Array.from(
        foregroundDialog.querySelectorAll<HTMLElement>('[role="alert"]'),
      ).some((alert) => !currentHost?.contains(alert) && alert.textContent?.trim() === ownAlertText))

      if (currentHost?.isConnected && foregroundDialog?.contains(currentHost) && !equivalentAlertExists) {
        return
      }

      if (currentHost) {
        hostRef.current = null
        currentHost.remove()
      }

      if (!foregroundDialog || equivalentAlertExists || surface?.closest(modalDialogSelector) === foregroundDialog) {
        setPortalHost(null)
        return
      }

      const host = document.createElement('div')
      host.className = 'foreground-dialog-error-host'
      insertErrorHost(foregroundDialog, host)
      hostRef.current = host
      setPortalHost(host)
    }

    synchronize()
    const observer = new MutationObserver(synchronize)
    observer.observe(document.body, { childList: true, subtree: true })

    return () => {
      observer.disconnect()
      hostRef.current?.remove()
      hostRef.current = null
    }
  }, [])

  const surface = <div ref={surfaceRef} className="foreground-dialog-error-surface">{children}</div>
  return portalHost ? createPortal(surface, portalHost) : surface
}

export function FormError({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <ForegroundDialogError>
      <div className="form-error" id={id} role="alert">
        {children}
      </div>
    </ForegroundDialogError>
  )
}

export function FormValidationSummary({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <ForegroundDialogError>
      <div className="form-error validation-summary" role="alert" aria-label={title}>
        <strong>{title}</strong>
        <ul>
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </div>
    </ForegroundDialogError>
  )
}
