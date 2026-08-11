import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react'

export type ToastKind = 'success' | 'error' | 'info'

export type ToastMessage = {
  id: number
  text: string
  kind: ToastKind
  title?: string
}

export function ToastViewport({ toast, onDismiss }: { toast: ToastMessage | null; onDismiss: () => void }) {
  if (!toast) return null

  const Icon = toast.kind === 'success' ? CheckCircle2 : toast.kind === 'error' ? AlertCircle : Info
  const title = toast.title ?? (toast.kind === 'success' ? 'Готово' : toast.kind === 'error' ? 'Ошибка' : 'Информация')

  return (
    <div className="toast-viewport" aria-label="Уведомления">
      <div className={`toast-message toast-message--${toast.kind}`} role={toast.kind === 'error' ? 'alert' : 'status'} aria-live={toast.kind === 'error' ? 'assertive' : 'polite'}>
        <Icon className="toast-message__icon" size={20} aria-hidden="true" />
        <div className="toast-message__content">
          <strong>{title}</strong>
          <span>{toast.text}</span>
        </div>
        <button className="toast-message__close" type="button" aria-label="Закрыть уведомление" onClick={onDismiss}>
          <X size={16} aria-hidden="true" />
        </button>
      </div>
    </div>
  )
}
