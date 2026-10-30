import { useEffect, useRef, useState } from 'react'
import type { AuthResponse } from '../../services/authApi'
import type { FinanceClient, MeterDeviceReplacementDetailsDto } from '../../services/financeApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'
import { AsyncErrorState, TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { formatDateOnly, formatNullableNumber, getLocalDateInputValue } from '../../shared/formatters'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { parseReplacementValue } from '../meterReadings/meterReplacementConsumption'

export function MeterReplacementDetailsCard({ auth, financeClient, garageId, deviceId, onClose, onSaved, onBusyChange }: {
  auth: AuthResponse; financeClient: FinanceClient; garageId: string; deviceId: string; onClose: () => void; onSaved: () => void; onBusyChange: (busy: boolean) => void
}) {
  const [required] = useActionCommentSettings()
  const [retry, setRetry] = useState(0)
  const [result, setResult] = useState<{ key: string; data?: MeterDeviceReplacementDetailsDto; error?: string } | null>(null)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState({ date: '', serial: '', final: '', initial: '', reason: '' })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const submitting = useRef(false)
  const key = JSON.stringify([auth.accessToken, garageId, deviceId, retry])
  const current = result?.key === key ? result : null
  const data = current?.data
  const canRead = hasPermission(auth, permissions.paymentsRead)
  const currentMonth = getLocalDateInputValue().slice(0, 7)
  const canEdit = hasPermission(auth, permissions.paymentsWrite) && data && !data.device.isCanceled && data.reading &&
    (data.device.replacementAccountingMonth?.slice(0, 7) === currentMonth || hasPermission(auth, permissions.historicalMeterReadingsCorrect))
  useEffect(() => {
    if (!canRead) return
    const controller = new AbortController()
    async function load() {
      try {
        if (!financeClient.getMeterDeviceReplacement) throw new Error('Карточка замены недоступна.')
        const data = await financeClient.getMeterDeviceReplacement(auth.accessToken, deviceId, controller.signal)
        if (data.device.garageId !== garageId || data.device.id !== deviceId) throw new Error('Получена карточка другого прибора.')
        if (!controller.signal.aborted) setResult({ key, data })
      } catch (caught) {
        if (!controller.signal.aborted) setResult({ key, error: caught instanceof Error ? caught.message : 'Не удалось открыть карточку.' })
      }
    }
    void load()
    return () => controller.abort()
  }, [auth.accessToken, canRead, deviceId, financeClient, garageId, key])

  async function save() {
    if (!canEdit || !data?.reading || !financeClient.updateMeterDeviceReplacement || submitting.current) return
    const final = parseReplacementValue(draft.final)
    const initial = parseReplacementValue(draft.initial)
    if (!draft.date || !draft.serial.trim() || final === null || initial === null || required && !draft.reason.trim() || draft.reason.trim().length > 500) {
      setError('Проверьте дату, номер, оба показания и причину исправления.')
      return
    }
    submitting.current = true; setSaving(true); onBusyChange(true); setError(null)
    try {
      await financeClient.updateMeterDeviceReplacement(auth.accessToken, deviceId, {
        expectedDeviceVersion: data.device.version, expectedRemovedDeviceVersion: data.removedDevice.version,
        expectedReadingVersion: data.reading.version, accountingMonth: data.reading.accountingMonth,
        replacementDate: draft.date, newSerialNumber: draft.serial.trim(), removedDeviceFinalValue: final,
        newInitialValue: initial, reason: draft.reason.trim(),
      })
      setEditing(false); setRetry((value) => value + 1); onSaved()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить замену.')
      // A conflict must be reviewed using a newly opened card, never silently retried.
    } finally { submitting.current = false; setSaving(false); onBusyChange(false) }
  }

  return <section className="dictionary-form-section" aria-label="Карточка замены счётчика">
    <h4>Карточка замены счётчика</h4>
    {!canRead ? <FormError>Нет права просматривать показания.</FormError> : !current ? <TableLoadingState label="Открываем карточку замены" rows={3} columns={2} />
      : current.error ? <AsyncErrorState message={current.error} onRetry={() => setRetry((value) => value + 1)} /> : data ? <>
        <p>Замена {formatDateOnly(data.device.installedOn)}. Старый № {data.removedDevice.serialNumber}: конечное {formatNullableNumber(data.removedDevice.finalValue)}. Новый № {data.device.serialNumber}: начальное {formatNullableNumber(data.device.initialValue)}.</p>
        <p>Причина: {data.device.replacementReason || 'не указана'}. {data.device.isCanceled ? 'Замена отменена.' : ''}</p>
        {data.reading ? <p>Показание нового прибора за месяц: {formatNullableNumber(data.reading.currentValue)}. Расход: {formatNullableNumber(data.reading.consumption)} (снятые приборы {formatNullableNumber(data.reading.previousDeviceConsumption ?? 0)} + новый {formatNullableNumber(data.reading.currentValue - data.device.initialValue)}).</p> : <p>Показание уже относится к другой записи истории. Карточка доступна для просмотра.</p>}
        {!editing && canEdit ? <button type="button" className="ghost-button" onClick={() => {
          setDraft({ date: data.device.installedOn, serial: data.device.serialNumber, final: data.removedDevice.finalValue === null ? '' : String(data.removedDevice.finalValue), initial: String(data.device.initialValue), reason: '' })
          setError(null); setEditing(true)
        }}>Изменить замену</button> : null}
        {editing ? <form className="dictionary-modal-form" noValidate onSubmit={(event) => { event.preventDefault(); void save() }}>
          <p>Начисления и распределение зачётов пересчитаются. Внесённые денежные платежи сохранятся. Исправление останется в истории изменений.</p>
          <FormField label="Дата замены"><LocalizedDatePicker ariaLabel="Исправленная дата замены" mode="date" required value={draft.date} disabled={saving} onChange={(date) => setDraft({ ...draft, date })} /></FormField>
          <FormField label="Номер нового счётчика"><input aria-label="Исправленный номер счётчика" maxLength={100} required disabled={saving} value={draft.serial} onChange={(event) => setDraft({ ...draft, serial: event.target.value })} /></FormField>
          <FormField label="Конечное показание старого счётчика"><MeterReadingInput aria-label="Исправленное конечное показание" required disabled={saving} value={draft.final} onChange={(event) => setDraft({ ...draft, final: event.target.value })} /></FormField>
          <FormField label="Начальное показание нового счётчика"><MeterReadingInput aria-label="Исправленное начальное показание" required disabled={saving} value={draft.initial} onChange={(event) => setDraft({ ...draft, initial: event.target.value })} /></FormField>
          <FormField label="Причина исправления"><textarea aria-label="Причина исправления замены" maxLength={500} required={required} disabled={saving} value={draft.reason} onChange={(event) => setDraft({ ...draft, reason: event.target.value })} /></FormField>
          {error ? <FormError>{error}</FormError> : null}
          <button type="submit" className="secondary-button" disabled={saving || !canEdit} aria-busy={saving}>{saving ? 'Сохраняем…' : 'Сохранить исправление'}</button>
          <button type="button" className="ghost-button" disabled={saving} onClick={() => setEditing(false)}>Не изменять</button>
        </form> : null}
      </> : null}
    <button type="button" className="ghost-button" disabled={saving} onClick={onClose}>Закрыть карточку замены</button>
  </section>
}
