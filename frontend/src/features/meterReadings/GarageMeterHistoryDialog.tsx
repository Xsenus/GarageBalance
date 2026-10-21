import { useEffect, useRef, useState } from 'react'
import { LoaderCircle, RotateCcw, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { FinanceClient, FinancePagedResult, MeterDeviceDto } from '../../services/financeApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { formatDateOnly, formatNullableNumber } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { TablePagination } from '../../shared/TablePagination'
import { parseReplacementValue } from './meterReplacementConsumption'

export default function GarageMeterHistoryDialog({ auth, financeClient, garage, onClose, onChanged }: {
  auth: AuthResponse; financeClient: FinanceClient; garage: { id: string; number: string }
  onClose: () => void; onChanged: () => void
}) {
  const [reasonRequired] = useActionCommentSettings()
  const [query, setQuery] = useState({ offset: 0, limit: 25 })
  const [revision, setRevision] = useState(0)
  const [loaded, setLoaded] = useState<{ key: string; page?: FinancePagedResult<MeterDeviceDto>; error?: string } | null>(null)
  const [target, setTarget] = useState<MeterDeviceDto | null>(null)
  const [reason, setReason] = useState('')
  const [restoredValue, setRestoredValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const busy = useRef(false)
  const canRead = hasPermission(auth, permissions.paymentsRead)
  const canWrite = hasPermission(auth, permissions.paymentsWrite)
  const key = JSON.stringify([auth.accessToken, garage.id, query, revision])
  const result = loaded?.key === key ? loaded : null
  const page = result?.page
  const loading = canRead && !result
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(true)
  const closeRef = useFocusOnOpen<HTMLButtonElement>(true)
  useRestoreFocusOnClose(Boolean(target))
  const reasonRef = useFocusOnOpen<HTMLTextAreaElement>(Boolean(target))
  useEscapeKey(!saving, target ? () => setTarget(null) : onClose)
  useEffect(() => {
    if (!canRead) return
    const controller = new AbortController()
    async function load() {
      try {
        if (!financeClient.getMeterDeviceHistory) throw new Error('Не удалось загрузить данные.')
        const result = await financeClient.getMeterDeviceHistory(auth.accessToken, garage.id, query, controller.signal)
        if (!controller.signal.aborted) setLoaded({ key, page: result })
      } catch (caught) {
        if (!controller.signal.aborted) setLoaded({ key, error: caught instanceof Error ? caught.message : 'Не удалось загрузить данные.' })
      }
    }
    void load()
    return () => controller.abort()
  }, [auth.accessToken, canRead, financeClient, garage.id, key, query])

  async function cancel() {
    const selection = page?.items.find((device) => device.id === target?.id)
    if (!canRead || !canWrite || busy.current || !target || !selection || selection.isCanceled || selection.removedOn || !financeClient.cancelMeterDeviceReplacement || !selection.replacementReadingVersion || !selection.replacementAccountingMonth) return
    const numeric = parseReplacementValue(restoredValue)
    if (reasonRequired && !reason.trim() || reason.trim().length > 500 || target.requiresRestoredValue && numeric === null) {
      setError('Проверьте причину и показание.')
      return
    }
    busy.current = true
    setSaving(true)
    setError(null)
    try {
      await financeClient.cancelMeterDeviceReplacement(auth.accessToken, selection.id, { expectedDeviceVersion: selection.version,
        expectedReadingVersion: selection.replacementReadingVersion, accountingMonth: selection.replacementAccountingMonth,
        reason: reason.trim(), ...(target.requiresRestoredValue ? { restoredCurrentValue: numeric! } : {}) })
      setTarget(null)
      setRevision((value) => value + 1)
      closeRef.current?.focus()
      onChanged()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось отменить замену.')
      // Keep the draft; refreshed versions must be selected explicitly before another cancellation.
      setRevision((value) => value + 1)
    } finally {
      busy.current = false
      setSaving(false)
    }
  }

  return <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
    <section ref={dialogRef} className="detail-dialog garage-balance-dialog dictionary-modal-form" role="dialog" aria-modal="true" aria-labelledby="meter-history-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="detail-dialog-header"><div><h3 id="meter-history-title">История приборов</h3><p>Гараж {garage.number}</p></div>
        <button ref={closeRef} type="button" className="icon-button" disabled={saving} aria-label="Закрыть историю приборов" onClick={onClose}><X size={18} /></button></div>
        {!canRead ? <FormError>Нет права просматривать историю приборов.</FormError> : loading ? <TableLoadingState label="Загружаем историю приборов" rows={4} columns={5} />
          : result?.error ? <AsyncErrorState message={result.error} onRetry={() => setRevision((value) => value + 1)} />
          : page ? <>
            <div className="dictionary-table-scroll" style={{ maxHeight: 340 }}><table className="dictionary-data-table" aria-label="Приборы гаража"><thead><tr>
              <th>Услуга / прибор</th><th>Установлен / снят</th><th>Начало / конец</th><th>Статус / причина</th><th>Действия</th>
            </tr></thead><tbody>{page.items.map((device) => <tr key={device.id}>
              <td>{device.serviceName ?? 'Счётчик'}<br /><strong>№ {device.serialNumber}</strong></td>
              <td>{formatDateOnly(device.installedOn)}<br />{device.removedOn ? formatDateOnly(device.removedOn) : '—'}</td>
              <td>{formatNullableNumber(device.initialValue)}<br />{formatNullableNumber(device.finalValue)}</td>
              <td>{device.isCanceled ? 'Замена отменена' : device.removedOn ? 'Снят' : 'Действующий'}<br />{device.cancellationReason ?? device.replacementReason ?? '—'}</td>
              <td>{canWrite && !device.isCanceled && !device.removedOn && device.replacedDeviceId && device.replacementReadingVersion ?
                <button type="button" className="ghost-button" disabled={saving || loading || !financeClient.cancelMeterDeviceReplacement} onClick={() => { setTarget(device); setReason(''); setRestoredValue(''); setError(null) }} aria-label={`Отменить замену на прибор ${device.serialNumber}`}><RotateCcw size={16} />Отменить замену</button> : '—'}</td>
            </tr>)}</tbody></table>{page.items.length === 0 ? <EmptyState>Приборов пока нет.</EmptyState> : null}</div>
            <TablePagination ariaLabel="Пагинация истории приборов" pageSizeLabel="Количество приборов" {...query}
              totalCount={page.totalCount} visibleCount={page.items.length} disabled={saving || Boolean(target)}
              onPageChange={(number) => setQuery({ ...query, offset: (number - 1) * query.limit })} onPageSizeChange={(limit) => setQuery({ offset: 0, limit })} />
          </> : null}
        {target && canRead ? <form className="dictionary-form-section dictionary-modal-form" aria-label="Подтверждение отмены замены" noValidate onSubmit={(event) => { event.preventDefault(); void cancel() }}>
          <h4>Отменить замену на № {target.serialNumber}?</h4>
          <p>Прибор и показание восстановятся, начисления пересчитаются. Отмена недоступна, если в более поздних месяцах уже есть показания. Внесённые оплаты сохранятся, разница станет долгом или переплатой.</p>
          {error ? <FormError>{error}</FormError> : null}
          {target.requiresRestoredValue ? <FormField label="Показание восстанавливаемого прибора" help="Укажите фактическое показание за месяц."><MeterReadingInput aria-label="Показание восстанавливаемого прибора" required value={restoredValue} disabled={saving} onChange={(event) => setRestoredValue(event.target.value)} /></FormField> : null}
          <FormField label="Причина отмены замены"><textarea ref={reasonRef} required={reasonRequired} maxLength={500} value={reason} disabled={saving} onChange={(event) => setReason(event.target.value)} /></FormField>
          <div className="detail-dialog-actions"><button type="submit" className="secondary-button" aria-busy={saving} disabled={saving || loading || !canWrite}>{saving ? <LoaderCircle size={16} className="button-spinner" /> : <RotateCcw size={16} />}Подтвердить отмену</button>
            <button type="button" className="ghost-button" disabled={saving} onClick={() => setTarget(null)}>Оставить замену</button></div>
        </form> : null}
    </section>
  </div>
}
