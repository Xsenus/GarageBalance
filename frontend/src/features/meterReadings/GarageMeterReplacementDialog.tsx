import { useEffect, useRef, useState } from 'react'
import { LoaderCircle, Save, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { FinanceClient, MeterDeviceDto, MeterReadingDto } from '../../services/financeApi'
import { hasPermission, permissions } from '../../shared/accessControl'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'
import { AsyncErrorState, TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { FormError } from '../../shared/formFeedback'
import { formatNullableNumber as formatAmount, getLocalDateInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { getMeterReplacementConsumption, parseReplacementValue as parseValue } from './meterReplacementConsumption'
import { SelectControl } from '../../shared/SelectControl'

const defaultOptions = [{ value: 'electricity', label: 'Электроэнергия' }, { value: 'water', label: 'Вода' }]
export function GarageMeterReplacementDialog({ auth, dictionaryClient, financeClient, garage, initialMeterKind = 'electricity', onClose, onSaved }: {
  auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient; garage: { id: string; number: string }; initialMeterKind?: string
  onClose: () => void; onSaved: () => void
}) {
  const [reasonRequired] = useActionCommentSettings()
  const [kind, setKind] = useState(initialMeterKind)
  const [date, setDate] = useState(getLocalDateInputValue)
  const [serial, setSerial] = useState('')
  const [initial, setInitial] = useState('0')
  const [final, setFinal] = useState('')
  const [finalTouched, setFinalTouched] = useState(false)
  const [reason, setReason] = useState('')
  const [retry, setRetry] = useState(0)
  const [loaded, setLoaded] = useState<{ key: string; reading?: MeterReadingDto; activeDevice?: MeterDeviceDto; options: typeof defaultOptions } | null>(null)
  const [loadError, setLoadError] = useState<{ key: string; message: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const submitting = useRef(false)
  const canWrite = hasPermission(auth, permissions.paymentsWrite)
  const month = date.slice(0, 7)
  const requestKey = JSON.stringify([auth.accessToken, garage.id, garage.number, kind, month, retry])
  const loading = canWrite && loaded?.key !== requestKey && loadError?.key !== requestKey
  const loadedForRequest = loaded?.key === requestKey ? loaded : null
  // An entered month value belongs to the new meter, so the removed meter starts from the month's
  // opening value; a repeated replacement removes the meter that value was entered for.
  const savedFinal = loadedForRequest?.reading
    ? loadedForRequest.reading.isMeterReplacement ? loadedForRequest.reading.currentValue : loadedForRequest.reading.previousValue
    : loadedForRequest?.activeDevice?.lastReadingValue ?? loadedForRequest?.activeDevice?.initialValue
  const defaultFinal = typeof savedFinal === 'number' ? String(savedFinal) : ''
  const finalText = finalTouched ? final : defaultFinal
  const consumptionBaseline = loadedForRequest?.reading
    ? loadedForRequest.reading.previousValue
    : loadedForRequest?.activeDevice ? loadedForRequest.activeDevice.lastReadingValue ?? loadedForRequest.activeDevice.initialValue : null
  const earlierConsumption = loadedForRequest?.reading?.isMeterReplacement ? loadedForRequest.reading.previousDeviceConsumption ?? 0 : 0
  // The month-end value is entered in «Платежи»; an already entered value of the month stays the new meter's value
  // unless it is below the new meter's start: then it was taken from the removed meter before the replacement.
  const enteredMonthValue = loadedForRequest?.reading?.currentValue ?? null
  const finalValue = parseValue(finalText)
  const initialValue = parseValue(initial)
  const monthValueBelongsToRemovedMeter = enteredMonthValue !== null && initialValue !== null && enteredMonthValue < initialValue
  const monthValue = monthValueBelongsToRemovedMeter ? null : enteredMonthValue
  const consumption = getMeterReplacementConsumption(
    typeof consumptionBaseline === 'number' ? consumptionBaseline : null, finalValue, initialValue, monthValue ?? initialValue, earlierConsumption)
  const ref = useFocusTrap<HTMLElement>(true)
  useRestoreFocusOnClose(true)
  useEscapeKey(!saving, onClose)

  useEffect(() => {
    if (!canWrite) return
    const controller = new AbortController()
    async function load() {
      try {
        const settings = await dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, 500, false, true, true, controller.signal)
        const options = [...defaultOptions]
        for (const setting of settings) {
          if (setting.meterKind && !options.some((option) => option.value === setting.meterKind)
            && (!setting.appliesToSelectedGarages || setting.garageIds?.includes(garage.id))) {
            options.push({ value: setting.meterKind, label: setting.name })
          }
        }
        let offset = 0
        let reading: MeterReadingDto | undefined
        while (!controller.signal.aborted && month) {
          const page = await financeClient.getMeterReadingsPage(auth.accessToken,
            { monthFrom: month, monthTo: month, meterKind: kind, search: garage.number, offset, limit: 100 }, controller.signal)
          reading = page.items.find((item) => item.garageId === garage.id && !item.isCanceled)
          offset += page.items.length
          if (reading || offset >= page.totalCount) break
          if (page.items.length === 0) throw new Error('Incomplete meter reading page')
        }
        const devices = financeClient.getMeterDevices
          ? await financeClient.getMeterDevices(auth.accessToken, garage.id, kind, controller.signal)
          : []
        const activeDevice = devices.find((device) => !device.removedOn)
        if (!controller.signal.aborted) setLoaded({ key: requestKey, reading, activeDevice, options })
      } catch {
        if (!controller.signal.aborted) setLoadError({ key: requestKey, message: 'Не удалось проверить показания перед заменой счётчика.' })
      }
    }
    void load()
    return () => controller.abort()
  }, [auth.accessToken, dictionaryClient, financeClient, garage.id, garage.number, kind, month, requestKey, canWrite])

  async function save() {
    if (!canWrite || loading || loaded?.key !== requestKey || submitting.current || !financeClient.replaceMeterDevice) return
    if (!date || !serial.trim() || serial.trim().length > 100 || initialValue === null || finalValue === null
      || reasonRequired && !reason.trim()) {
      setError('Укажите дату, номер нового счётчика, корректные показания и причину замены.')
      return
    }
    submitting.current = true
    setSaving(true)
    setError(null)
    try {
      await financeClient.replaceMeterDevice(auth.accessToken, { garageId: garage.id, meterKind: kind, accountingMonth: `${month}-01`,
        replacementDate: date, newSerialNumber: serial.trim(), newInitialValue: initialValue,
        removedDeviceFinalValue: finalValue, reason: reason.trim(), meterReadingId: loaded.reading?.id, expectedReadingVersion: loaded.reading?.version })
      onSaved()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось заменить счётчик.')
      setRetry((value) => value + 1)
    } finally {
      submitting.current = false
      setSaving(false)
    }
  }

  const disabled = saving || !canWrite
  return <div className="modal-backdrop" role="presentation" onMouseDown={saving ? undefined : onClose}>
    <section ref={ref} className="detail-dialog contractors-dialog" role="dialog" aria-modal="true" aria-labelledby="garage-meter-replacement-title" onMouseDown={(event) => event.stopPropagation()}>
      <div className="detail-dialog-header"><div><h3 id="garage-meter-replacement-title">Замена счётчика</h3><p>Гараж {garage.number}</p></div>
        <button type="button" className="icon-button" aria-label="Закрыть замену счётчика" disabled={saving} onClick={onClose}><X size={18} /></button></div>
      <form className="dictionary-modal-form" noValidate onSubmit={(event) => { event.preventDefault(); void save() }}>
        {!canWrite ? <FormError>Нет права изменять показания и заменять счётчики.</FormError> : null}
        {error ? <FormError>{error}</FormError> : null}
        <FormField label="Счётчик"><SelectControl aria-label="Счётчик для замены" options={loaded?.options ?? defaultOptions} value={kind} disabled={disabled} onChange={(value) => { setKind(value); setFinal(''); setFinalTouched(false); setError(null) }} /></FormField>
        <FormField label="Дата замены"><LocalizedDatePicker ariaLabel="Дата замены счетчика" mode="date" required value={date} disabled={disabled} onChange={setDate} /></FormField>
        {loading ? <TableLoadingState label="Проверяем показания счётчика" rows={2} columns={2} /> : loadError?.key === requestKey ? <AsyncErrorState message={loadError.message} onRetry={() => setRetry((value) => value + 1)} /> : null}
        <FormField label="Номер нового счётчика"><input aria-label="Номер нового счетчика" required maxLength={100} value={serial} disabled={disabled} onChange={(event) => setSerial(event.target.value)} /></FormField>
        <FormField label="Конечное показание старого счётчика" help="Последнее значение снимаемого счётчика. Расход до замены сохраняется в начислении этого месяца."><MeterReadingInput aria-label="Конечное показание старого счетчика" required value={finalText} disabled={disabled} onChange={(event) => { setFinalTouched(true); setFinal(event.target.value) }} /></FormField>
        <FormField label="Начальное показание нового счётчика" help="Старт в карточке гаража не меняется."><MeterReadingInput aria-label="Начальное показание нового счетчика" required value={initial} disabled={disabled} onChange={(event) => setInitial(event.target.value)} /></FormField>
        <button type="button" className="ghost-button" disabled={disabled || parseValue(finalText) === null} onClick={() => setInitial(finalText)}>Перенести конечное в начальное</button>
        {consumption ? <p className={`form-hint${consumption.oldMeter < 0 || consumption.newMeter < 0 ? ' form-hint--warning' : ''}`} role="status" aria-live="polite">
          {consumption.oldMeter < 0
            ? `Конечное показание старого счётчика меньше его последнего показания (${formatAmount(consumptionBaseline as number)}).`
            : consumption.newMeter < 0
              ? `Показание за месяц (${formatAmount(monthValue as number)}) меньше начального показания нового счётчика.`
              : monthValue === null
                ? `${monthValueBelongsToRemovedMeter ? `Показание за месяц ${formatAmount(enteredMonthValue as number)} меньше начального показания нового счётчика и будет считаться показанием старого счётчика. ` : ''}Расход старого счётчика: ${formatAmount(consumption.oldMeter)}${earlierConsumption ? ` (ранее снятые приборы ${formatAmount(earlierConsumption)})` : ''}. Показание нового счётчика на конец месяца введите в «Платежах» — расход посчитается по обоим счётчикам.`
                : `Расход за месяц: ${formatAmount(consumption.total)} (${earlierConsumption ? `ранее снятые приборы ${formatAmount(earlierConsumption)}, ` : ''}старый счётчик ${formatAmount(consumption.oldMeter)}, новый ${formatAmount(consumption.newMeter)}; показание за месяц ${formatAmount(monthValue)} из «Платежей»).`}
        </p> : null}
        <FormField label="Причина замены счетчика"><textarea required={reasonRequired} maxLength={500} value={reason} disabled={disabled} onChange={(event) => setReason(event.target.value)} /></FormField>
        <div className="detail-dialog-actions"><button type="submit" className="secondary-button" disabled={disabled || loading || loaded?.key !== requestKey || !financeClient.replaceMeterDevice} aria-busy={saving}>{saving ? <LoaderCircle size={17} className="financial-report-button__spinner" /> : <Save size={17} />}<span>Сохранить замену</span></button>
          <button type="button" className="ghost-button" disabled={saving} onClick={onClose}>Отмена</button></div>
      </form>
    </section>
  </div>
}
