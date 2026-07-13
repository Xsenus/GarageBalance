import { useId, useState } from 'react'
import { FileText, Trash2, X } from 'lucide-react'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from './focusHooks'

export type DictionaryListItem = {
  id: string
  title: string
  meta: string
  isActive?: boolean
  activeLabel?: string
  openLabel?: string
  onOpen?: () => void
  archiveLabel?: string
  onArchive?: (reason: string) => Promise<void> | void
}

export function DictionaryList({ items, emptyText }: { items: DictionaryListItem[]; emptyText: string }) {
  const [pendingArchive, setPendingArchive] = useState<DictionaryListItem | null>(null)
  const [confirmingArchive, setConfirmingArchive] = useState(false)
  const [archiveReason, setArchiveReason] = useState('')
  const [archiveReasonError, setArchiveReasonError] = useState<string | null>(null)
  const [showAllItems, setShowAllItems] = useState(false)
  const listId = useId()
  const compactLimit = 5
  const visibleItems = showAllItems ? items : items.slice(0, compactLimit)
  const hasHiddenItems = items.length > compactLimit
  useRestoreFocusOnClose(Boolean(pendingArchive))
  const archiveCancelButtonRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingArchive) && !confirmingArchive)
  const archiveDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingArchive))

  useEscapeKey(Boolean(pendingArchive) && !confirmingArchive, () => closeArchiveDialog())

  function openArchiveDialog(item: DictionaryListItem) {
    setArchiveReason('')
    setArchiveReasonError(null)
    setPendingArchive(item)
  }

  function closeArchiveDialog() {
    setPendingArchive(null)
    setArchiveReason('')
    setArchiveReasonError(null)
  }

  async function confirmArchive() {
    if (!pendingArchive?.onArchive) {
      return
    }

    const reason = archiveReason.trim()
    if (!reason) {
      setArchiveReasonError('Укажите причину архивирования записи.')
      return
    }

    setConfirmingArchive(true)
    try {
      await pendingArchive.onArchive(reason)
      closeArchiveDialog()
    } finally {
      setConfirmingArchive(false)
    }
  }

  if (items.length === 0) {
    return <p className="empty-state" role="status" aria-live="polite">{emptyText}</p>
  }

  return (
    <>
      <ul className="dictionary-list" id={listId}>
        {visibleItems.map((item) => (
          <li className={item.isActive ? 'is-active' : undefined} aria-current={item.isActive ? 'true' : undefined} key={item.id}>
            <span>
              <strong>
                {item.title}
                {item.isActive ? <span className="dictionary-state">{item.activeLabel ?? 'Открыто'}</span> : null}
              </strong>
              <span>{item.meta}</span>
            </span>
            <span className="dictionary-actions">
              {item.onOpen ? (
                <button className="icon-button" type="button" aria-label={item.openLabel ?? `Открыть ${item.title}`} onClick={item.onOpen} disabled={item.isActive} title={item.isActive ? 'Запись уже открыта' : undefined}>
                  <FileText size={16} />
                </button>
              ) : null}
              {item.onArchive ? (
                <button className="icon-button" type="button" aria-label={item.archiveLabel ?? `Архивировать ${item.title}`} onClick={() => openArchiveDialog(item)}>
                  <Trash2 size={16} />
                </button>
              ) : null}
            </span>
          </li>
        ))}
      </ul>
      {hasHiddenItems ? (
        <div className="dictionary-list-footer">
          <p className="empty-state" role="status" aria-live="polite">Показано {visibleItems.length} из {items.length} записей</p>
          <button className="ghost-button" type="button" aria-controls={listId} aria-expanded={showAllItems} onClick={() => setShowAllItems((value) => !value)}>
            {showAllItems ? 'Свернуть список' : 'Показать все записи'}
          </button>
        </div>
      ) : null}
      {pendingArchive ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => {
          if (!confirmingArchive) {
            closeArchiveDialog()
          }
        }}>
          <section ref={archiveDialogRef} className="detail-dialog" role="dialog" aria-modal="true" aria-labelledby={`archive-confirmation-${pendingArchive.id}`} aria-describedby={`archive-confirmation-description-${pendingArchive.id}`} onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Архивирование</p>
                <h3 id={`archive-confirmation-${pendingArchive.id}`}>Подтвердите архивирование</h3>
                <p>{pendingArchive.title}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить архивирование" onClick={() => closeArchiveDialog()} disabled={confirmingArchive}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id={`archive-confirmation-description-${pendingArchive.id}`}>Запись исчезнет из рабочих списков, но останется в истории изменений.</p>
            <label className="field-label" htmlFor={`archive-reason-${pendingArchive.id}`}>Причина архивирования</label>
            <textarea
              id={`archive-reason-${pendingArchive.id}`}
              aria-label="Причина архивирования"
              aria-invalid={Boolean(archiveReasonError)}
              aria-describedby={archiveReasonError ? `archive-reason-error-${pendingArchive.id}` : undefined}
              maxLength={1000}
              value={archiveReason}
              onChange={(event) => {
                setArchiveReason(event.target.value)
                if (archiveReasonError && event.target.value.trim()) {
                  setArchiveReasonError(null)
                }
              }}
              placeholder="Например: дубль, ошибочная запись, больше не используется"
              disabled={confirmingArchive}
              required
            />
            {archiveReasonError ? <p className="form-error" id={`archive-reason-error-${pendingArchive.id}`}>{archiveReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={archiveCancelButtonRef} className="ghost-button" type="button" onClick={() => closeArchiveDialog()} disabled={confirmingArchive}>
                Отменить
              </button>
              <button className="secondary-button danger-button" type="button" onClick={() => void confirmArchive()} disabled={confirmingArchive || !archiveReason.trim()}>
                <Trash2 size={16} />
                <span>{confirmingArchive ? 'Архивируем...' : 'Архивировать запись'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </>
  )
}
