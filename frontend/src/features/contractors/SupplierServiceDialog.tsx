import { useState, type FormEvent } from 'react'
import { LoaderCircle, Save, X } from 'lucide-react'
import type { SupplierServiceDto, UpsertSupplierServiceRequest } from '../../services/dictionariesApi'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { SelectControl } from '../../shared/SelectControl'
import { useEscapeKey, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'

export function SupplierServiceDialog({ edit = false, services, onClose, onSave }: {
  edit?: boolean
  services: SupplierServiceDto[]
  onClose: () => void
  onSave: (request: UpsertSupplierServiceRequest, id?: string) => Promise<void>
}) {
  const [selectedId, setSelectedId] = useState('')
  const [name, setName] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(true)
  useEscapeKey(!saving, onClose)
  const selected = services.find((service) => service.id === selectedId && !service.isArchived)

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (saving) return
    if (!name.trim() || name.trim().length > 200 || (edit && !selected)) {
      setError(edit && !selected ? 'Выберите услугу для изменения.' : 'Введите наименование услуги длиной до 200 символов.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      await onSave({ name: name.trim(), ...(edit ? { version: selected!.version } : {}) }, edit ? selected!.id : undefined)
      onClose()
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Не удалось сохранить услугу поставщика.')
      setSaving(false)
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
      <section ref={dialogRef} className={`detail-dialog contractors-dialog${edit ? ' supplier-service-edit-dialog' : ''}`} role="dialog" aria-modal="true" aria-labelledby="supplier-service-dialog-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="detail-dialog-header">
          <h3 id="supplier-service-dialog-title">{edit ? 'Изменить услугу поставщика' : 'Новая услуга поставщика'}</h3>
          <button className="icon-button" type="button" aria-label="Закрыть форму услуги" disabled={saving} onClick={onClose}><X size={18} /></button>
        </div>
        <form className="dictionary-modal-form" onSubmit={submit} noValidate>
          {error ? <FormError>{error}</FormError> : null}
          {edit ? <FormField label="Услуга для изменения">
            <SelectControl aria-label="Услуга для изменения" value={selectedId} required disabled={saving} options={[{ value: '', label: 'Выберите услугу' }, ...services.filter((service) => !service.isArchived).map((service) => ({ value: service.id, label: service.name }))]} onChange={(id) => {
              setSelectedId(id)
              setName(services.find((service) => service.id === id)?.name ?? '')
              setError(null)
            }} />
          </FormField> : null}
          <FormField label="Наименование услуги">
            <input aria-label="Наименование услуги" value={name} maxLength={200} required disabled={saving} onChange={(event) => setName(event.target.value)} />
          </FormField>
          <div className="detail-dialog-actions contractors-dialog-actions">
            <button className="ghost-button" type="button" disabled={saving} onClick={onClose}>Отмена</button>
            <button className="secondary-button" type="submit" disabled={saving} aria-busy={saving}>
              {saving ? <LoaderCircle className="financial-report-button__spinner" size={17} aria-hidden="true" /> : <Save size={17} aria-hidden="true" />}
              <span>{saving ? 'Сохраняем...' : 'Сохранить'}</span>
            </button>
          </div>
          {saving ? <span className="sr-only" role="status" aria-live="polite">Сохраняем услугу поставщика</span> : null}
        </form>
      </section>
    </div>
  )
}
