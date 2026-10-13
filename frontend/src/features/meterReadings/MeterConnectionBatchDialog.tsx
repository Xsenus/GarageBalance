import { useEffect, useState } from 'react'
import { LoaderCircle, PlugZap, X } from 'lucide-react'
import type { FinanceClient, MeterReadingYearPageDto } from '../../services/financeApi'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { FormError } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { TablePagination } from '../../shared/TablePagination'
import { formatDateOnly } from '../../shared/formatters'
import { useEscapeKey, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { parseMeterStartValue, toGarageMeterStartRows, validateGarageMeterStartRows } from '../contractors/garageMeterStartValues'

type Selection = Record<string, { number: string; value: string }>

export function MeterConnectionBatchDialog({ accessToken, financeClient, meterKind, serviceName, year, onClose, onSaved }: {
  accessToken: string; financeClient: FinanceClient; meterKind: string; serviceName: string; year: string; onClose: () => void; onSaved: () => void
}) {
  const [connectedOn, setConnectedOn] = useState('')
  const [selected, setSelected] = useState<Selection>({})
  const [page, setPage] = useState<MeterReadingYearPageDto | null>(null)
  const [offset, setOffset] = useState(0)
  const [limit, setLimit] = useState(25)
  const [retry, setRetry] = useState(0)
  const [loadedKey, setLoadedKey] = useState('')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState(false)
  const [saving, setSaving] = useState(false)
  const dialogRef = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(true)
  useEscapeKey(!saving, onClose)
  const requestKey = `${accessToken}|${meterKind}|${year}|${offset}|${limit}|${retry}`
  const loading = loadedKey !== requestKey
  const entries = Object.entries(selected)

  useEffect(() => {
    const controller = new AbortController()
    financeClient.getMeterReadingYearPage(accessToken, { year: Number(year), meterKind, offset, limit }, controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return
        setPage(result)
        setLoadError(null)
        setLoadedKey(requestKey)
      }).catch((caught) => {
        if (controller.signal.aborted) return
        setLoadError(caught instanceof Error ? caught.message : 'Не удалось загрузить гаражи.')
        setLoadedKey(requestKey)
      })
    return () => controller.abort()
  }, [accessToken, financeClient, meterKind, year, offset, limit, requestKey])

  function validate() {
    if (!entries.length) return 'Выберите хотя бы один гараж.'
    if (entries.length > 100) return 'За один раз можно подключить не более 100 гаражей.'
    if (!connectedOn) return 'Укажите дату подключения.'
    const rows = toGarageMeterStartRows(entries.map(([id, row]) => ({ meterKind: id, serviceName: `Гараж ${row.number}`, unitName: null, value: null, hasReadings: false, requiresConnectionDate: true })))
      .map((row) => ({ ...row, connectedOn, value: selected[row.meterKind].value }))
    return Object.values(validateGarageMeterStartRows(rows))[0] ?? null
  }

  async function submit() {
    const validationError = validate()
    setError(validationError)
    if (validationError || saving) return
    if (!preview) { setPreview(true); return }
    setSaving(true)
    try {
      await financeClient.connectMeterServicesBatch!(accessToken, { meterKind, connectedOn, items: entries.map(([garageId, row]) => ({ garageId, value: parseMeterStartValue(row.value) })) })
      onSaved()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось подключить услугу.')
    } finally { setSaving(false) }
  }

  return <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
    <section ref={dialogRef} className="detail-dialog contractors-dialog contractors-dialog--wide meter-connection-dialog" role="dialog" aria-modal="true" aria-labelledby="meter-connection-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="detail-dialog-header"><h3 id="meter-connection-title">Подключить услугу: {serviceName}</h3><button className="icon-button" type="button" aria-label="Закрыть подключение услуги" disabled={saving} onClick={onClose}><X size={18} /></button></div>
      <div className="dictionary-modal-form">
        {error ? <FormError>{error}</FormError> : null}
        {preview ? <p className="confirmation-text">Дата подключения: {formatDateOnly(connectedOn)}. Первый расчётный месяц: {connectedOn.slice(5, 7)}.{connectedOn.slice(0, 4)}.</p> : <FormField label="Общая дата подключения" help="Дата применяется ко всем выбранным гаражам. Для каждого укажите фактическое стартовое показание; пустое значение оставит услугу в ожидании базы.">
          <LocalizedDatePicker ariaLabel="Общая дата подключения" mode="date" value={connectedOn} onChange={(value) => { setConnectedOn(value); setError(null) }} required />
        </FormField>}
        {!preview && loadError ? <AsyncErrorState message={loadError} onRetry={() => setRetry((value) => value + 1)} retrying={loading} /> : null}
        {!preview && loading ? <TableLoadingState label="Загружаем гаражи для подключения" columns={3} /> : preview ? (
          <div className="meter-connection-table" role="table" aria-label="Проверка подключения гаражей">
            <div role="row"><span role="columnheader">Гараж</span><span role="columnheader">Стартовое показание</span></div>
            {entries.map(([id, row]) => <div role="row" key={id}><span role="cell">{row.number}</span><span role="cell">{row.value.trim() || 'Требуется стартовое показание'}</span></div>)}
          </div>
        ) : !loadError && page ? <>
          <div className="meter-connection-table" role="table" aria-label="Гаражи для подключения услуги">
            <div role="row"><span role="columnheader">Гараж</span><span role="columnheader">Стартовое показание</span></div>
            {page.garages.map((garage) => <div role="row" key={garage.id}>
              <label role="cell"><input type="checkbox" aria-label={`Подключить гараж ${garage.number}`} checked={Boolean(selected[garage.id])} disabled={!garage.requiresConnectionDate || !selected[garage.id] && entries.length >= 100}
                onChange={(event) => { const checked = event.target.checked; setSelected((current) => { const next = { ...current }; if (checked) next[garage.id] = { number: garage.number, value: '' }; else delete next[garage.id]; return next }); setError(null) }} /> Гараж {garage.number}</label>
              <span role="cell">{garage.requiresConnectionDate ? <MeterReadingInput aria-label={`Стартовое показание гаража ${garage.number}`} value={selected[garage.id]?.value ?? ''} disabled={!selected[garage.id]}
                onChange={(event) => { const value = event.target.value; setSelected((current) => ({ ...current, [garage.id]: { number: garage.number, value } })); setError(null) }} /> : 'Уже ведётся учёт'}</span>
            </div>)}
          </div>
          {!page.garages.length ? <EmptyState>Нет гаражей для этой услуги.</EmptyState> : null}
          <TablePagination ariaLabel="Пагинация подключения услуг" totalCount={page.totalCount} offset={offset} limit={limit} visibleCount={page.garages.length} disabled={loading} onPageChange={(value) => setOffset((value - 1) * limit)} onPageSizeChange={(value) => { setLimit(value); setOffset(0) }} />
        </> : null}
        <p className="hint" role="status" aria-live="polite">Выбрано гаражей: {entries.length} из 100</p>
        <div className="detail-dialog-actions contractors-dialog-actions">
          <button className="ghost-button" type="button" disabled={saving} onClick={preview ? () => { setPreview(false); setError(null) } : onClose}>{preview ? 'Назад' : 'Отмена'}</button>
          <button className="secondary-button create-action-button" type="button" disabled={saving || !preview && (loading || Boolean(loadError))} aria-busy={saving} onClick={() => void submit()}>{saving ? <LoaderCircle className="button-spinner" size={17} aria-hidden="true" /> : <PlugZap size={17} />}<span>{preview ? 'Подключить' : 'Проверить'}</span></button>
        </div>
      </div>
    </section>
  </div>
}
